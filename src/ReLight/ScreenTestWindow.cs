using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MColor = System.Windows.Media.Color;

namespace ReLight;

/// <summary>
/// Screen-mode test: a bright square follows the mouse over a see-through layer on all monitors.
/// Bring it to the screen edges and the LEDs there must light up — the layout check at a glance.
/// Wheel up/down: bigger/smaller; wheel click: next color; left or right click, Esc: finish.
/// </summary>
public sealed class ScreenTestWindow : Window
{
    static readonly MColor[] Colors =
    [
        MColor.FromRgb(255, 0, 0), MColor.FromRgb(0, 255, 0), MColor.FromRgb(0, 0, 255), MColor.FromRgb(255, 255, 0),
        MColor.FromRgb(0, 255, 255), MColor.FromRgb(255, 0, 255), MColor.FromRgb(255, 255, 255),
    ];

    const double StartSizePx = 300;

    double _sizePx = StartSizePx;
    int _color;
    Point _mouse = new(-10000, -10000);

    public ScreenTestWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Almost transparent, but not fully: otherwise clicks and the wheel would fall through to the windows below.
        Background = new SolidColorBrush(MColor.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "ReLight test";

        SourceInitialized += (_, _) =>
        {
            // Cover every monitor, in physical pixels.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, new IntPtr(-1),
                GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79), 0x0040);
        };
        MouseMove += (_, e) => { _mouse = e.GetPosition(this); InvalidateVisual(); };
        MouseWheel += (_, e) =>
        {
            _sizePx = Math.Clamp(_sizePx * (e.Delta > 0 ? 1.15 : 1 / 1.15), 20, 2000);
            InvalidateVisual();
        };
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _color = (_color + 1) % Colors.Length;
                InvalidateVisual();
            }
            else if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
                Close();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            _mouse = Mouse.GetPosition(this);
            InvalidateVisual();
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Size is given in physical pixels so 100 means 100 on the TV whatever its scaling.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double size = _sizePx / scale;
        dc.DrawRectangle(new SolidColorBrush(Colors[_color]), null,
            new Rect(_mouse.X - size / 2, _mouse.Y - size / 2, size, size));
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
