using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ReLight.Audio;
using ReLight.Capture;
using ReLight.Core;
using ReLight.Services;
using MColor = System.Windows.Media.Color;

namespace ReLight;

/// <summary>
/// Cinema test: a live miniature of the very frame the strip reads, ten times a second.
/// While the cinema mode runs the picture comes from the engine itself (a second desktop
/// duplication in the same program may get no frames); with the strip off it captures on its own.
/// The black bars found are shaded red, the picture is framed, and every LED's area is
/// outlined and tinted with the color it gets. The window hides itself from screen capture,
/// so it never ends up in its own picture or on the strip.
/// </summary>
public partial class CinemaTestWindow : Window
{
    readonly AppSettings _settings;
    readonly LightEngine _engine;
    CinemaSnapshot? _seenView;
    double _seenAt = -10;
    int _emptyTicks;
    bool _forceGdi;
    System.Drawing.RectangleF _shownContent;
    System.Drawing.Rectangle[] _shownAreas = [];
    readonly LedLayout _layout;
    readonly LetterboxDetector _bars = new();
    readonly CinemaSound _sound = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    IScreenSource? _screen;
    AudioAnalyzer? _audio;
    ScreenSampler? _sampler;
    WriteableBitmap? _bitmap;
    System.Drawing.Color[] _colors = [];
    double _last;
    int _depth = -1;
    bool _barsOn;

    public CinemaTestWindow(AppSettings settings, LightEngine engine)
    {
        _settings = settings;
        _engine = engine;
        _engine.CinemaViewWanted = true;
        _layout = new LedLayout(settings.Layout.Clone());
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _engine.CinemaViewWanted = false;
            _engine.CinemaView = null;
            _screen?.Dispose();
            _audio?.Dispose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // WDA_EXCLUDEFROMCAPTURE: not in screenshots, desktop duplication or GDI copies (Windows 10 2004+).
        SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
    }

    void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds, dt = now - _last;
        _last = now;

