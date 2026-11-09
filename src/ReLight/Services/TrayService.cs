using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ReLight.Core;
using WinForms = System.Windows.Forms;

namespace ReLight.Services;

/// <summary>
/// Tray icon. Left click opens the window; right click shows a themed WPF menu
/// (open, settings, on/off with a state dot, modes with a tick on the current one, exit) instead of the grey system one.
/// </summary>
public sealed class TrayService : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly Window _owner;
    readonly AppSettings _settings;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? ToggleRequested;
    public event Action<LightMode>? ModeRequested;

    public TrayService(Window owner, AppSettings settings)
    {
        _owner = owner;
        _settings = settings;
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
        _icon = new WinForms.NotifyIcon
        {
            Icon = stream != null ? new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke();
            else if (e.Button == WinForms.MouseButtons.Right) ShowMenu();
        };
        Refresh();
    }

    /// <summary>Update the hover text after mode or power changes.</summary>
    public void Refresh()
    {
        var text = _settings.LightOn ? Loc.T("tray.tip.on", Loc.T("mode." + _settings.Mode)) : Loc.T("tray.tip.off");
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Loc.T("tray.open"), () => OpenRequested?.Invoke()));
        menu.Items.Add(Item(Loc.T("tip.settings"), () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        // The item says what a click does; its dot shows the state now: green lit, red off.
        var power = Item(Loc.T(_settings.LightOn ? "tray.light.off" : "tray.light.on"), () => ToggleRequested?.Invoke());
        var dot = new Ellipse { Width = 8, Height = 8 };
        dot.SetResourceReference(Shape.FillProperty, _settings.LightOn ? "Rl.Status.Good" : "Rl.Status.Bad");
        power.Icon = dot;
        menu.Items.Add(power);
        foreach (var mode in Enum.GetValues<LightMode>())
        {
            var m = mode;
            var item = Item(Loc.T("mode." + mode), () => ModeRequested?.Invoke(m));
            item.Icon = ModeIcon(mode);
            item.Padding = new Thickness(16, 7, 32, 7); // modes stand indented under the power item
            item.IsChecked = _settings.Mode == mode; // the tick replaces the icon of the current mode
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("tray.exit"), () => ExitRequested?.Invoke()));

        // The menu must belong to a foreground window, or it will not close when clicking elsewhere.
        var hwnd = new WindowInteropHelper(_owner).EnsureHandle();
        SetForegroundWindow(hwnd);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>The same glyphs as on the mode tabs of the main window.</summary>
    static TextBlock ModeIcon(LightMode mode)
    {
        var icon = new TextBlock
        {
            Text = mode switch
            {
                LightMode.Screen => "",
                LightMode.Color => "",
                LightMode.Rainbow => "",
                LightMode.Music => "",
                LightMode.Cinema => "",
                _ => "",
            },
            FontSize = 12,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Rl.Text.Muted");
        icon.Opacity = 0.6; // pale, so the green tick of the current mode stands out
        return icon;
    }

    static MenuItem Item(string text, Action onClick)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => onClick();
        return item;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);
}
