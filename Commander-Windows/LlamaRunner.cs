using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Commander;

public sealed class LlamaRunner : IDisposable
{
    public static LlamaRunner Shared { get; } = new();
    public static readonly Uri BaseUri = new("http://127.0.0.1:43817/");

    private readonly object _sync = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SafeFileHandle? _jobHandle;
    private Process? _process;
    private Timer? _idleTimer;

    private LlamaRunner()
    {
        _jobHandle = CreateKillOnCloseJob();
    }

    public async Task<bool> EnsureReadyAsync(TimeSpan timeout, CancellationToken token)
    {
        ScheduleIdleStop();
        if (!ModelCatalog.IsLlmReady) return false;
        if (IsOwnedProcessRunning() && await HealthCheckAsync(token)) return true;

        await _startGate.WaitAsync(token);
        try
        {
            if (IsOwnedProcessRunning() && await HealthCheckAsync(token)) return true;

            StopOwnedProcess();
            TerminateResidualServers();
            StartProcess();
        }
        finally
        {
            _startGate.Release();
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (await HealthCheckAsync(token)) return true;
            await Task.Delay(300, token);
        }
        return false;
    }

    private void StartProcess()
    {
        var executable = Path.Combine(ModelCatalog.LlamaRuntime.DirectoryPath, "llama-server.exe");
        var model = Path.Combine(ModelCatalog.Llm.DirectoryPath, "Qwen3-8B-Q4_K_M.gguf");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = ModelCatalog.LlamaRuntime.DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(model);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("43817");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("4096");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(Math.Clamp(Environment.ProcessorCount - 1, 2, 12).ToString());
        startInfo.ArgumentList.Add("--prio");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--cache-ram");
        startInfo.ArgumentList.Add("256");
        startInfo.ArgumentList.Add("--jinja");
        startInfo.ArgumentList.Add("--no-webui");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start llama-server.");
        try
        {
            if (_jobHandle is not null && !AssignProcessToJobObject(_jobHandle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(LlamaRunner), exception, "Could not attach llama-server lifecycle guard");
        }
        try
        {
            process.PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(LlamaRunner), exception, "Could not raise llama-server priority");
        }
        lock (_sync) _process = process;
        AppLog.Info(nameof(LlamaRunner), $"Started llama-server pid={process.Id}");
    }

    private bool IsOwnedProcessRunning()
    {
        lock (_sync)
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    private async Task<bool> HealthCheckAsync(CancellationToken token)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(BaseUri, "health"), token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void ScheduleIdleStop()
    {
        lock (_sync)
        {
            _idleTimer?.Dispose();
            _idleTimer = new Timer(_ => Stop(), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
        StopOwnedProcess();
        TerminateResidualServers();
    }

    private void StopOwnedProcess()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
        }
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(LlamaRunner), exception, "Cannot stop llama-server");
        }
    }

    private static void TerminateResidualServers()
    {
        var expectedPath = Path.GetFullPath(
            Path.Combine(ModelCatalog.LlamaRuntime.DirectoryPath, "llama-server.exe"));
        foreach (var process in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                var actualPath = process.MainModule?.FileName;
                if (actualPath is null || !string.Equals(
                        Path.GetFullPath(actualPath),
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                AppLog.Info(nameof(LlamaRunner), $"Stopping residual llama-server pid={process.Id}");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (Exception exception)
            {
                AppLog.Error(nameof(LlamaRunner), exception, "Cannot inspect residual llama-server");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static SafeFileHandle? CreateKillOnCloseJob()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) return null;

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, pointer, false);
            if (SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, pointer, (uint)size))
                return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        handle.Dispose();
        return null;
    }

    public void Dispose()
    {
        Stop();
        _jobHandle?.Dispose();
        _httpClient.Dispose();
        _startGate.Dispose();
    }

    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
