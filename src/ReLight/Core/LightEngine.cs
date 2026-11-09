using System.Diagnostics;
using System.Drawing;
using ReLight.Audio;
using ReLight.Capture;
using ReLight.Core.Effects;
using ReLight.Services;

namespace ReLight.Core;

/// <summary>
/// Produces the strip colors for the chosen mode (screen, single color, rainbow, music, cinema, mic)
/// on its own thread and hands them to the device. Setup windows suspend it while they
/// drive the strip themselves.
/// </summary>
public sealed class LightEngine : IDisposable
{
    readonly AppSettings _settings;
    readonly AdalightDevice _device;
    Thread? _thread;
    volatile bool _running;
    int _suspended;
    volatile bool _layoutDirty = true;
    volatile bool _sourceDirty = true;

    /// <summary>"DXGI" or "GDI" while the screen mode captures, otherwise null.</summary>
    public string? CaptureName { get; private set; }

    /// <summary>Frames per second actually produced.</summary>
    public double Fps { get; private set; }

    /// <summary>Set by the window while an excluded program runs (or a player is in front); replaces the chosen mode.</summary>
    public volatile ActiveExclusion? Exclusion;

    ActiveExclusion? ActiveOverride => Exclusion;

    /// <summary>Rainbow mode: the chosen effect or playlist.</summary>
    public EffectPlayer Effects { get; }

    readonly MicVisualizer _mic = new();

    /// <summary>Microphone mode: absolute loudness heard right now, 0 = -70 dB .. 1 = -10 dB, for the meters.</summary>
    public float MicLevel { get; private set; }

    /// <summary>Microphone mode: the silence threshold in use now, dBFS (moves by itself with the automatic one).</summary>
    public double MicGateDb => _settings.Mic.AutoGate ? _silence.GateDb : _settings.Mic.GateDb;

    readonly SilenceGate _silence = new(-55);

    readonly LetterboxDetector _bars = new();
    readonly CinemaSound _cinemaSound = new();
    int _samplerDepth = -1;
    ScreenFrame? _lastFrame;

    /// <summary>Cinema mode: black bar above (= below) the picture, 0..1 of the screen height.</summary>
    public double BarsVertical { get; private set; }

    /// <summary>Cinema mode: black bar left (= right) of the picture, 0..1 of the screen width.</summary>
    public double BarsHorizontal { get; private set; }

    /// <summary>Cinema mode: the soundtrack swell 0..1, for the meter.</summary>
    public double CinemaSwell { get; private set; }

    /// <summary>Cinema test window is open: publish <see cref="CinemaView"/> ten times a second.</summary>
    public volatile bool CinemaViewWanted;

    /// <summary>Cinema mode: the last frame the strip read, its bars, LED areas and colors (only while wanted).</summary>
    public volatile CinemaSnapshot? CinemaView;

    double _viewTime = -1;

    /// <summary>Set when music mode cannot open the sound output.</summary>
    public string? AudioError { get; private set; }

    public LightEngine(AppSettings settings, AdalightDevice device)
    {
        _settings = settings;
        _silence.Reset(settings.Mic.GateDb); // the automatic threshold starts from the last manual one
        _device = device;
        Effects = new EffectPlayer(settings);
    }

