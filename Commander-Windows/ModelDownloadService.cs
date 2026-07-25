using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;

namespace Commander;

public enum ModelDownloadTarget
{
    QwenAsr,
    SenseVoiceAsr,
    Llm,
}

public sealed record ModelDownloadProgress(
    ModelDownloadTarget Target,
    ModelComponentKind? Component,
    string Status,
    double Fraction,
    bool IsIndeterminate = false);

public sealed class ModelDownloadService : IDisposable
{
    private const string BundledLlamaResource = "Commander.Bundled.llama-runtime.zip";
    private const string BundledLlamaLicenseResource = "Commander.Bundled.llama-license.txt";
    private const string BundledLlamaSha256 =
        "18994b4b365dc67db9933c8d285f9f5dca614ff771a5aa0097377c2a170b7bff";

    public static ModelDownloadService Shared { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly object _stateLock = new();
    private readonly Dictionary<ModelDownloadTarget, DownloadOperation> _operations = [];

    public event EventHandler<ModelDownloadProgress>? ProgressChanged;
    public event EventHandler? ModelsChanged;

    public bool IsAnyRunning
    {
        get
        {
            lock (_stateLock) return _operations.Count > 0;
        }
    }

    private ModelDownloadService()
    {
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(40),
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Receptionist-Windows/2.0");
    }

    public bool IsDownloading(ModelDownloadTarget target)
    {
        lock (_stateLock) return _operations.ContainsKey(target);
    }

    public bool IsDownloadingAsr(AsrModelKind model) =>
        IsDownloading(TargetForAsr(model));

    public Task DownloadAsrAsync(AsrModelKind model) =>
        DownloadAsync(TargetForAsr(model), ModelCatalog.ForAsr(model));

    public Task DownloadLlmAsync() =>
        DownloadAsync(ModelDownloadTarget.Llm, ModelCatalog.LlmGroup);

    public void Cancel(ModelDownloadTarget target)
    {
        lock (_stateLock)
        {
            if (_operations.TryGetValue(target, out var operation))
                operation.Cancellation.Cancel();
        }
    }

    public void DeleteAsr(AsrModelKind model) =>
        Delete(TargetForAsr(model), ModelCatalog.ForAsr(model));

    public void DeleteLlm() =>
        Delete(ModelDownloadTarget.Llm, ModelCatalog.LlmGroup);

    private void Delete(ModelDownloadTarget target, IReadOnlyList<ModelItem> items)
    {
        if (IsDownloading(target)) return;
        foreach (var item in items)
        {
            try
            {
                if (Directory.Exists(item.DirectoryPath)) Directory.Delete(item.DirectoryPath, recursive: true);
            }
            catch (Exception exception)
            {
                AppLog.Error(nameof(ModelDownloadService), exception, $"Cannot delete {item.Kind}");
            }
        }
        ModelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DownloadAsync(ModelDownloadTarget target, IReadOnlyList<ModelItem> items)
    {
        var missing = items.Where(item => !ModelCatalog.IsReady(item)).ToArray();
        if (missing.Length == 0)
        {
            Emit(target, null, T("模型已就绪", "Models are ready"), 1);
            ModelsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var cancellation = new CancellationTokenSource();
        var reservedMb = missing.Sum(item => item.SizeMb);
        lock (_stateLock)
        {
            if (_operations.ContainsKey(target))
            {
                cancellation.Dispose();
                return;
            }
            _operations[target] = new DownloadOperation(cancellation, reservedMb);
        }

        try
        {
            double totalReservedMb;
            lock (_stateLock)
            {
                totalReservedMb = _operations.Values.Sum(operation => operation.ReservedMb);
            }
            EnsureDiskSpace(totalReservedMb);
            Directory.CreateDirectory(AppPaths.ModelsRoot);

            var totalMb = missing.Sum(item => item.SizeMb);
            var completedMb = 0d;
            foreach (var item in missing)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Emit(target, item.Kind, DownloadingText(item.Kind), completedMb / totalMb);
                var installed = await FetchItemAsync(
                    target,
                    item,
                    completedMb,
                    totalMb,
                    cancellation.Token);
                if (!installed || !ModelCatalog.IsReady(item))
                {
                    throw new InvalidDataException($"Downloaded files for {item.Kind} did not pass validation.");
                }
                completedMb += item.SizeMb;
                ModelsChanged?.Invoke(this, EventArgs.Empty);
            }
            Emit(target, null, T("下载完成", "Download complete"), 1);
        }
        catch (OperationCanceledException)
        {
            Emit(
                target,
                null,
                T("下载已取消，可稍后继续", "Download paused. You can resume later"),
                0,
                isIndeterminate: true);
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(ModelDownloadService), exception);
            Emit(
                target,
                null,
                T("下载失败，已保留进度，请重试", "Download failed. Progress was saved; try again"),
                0,
                isIndeterminate: true);
        }
        finally
        {
            lock (_stateLock)
            {
                _operations.Remove(target);
            }
            cancellation.Dispose();
            ModelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> FetchItemAsync(
        ModelDownloadTarget target,
        ModelItem item,
        double completedMb,
        double totalMb,
        CancellationToken token)
    {
        if (item.Kind == ModelComponentKind.LlamaRuntime &&
            await TryInstallBundledLlamaRuntimeAsync(target, item, completedMb, totalMb, token))
            return true;
        if (item.PreferArchives && await TryArchivesAsync(target, item, completedMb, totalMb, token))
            return true;
        if (await TryFileSourcesAsync(target, item, completedMb, totalMb, token)) return true;
        return !item.PreferArchives &&
               await TryArchivesAsync(target, item, completedMb, totalMb, token);
    }

    private async Task<bool> TryInstallBundledLlamaRuntimeAsync(
        ModelDownloadTarget target,
        ModelItem item,
        double completedMb,
        double totalMb,
        CancellationToken token)
    {
        var archivePath = Path.Combine(
            AppPaths.ModelsRoot,
            ".downloads",
            "llama-runtime-bundled.zip");
        try
        {
            await using var resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(BundledLlamaResource);
            if (resource is null) return false;

            Emit(target, item.Kind, PreparingText(item.Kind), completedMb / totalMb, true);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
            await using (var output = new FileStream(
                             archivePath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 128,
                             useAsync: true))
            {
                await resource.CopyToAsync(output, 1024 * 128, token);
                await output.FlushAsync(token);
            }

            await using (var archive = File.OpenRead(archivePath))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(archive, token)).ToLowerInvariant();
                if (!string.Equals(digest, BundledLlamaSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Bundled llama runtime failed SHA-256 validation.");
            }

            Emit(target, item.Kind, PreparingText(item.Kind), completedMb / totalMb, true);
            var installed = await InstallArchiveAsync(archivePath, item, token);
            if (installed) await InstallBundledLlamaLicenseAsync(item, token);
            return installed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(ModelDownloadService), exception, "Bundled llama runtime installation failed");
            try { File.Delete(archivePath); }
            catch { }
            return false;
        }
    }

    private static async Task InstallBundledLlamaLicenseAsync(ModelItem item, CancellationToken token)
    {
        try
        {
            await using var resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(BundledLlamaLicenseResource);
            if (resource is null) return;
            await using var output = new FileStream(
                Path.Combine(item.DirectoryPath, "LICENSE.llama.cpp.txt"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                4096,
                useAsync: true);
            await resource.CopyToAsync(output, 4096, token);
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(ModelDownloadService), exception, "Cannot install llama.cpp license");
        }
    }

    private async Task<bool> TryFileSourcesAsync(
        ModelDownloadTarget target,
        ModelItem item,
        double completedMb,
        double totalMb,
        CancellationToken token)
    {
        foreach (var source in item.FileSources)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(item.DirectoryPath);
                    var estimatedFileMb = item.SizeMb / Math.Max(source.Files.Count, 1);
                    var sourceCompletedMb = 0d;
                    foreach (var file in source.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        var destination = Path.Combine(
                            item.DirectoryPath,
                            file.LocalPath.Replace('/', Path.DirectorySeparatorChar));
                        if (ModelCatalog.IsReadyFile(item, file.LocalPath))
                        {
                            sourceCompletedMb += estimatedFileMb;
                            continue;
                        }
                        try { if (File.Exists(destination)) File.Delete(destination); }
                        catch { }

                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        var uri = new Uri(new Uri(source.BaseUrl), file.RemotePath);
                        await DownloadFileAsync(
                            uri,
                            destination,
                            fraction => Emit(
                                target,
                                item.Kind,
                                DownloadingText(item.Kind),
                                Math.Clamp(
                                    (completedMb + sourceCompletedMb + estimatedFileMb * fraction) / totalMb,
                                    0,
                                    1)),
                            token);
                        sourceCompletedMb += estimatedFileMb;
                    }
                    if (ModelCatalog.IsReady(item)) return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Error(
                        nameof(ModelDownloadService),
                        exception,
                        $"File source {source.BaseUrl} failed for {item.Kind}, attempt {attempt}");
                    if (attempt < 2)
                    {
                        Emit(
                            target,
                            item.Kind,
                            RetryingText(item.Kind, exception),
                            completedMb / totalMb,
                            true);
                        await Task.Delay(TimeSpan.FromSeconds(2), token);
                    }
                }
            }
        }
        return false;
    }

    private async Task<bool> TryArchivesAsync(
        ModelDownloadTarget target,
        ModelItem item,
        double completedMb,
        double totalMb,
        CancellationToken token)
    {
        foreach (var address in item.ArchiveUrls)
        {
            var extension = ArchiveExtension(address);
            var downloads = Path.Combine(AppPaths.ModelsRoot, ".downloads");
            Directory.CreateDirectory(downloads);
            var local = Path.Combine(downloads, $"{item.Kind}{extension}");
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await DownloadFileAsync(
                        new Uri(address),
                        local,
                        fraction => Emit(
                            target,
                            item.Kind,
                            DownloadingText(item.Kind),
                            Math.Clamp((completedMb + item.SizeMb * fraction) / totalMb, 0, 1)),
                        token,
                        preserveCompletedFile: true);
                    Emit(target, item.Kind, InstallingText(item.Kind), completedMb / totalMb, true);
                    if (await InstallArchiveAsync(local, item, token)) return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Error(
                        nameof(ModelDownloadService),
                        exception,
                        $"Archive source {address} failed for {item.Kind}, attempt {attempt}");
                    if (attempt < 2)
                    {
                        Emit(
                            target,
                            item.Kind,
                            RetryingText(item.Kind, exception),
                            completedMb / totalMb,
                            true);
                        await Task.Delay(TimeSpan.FromSeconds(2), token);
                    }
                }
            }
        }
        return false;
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        Action<double> onProgress,
        CancellationToken token,
        bool preserveCompletedFile = false)
    {
        var partial = destination + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var remoteLength = response.Content.Headers.ContentRange?.Length;
            if (remoteLength is > 0 && existing == remoteLength)
            {
                ValidateDownloadedFile(partial, destination);
                File.Move(partial, destination, overwrite: true);
                ValidateDownloadedFile(destination, destination);
                onProgress(1);
                return;
            }

            File.Delete(partial);
            await DownloadFileAsync(uri, destination, onProgress, token, preserveCompletedFile);
            return;
        }

        if (existing > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            existing = 0;
            File.Delete(partial);
        }
        response.EnsureSuccessStatusCode();

        var incoming = response.Content.Headers.ContentLength ?? 0;
        var expected = response.Content.Headers.ContentRange?.Length ??
                       (incoming > 0 ? existing + incoming : 0);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(
            partial,
            existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        var received = existing;
        var speedWindow = Stopwatch.StartNew();
        var speedWindowStart = received;
        while (true)
        {
            int read;
            try
            {
                read = await input.ReadAsync(buffer, token)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(45), token);
            }
            catch (Exception exception) when (
                exception is IOException or TimeoutException &&
                expected > 0 &&
                received == expected)
            {
                // Some CDNs reset a ranged connection after sending the final byte.
                break;
            }
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (expected > 0)
                onProgress(Math.Clamp((double)received / expected, 0, 0.99));

            if (speedWindow.Elapsed >= TimeSpan.FromSeconds(30))
            {
                var bytesInWindow = received - speedWindowStart;
                if (bytesInWindow < 900_000)
                    throw new IOException("Download source stayed below 30 KB/s for 30 seconds.");
                speedWindow.Restart();
                speedWindowStart = received;
            }
        }
        await output.FlushAsync(token);
        if (expected > 0 && received != expected)
            throw new EndOfStreamException(
                $"Download ended at {received} of {expected} bytes.");

        ValidateDownloadedFile(partial, destination);
        File.Move(partial, destination, overwrite: true);
        if (!preserveCompletedFile) ValidateDownloadedFile(destination, destination);
        onProgress(1);
    }

    private static void ValidateDownloadedFile(string path, string destination)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024) throw new InvalidDataException("Downloaded file is empty.");

        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[16];
        _ = stream.Read(head);
        var text = System.Text.Encoding.ASCII.GetString(head).ToLowerInvariant();
        if (text.Contains("<!doctype") || text.Contains("<html"))
            throw new InvalidDataException("The download source returned an HTML error page.");

        if (destination.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) &&
            !head[..4].SequenceEqual("GGUF"u8))
            throw new InvalidDataException("Invalid GGUF file header.");
    }

    private static async Task<bool> InstallArchiveAsync(
        string archivePath,
        ModelItem item,
        CancellationToken token)
    {
        if (archivePath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(item.DirectoryPath);
            File.Move(archivePath, Path.Combine(item.DirectoryPath, item.RequiredFiles[0]), overwrite: true);
            return ModelCatalog.IsReady(item);
        }

        var extractionRoot = Path.Combine(Path.GetTempPath(), "aicmd-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, extractionRoot, overwriteFiles: true);
            }
            else if (archivePath.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "tar.exe",
                    ArgumentList = { "-xjf", archivePath, "-C", extractionRoot },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (process is null) throw new InvalidOperationException("Unable to start Windows tar.exe.");
                await process.WaitForExitAsync(token);
                if (process.ExitCode != 0) throw new InvalidDataException("Model archive extraction failed.");
            }
            else
            {
                throw new NotSupportedException("Unsupported model archive format.");
            }

            var found = FindInstallRoot(extractionRoot, item.RequiredFiles);
            if (found is null) return false;

            Directory.CreateDirectory(AppPaths.ModelsRoot);
            var staging = item.DirectoryPath + ".installing";
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.Move(found, staging);
            if (Directory.Exists(item.DirectoryPath)) Directory.Delete(item.DirectoryPath, recursive: true);
            Directory.Move(staging, item.DirectoryPath);
            return ModelCatalog.IsReady(item);
        }
        finally
        {
            try { if (Directory.Exists(extractionRoot)) Directory.Delete(extractionRoot, recursive: true); }
            catch { }
            try { File.Delete(archivePath); }
            catch { }
        }
    }

    private static string? FindInstallRoot(string root, IReadOnlyList<string> requiredFiles)
    {
        static bool ContainsAll(string directory, IReadOnlyList<string> required) => required.All(relative =>
            File.Exists(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar))));

        if (ContainsAll(root, requiredFiles)) return root;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (ContainsAll(directory, requiredFiles)) return directory;
        }
        return null;
    }

    private static string ArchiveExtension(string address)
    {
        var path = new Uri(address).AbsolutePath;
        if (path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)) return ".tar.bz2";
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ".zip";
        if (path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) return ".onnx";
        return ".download";
    }

    private static void EnsureDiskSpace(double requiredMb)
    {
        Directory.CreateDirectory(AppPaths.ModelsRoot);
        var root = Path.GetPathRoot(Path.GetFullPath(AppPaths.ModelsRoot))!;
        var availableMb = new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d;
        var safetyMb = requiredMb * 1.1 + 256;
        if (availableMb < safetyMb)
        {
            throw new IOException($"Insufficient disk space. Need about {safetyMb / 1024:0.0} GB.");
        }
    }

    private void Emit(
        ModelDownloadTarget target,
        ModelComponentKind? component,
        string status,
        double fraction,
        bool isIndeterminate = false) =>
        ProgressChanged?.Invoke(
            this,
            new ModelDownloadProgress(
                target,
                component,
                status,
                component is null && fraction >= 1
                    ? 1
                    : Math.Clamp(fraction, 0, 0.99),
                isIndeterminate));

    private static string DisplayName(ModelComponentKind kind) => kind switch
    {
        ModelComponentKind.QwenAsr => T("Qwen3 高精度模型", "Qwen3 high-accuracy model"),
        ModelComponentKind.SileroVad => T("语音分段模型", "speech segmentation model"),
        ModelComponentKind.SenseVoiceAsr => T("SenseVoice 轻量模型", "SenseVoice lightweight model"),
        ModelComponentKind.Punctuation => T("标点模型", "punctuation model"),
        ModelComponentKind.LlamaRuntime => T("语义增强大模型", "semantic enhancement model"),
        ModelComponentKind.Llm => T("语义增强大模型", "semantic enhancement model"),
        _ => T("模型", "model"),
    };

    private static string DownloadingText(ModelComponentKind kind) => kind == ModelComponentKind.LlamaRuntime
        ? PreparingText(kind)
        : T($"正在下载 {DisplayName(kind)}", $"Downloading {DisplayName(kind)}");

    private static string InstallingText(ModelComponentKind kind) => kind == ModelComponentKind.LlamaRuntime
        ? PreparingText(kind)
        : T($"正在安装 {DisplayName(kind)}", $"Installing {DisplayName(kind)}");

    private static string PreparingText(ModelComponentKind kind) =>
        T($"正在准备 {DisplayName(kind)}", $"Preparing {DisplayName(kind)}");

    private static string RetryingText(ModelComponentKind kind, Exception exception)
    {
        if (exception is InvalidDataException)
        {
            return T(
                $"文件校验失败，正在切换 {DisplayName(kind)} 下载线路",
                $"File validation failed. Trying another source for {DisplayName(kind)}");
        }

        if (exception is HttpRequestException or TimeoutException or TaskCanceledException or IOException)
        {
            return T(
                $"下载中断，正在重试 {DisplayName(kind)}",
                $"Download interrupted. Retrying {DisplayName(kind)}");
        }

        return T(
            $"模型准备失败，正在尝试备用方式",
            $"Model setup failed. Trying another method");
    }

    private static ModelDownloadTarget TargetForAsr(AsrModelKind model) =>
        model == AsrModelKind.Qwen ? ModelDownloadTarget.QwenAsr : ModelDownloadTarget.SenseVoiceAsr;

    private static string T(string chinese, string english) => I18n.IsChinese ? chinese : english;

    public void Dispose()
    {
        CancellationTokenSource[] cancellations;
        lock (_stateLock)
        {
            cancellations = _operations.Values.Select(operation => operation.Cancellation).ToArray();
        }
        foreach (var cancellation in cancellations) cancellation.Cancel();
        _httpClient.Dispose();
    }

    private sealed record DownloadOperation(CancellationTokenSource Cancellation, double ReservedMb);
}
