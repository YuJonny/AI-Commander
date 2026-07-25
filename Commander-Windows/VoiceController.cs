using System.Diagnostics;
using System.IO;

namespace Commander;

public enum VoiceStateKind
{
    Ready,
    Listening,
    Transcribing,
    Enhancing,
    Inserted,
    NoAudio,
    NoTranscript,
    ModelMissing,
    Error,
}

public sealed record VoiceState(VoiceStateKind Kind, string Text, string? RawText = null);

public sealed class VoiceController : IDisposable
{
    public const int LlmInputMaxCharacters = 1000;
    public static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(20);

    private readonly object _stateLock = new();
    private readonly object _engineLock = new();
    private readonly AudioRecorder _recorder = new();
    private VoiceEngine? _engine;
    private AsrModelKind? _engineModel;
    private Task<VoiceEngine>? _preloadTask;
    private Timer? _autoStopTimer;
    private Timer? _engineReleaseTimer;
    private CancellationTokenSource _lifetime = new();
    private DateTime _lastToggleUtc = DateTime.MinValue;
    private bool _isRecording;
    private bool _isProcessing;

    private RecordingContext? _recordingContext;

    public event EventHandler<VoiceState>? StateChanged;

    public AsrModelKind SelectedModel { get; set; } = AsrModelKind.Qwen;
    public bool CleanFillers { get; set; } = true;
    public bool UseLlm { get; set; }

    public bool IsRecording
    {
        get { lock (_stateLock) return _isRecording; }
    }

    public bool IsProcessing
    {
        get { lock (_stateLock) return _isProcessing; }
    }

    public void Toggle()
    {
        bool stop;
        lock (_stateLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastToggleUtc < TimeSpan.FromSeconds(0.5)) return;
            _lastToggleUtc = now;
            if (_isProcessing) return;
            stop = _isRecording;
        }

