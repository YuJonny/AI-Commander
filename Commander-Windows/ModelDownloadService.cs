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
    private const string BundledVadResource = "Commander.Bundled.silero_vad.onnx";
    private const string BundledVadSha256 =
        "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6";

    public static ModelDownloadService Shared { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly HttpClient _directClient;      // 绕过系统代理的备用通道
    private bool _systemProxyConfigured;
    private bool _preferDirect;                     // 一旦直连成功过，后续直接走直连
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
        _httpClient = CreateClient(useProxy: true);
        _directClient = CreateClient(useProxy: false);

        // 所有下载源都是国内可直连的（ModelScope / hf-mirror），本就不需要代理。
        // 但 .NET 默认走 Windows 系统代理，而 Clash 一类工具退出后常把 127.0.0.1:7890
        // 留在系统设置里——用户以为"没开代理"，程序却在往一个死端口发请求，表现就是
        // 每条线路都连不上。这里把检测到的代理记进日志，便于事后定位。
        try
        {
            var proxy = HttpClient.DefaultProxy?.GetProxy(new Uri("https://modelscope.cn"));
            _systemProxyConfigured = proxy is not null;
            AppLog.Info(
                nameof(ModelDownloadService),
                _systemProxyConfigured
                    ? $"System proxy detected: {proxy}. Will fall back to a direct connection if it fails."
                    : "No system proxy detected; using direct connections.");
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(ModelDownloadService), exception, "Failed to inspect system proxy settings.");
        }
    }

    private static HttpClient CreateClient(bool useProxy)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(useProxy ? 20 : 40),   // 走代理时更早失败，好尽快改直连
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = useProxy,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Receptionist-Windows/2.0");
        return client;
    }

    /// 连接类错误：多半是代理死了 / 端口不通，值得换直连再试一次
    private static bool IsConnectionFailure(Exception exception) =>
        exception is HttpRequestException { StatusCode: null } ||
        exception is TaskCanceledException or TimeoutException ||
        (exception is HttpRequestException http &&
         http.InnerException is System.Net.Sockets.SocketException or IOException);

    /// 发请求；系统代理连不上时自动改直连重试一次。
    /// 用户的实际场景就是这个：Windows 上"没开代理"，但系统设置里残留着已停止的代理端口。
    private async Task<HttpResponseMessage> SendWithProxyFallbackAsync(
        Uri uri, long existing, CancellationToken token)
    {
        HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
            return request;
        }

        // 已经确认过直连可用，就别再浪费时间试代理
        if (_preferDirect || !_systemProxyConfigured)
        {
            using var direct = Build();
            return await _directClient.SendAsync(direct, HttpCompletionOption.ResponseHeadersRead, token);
        }

        try
        {
            using var viaProxy = Build();
            return await _httpClient.SendAsync(viaProxy, HttpCompletionOption.ResponseHeadersRead, token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested && IsConnectionFailure(exception))
        {
            AppLog.Error(
                nameof(ModelDownloadService),
                exception,
                $"System proxy failed for {uri.Host}; retrying with a direct connection.");
            using var direct = Build();
            var response = await _directClient.SendAsync(direct, HttpCompletionOption.ResponseHeadersRead, token);
            _preferDirect = true;      // 直连成功，本次会话后续都走直连
            AppLog.Info(nameof(ModelDownloadService), "Direct connection succeeded; bypassing the system proxy from now on.");
            return response;
        }
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

        ModelItem? failingItem = null;
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
                failingItem = item;   // 出错时好指名道姓：990MB 的主模型下完了却卡在 629KB 的组件上，
                                      // 只报"下载失败"会让用户以为整包白下了
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
                failingItem = null;
                ModelsChanged?.Invoke(this, EventArgs.Empty);
            }
            Emit(target, null, T("下载完成", "Download complete"), 1);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
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
            // 失败文案必须给三样东西，否则用户只会觉得"点几次都失败"：
            // ① 到底为什么失败 ② 已经下了多少（证明再点是续传不是重来）③ 下一步做什么
            Emit(target, null, FailureText(missing, failingItem, exception), 0, isIndeterminate: true);
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
        // Silero VAD 直接从内嵌资源装，不走网络（629KB，Qwen 档的必要组件）
        if (item.Kind == ModelComponentKind.SileroVad &&
            await TryInstallBundledVadAsync(target, item, completedMb, totalMb, token))
            return true;
        if (item.PreferArchives && await TryArchivesAsync(target, item, completedMb, totalMb, token))
            return true;
        if (await TryFileSourcesAsync(target, item, completedMb, totalMb, token)) return true;
        return !item.PreferArchives &&
               await TryArchivesAsync(target, item, completedMb, totalMb, token);
    }

    /// 从内嵌资源装 Silero VAD。此前它唯一的国内源是 hf-mirror，一旦那条线路在用户网络上
    /// 不通，整个 Qwen 档就卡死在 GitHub 镜像——而这个文件只有 629KB，没有任何理由走网络。
    private async Task<bool> TryInstallBundledVadAsync(
        ModelDownloadTarget target,
        ModelItem item,
        double completedMb,
        double totalMb,
        CancellationToken token)
    {
        try
        {
            await using var resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(BundledVadResource);
            if (resource is null) return false;

            Emit(target, item.Kind, PreparingText(item.Kind), completedMb / totalMb, true);
            Directory.CreateDirectory(item.DirectoryPath);
            var destination = Path.Combine(item.DirectoryPath, item.RequiredFiles[0]);
            var staging = destination + ".bundled";
            await using (var output = new FileStream(
                             staging, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true))
            {
                await resource.CopyToAsync(output, 1024 * 64, token);
                await output.FlushAsync(token);
            }

            await using (var written = File.OpenRead(staging))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(written, token)).ToLowerInvariant();
                if (!string.Equals(digest, BundledVadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Bundled Silero VAD failed SHA-256 validation.");
            }

            File.Move(staging, destination, overwrite: true);
            return ModelCatalog.IsReady(item);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 内嵌装不上就退回网络下载，不阻断流程
            AppLog.Error(nameof(ModelDownloadService), exception, "Bundled Silero VAD install failed; falling back to network.");
            return false;
        }
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
        catch (OperationCanceledException) when (token.IsCancellationRequested)
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
        var sourceIndex = 0;
        var sourceTotal = item.FileSources.Count + item.ArchiveUrls.Count;
        foreach (var source in item.FileSources)
        {
            sourceIndex++;
            // 从第二条线路起，明确告诉用户"在换第几条线、换到哪家"，切换才不像乱撞
            if (sourceIndex > 1)
                Emit(target, item.Kind,
                     SwitchingSourceText(item.Kind, sourceIndex, sourceTotal, source.BaseUrl),
                     completedMb / totalMb, true);
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
                            (fraction, detail) => Emit(
                                target,
                                item.Kind,
                                DownloadingText(item.Kind, detail),
                                Math.Clamp(
                                    (completedMb + sourceCompletedMb + estimatedFileMb * fraction) / totalMb,
                                    0,
                                    1)),
                            token);
                        sourceCompletedMb += estimatedFileMb;
                    }
                    if (ModelCatalog.IsReady(item)) return true;
                    // 文件都下完了却判定未就绪——把到底差哪个文件、差多少写清楚，
                    // 否则用户只会看到进度跑满后莫名其妙开始换线路
                    AppLog.Error(
                        nameof(ModelDownloadService),
                        new InvalidDataException("Validation failed after download."),
                        $"{item.Kind} incomplete from {source.BaseUrl}: {ModelCatalog.DescribeMissing(item)}");
                }
                // ⚠️ TaskCanceledException 继承自 OperationCanceledException，HttpClient 超时抛的正是它。
                // 只有我们自己的 token 真被取消才算"用户取消"，否则当普通失败继续换源。
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Error(
                        nameof(ModelDownloadService),
                        exception,
                        $"File source {source.BaseUrl} failed for {item.Kind}, attempt {attempt}");
                    if (IsPermanentFailure(exception)) break;   // 404/DNS 重试第二次没有意义，直接换线路
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
        // 归档源接在散装源之后编号，用户看到的线路序号是连续的
        var sourceIndex = item.FileSources.Count;
        var sourceTotal = item.FileSources.Count + item.ArchiveUrls.Count;
        foreach (var address in item.ArchiveUrls)
        {
            sourceIndex++;
            if (sourceTotal > 1)
                Emit(target, item.Kind,
                     SwitchingSourceText(item.Kind, sourceIndex, sourceTotal, address),
                     completedMb / totalMb, true);
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
                        (fraction, detail) => Emit(
                            target,
                            item.Kind,
                            DownloadingText(item.Kind, detail),
                            Math.Clamp((completedMb + item.SizeMb * fraction) / totalMb, 0, 1)),
                        token,
                        preserveCompletedFile: true);
                    Emit(target, item.Kind, InstallingText(item.Kind), completedMb / totalMb, true);
                    if (await InstallArchiveAsync(local, item, token)) return true;
                }
                // ⚠️ TaskCanceledException 继承自 OperationCanceledException，HttpClient 超时抛的正是它。
                // 只有我们自己的 token 真被取消才算"用户取消"，否则当普通失败继续换源。
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Error(
                        nameof(ModelDownloadService),
                        exception,
                        $"Archive source {address} failed for {item.Kind}, attempt {attempt}");
                    if (IsPermanentFailure(exception)) break;
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
        Action<double, string> onProgress,   // (进度, 速度/剩余时间文案)
        CancellationToken token,
        bool preserveCompletedFile = false)
    {
        var partial = destination + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        using var response = await SendWithProxyFallbackAsync(uri, existing, token);

        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var remoteLength = response.Content.Headers.ContentRange?.Length;
            if (remoteLength is > 0 && existing == remoteLength)
            {
                ValidateDownloadedFile(partial, destination);
                File.Move(partial, destination, overwrite: true);
                ValidateDownloadedFile(destination, destination);
                onProgress(1, string.Empty);
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
        var totalTimer = Stopwatch.StartNew();
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
            {
                // 大文件（Qwen 838MB / GGUF 4.7GB）光有百分比，用户看不出到底还动不动。
                // 给出实时速度和剩余时间，"卡住了"和"在下但慢"才区分得开。
                var elapsed = totalTimer.Elapsed.TotalSeconds;
                var detail = string.Empty;
                if (elapsed > 1.5)
                {
                    var bytesPerSecond = (received - existing) / elapsed;
                    if (bytesPerSecond > 1)
                    {
                        var remaining = TimeSpan.FromSeconds((expected - received) / bytesPerSecond);
                        detail = $"{FormatSpeed(bytesPerSecond)} · {FormatRemaining(remaining)}";
                    }
                }
                onProgress(Math.Clamp((double)received / expected, 0, 0.99), detail);
            }

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
        onProgress(1, string.Empty);
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

    /// 带速度/剩余时间的下载文案：大文件下"还动不动"比百分比更重要
    private static string DownloadingText(ModelComponentKind kind, string detail) =>
        string.IsNullOrEmpty(detail) ? DownloadingText(kind) : $"{DownloadingText(kind)} · {detail}";

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:F1} MB/s"
            : $"{bytesPerSecond / 1024:F0} KB/s";

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalSeconds < 1 || remaining.TotalHours > 24)
            return T("正在估算", "estimating");
        if (remaining.TotalMinutes < 1)
            return T($"约 {remaining.Seconds} 秒", $"~{remaining.Seconds}s left");
        if (remaining.TotalHours < 1)
            return T($"约 {remaining.Minutes} 分钟", $"~{remaining.Minutes} min left");
        return T($"约 {(int)remaining.TotalHours} 小时 {remaining.Minutes} 分",
                 $"~{(int)remaining.TotalHours}h {remaining.Minutes}m left");
    }

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

    /// 换源时告诉用户"第几条线路 / 来源是谁"，让切换看起来是设计好的流程，而不是在乱撞
    private static string SwitchingSourceText(ModelComponentKind kind, int index, int total, string baseUrl) =>
        T($"正在切换线路 {index}/{total}（{SourceLabel(baseUrl)}）· {DisplayName(kind)}",
          $"Trying source {index}/{total} ({SourceLabel(baseUrl)}) · {DisplayName(kind)}");

    private static string SourceLabel(string url)
    {
        if (url.Contains("modelscope", StringComparison.OrdinalIgnoreCase)) return "ModelScope";
        if (url.Contains("hf-mirror", StringComparison.OrdinalIgnoreCase)) return T("HF 镜像", "HF mirror");
        if (url.Contains("huggingface", StringComparison.OrdinalIgnoreCase)) return "Hugging Face";
        if (url.Contains("ghfast", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("gh-proxy", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("ghproxy", StringComparison.OrdinalIgnoreCase)) return T("GitHub 镜像", "GitHub mirror");
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return T("GitHub 直连", "GitHub direct");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    /// 永久性错误（404、DNS 查不到）重试第二次纯属浪费用户的时间，直接换下一条线路
    private static bool IsPermanentFailure(Exception exception) =>
        exception is HttpRequestException http &&
        (http.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone ||
         http.InnerException is System.Net.Sockets.SocketException
         {
             SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound
                           or System.Net.Sockets.SocketError.NoData
         });

    /// 最终失败文案：说清原因 + 已下多少（证明再点是续传）+ 下一步
    private static string FailureText(IReadOnlyList<ModelItem> items, ModelItem? failingItem, Exception exception)
    {
        var component = failingItem is null
            ? string.Empty
            : T($"（{DisplayName(failingItem.Kind)}）", $" ({DisplayName(failingItem.Kind)})");
        var reason = exception switch
        {
            InvalidDataException => T("文件校验未通过", "downloaded files failed validation"),
            TimeoutException or TaskCanceledException => T("连接超时", "the connection timed out"),
            IOException io when io.Message.Contains("30 KB/s") => T("所有线路速度都过慢", "every source was too slow"),
            HttpRequestException { StatusCode: not null } http => T($"服务器返回 {(int)http.StatusCode!}", $"server returned {(int)http.StatusCode!}"),
            HttpRequestException => T("连不上下载服务器", "could not reach the download server"),
            IOException => T("网络中断", "the network dropped"),
            _ => T("下载出错", "the download failed"),
        };

        var downloadedMb = items.Sum(DownloadedMb);
        var totalMb = items.Sum(item => item.SizeMb);
        var progress = downloadedMb >= 1
            ? T($"已下载 {downloadedMb:F0}/{totalMb:F0} MB，再次点击可从断点继续。",
                $"{downloadedMb:F0}/{totalMb:F0} MB already downloaded — click again to resume.")
            : T("再次点击可重试；若一直失败，请检查网络或代理设置。",
                "Click again to retry; if it keeps failing, check your network or proxy settings.");

        return T($"下载失败{component}（{reason}）。{progress}", $"Download failed{component} ({reason}). {progress}");
    }

    /// 已落盘的字节数（含 .part 断点文件）——这是"再点一次不是从头开始"的证据
    private static double DownloadedMb(ModelItem item)
    {
        try
        {
            if (!Directory.Exists(item.DirectoryPath)) return 0;
            var bytes = new DirectoryInfo(item.DirectoryPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
            return bytes / 1024d / 1024d;
        }
        catch
        {
            return 0;
        }
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
        _directClient.Dispose();
    }

    private sealed record DownloadOperation(CancellationTokenSource Cancellation, double ReservedMb);
}
