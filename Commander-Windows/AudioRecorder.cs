using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Commander;

public sealed record AudioCapture(
    float[] Samples,
    int SampleRate,
    int NativeSampleRate,
    long NativeSampleCount,
    int ChannelCount);

public sealed class AudioRecorder : IDisposable
{
    private readonly object _sync = new();
    private WasapiCapture? _capture;
    private MemoryStream? _nativeAudio;
    private WaveFormat? _nativeFormat;
    private ManualResetEventSlim? _stopped;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording) throw new InvalidOperationException("Recording is already active.");

        var capture = new WasapiCapture();
        var stream = new MemoryStream();
        var stopped = new ManualResetEventSlim(false);
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        lock (_sync)
        {
            _capture = capture;
            _nativeAudio = stream;
            _nativeFormat = capture.WaveFormat;
            _stopped = stopped;
        }

        try
        {
            capture.StartRecording();
            IsRecording = true;
        }
        catch
        {
            CleanupCapture();
            throw;
        }
    }

    public AudioCapture Stop()
    {
        WasapiCapture? capture;
        ManualResetEventSlim? stopped;
        lock (_sync)
        {
            capture = _capture;
            stopped = _stopped;
        }

        if (capture is not null && IsRecording)
        {
            capture.StopRecording();
            stopped?.Wait(TimeSpan.FromSeconds(3));
        }
        IsRecording = false;

        byte[] bytes;
        WaveFormat format;
        lock (_sync)
        {
            bytes = _nativeAudio?.ToArray() ?? [];
            format = _nativeFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1);
        }
        CleanupCapture();

        var nativeSamples = format.BlockAlign > 0 ? bytes.LongLength / format.BlockAlign : 0;
        return new AudioCapture(
            ResampleTo16KhzMono(bytes, format),
            16_000,
            format.SampleRate,
            nativeSamples,
            format.Channels);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        lock (_sync)
        {
            _nativeAudio?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs) => _stopped?.Set();

    private static float[] ResampleTo16KhzMono(byte[] bytes, WaveFormat format)
    {
        if (bytes.Length == 0) return [];

        using var memory = new MemoryStream(bytes, writable: false);
        using var raw = new RawSourceWaveStream(memory, format);
        ISampleProvider samples = raw.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1) samples = new MonoSampleProvider(samples);
        if (samples.WaveFormat.SampleRate != 16_000)
            samples = new WdlResamplingSampleProvider(samples, 16_000);

        var result = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            result.AddRange(buffer.AsSpan(0, read).ToArray());
        return result.ToArray();
    }

    private void CleanupCapture()
    {
        lock (_sync)
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
            }
            _capture = null;
            _nativeAudio?.Dispose();
            _nativeAudio = null;
            _nativeFormat = null;
            _stopped?.Dispose();
            _stopped = null;
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            try { _capture?.StopRecording(); }
            catch { }
        }
        CleanupCapture();
    }

    private sealed class MonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer = [];

        public MonoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var required = count * _channels;
            if (_sourceBuffer.Length < required) _sourceBuffer = new float[required];
            var sourceRead = _source.Read(_sourceBuffer, 0, required);
            var frames = sourceRead / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < _channels; channel++)
                    sum += _sourceBuffer[frame * _channels + channel];
                buffer[offset + frame] = sum / _channels;
            }
            return frames;
        }
    }
}
