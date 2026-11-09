using System.Windows;
using ReLight.Core;
using ReLight.Services;

namespace ReLight;

/// <summary>
/// Program settings. Every change works at once (the main window reacts to <see cref="Applied"/>);
/// Save keeps it, Cancel or closing puts back what was there. Hotkeys are the exception: they are
/// switched off while the window is open (a new one could not be recorded otherwise) and count on closing.
/// </summary>
public partial class SettingsWindow : Window
{
    static readonly int[] BaudRates = [57600, 115200, 230400, 250000, 460800, 500000, 921600, 1000000];

    readonly AppSettings _settings;
    readonly (string language, bool autostart, bool startMin, bool closeTray, string theme, int baud, string order, int fps, int depth) _before;
    bool _loading = true, _saved;

    /// <summary>Called after every change.</summary>
    public event Action? Applied;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        LanguageBox.Items.Add(Loc.T("lang.auto"));
        LanguageBox.Items.Add("Русский");
        LanguageBox.Items.Add("English");
        LanguageBox.SelectedIndex = settings.Language switch { "ru" => 1, "en" => 2, _ => 0 };

        AutostartBox.IsChecked = AutostartService.IsEnabled();
        StartMinBox.IsChecked = settings.StartMinimized;
        CloseTrayBox.IsChecked = settings.CloseToTray;

        ThemeSystem.IsChecked = settings.Theme is not ("dark" or "light");
        ThemeDark.IsChecked = settings.Theme == "dark";
        ThemeLight.IsChecked = settings.Theme == "light";

        foreach (var b in BaudRates) BaudBox.Items.Add(b);
        BaudBox.SelectedItem = BaudRates.Contains(settings.BaudRate) ? settings.BaudRate : 115200;
        foreach (var o in ColorOutput.Orders) OrderBox.Items.Add(o);
        OrderBox.SelectedItem = ColorOutput.Orders.Contains(settings.ColorOrder) ? settings.ColorOrder : "RGB";

        FpsBox.Value = settings.ScreenFps;
        DepthBox.Value = settings.CaptureDepth;
        ToggleKeyBox.Shortcut = settings.HotkeyToggle;
        ModeKeyBox.Shortcut = settings.HotkeyNextMode;

        _before = (settings.Language, AutostartBox.IsChecked == true, settings.StartMinimized, settings.CloseToTray,
            settings.Theme, settings.BaudRate, settings.ColorOrder, settings.ScreenFps, settings.CaptureDepth);
        _loading = false;

        LanguageBox.SelectionChanged += (_, _) => Apply();
        BaudBox.SelectionChanged += (_, _) => Apply();
        OrderBox.SelectionChanged += (_, _) => Apply();
        foreach (var box in new[] { AutostartBox, StartMinBox, CloseTrayBox }) box.Click += (_, _) => Apply();
        foreach (var radio in new[] { ThemeSystem, ThemeDark, ThemeLight }) radio.Checked += (_, _) => Apply();
        FpsBox.ValueChanged += (_, _) => Apply();
        DepthBox.ValueChanged += (_, _) => Apply();
        Closing += (_, _) => { if (!_saved) Restore(); };
    }

    void Apply()
    {
        if (_loading) return;
        _settings.Language = LanguageBox.SelectedIndex switch { 1 => "ru", 2 => "en", _ => "auto" };
        bool autostart = AutostartBox.IsChecked == true;
        if (autostart != AutostartService.IsEnabled()) AutostartService.SetEnabled(autostart);
        _settings.StartMinimized = StartMinBox.IsChecked == true;
        _settings.CloseToTray = CloseTrayBox.IsChecked == true;
        _settings.Theme = ThemeDark.IsChecked == true ? "dark" : ThemeLight.IsChecked == true ? "light" : "system";
        if (BaudBox.SelectedItem is int baud) _settings.BaudRate = baud;
        if (OrderBox.SelectedItem is string order) _settings.ColorOrder = order;
        _settings.ScreenFps = FpsBox.Value;
        _settings.CaptureDepth = DepthBox.Value;
        Applied?.Invoke();
    }

    void Restore()
    {
        var b = _before;
        _settings.Language = b.language;
        if (b.autostart != AutostartService.IsEnabled()) AutostartService.SetEnabled(b.autostart);
        _settings.StartMinimized = b.startMin;
        _settings.CloseToTray = b.closeTray;
        _settings.Theme = b.theme;
        _settings.BaudRate = b.baud;
        _settings.ColorOrder = b.order;
        _settings.ScreenFps = b.fps;
        _settings.CaptureDepth = b.depth;
        Applied?.Invoke();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        _settings.HotkeyToggle = ToggleKeyBox.Shortcut;
        _settings.HotkeyNextMode = ModeKeyBox.Shortcut;
        _saved = true;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void About_Click(object sender, RoutedEventArgs e) => AboutWindow.ShowFor(this);
}