    public void Start()
    {
        if (_thread != null) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ReLight engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Call after the layout, monitor or mode changed.</summary>
    public void Refresh()
    {
        _layoutDirty = true;
        _sourceDirty = true;
    }

    public void Suspend() => Interlocked.Increment(ref _suspended);

    public void Resume()
    {
        if (Interlocked.Decrement(ref _suspended) < 0) _suspended = 0;
        Refresh();
    }

    void Loop()
    {
        LedLayout? layout = null;
        float[] current = [];
        var target = Array.Empty<Color>();
        IScreenSource? screen = null;
        ScreenSampler? sampler = null;
        AudioAnalyzer? audio = null;
        LightMode? activeMode = null;
        bool dark = false;
        bool excludedDark = false; // black already sent for an "off" exclusion
        int dxgiFailures = 0;
        EffectFrame? effectFrame = null;
        var effectColors = Array.Empty<Rgb>();
        var clock = Stopwatch.StartNew();
        var fpsClock = Stopwatch.StartNew();
        int frames = 0;
        double last = 0;
        byte[]? lastPacket = null;
        double lastSent = -10;

        void ReleaseSources()
        {
            screen?.Dispose(); screen = null; sampler = null; CaptureName = null;
            audio?.Dispose(); audio = null;
            activeMode = null;
        }

        while (_running)
        {
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.2, now - last);
            last = now;

            if (Volatile.Read(ref _suspended) > 0)
            {
                ReleaseSources();
                dark = false; // the setup window owns the strip now
                lastPacket = null;
                Thread.Sleep(50);
                continue;
            }

            if (_layoutDirty || layout == null)
            {
                _layoutDirty = false;
                layout = new LedLayout(_settings.Layout.Clone());
                current = new float[layout.Count * 3];
                target = new Color[layout.Count];
                effectFrame = EffectFrame.For(layout);
                effectColors = new Rgb[layout.Count];
                sampler = null;
            }

            int outputCount = Math.Max(layout.Count, _settings.KnownLedCount);
            if (!_settings.LightOn || _device.State != DeviceState.Connected || layout.Count == 0)
            {
                ReleaseSources();
                if (!dark && _device.State == DeviceState.Connected)
                {
                    _device.Send(new byte[Math.Max(1, outputCount) * 3]);
                    Array.Clear(current);
                    lastPacket = null;
                    dark = true;
                }
                if (_device.State != DeviceState.Connected) dark = false;
                Fps = 0;
                Thread.Sleep(100);
                continue;
            }
            dark = false;

            // An excluded program (a game, a player in front) is running: the mode gives way to the chosen action.
            var mode = _settings.Mode;
            if (Exclusion is { } excluded)
            {
                if (excluded.Action == ExclusionAction.Off)
                {
                    ReleaseSources();
                    if (!excludedDark)
                    {
                        _device.Send(new byte[Math.Max(1, outputCount) * 3]);
                        Array.Clear(current);
                        lastPacket = null;
                        excludedDark = true;
                    }
                    Fps = 0;
                    Thread.Sleep(100);
                    continue;
                }
                excludedDark = false;
                mode = excluded.Action switch
                {
                    ExclusionAction.Screen => LightMode.Screen,
                    ExclusionAction.Color => LightMode.Color,
                    ExclusionAction.Music => LightMode.Music,
                    ExclusionAction.Mic => LightMode.Mic,
                    ExclusionAction.Cinema => LightMode.Cinema,
                    _ => LightMode.Rainbow,
                };
            }
            if (Exclusion == null) excludedDark = false;
            if (mode != activeMode || _sourceDirty)
            {
                ReleaseSources();
                _sourceDirty = false;
                activeMode = mode;
                AudioError = null;
                _bars.Reset();
                BarsVertical = BarsHorizontal = 0;
                if (mode is LightMode.Music or LightMode.Mic or LightMode.Cinema)
                {
                    try { audio = mode == LightMode.Mic ? AudioAnalyzer.ForMicrophone(_settings.MicDevice) : new AudioAnalyzer(); }
                    catch (Exception ex) { AudioError = ex.Message; }
                    if (mode == LightMode.Mic) _silence.Reset(_settings.Mic.GateDb); // a new microphone: learn its room again
                }
            }

            switch (mode)
            {
                case LightMode.Screen:
                    if (!RenderScreen(layout, target, ref screen, ref sampler, ref dxgiFailures, now, cinema: false))
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    Smooth(target, current, _settings.Smoothing);
                    break;
                case LightMode.Cinema:
                    if (!RenderScreen(layout, target, ref screen, ref sampler, ref dxgiFailures, now, cinema: true))
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    Breathe(target, audio, dt);
                    Smooth(target, current, _settings.CinemaSmoothing);
                    if (CinemaViewWanted && now - _viewTime >= 0.1 && _lastFrame != null && sampler != null)
                    {
                        _viewTime = now;
                        CinemaView = CinemaSnapshot.Take(_lastFrame, sampler, current, now);
                    }
                    break;
                case LightMode.Color:
                    var solid = ColorMath.FromHex(ActiveOverride?.Color ?? _settings.SolidColor, Color.White);
                    Array.Fill(target, solid);
                    Copy(target, current);
                    break;
                case LightMode.Rainbow:
                    Effects.Render(effectFrame!, dt, effectColors, ActiveOverride?.Effect);
                    for (int i = 0; i < layout.Count; i++) target[i] = effectColors[i].ToColor();
                    Copy(target, current);
                    break;
                case LightMode.Music:
                    RenderMusic(layout, target, audio);
                    Copy(target, current);
                    break;
                case LightMode.Mic:
                    var micOptions = _settings.Mic;
                    double gate = micOptions.AutoGate ? _silence.GateDb : micOptions.GateDb;
                    var sound = audio?.ReadFeatures(_settings.MicSensitivity, gate) ?? default;
                    MicLevel = sound.Loudness;
                    if (audio != null && micOptions.AutoGate) _silence.Update(sound.Loudness * 60 - 70, dt); // the loudness already measured, nothing extra
                    _mic.Render(effectFrame!, sound, micOptions, dt, effectColors);
                    for (int i = 0; i < layout.Count; i++) target[i] = effectColors[i].ToColor();
                    Copy(target, current);
                    break;
            }

            // A single color stands still: send it only when it changes, and once a second so the
            // controller knows we are alive. Light enough to run next to a game unnoticed.
            var packet = ToBytes(current, outputCount);
            bool still = mode == LightMode.Color && lastPacket != null && packet.AsSpan().SequenceEqual(lastPacket)
                         && now - lastSent < 1;
            if (!still)
            {
                _device.Send(packet);
                lastPacket = packet;
                lastSent = now;
            }

            frames++;
            if (fpsClock.ElapsedMilliseconds >= 1000)
            {
                Fps = frames * 1000.0 / fpsClock.ElapsedMilliseconds;
                frames = 0;
                fpsClock.Restart();
            }

            // Never ask for more frames than the serial line can carry.
            double lineMs = (6 + 3.0 * outputCount) * 10 * 1000 / Math.Max(9600, _device.BaudRate) * 1.05;
            double wantMs = 1000.0 / (mode switch
            {
                LightMode.Screen or LightMode.Cinema => Math.Clamp(_settings.ScreenFps, 5, 60),
                LightMode.Color => 10, // nothing moves: ten looks a second are enough to catch a new color
                _ => 50,
            });
            double waitMs = Math.Max(lineMs, wantMs) - (clock.Elapsed.TotalSeconds - now) * 1000;
            if (waitMs > 1) Thread.Sleep((int)waitMs);
        }

        ReleaseSources();
    }