        if (stop) StopAndTranscribe();
        else StartRecording();
    }

    private void StartRecording()
    {
        var model = SelectedModel;
        var modelDirectory = ModelCatalog.ResolveAsrDirectory(model);
        if (modelDirectory is null)
        {
            Emit(new VoiceState(
                VoiceStateKind.ModelMissing,
                ModelDownloadService.Shared.IsDownloadingAsr(model)
                    ? I18n.Get("voice.model.downloading")
                    : I18n.Get("voice.model.missing")));
            return;
        }

        var target = TextInserter.CaptureForegroundTarget();
        try
        {
            _recorder.Start();
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(VoiceController), exception, "Microphone capture failed");
            Emit(new VoiceState(VoiceStateKind.Error, I18n.Get("voice.microphone.error")));
            return;
        }

        var context = new RecordingContext(
            model,
            modelDirectory,
            CleanFillers,
            UseLlm,
            target,
            PipelineDiagnostics.Shared.Begin(
                model,
                modelDirectory,
                CleanFillers,
                UseLlm,
                target.ProcessName,
                target.ProcessPath));

        lock (_stateLock)
        {
            _recordingContext = context;
            _isRecording = true;
            _autoStopTimer?.Dispose();
            _autoStopTimer = new Timer(
                _ => AutoStop(),
                null,
                MaxRecordingDuration,
                Timeout.InfiniteTimeSpan);
        }
        PipelineDiagnostics.Shared.Record(
            context.DiagnosticsSessionId,
            "audio.capture_started",
            details: new Dictionary<string, string> { ["selectedModel"] = model.ToString() });

        _preloadTask = Task.Run(() => GetOrCreateEngine(model), _lifetime.Token);
        if (context.UseLlm && ModelCatalog.IsLlmReady)
        {
            PipelineDiagnostics.Shared.Record(context.DiagnosticsSessionId, "llm.warmup_requested");
            _ = Task.Run(async () =>
            {
                try
                {
                    await LlamaRunner.Shared.EnsureReadyAsync(TimeSpan.FromSeconds(12), _lifetime.Token);
                }
                catch { }
            });
        }
        Emit(new VoiceState(VoiceStateKind.Listening, I18n.Get("voice.listening")));
    }

    private void AutoStop()
    {
        lock (_stateLock)
        {
            if (!_isRecording) return;
        }
        PipelineDiagnostics.Shared.Record(
            _recordingContext?.DiagnosticsSessionId,
            "audio.auto_stop_limit",
            metrics: new Dictionary<string, double>
            {
                ["limitSeconds"] = MaxRecordingDuration.TotalSeconds,
            });
        StopAndTranscribe();
    }

    private void StopAndTranscribe()
    {
        RecordingContext? context;
        lock (_stateLock)
        {
            if (!_isRecording || _recordingContext is null) return;
            _isRecording = false;
            _isProcessing = true;
            _autoStopTimer?.Dispose();
            _autoStopTimer = null;
            context = _recordingContext;
            _recordingContext = null;
        }

        AudioCapture capture;
        try
        {
            capture = _recorder.Stop();
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(VoiceController), exception, "Microphone stop failed");
            PipelineDiagnostics.Shared.Finish(context.DiagnosticsSessionId, "capture_stop_failed");
            FinishProcessing(new VoiceState(VoiceStateKind.Error, I18n.Get("voice.microphone.error")));
            return;
        }

        Emit(new VoiceState(VoiceStateKind.Transcribing, I18n.Get("voice.transcribing")));
        _ = ProcessCaptureAsync(capture, context, _lifetime.Token);
    }

    private async Task ProcessCaptureAsync(
        AudioCapture capture,
        RecordingContext context,
        CancellationToken token)
    {
        var diagnostics = PipelineDiagnostics.Shared;
        try
        {
            diagnostics.RecordAudio(context.DiagnosticsSessionId, capture);
            var (peak, rms) = SignalMetrics(capture.Samples);
            var duration = capture.Samples.Length / 16_000d;
            var signalRejected = capture.Samples.Length < 1600 || (peak < 0.008f && rms < 0.0015f);
            diagnostics.Record(
                context.DiagnosticsSessionId,
                "audio.signal_gate",
                metrics: new Dictionary<string, double>
                {
                    ["samples"] = capture.Samples.Length,
                    ["durationSeconds"] = duration,
                    ["peak"] = peak,
                    ["rms"] = rms,
                    ["minimumSamples"] = 1600,
                    ["peakThreshold"] = 0.008,
                    ["rmsThreshold"] = 0.0015,
                    ["rejected"] = signalRejected ? 1 : 0,
                });
            if (signalRejected)
            {
                diagnostics.Finish(context.DiagnosticsSessionId, "no_audio");
                FinishProcessing(new VoiceState(VoiceStateKind.NoAudio, I18n.Get("voice.no_audio")));
                return;
            }

            var engineTimer = Stopwatch.StartNew();
            VoiceEngine engine;
            if (_preloadTask is not null)
                engine = await _preloadTask.WaitAsync(token);
            else
                engine = await Task.Run(() => GetOrCreateEngine(context.Model), token);
            diagnostics.Record(
                context.DiagnosticsSessionId,
                "engine.decode_ready",
                durationMs: engineTimer.Elapsed.TotalMilliseconds,
                details: new Dictionary<string, string>
                {
                    ["modelDirectory"] = Path.GetFileName(context.ModelDirectory),
                });

            var raw = await Task.Run(
                () => engine.TranscribeRaw(capture.Samples, context.DiagnosticsSessionId),
                token);
            if (string.IsNullOrWhiteSpace(raw))
            {
                diagnostics.Finish(context.DiagnosticsSessionId, "no_transcript");
                ScheduleEngineRelease();
                FinishProcessing(new VoiceState(VoiceStateKind.NoTranscript, I18n.Get("voice.no_transcript")));
                return;
            }

            diagnostics.Record(
                context.DiagnosticsSessionId,
                "pipeline.route",
                raw,
                details: new Dictionary<string, string>
                {
                    ["cleanFillers"] = context.CleanFillers.ToString().ToLowerInvariant(),
                    ["useLlm"] = context.UseLlm.ToString().ToLowerInvariant(),
                });

            var finalText = await BuildFinalTextAsync(engine, raw, context, token);
            finalText = finalText.Trim();
            diagnostics.Record(context.DiagnosticsSessionId, "pipeline.final_text", finalText);
            if (finalText.Length == 0)
            {
                diagnostics.Finish(context.DiagnosticsSessionId, "empty_final_text");
                ScheduleEngineRelease();
                FinishProcessing(new VoiceState(VoiceStateKind.NoTranscript, I18n.Get("voice.no_transcript")));
                return;
            }

            diagnostics.Record(
                context.DiagnosticsSessionId,
                "insertion.requested",
                finalText,
                metrics: new Dictionary<string, double>
                {
                    ["utf16Units"] = finalText.Length,
                    ["inputEvents"] = finalText.Length * 2,
                },
                details: new Dictionary<string, string>
                {
                    ["targetAppName"] = context.Target.ProcessName ?? "unknown",
                    ["targetAppPath"] = context.Target.ProcessPath ?? "unknown",
                });
            TextInserter.Insert(finalText, context.Target);
            diagnostics.Record(context.DiagnosticsSessionId, "insertion.events_posted", finalText);
            diagnostics.Finish(context.DiagnosticsSessionId, "insertion_events_posted", finalText);
            ScheduleEngineRelease();
            FinishProcessing(new VoiceState(VoiceStateKind.Inserted, finalText, raw));
        }
        catch (OperationCanceledException)
        {
            diagnostics.Finish(context.DiagnosticsSessionId, "cancelled");
            FinishProcessing(new VoiceState(VoiceStateKind.Error, I18n.Get("voice.cancelled")));
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(VoiceController), exception, "Voice pipeline failed");
            diagnostics.Record(
                context.DiagnosticsSessionId,
                "pipeline.failure",
                details: new Dictionary<string, string>
                {
                    ["exception"] = exception.GetType().Name,
                    ["message"] = exception.Message,
                });
            diagnostics.Finish(context.DiagnosticsSessionId, "error");
            ScheduleEngineRelease();
            FinishProcessing(new VoiceState(VoiceStateKind.Error, I18n.Get("voice.pipeline.error")));
        }
    }

    private async Task<string> BuildFinalTextAsync(
        VoiceEngine engine,
        string raw,
        RecordingContext context,
        CancellationToken token)
    {
        var diagnostics = PipelineDiagnostics.Shared;
        if (!context.UseLlm)
        {
            return await Task.Run(
                () => engine.Polish(raw, context.CleanFillers, context.DiagnosticsSessionId),
                token);
        }

        var beforeLlm = context.CleanFillers
            ? Rules.Clean(raw, onStep: (step, value) =>
                diagnostics.Record(context.DiagnosticsSessionId, $"rules.{step}", value))
            : raw;
        if (!context.CleanFillers)
            diagnostics.Record(context.DiagnosticsSessionId, "rules.bypassed", beforeLlm);

        if (beforeLlm.Length > LlmInputMaxCharacters)
        {
            diagnostics.Record(
                context.DiagnosticsSessionId,
                "llm.skipped_input_too_long",
                metrics: new Dictionary<string, double>
                {
                    ["characters"] = beforeLlm.Length,
                    ["threshold"] = LlmInputMaxCharacters,
                },
                details: new Dictionary<string, string> { ["fallback"] = "regular_pipeline" });
            return engine.Polish(raw, context.CleanFillers, context.DiagnosticsSessionId, "fallback");
        }

        diagnostics.Record(context.DiagnosticsSessionId, "punctuation.delegated_to_llm", beforeLlm);
        Emit(new VoiceState(VoiceStateKind.Enhancing, I18n.Get("voice.enhancing")));
        var enhanced = await LlmEnhancer.EnhanceAsync(beforeLlm, context.DiagnosticsSessionId, token);
        if (!string.IsNullOrWhiteSpace(enhanced))
        {
            diagnostics.Record(context.DiagnosticsSessionId, "pipeline.llm_selected", enhanced);
            return enhanced;
        }

        diagnostics.Record(
            context.DiagnosticsSessionId,
            "pipeline.llm_fallback",
            raw,
            details: new Dictionary<string, string> { ["fallback"] = "regular_pipeline" });
        return engine.Polish(raw, context.CleanFillers, context.DiagnosticsSessionId, "fallback");
    }

    private VoiceEngine GetOrCreateEngine(AsrModelKind model)
    {
        lock (_engineLock)
        {
            _engineReleaseTimer?.Dispose();
            _engineReleaseTimer = null;
            if (_engine is not null && _engineModel == model) return _engine;
            _engine?.Dispose();
            _engine = new VoiceEngine(model);
            _engineModel = model;
            return _engine;
        }
    }

    private void ScheduleEngineRelease()
    {
        lock (_engineLock)
        {
            _engineReleaseTimer?.Dispose();
            _engineReleaseTimer = new Timer(_ => ReleaseEngine(), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        }
    }

    private void ReleaseEngine()
    {
        lock (_engineLock)
        {
            _engineReleaseTimer?.Dispose();
            _engineReleaseTimer = null;
            _engine?.Dispose();
            _engine = null;
            _engineModel = null;
            _preloadTask = null;
        }
    }

    private static (float Peak, float Rms) SignalMetrics(IReadOnlyList<float> samples)
    {
        var peak = 0f;
        var sumSquares = 0d;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }
        return (peak, samples.Count == 0 ? 0 : (float)Math.Sqrt(sumSquares / samples.Count));
    }

    private void FinishProcessing(VoiceState state)
    {
        lock (_stateLock) _isProcessing = false;
        Emit(state);
    }

    private void Emit(VoiceState state) => StateChanged?.Invoke(this, state);

    public void Dispose()
    {
        _lifetime.Cancel();
        lock (_stateLock)
        {
            _autoStopTimer?.Dispose();
            _autoStopTimer = null;
        }
        _recorder.Dispose();
        ReleaseEngine();
        LlamaRunner.Shared.Stop();
        _lifetime.Dispose();
    }

    private sealed record RecordingContext(
        AsrModelKind Model,
        string ModelDirectory,
        bool CleanFillers,
        bool UseLlm,
        ForegroundTarget Target,
        string? DiagnosticsSessionId);
}
