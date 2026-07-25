using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Commander;

public sealed class PipelineDiagnostics
{
    public static PipelineDiagnostics Shared { get; } = new();
    private const int MaxSessions = 50;

    private readonly object _sync = new();
    private readonly Dictionary<string, ActiveSession> _sessions = [];
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool IsEnabled
    {
        get
        {
            var environment = Environment.GetEnvironmentVariable("AI_OPERATOR_TRACE")?.ToLowerInvariant();
            return environment is "1" or "true" or "yes" || Settings.Instance.PipelineDiagnosticsEnabled;
        }
    }

    public static string RootPath =>
        Environment.GetEnvironmentVariable("AI_OPERATOR_TRACE_DIR") is { Length: > 0 } custom
            ? custom
            : AppPaths.DiagnosticsRoot;

    public string? Begin(
        AsrModelKind model,
        string modelDirectory,
        bool cleanFillers,
        bool useLlm,
        string? targetAppName,
        string? targetAppPath)
    {
        if (!IsEnabled) return null;
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(RootPath);
                var now = DateTimeOffset.Now;
                var sessionId = $"{now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..28];
                var directory = Path.Combine(RootPath, sessionId);
                Directory.CreateDirectory(directory);
                PruneSessions(directory);

                var trace = new DiagnosticTrace
                {
                    SchemaVersion = 1,
                    SessionId = sessionId,
                    StartedAt = now,
                    AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
                    Model = model.ToString(),
                    ModelDirectory = Path.GetFileName(modelDirectory),
                    Settings = new DiagnosticSettings(cleanFillers, useLlm),
                    TargetAppName = targetAppName,
                    TargetAppPath = targetAppPath,
                    Stages =
                    [
                        new DiagnosticStage { Name = "session.started", AtMs = 0 }
                    ],
                };
                var session = new ActiveSession(trace, directory, Stopwatch.StartNew());
                _sessions[sessionId] = session;
                Persist(session);
                AppLog.Info(nameof(PipelineDiagnostics), $"Session {sessionId}: {directory}");
                return sessionId;
            }
            catch (Exception exception)
            {
                AppLog.Error(nameof(PipelineDiagnostics), exception, "Cannot start diagnostic session");
                return null;
            }
        }
    }

    public void Record(
        string? sessionId,
        string stage,
        string? text = null,
        double? durationMs = null,
        IReadOnlyDictionary<string, double>? metrics = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (sessionId is null) return;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;
            session.Trace.Stages.Add(new DiagnosticStage
            {
                Name = stage,
                AtMs = session.Stopwatch.Elapsed.TotalMilliseconds,
                DurationMs = durationMs,
                Text = text,
                TextCharacters = text?.Length,
                Metrics = metrics is null ? null : new Dictionary<string, double>(metrics),
                Details = details is null ? null : new Dictionary<string, string>(details),
            });
            Persist(session);
        }
    }

    public void RecordAudio(string? sessionId, AudioCapture capture)
    {
        if (sessionId is null) return;
        string directory;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;
            directory = session.Directory;
        }

        var audioPath = Path.Combine(directory, "input.wav");
        var saved = false;
        try
        {
            WritePcm16Wav(audioPath, capture.Samples, capture.SampleRate);
            saved = true;
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(PipelineDiagnostics), exception, "Cannot save diagnostic audio");
        }

        var peak = 0f;
        var sum = 0d;
        var sumSquares = 0d;
        var nearZero = 0;
        var clipped = 0;
        foreach (var value in capture.Samples)
        {
            var absolute = Math.Abs(value);
            peak = Math.Max(peak, absolute);
            sum += value;
            sumSquares += value * value;
            if (absolute < 0.000_001f) nearZero++;
            if (absolute >= 0.999f) clipped++;
        }
        var count = Math.Max(capture.Samples.Length, 1);
        Record(
            sessionId,
            "audio.captured_16k",
            metrics: new Dictionary<string, double>
            {
                ["sampleRate"] = capture.SampleRate,
                ["sampleCount"] = capture.Samples.Length,
                ["durationSeconds"] = (double)capture.Samples.Length / capture.SampleRate,
                ["nativeSampleRate"] = capture.NativeSampleRate,
                ["nativeSampleCount"] = capture.NativeSampleCount,
                ["nativeDurationSeconds"] = capture.NativeSampleRate > 0
                    ? (double)capture.NativeSampleCount / capture.NativeSampleRate
                    : 0,
                ["channelCount"] = capture.ChannelCount,
                ["peak"] = peak,
                ["rms"] = Math.Sqrt(sumSquares / count),
                ["dcOffset"] = sum / count,
                ["nearZeroRatio"] = (double)nearZero / count,
                ["clippedRatio"] = (double)clipped / count,
            },
            details: new Dictionary<string, string> { ["audioFile"] = saved ? "input.wav" : "write_failed" });
    }

    public void Finish(
        string? sessionId,
        string outcome,
        string? finalText = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (sessionId is null) return;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;
            session.Trace.CompletedAt = DateTimeOffset.Now;
            session.Trace.Outcome = outcome;
            session.Trace.Stages.Add(new DiagnosticStage
            {
                Name = "session.finished",
                AtMs = session.Stopwatch.Elapsed.TotalMilliseconds,
                DurationMs = session.Stopwatch.Elapsed.TotalMilliseconds,
                Text = finalText,
                TextCharacters = finalText?.Length,
                Details = details is null ? null : new Dictionary<string, string>(details),
            });
            Persist(session);
            _sessions.Remove(sessionId);
        }
    }

    private void Persist(ActiveSession session)
    {
        try
        {
            var target = Path.Combine(session.Directory, "trace.json");
            var temporary = target + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(session.Trace, _jsonOptions));
            File.Move(temporary, target, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(PipelineDiagnostics), exception, "Cannot persist trace");
        }
    }

    private static void PruneSessions(string current)
    {
        try
        {
            var directories = new DirectoryInfo(RootPath).EnumerateDirectories()
                .Where(directory => !string.Equals(directory.FullName, current, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .Skip(MaxSessions - 1);
            foreach (var directory in directories) directory.Delete(recursive: true);
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(PipelineDiagnostics), exception, "Cannot prune old traces");
        }
    }

    private static void WritePcm16Wav(string path, IReadOnlyList<float> samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = samples.Count * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
            writer.Write((short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
    }

    private sealed record ActiveSession(DiagnosticTrace Trace, string Directory, Stopwatch Stopwatch);

    private sealed class DiagnosticTrace
    {
        public int SchemaVersion { get; set; }
        public required string SessionId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public required string AppVersion { get; set; }
        public required string Model { get; set; }
        public required string ModelDirectory { get; set; }
        public required DiagnosticSettings Settings { get; set; }
        public string? TargetAppName { get; set; }
        public string? TargetAppPath { get; set; }
        public string? Outcome { get; set; }
        public required List<DiagnosticStage> Stages { get; set; }
    }

    private sealed record DiagnosticSettings(bool CleanFillers, bool UseLlm);

    private sealed class DiagnosticStage
    {
        public required string Name { get; set; }
        public double AtMs { get; set; }
        public double? DurationMs { get; set; }
        public string? Text { get; set; }
        public int? TextCharacters { get; set; }
        public Dictionary<string, double>? Metrics { get; set; }
        public Dictionary<string, string>? Details { get; set; }
    }
}