    /// <param name="cinema">Cinema mode: skip the black bars and look deeper into the picture.</param>
    bool RenderScreen(LedLayout layout, Color[] target, ref IScreenSource? screen, ref ScreenSampler? sampler,
        ref int dxgiFailures, double now, bool cinema)
    {
        try
        {
            if (screen == null)
            {
                var monitor = ScreenHelper.Find(_settings.Monitor);
                var method = _settings.CaptureMethod;
                if (method == "dxgi" || (method != "gdi" && dxgiFailures < 3))
                    screen = DxgiScreenSource.TryCreate(monitor.DeviceName);
                if (screen == null && method == "dxgi")
                    throw new InvalidOperationException("DXGI is not available for this screen");
                screen ??= new GdiScreenSource(monitor.Bounds);
                CaptureName = screen.Name;
                sampler = null;
            }
            if (!screen.TryGrab(out var frame) || frame == null) return false;
            _lastFrame = frame;
            if (cinema && _settings.CinemaBars)
            {
                if (_bars.Update(frame, now)) sampler = null;
            }
            else if (_bars.Vertical > 0 || _bars.Horizontal > 0)
            {
                _bars.Reset(); // bars switched off in the settings
                sampler = null;
            }
            BarsVertical = _bars.Vertical;
            BarsHorizontal = _bars.Horizontal;
            // The depth slider moves live: rebuild the LED areas when it changed.
            int depth = cinema ? _settings.CinemaDepth : _settings.CaptureDepth;
            if (depth != _samplerDepth) sampler = null;
            _samplerDepth = depth;
            if (sampler == null || !sampler.Fits(layout, frame))
                sampler = cinema
                    ? new ScreenSampler(layout, frame.Width, frame.Height, _settings.CinemaDepth / 100.0, _bars.Content)
                    : new ScreenSampler(layout, frame.Width, frame.Height, _settings.CaptureDepth / 100.0);
            sampler.Sample(frame, target);
            dxgiFailures = 0;
        }
        catch
        {
            // Desktop duplication is lost on mode changes, UAC prompts and some fullscreen games:
            // drop it and build a new one; after repeated failures stay on GDI.
            if (screen is DxgiScreenSource) dxgiFailures++;
            screen?.Dispose();
            screen = null;
            sampler = null;
            Thread.Sleep(200);
            return false;
        }

        float k = _settings.Saturation / 100f;
        if (cinema) k *= (float)_cinemaSound.Saturation(_settings.CinemaSound / 100.0);
        for (int i = 0; i < target.Length; i++)
        {
            var c = target[i];
            float gray = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
            target[i] = Color.FromArgb(
                Clamp(gray + (c.R - gray) * k), Clamp(gray + (c.G - gray) * k), Clamp(gray + (c.B - gray) * k));
        }
        return true;
    }

