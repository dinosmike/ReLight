using System.Windows;
using System.Windows.Media;
using ReLight.Core;
using ReLight.Core.Effects;

namespace ReLight.Controls;

/// <summary>
/// Animated icon of an effect: a little screen with LED dots around it playing the effect live.
/// All previews share one clock and only draw while they are on screen.
/// </summary>
public sealed class EffectPreview : FrameworkElement
{
    static readonly LedLayout Ring = new(new StripLayout { Top = 14, Right = 8, Bottom = 14, Left = 8, Start = StartCorner.BottomLeft, Clockwise = true });
    static readonly List<WeakReference<EffectPreview>> Live = [];
    static DateTime _lastTick = DateTime.UtcNow;

    readonly EffectFrame _frame = EffectFrame.For(Ring);
    readonly Rgb[] _colors = new Rgb[Ring.Count];
    EffectRunner? _runner;
    EffectSettings? _settings;

    /// <summary>What to play: effect id with its colors and speed.</summary>
    public EffectSettings? Settings
    {
        get => _settings;
        set
        {
            if (value?.Id != _settings?.Id) _runner = value == null ? null : new EffectRunner(EffectCatalog.Get(value.Id));
            _settings = value;
            InvalidateVisual();
        }
    }

    // WPF runs its render loop at the monitor's refresh rate for as long as anyone listens to
    // CompositionTarget.Rendering — even with the window hidden in the tray, i.e. during a game.
    // So the previews listen only while at least one of them can actually be seen.
    static bool _subscribed;

    static void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds;
        if (dt < 1 / 30.0) return; // 30 frames per second is plenty for icons
        _lastTick = now;
        dt = Math.Min(dt, 0.2);
        lock (Live)
        {
            Live.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in Live)
                if (w.TryGetTarget(out var p) && p.Seen) p.Tick(dt);
        }
    }

    /// <summary>On screen: visible and its window not minimized.</summary>
    bool Seen => IsVisible && Window.GetWindow(this) is not { WindowState: WindowState.Minimized };

    static void UpdateSubscription()
    {
        bool need;
        lock (Live) need = Live.Any(w => w.TryGetTarget(out var p) && p.Seen);
        if (need == _subscribed) return;
        _subscribed = need;
        if (need)
        {
            _lastTick = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRendering;
        }
        else
            CompositionTarget.Rendering -= OnRendering;
    }

    Window? _window;

    public EffectPreview()
    {
        lock (Live) Live.Add(new WeakReference<EffectPreview>(this));
        IsVisibleChanged += (_, _) => UpdateSubscription();
        Loaded += (_, _) =>
        {
            if (_window != null) _window.StateChanged -= OnWindowState;
            _window = Window.GetWindow(this);
            if (_window != null) _window.StateChanged += OnWindowState;
            UpdateSubscription();
        };
    }

    static void OnWindowState(object? sender, EventArgs e) => UpdateSubscription();

    void Tick(double dt)
    {
        if (_runner == null || _settings == null) return;
        _runner.Render(_frame, _settings, dt, _colors);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var screen = new Rect(7, 7, w - 14, h - 14);
        var tv = TryFindResource("Rl.Preview.Screen") as Brush ?? Brushes.Black;
        dc.DrawRoundedRectangle(tv, null, screen, 4, 4);

        // Soft glow of the average color behind the screen, like light on a wall.
        float r = 0, g = 0, b = 0;
        foreach (var c in _colors) { r += c.R; g += c.G; b += c.B; }
        int n = Math.Max(1, _colors.Length);
        var avg = Color.FromArgb(60, ToByte(r / n), ToByte(g / n), ToByte(b / n));
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(avg), 6), new Rect(4, 4, w - 8, h - 8), 7, 7);

        double dot = Math.Clamp(Math.Min(w, h) / 32, 1.6, 3.2);
        for (int i = 0; i < Ring.Count; i++)
        {
            var c = _colors[i];
            var p = new Point(screen.X + _frame.X[i] * screen.Width, screen.Y + _frame.Y[i] * screen.Height);
            p = new Point(
                _frame.X[i] < 0.02 ? screen.Left : _frame.X[i] > 0.98 ? screen.Right : p.X,
                _frame.Y[i] < 0.02 ? screen.Top : _frame.Y[i] > 0.98 ? screen.Bottom : p.Y);
            var brush = new SolidColorBrush(Color.FromRgb(ToByte(c.R), ToByte(c.G), ToByte(c.B)));
            dc.DrawEllipse(brush, null, p, dot, dot);
        }
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255 + 0.5f), 0, 255);
}
