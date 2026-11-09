using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace ReLight.Tests;

/// <summary>
/// Real mouse test: moves the system cursor over a topmost window and drags the slider.
/// Takes the mouse for about two seconds, so it runs only with RELIGHT_MOUSE_TESTS=1.
/// </summary>
public class SliderDragTests
{
    [Fact]
    public void Slider_FollowsTheMouseWhileDragging()
    {
        if (Environment.GetEnvironmentVariable("RELIGHT_MOUSE_TESTS") != "1") return;

        var values = new List<double>();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2, so cursor and WPF use the same pixels
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var name in new[] { "Colors.Dark", "Controls" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/ReLight;component/Themes/{name}.xaml"),
                    });

                foreach (var styled in new[] { true, false, true, false })
                {
                    var slider = new Slider { Minimum = 0, Maximum = 100, Value = 0, Margin = new Thickness(20) };
                    if (styled) slider.SetResourceReference(FrameworkElement.StyleProperty, "Rl.Slider.Hue");
                    if (styled) slider.Maximum = 360;
                    var window = new Window
                    {
                        WindowStyle = WindowStyle.None, Topmost = true, ShowActivated = false,
                        Left = 60, Top = 60, Width = 500, Height = 80, Content = slider,
                    };
                    window.Show();
                    Pump(300);

                    var origin = slider.PointToScreen(new Point(0, slider.ActualHeight / 2));
                    var end = slider.PointToScreen(new Point(slider.ActualWidth, slider.ActualHeight / 2));
                    int Y = (int)origin.Y;
                    int X(double share) => (int)(origin.X + (end.X - origin.X) * share);

                    SetCursorPos(X(0.25), Y); Pump(80);
                    mouse_event(0x0002, 0, 0, 0, 0); Pump(80);                    // down on the track
                    values.Add(slider.Value / slider.Maximum);
                    for (double s = 0.3; s <= 0.8; s += 0.1) { SetCursorPos(X(s), Y); Pump(40); }
                    values.Add(slider.Value / slider.Maximum);                    // still held: must have followed
                    SetCursorPos(X(0.4), Y); Pump(80);
                    values.Add(slider.Value / slider.Maximum);                    // and back
                    mouse_event(0x0004, 0, 0, 0, 0); Pump(80);                    // up
                    SetCursorPos(X(0.9), Y); Pump(80);
                    values.Add(slider.Value / slider.Maximum);                    // released: must not follow
                    window.Close();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Console.WriteLine("values: " + string.Join(", ", values.Select(v => v.ToString("0.00"))));
        // The first windows only warm up mouse delivery to the test process; the last two count.
        for (int k = 2; k < 4; k++)
        {
            var v = values.Skip(k * 4).Take(4).ToArray();
            Assert.InRange(v[0], 0.18, 0.32);                  // the press lands under the cursor
            Assert.True(v[1] > v[0] + 0.2, $"dragging right must follow the mouse: {v[0]:0.00} -> {v[1]:0.00}");
            Assert.InRange(v[2], 0.33, 0.47);                  // dragged back to 40 %
            Assert.Equal(v[2], v[3], 3);                       // released: it no longer follows
        }
    }

    static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
}