    /// <summary>Cinema mode: the soundtrack slowly breathes the brightness of the screen colors.</summary>
    void Breathe(Color[] target, AudioAnalyzer? audio, double dt)
    {
        double amount = _settings.CinemaSound / 100.0;
        CinemaSwell = audio == null ? 0 : _cinemaSound.Update(audio.LoudnessDb(), dt);
        if (audio == null || amount <= 0) return;
        float k = (float)_cinemaSound.Brightness(amount);
        for (int i = 0; i < target.Length; i++)
        {
            var c = target[i];
            target[i] = Color.FromArgb(Clamp(c.R * k), Clamp(c.G * k), Clamp(c.B * k));
        }
    }

    void RenderMusic(LedLayout layout, Color[] target, AudioAnalyzer? audio)
    {
        if (audio == null)
        {
            Array.Fill(target, Color.Black);
            return;
        }
        var bands = audio.Read(_settings.MusicSensitivity);
        for (int i = 0; i < layout.Count; i++)
        {
            // Equalizer around the screen: bass at the bottom, treble at the top.
            var r = layout.RegionOf(i, 0, 0);
            double u = 1 - (r.Top + r.Bottom) / 2;
            double pos = u * (bands.Length - 1);
            int b0 = (int)pos, b1 = Math.Min(bands.Length - 1, b0 + 1);
            double level = bands[b0] + (bands[b1] - bands[b0]) * (pos - b0);
            level = Math.Pow(Math.Clamp(level, 0, 1), 1.6);
            target[i] = ColorMath.FromHsv(u * 0.78, 1, level);
        }
    }

    static void Smooth(Color[] target, float[] current, int smoothing)
    {
        float alpha = 1 - Math.Clamp(smoothing, 0, 100) / 100f * 0.92f;
        for (int i = 0; i < target.Length; i++)
        {
            current[i * 3] += (target[i].R - current[i * 3]) * alpha;
            current[i * 3 + 1] += (target[i].G - current[i * 3 + 1]) * alpha;
            current[i * 3 + 2] += (target[i].B - current[i * 3 + 2]) * alpha;
        }
    }

    static void Copy(Color[] target, float[] current)
    {
        for (int i = 0; i < target.Length; i++)
        {
            current[i * 3] = target[i].R;
            current[i * 3 + 1] = target[i].G;
            current[i * 3 + 2] = target[i].B;
        }
    }

    byte[] ToBytes(float[] current, int outputCount)
    {
        var colors = new Color[outputCount];
        int n = current.Length / 3;
        for (int i = 0; i < outputCount; i++)
            colors[i] = i < n ? Color.FromArgb(Clamp(current[i * 3]), Clamp(current[i * 3 + 1]), Clamp(current[i * 3 + 2])) : Color.Black;
        return ColorOutput.ToBytes(colors, _settings.ColorOrder, _settings.Brightness);
    }

    static int Clamp(float v) => Math.Clamp((int)(v + 0.5f), 0, 255);

    public void Dispose()
    {
        _running = false;
        _thread?.Join(2000);
        _thread = null;
    }
}

/// <summary>What the cinema mode read in one frame, copied for the test window.</summary>
public sealed record CinemaSnapshot(int Width, int Height, byte[] Pixels, RectangleF Content,
    Rectangle[] Areas, Color[] Colors, double Time)
{
    public static CinemaSnapshot Take(ScreenFrame frame, ScreenSampler sampler, float[] current, double time)
    {
        var colors = new Color[sampler.Areas.Count];
        for (int i = 0; i < colors.Length && i * 3 + 2 < current.Length; i++)
            colors[i] = Color.FromArgb(Math.Clamp((int)current[i * 3], 0, 255),
                Math.Clamp((int)current[i * 3 + 1], 0, 255), Math.Clamp((int)current[i * 3 + 2], 0, 255));
        return new CinemaSnapshot(frame.Width, frame.Height, (byte[])frame.Pixels.Clone(), sampler.Content,
            sampler.Areas.ToArray(), colors, time);
    }
}