        var view = _engine.CinemaView;
        if (!ReferenceEquals(view, _seenView)) { _seenView = view; _seenAt = now; }
        if (view != null && now - _seenAt < 0.6)
        {
            // The engine is running the cinema mode: show exactly what it read and sent.
            DropOwnCapture();
            SwellMeter.Width = SwellTrack.ActualWidth * _engine.CinemaSwell;
            Show(view.Width, view.Height, view.Pixels, view.Content, view.Areas, view.Colors,
                _engine.BarsVertical, _engine.BarsHorizontal);
            return;
        }
        OwnCapture(now, dt);
    }

    /// <summary>The strip is not lit by the cinema mode now: capture and analyse here, the same way.</summary>
    void OwnCapture(double now, double dt)
    {
        _audio ??= TryAudio();
        if (_audio != null) _sound.Update(_audio.LoudnessDb(), dt);
        SwellMeter.Width = SwellTrack.ActualWidth * _sound.Level;

        ScreenFrame? frame;
        try
        {
            if (_screen == null)
            {
                var monitor = ScreenHelper.Find(_settings.Monitor);
                if (_settings.CaptureMethod != "gdi" && !_forceGdi) _screen = DxgiScreenSource.TryCreate(monitor.DeviceName);
                _screen ??= new GdiScreenSource(monitor.Bounds);
                _emptyTicks = 0;
            }
            if (!_screen.TryGrab(out frame) || frame == null)
            {
                // Desktop duplication that never delivers a first frame: take the simple GDI copy.
                if (++_emptyTicks >= 10 && _screen is DxgiScreenSource)
                {
                    _forceGdi = true;
                    DropOwnCapture();
                }
                return;
            }
        }
        catch
        {
            DropOwnCapture(); // lost (mode change, UAC): try again next tick
            return;
        }

        bool barsOn = _settings.CinemaBars;
        if (barsOn != _barsOn) { _bars.Reset(); _sampler = null; _barsOn = barsOn; }
        if (barsOn && _bars.Update(frame, now)) _sampler = null;
        if (_settings.CinemaDepth != _depth) { _sampler = null; _depth = _settings.CinemaDepth; }
        if (_sampler == null || !_sampler.Fits(_layout, frame))
        {
            _sampler = new ScreenSampler(_layout, frame.Width, frame.Height, _depth / 100.0, _bars.Content);
            _colors = new System.Drawing.Color[_layout.Count];
        }
        _sampler.Sample(frame, _colors);
        Show(frame.Width, frame.Height, frame.Pixels, _sampler.Content, _sampler.Areas, _colors, _bars.Vertical, _bars.Horizontal);
    }

    static AudioAnalyzer? TryAudio()
    {
        try { return new AudioAnalyzer(); } catch { return null; } // no sound output: the meter just stays empty
    }

    void DropOwnCapture()
    {
        _screen?.Dispose();
        _screen = null;
    }

    void Show(int width, int height, byte[] pixels, System.Drawing.RectangleF content,
        IReadOnlyList<System.Drawing.Rectangle> areas, System.Drawing.Color[] colors, double vertical, double horizontal)
    {
        ShowFrame(width, height, pixels);
        if (content != _shownContent || !areas.SequenceEqual(_shownAreas))
        {
            _shownContent = content;
            _shownAreas = areas.ToArray();
            BuildOverlay(width, height, content, _shownAreas);
        }
        PaintAreas(colors);
        BarsText.Text = Describe(vertical, horizontal);
        Waiting.Visibility = Visibility.Collapsed;
    }

    void ShowFrame(int width, int height, byte[] pixels)
    {
        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            Shot.Source = _bitmap;
            Frame.Width = width;
            Frame.Height = height;
            _shownAreas = []; // overlay is in frame pixels: rebuild it
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }

    readonly List<Rectangle> _areas = [];
    static readonly Brush AreaOutline = new SolidColorBrush(MColor.FromArgb(80, 255, 255, 255));

    /// <summary>Bars, the picture's frame and one outline per LED, in frame pixels.</summary>
    void BuildOverlay(int width, int height, System.Drawing.RectangleF c, System.Drawing.Rectangle[] areas)
    {
        Overlay.Children.Clear();
        _areas.Clear();
        double w = width, h = height;
        double l = c.Left * w, t = c.Top * h, r = c.Right * w, b = c.Bottom * h;
        var shade = new SolidColorBrush(MColor.FromArgb(110, 255, 59, 48));
        foreach (var (x, y, bw, bh) in new[] { (0.0, 0.0, w, t), (0.0, b, w, h - b), (0.0, t, l, b - t), (r, t, w - r, b - t) })
            if (bw > 0 && bh > 0) Add(new Rectangle { Width = bw, Height = bh, Fill = shade }, x, y);

        var picture = new Rectangle
        {
            Width = Math.Max(0, r - l), Height = Math.Max(0, b - t), StrokeThickness = 0.5,
            StrokeDashArray = [4, 3],
        };
        picture.SetResourceReference(Shape.StrokeProperty, "Rl.Accent");
        Add(picture, l, t);

        foreach (var a in areas)
        {
            // A light outline keeps every area visible even when its color is dark.
            var rect = new Rectangle { Width = a.Width, Height = a.Height, StrokeThickness = 0.2, Stroke = AreaOutline };
            _areas.Add(rect);
            Add(rect, a.X, a.Y);
        }

        void Add(Rectangle shape, double x, double y)
        {
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            Overlay.Children.Add(shape);
        }
    }

    void PaintAreas(System.Drawing.Color[] colors)
    {
        for (int i = 0; i < _areas.Count && i < colors.Length; i++)
        {
            var c = colors[i];
            _areas[i].Fill = new SolidColorBrush(MColor.FromArgb(200, c.R, c.G, c.B));
        }
    }

    string Describe(double v, double h)
    {
        if (!_settings.CinemaBars) return Loc.T("cinema.bars.offinfo");
        return v > 0 && h > 0 ? Loc.T("cinema.bars.both", P(v), P(h))
            : v > 0 ? Loc.T("cinema.bars.tb", P(v))
            : h > 0 ? Loc.T("cinema.bars.lr", P(h))
            : Loc.T("cinema.bars.none");

        static string P(double share) => $"{Math.Round(share * 100)}%";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
