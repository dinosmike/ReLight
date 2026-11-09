using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ReLight.Core;
using WinScreen = System.Windows.Forms.Screen;

namespace ReLight.Services;

public static class ScreenHelper
{
    /// <summary>Screens left to right with labels people recognise ("Screen 2 — 3840×2160").</summary>
    public static IReadOnlyList<(WinScreen screen, string label)> List()
    {
        var screens = WinScreen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
        var list = new List<(WinScreen, string)>();
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            list.Add((s, Loc.T("screen.item", i + 1, s.Bounds.Width, s.Bounds.Height) + (s.Primary ? Loc.T("screen.primary") : "")));
        }
        return list;
    }

    public static WinScreen Find(string? deviceName) =>
        WinScreen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName) ?? WinScreen.PrimaryScreen ?? WinScreen.AllScreens[0];

    /// <summary>
    /// Make a borderless window cover exactly one screen, in physical pixels,
    /// so it lands on the TV whatever scaling each monitor uses.
    /// </summary>
    public static void CoverScreen(Window window, WinScreen screen)
    {
        var b = screen.Bounds;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Topmost = true;
        window.Left = b.Left;
        window.Top = b.Top;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, HwndTopmost, b.Left, b.Top, b.Width, b.Height, SwpShowWindow);
        };
        window.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, HwndTopmost, b.Left, b.Top, b.Width, b.Height, SwpShowWindow);
        };
    }

    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
