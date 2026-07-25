using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SherpaOnnx;

namespace Commander;

public sealed class VoiceEngine : IDisposable
{
    private readonly OfflineRecognizer _recognizer;
    private readonly OfflinePunctuation? _punctuation;
    private readonly VoiceActivityDetector? _vad;

    public VoiceEngine(AsrModelKind model, int? numberOfThreads = null)
    {
        Model = model;
        var modelDirectory = ModelCatalog.ResolveAsrDirectory(model)
            ?? throw new DirectoryNotFoundException($"The selected {model} model is not ready.");
        ModelDirectory = modelDirectory;
        var threads = numberOfThreads ?? Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

        if (model == AsrModelKind.Qwen)
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16_000;
            config.FeatConfig.FeatureDim = 128;
            config.ModelConfig.NumThreads = threads;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(modelDirectory, "conv_frontend.onnx");
            config.ModelConfig.Qwen3Asr.Encoder = Path.Combine(modelDirectory, "encoder.int8.onnx");
            config.ModelConfig.Qwen3Asr.Decoder = Path.Combine(modelDirectory, "decoder.int8.onnx");
            config.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(modelDirectory, "tokenizer");
            config.ModelConfig.Qwen3Asr.MaxNewTokens = 512;
            _recognizer = new OfflineRecognizer(config);

            var vadConfig = new VadModelConfig();
            vadConfig.SampleRate = 16_000;
            vadConfig.NumThreads = 1;
            vadConfig.Provider = "cpu";
            vadConfig.SileroVad.Model = Path.Combine(ModelCatalog.SileroVad.DirectoryPath, "silero_vad.onnx");
            vadConfig.SileroVad.Threshold = 0.2f;
            vadConfig.SileroVad.MinSilenceDuration = 1.5f;
            vadConfig.SileroVad.MinSpeechDuration = 0.2f;
            vadConfig.SileroVad.WindowSize = 512;
            vadConfig.SileroVad.MaxSpeechDuration = 20;
            _vad = new VoiceActivityDetector(vadConfig, 30);
        }
        else
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16_000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.NumThreads = threads;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Tokens = Path.Combine(modelDirectory, "tokens.txt");
            config.ModelConfig.SenseVoice.Model = Path.Combine(modelDirectory, "model.int8.onnx");
            config.ModelConfig.SenseVoice.Language = "auto";
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            _recognizer = new OfflineRecognizer(config);

