using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ReLight.Core;
using ReLight.Services;
using DColor = System.Drawing.Color;
using WinScreen = System.Windows.Forms.Screen;

namespace ReLight;

/// <summary>
/// Lights the strip one LED further at a time (passed LEDs stay dim) so the user can see
/// where the strip ends and how many LEDs it really has.
/// </summary>
public partial class DotTestWindow : Window
{
    static readonly DColor Passed = DColor.FromArgb(40, 40, 40);

    readonly AppSettings _settings;
    readonly AdalightDevice _device;
    readonly WinScreen _screen;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int _current; // 0-based

    /// <summary>How many LEDs the test addressed, so the caller can switch all of them off.</summary>
    public int TestedCount => MaxBox.Value;

    public DotTestWindow(AppSettings settings, AdalightDevice device, WinScreen screen)
    {
        _settings = settings;
        _device = device;
        _screen = screen;
        InitializeComponent();
        _hold.Tick += (_, _) => HoldTick();
        ScreenHelper.CoverScreen(this, screen);

        MaxBox.Value = Math.Max(300, settings.KnownLedCount + 50);
        MaxBox.ValueChanged += (_, _) => Show(Math.Min(_current, MaxBox.Value - 1));
        AutoBox.Checked += (_, _) => _timer.Start();
        AutoBox.Unchecked += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) =>
        {
            if (_current + 1 >= MaxBox.Value) AutoBox.IsChecked = false;
            else Step(+1);
        };
        Closed += (_, _) => { _timer.Stop(); _hold.Stop(); };

        Show(0);
    }

    void Step(int delta) => Show(Math.Clamp(_current + delta, 0, MaxBox.Value - 1));

    void Show(int index)
    {
        _current = index;
        NumberText.Text = Loc.T("dot.current", index + 1);
        var colors = new DColor[MaxBox.Value];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = i < index ? Passed : i == index ? DColor.White : DColor.Black;
        _device.Send(ColorOutput.ToBytes(colors, "RGB", _settings.Brightness));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (MaxBox.IsKeyboardFocusWithin && e.Key is not Key.Escape) return;
        switch (e.Key)
        {
            case Key.Left: Step(-1); break;
            case Key.Right: Step(+1); break;
            case Key.Space: AutoBox.IsChecked = AutoBox.IsChecked != true; break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    // Holding Back/Forward runs on by itself, faster and faster: first one LED at a time,
    // then shorter pauses, then several LEDs per step.
    readonly DispatcherTimer _hold = new();
    int _holdDir, _holdTicks;

    void Back_Down(object sender, MouseButtonEventArgs e) => StartHold(-1);

    void Next_Down(object sender, MouseButtonEventArgs e) => StartHold(+1);

    void StartHold(int direction)
    {
        AutoBox.IsChecked = false;
        _holdDir = direction;
        _holdTicks = 0;
        Step(direction);
        _hold.Interval = TimeSpan.FromMilliseconds(400);
        _hold.Start();
    }

    void HoldTick()
    {
        _holdTicks++;
        int stride = _holdTicks > 60 ? 5 : _holdTicks > 30 ? 2 : 1;
        Step(_holdDir * stride);
        _hold.Interval = TimeSpan.FromMilliseconds(Math.Max(25, 180 * Math.Pow(0.88, _holdTicks)));
    }

    void Hold_Up(object sender, MouseButtonEventArgs e) => _hold.Stop();

    void Hold_Lost(object sender, MouseEventArgs e) => _hold.Stop();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// The found count goes straight into the program: the layout is spread over the sides
    /// in proportion to the screen; start corner and direction stay as they were.
    /// </summary>
    void Last_Click(object sender, RoutedEventArgs e)
    {
        int count = _current + 1;
        _settings.KnownLedCount = count;
        if (_settings.Layout.Total != count)
        {
            var layout = _settings.Layout.Clone();
            layout.SpreadOver(count, _screen.Bounds.Width, _screen.Bounds.Height);
            _settings.Layout = layout;
        }
        Close();
    }
}
