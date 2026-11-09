using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace ReLight.Audio;

/// <summary>What the microphone mode needs to know about the sound right now.</summary>
public readonly record struct SoundFeatures(
    float Level,     // overall loudness 0..1, adapted to the room; 0 below the silence threshold
    float Voice,     // how much of it is in the voice range 0..1
    float Pitch,     // spectral centre 0 (deep) .. 1 (high)
    float Hit,       // sudden onset — clap, knock, click — 0..1, 0 most of the time
    float Loudness = 0); // absolute loudness for meters: 0 = -70 dB .. 1 = -10 dB

/// <summary>
/// Listens either to what the computer plays (WASAPI loopback) or to a microphone,
/// and reports loudness in frequency bands, each normalised to its own recent peak
/// so quiet and loud sound both fill the strip.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    public const int Bands = 16;
    const int FftOrder = 11;            // 2048 samples ≈ 43 ms at 48 kHz
    const int FftSize = 1 << FftOrder;

    readonly WasapiCapture _capture;
    readonly float[] _ring = new float[FftSize];
    readonly object _lock = new();
    readonly Complex[] _fft = new Complex[FftSize];
    readonly float[] _window = new float[FftSize];
    readonly float[] _magnitudes = new float[FftSize / 2];
    readonly float[] _previous = new float[FftSize / 2];
    readonly float[] _peaks = new float[Bands];
    readonly float[] _levels = new float[Bands];
    readonly int _channels;
    readonly int _sampleRate;
    readonly bool _isFloat;
    int _ringPos;
    float _levelPeak = 1e-3f, _fluxAverage = 1e-3f, _voiceSmooth, _levelSmooth, _pitchSmooth = 0.5f;
    DateTime _lastHit;

    /// <summary>Sound card output (what the computer plays).</summary>
    public AudioAnalyzer() : this(new WasapiLoopbackCapture()) { }

    AudioAnalyzer(WasapiCapture capture)
    {
        _capture = capture;
        _channels = _capture.WaveFormat.Channels;
        _sampleRate = _capture.WaveFormat.SampleRate;
        _isFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                   || (_capture.WaveFormat is WaveFormatExtensible && _capture.WaveFormat.BitsPerSample == 32);
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        for (int i = 0; i < Bands; i++) _peaks[i] = 1e-4f;
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    /// <summary>A microphone; null id means the Windows default microphone.</summary>
    public static AudioAnalyzer ForMicrophone(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        if (!string.IsNullOrEmpty(deviceId))
        {
            try { device = enumerator.GetDevice(deviceId); } catch { }
        }
        device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        return new AudioAnalyzer(new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared });
    }

    /// <summary>Microphones connected now: id and name.</summary>
    public static List<(string id, string name)> Microphones()
    {
        var list = new List<(string, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add((d.ID, d.FriendlyName));
        }
        catch
        {
            // No audio service: no microphones to offer.
        }
        return list;
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _isFloat ? 4 : 2;
        int frame = bytesPerSample * _channels;
        lock (_lock)
        {
            for (int o = 0; o + frame <= e.BytesRecorded; o += frame)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++)
                {
                    int p = o + c * bytesPerSample;
                    sum += _isFloat ? BitConverter.ToSingle(e.Buffer, p) : BitConverter.ToInt16(e.Buffer, p) / 32768f;
                }
                _ring[_ringPos] = sum / _channels;
                _ringPos = (_ringPos + 1) % FftSize;
            }
        }
    }

    void Analyse()
    {
        lock (_lock)
        {
            for (int i = 0; i < FftSize; i++)
            {
                _fft[i].X = _ring[(_ringPos + i) % FftSize] * _window[i];
                _fft[i].Y = 0;
            }
        }
        FastFourierTransform.FFT(true, FftOrder, _fft);
        for (int i = 0; i < _magnitudes.Length; i++)
            _magnitudes[i] = MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
    }

    double BinHz => (double)_sampleRate / FftSize;

    float Sum(double fromHz, double toHz)
    {
        int i0 = Math.Max(1, (int)(fromHz / BinHz)), i1 = Math.Min(_magnitudes.Length, Math.Max(i0 + 1, (int)(toHz / BinHz)));
        float s = 0;
        for (int i = i0; i < i1; i++) s += _magnitudes[i];
        return s;
    }

    /// <summary>Band levels 0..1, low frequencies first. <paramref name="sensitivity"/> 0..100.</summary>
    public ReadOnlySpan<float> Read(int sensitivity)
    {
        Analyse();

        // Bands spaced evenly on a log scale from 40 Hz to 12 kHz.
        double lo = 40, hi = Math.Min(12000, _sampleRate / 2.0);
        float gain = 0.4f + sensitivity / 100f * 1.6f;
        for (int b = 0; b < Bands; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / Bands);
            double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / Bands);
            int i0 = Math.Max(1, (int)(f0 / BinHz)), i1 = Math.Max(i0 + 1, (int)(f1 / BinHz));
            float v = Sum(f0, f1) / (i1 - i0);

            // Peaks fall slowly so each band adapts to the music; silence stays dark.
            _peaks[b] = Math.Max(v, _peaks[b] * 0.996f);
            float level = _peaks[b] > 2e-4f ? v / _peaks[b] : 0;
            level = Math.Clamp(level * gain, 0, 1);
            _levels[b] = level > _levels[b] ? level : _levels[b] * 0.82f + level * 0.18f; // fast attack, soft decay
        }
        return _levels;
    }

    /// <summary>Loudness of the last ~43 ms in dBFS (about -180 for digital silence).</summary>
    public double LoudnessDb()
    {
        double square = 0;
        lock (_lock)
            foreach (var s in _ring) square += s * s;
        return 20 * Math.Log10(Math.Sqrt(square / FftSize) + 1e-9);
    }

    /// <summary>
    /// Loudness, voice, pitch and sudden hits for the microphone mode. <paramref name="sensitivity"/> 0..100.
    /// Anything quieter than <paramref name="gateDb"/> counts as silence: everything returns to 0.
    /// </summary>
    public SoundFeatures ReadFeatures(int sensitivity, double gateDb = -55)
    {
        Analyse();
        float gain = 0.5f + sensitivity / 100f * 2.5f;

        double square = 0;
        lock (_lock)
            foreach (var s in _ring) square += s * s;
        double db = 20 * Math.Log10(Math.Sqrt(square / FftSize) + 1e-9);
        float loudness = (float)Math.Clamp((db + 70) / 60, 0, 1);
        if (db < gateDb)
        {
            // Silence: fade what is left out quickly and report nothing, so the strip goes black.
            _levelSmooth *= 0.6f;
            _voiceSmooth *= 0.6f;
            Array.Copy(_magnitudes, _previous, _previous.Length);
            if (_levelSmooth < 0.01f) _levelSmooth = 0;
            if (_voiceSmooth < 0.01f) _voiceSmooth = 0;
            return new SoundFeatures(_levelSmooth, _voiceSmooth, _pitchSmooth, 0, loudness);
        }

        float total = Sum(60, 8000);
        float voice = Sum(150, 3000);
        _levelPeak = Math.Max(total, _levelPeak * 0.998f);
        float floor = 0.02f / gain; // room noise does not count
        float level = Math.Clamp((total / Math.Max(_levelPeak, 1e-3f) - floor) * gain, 0, 1);
        _levelSmooth += (level - _levelSmooth) * (level > _levelSmooth ? 0.6f : 0.15f);

        float voiceShare = total > 1e-4f ? voice / total : 0;
        float v = Math.Clamp(voiceShare * 1.3f * _levelSmooth, 0, 1);
        _voiceSmooth += (v - _voiceSmooth) * 0.3f;

        // Spectral centre on a log scale, 100 Hz .. 6 kHz.
        double weighted = 0, sum = 0;
        for (int i = (int)(100 / BinHz); i < Math.Min(_magnitudes.Length, (int)(6000 / BinHz)); i++)
        {
            weighted += _magnitudes[i] * Math.Log(i * BinHz);
            sum += _magnitudes[i];
        }
        float pitch = sum > 1e-4 ? (float)((weighted / sum - Math.Log(100)) / (Math.Log(6000) - Math.Log(100))) : _pitchSmooth;
        _pitchSmooth += (Math.Clamp(pitch, 0, 1) - _pitchSmooth) * 0.2f;

        // Spectral flux: how much louder the spectrum got since last time. Claps make it jump.
        float flux = 0;
        for (int i = 2; i < _magnitudes.Length / 2; i++)
        {
            float d = _magnitudes[i] - _previous[i];
            if (d > 0) flux += d;
            _previous[i] = _magnitudes[i];
        }
        float hit = 0;
        if (flux > _fluxAverage * (4.5f - sensitivity / 100f * 2.5f) && _levelSmooth > 0.08f
            && (DateTime.UtcNow - _lastHit).TotalMilliseconds > 120)
        {
            hit = Math.Clamp(flux / (_fluxAverage * 10), 0.3f, 1);
            _lastHit = DateTime.UtcNow;
        }
        _fluxAverage = _fluxAverage * 0.95f + flux * 0.05f + 1e-5f;

        return new SoundFeatures(_levelSmooth, _voiceSmooth, _pitchSmooth, hit, loudness);
    }

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
    }
}