            var punctuationConfig = new OfflinePunctuationConfig();
            punctuationConfig.Model.CtTransformer = Path.Combine(
                ModelCatalog.Punctuation.DirectoryPath,
                "model.int8.onnx");
            punctuationConfig.Model.NumThreads = 1;
            punctuationConfig.Model.Provider = "cpu";
            _punctuation = new OfflinePunctuation(punctuationConfig);
        }
    }

    public AsrModelKind Model { get; }
    public string ModelDirectory { get; }

    public string TranscribeRaw(float[] samples, string? diagnosticsSessionId = null)
    {
        var diagnostics = PipelineDiagnostics.Shared;
        var chunks = Model == AsrModelKind.Qwen ? SplitQwenSpeech(samples) : [samples];
        diagnostics.Record(
            diagnosticsSessionId,
            Model == AsrModelKind.Qwen ? "vad.summary" : "vad.bypassed",
            metrics: new Dictionary<string, double>
            {
                ["inputSamples"] = samples.Length,
                ["inputDurationSeconds"] = samples.Length / 16_000d,
                ["segmentCount"] = chunks.Count,
            },
            details: new Dictionary<string, string>
            {
                ["engine"] = Model.ToString(),
                ["vadAvailable"] = (_vad is not null).ToString().ToLowerInvariant(),
            });

        var parts = new List<string>();
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var number = (index + 1).ToString("000");
            diagnostics.Record(
                diagnosticsSessionId,
                $"vad.segment.{number}",
                metrics: new Dictionary<string, double>
                {
                    ["samples"] = chunk.Length,
                    ["durationSeconds"] = chunk.Length / 16_000d,
                });

            var timer = Stopwatch.StartNew();
            var text = Decode(chunk).Trim();
            diagnostics.Record(
                diagnosticsSessionId,
                $"asr.segment.{number}",
                text,
                timer.Elapsed.TotalMilliseconds,
                new Dictionary<string, double>
                {
                    ["samples"] = chunk.Length,
                    ["durationSeconds"] = chunk.Length / 16_000d,
                });
            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
        }

        var joined = JoinSegments(parts);
        diagnostics.Record(
            diagnosticsSessionId,
            "asr.joined_raw",
            joined,
            metrics: new Dictionary<string, double>
            {
                ["segmentCount"] = chunks.Count,
                ["recognizedSegmentCount"] = parts.Count,
            });
        var normalized = Hotwords.Correct(joined);
        diagnostics.Record(
            diagnosticsSessionId,
            "terms.normalized",
            normalized,
            metrics: new Dictionary<string, double>
            {
                ["changed"] = normalized == joined ? 0 : 1,
                ["characterDelta"] = normalized.Length - joined.Length,
            });
        return normalized;
    }

    public string Polish(
        string text,
        bool cleanFillers,
        string? diagnosticsSessionId = null,
        string stagePrefix = "")
    {
        var diagnostics = PipelineDiagnostics.Shared;
        var prefix = string.IsNullOrEmpty(stagePrefix) ? "" : stagePrefix + ".";
        var result = text;
        if (cleanFillers)
        {
            var timer = Stopwatch.StartNew();
            result = Rules.Clean(result, onStep: (step, value) =>
                diagnostics.Record(diagnosticsSessionId, $"{prefix}rules.{step}", value));
            diagnostics.Record(
                diagnosticsSessionId,
                $"{prefix}rules.completed",
                result,
                timer.Elapsed.TotalMilliseconds);
        }
        else
        {
            diagnostics.Record(diagnosticsSessionId, $"{prefix}rules.bypassed", result);
        }

        if (_punctuation is null)
        {
            diagnostics.Record(
                diagnosticsSessionId,
                $"{prefix}punctuation.bypassed",
                result,
                details: new Dictionary<string, string>
                {
                    ["reason"] = Model == AsrModelKind.Qwen
                        ? "qwen_has_native_punctuation"
                        : "punctuation_model_unavailable",
                });
            return result;
        }

        diagnostics.Record(diagnosticsSessionId, $"{prefix}punctuation.input", result);
        var stripped = StripPunctuation(result);
        diagnostics.Record(diagnosticsSessionId, $"{prefix}punctuation.stripped", stripped);
        var punctuationTimer = Stopwatch.StartNew();
        var modelOutput = _punctuation.AddPunct(stripped);
        diagnostics.Record(
            diagnosticsSessionId,
            $"{prefix}punctuation.model_output",
            modelOutput,
            punctuationTimer.Elapsed.TotalMilliseconds);
        result = DedupePunctuation(modelOutput);
        diagnostics.Record(diagnosticsSessionId, $"{prefix}punctuation.output", result);
        return result;
    }

    private string Decode(float[] samples)
    {
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(16_000, samples);
        _recognizer.Decode(stream);
        return stream.Result.Text;
    }

    private IReadOnlyList<float[]> SplitQwenSpeech(float[] samples)
    {
        if (_vad is null || samples.Length == 0) return [samples];
        _vad.Reset();
        var segments = new List<float[]>();

        void Drain()
        {
            while (!_vad.IsEmpty())
            {
                var segment = _vad.Front();
                if (segment.Samples.Length > 0) segments.Add(segment.Samples.ToArray());
                _vad.Pop();
            }
        }

        for (var offset = 0; offset < samples.Length; offset += 512)
        {
            var length = Math.Min(512, samples.Length - offset);
            var window = new float[length];
            Array.Copy(samples, offset, window, 0, length);
            _vad.AcceptWaveform(window);
            Drain();
        }
        _vad.Flush();
        Drain();
        _vad.Reset();

        // If VAD misses quiet speech, decode the complete recording instead of returning no text.
        return segments.Count == 0 ? [samples] : segments;
    }

    private static string JoinSegments(IReadOnlyList<string> parts)
    {
        var result = new StringBuilder();
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (index < parts.Count - 1 && part.EndsWith('。')) part = part[..^1] + "，";
            if (result.Length > 0 && IsAsciiAlphaNumeric(result[^1]) && IsAsciiAlphaNumeric(part[0]))
                result.Append(' ');
            result.Append(part);
        }
        return result.ToString();
    }

    private static bool IsAsciiAlphaNumeric(char value) => value < 128 && char.IsLetterOrDigit(value);

    private static string StripPunctuation(string text) =>
        Regex.Replace(text, "[，。！？；、：,;!?]", "");

    private static string DedupePunctuation(string text)
    {
        var value = Regex.Replace(text, "[，。！？；、：]+([，。！？；、：])", "$1");
        return Regex.Replace(value, "([,;!?])[,;!?]+", "$1");
    }

    public void Dispose()
    {
        _vad?.Dispose();
        _punctuation?.Dispose();
        _recognizer.Dispose();
    }
}
