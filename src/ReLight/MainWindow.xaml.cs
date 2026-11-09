using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ReLight.Audio;
using ReLight.Controls;
using ReLight.Core;
using ReLight.Core.Effects;
using ReLight.Services;
using MColor = System.Windows.Media.Color;

namespace ReLight;

public partial class MainWindow : Window
{
    static readonly string[] SwatchColors =
        ["#FFB45A", "#FFFFFF", "#FF0000", "#FF6A00", "#FFD000", "#00FF40", "#00E5FF", "#0040FF", "#8A2BFF", "#FF2D95"];

    readonly AppSettings _settings;
    readonly AdalightDevice _device;
    readonly LightEngine _engine;
    readonly DispatcherTimer _deviceChangeDebounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    IReadOnlyList<SerialPortInfo> _ports = [];
    TrayService? _tray;
    HotkeyService? _hotkeys;
    bool _loading = true; // sliders fire ValueChanged while the XAML is still loading
    bool _exiting;

    public MainWindow(AppSettings settings, AdalightDevice device, LightEngine engine)
    {
        _settings = settings;
        _device = device;
        _engine = engine;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;

        _device.StateChanged += _ => Dispatcher.BeginInvoke(() => { UpdateStatus(); UpdateLightInfo(); });
        Loc.Changed += ApplyTexts;
        _deviceChangeDebounce.Tick += (_, _) => { _deviceChangeDebounce.Stop(); RefreshPorts(); };
        _statusTimer.Tick += (_, _) => { UpdateLightInfo(); UpdateEffectTiles(); };

        _loading = true;
        BrightnessSlider.Value = Math.Clamp(_settings.Brightness, 5, 100);
        SmoothSlider.Value = _settings.Smoothing;
        SaturationSlider.Value = _settings.Saturation;
        PlaylistToggle.IsChecked = _settings.PlaylistOn;
        PlaylistSeconds.Value = _settings.PlaylistSeconds;
        PlaylistSeconds.ValueChanged += (_, _) => { _settings.PlaylistSeconds = PlaylistSeconds.Value; _settings.SaveSoon(); };
        BuildEffectTiles();
        MusicSlider.Value = _settings.MusicSensitivity;
        CinemaSmoothSlider.Value = _settings.CinemaSmoothing;
        CinemaDepthSlider.Value = _settings.CinemaDepth;
        CinemaSoundSlider.Value = _settings.CinemaSound;
        CinemaBarsToggle.IsChecked = _settings.CinemaBars;
        MicSlider.Value = _settings.MicSensitivity;
        _micTimer.Tick += (_, _) => UpdateMicMeter();
        PowerToggle.IsChecked = _settings.LightOn;
        BuildSwatches();
        ShowColor(ColorMath.FromHex(_settings.SolidColor, System.Drawing.Color.White), moveHue: true);
        _loading = false;
        ShowSliderValues();
        SelectModeTab();

        RefreshPorts();
        ApplyTexts();

        if (_settings.AutoConnect && (_settings.PortDeviceId != null || _settings.Port != null))
            StartDevice();
        _engine.Start();
        _statusTimer.Start();
        _exclusionTimer.Tick += (_, _) => ScanExclusions();
        _exclusionTimer.Start();
        HookForeground();
        UpdateStatus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Windows announces plugged/unplugged USB devices with WM_DEVICECHANGE; refresh the port list then.
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (msg == 0x0219) { _deviceChangeDebounce.Stop(); _deviceChangeDebounce.Start(); }
            return IntPtr.Zero;
        });

        _tray = new TrayService(this, _settings);
        _tray.OpenRequested += ShowFromTray;
        _tray.SettingsRequested += () =>
        {
            ShowFromTray(); // the settings window opens over the main window
            Settings_Click(this, new RoutedEventArgs());
        };
        _tray.ExitRequested += ExitApp;
        _tray.ToggleRequested += () => SetLight(!_settings.LightOn);
        _tray.ModeRequested += SetMode;

        _hotkeys = new HotkeyService(hwnd);
        ApplyHotkeys();
    }

    /// <summary>Register the shortcuts from settings; returns the ones Windows refused.</summary>
    public List<string> ApplyHotkeys()
    {
        var failed = new List<string>();
        if (_hotkeys == null) return failed;
        _hotkeys.Clear();
        if (!_hotkeys.Register(_settings.HotkeyToggle, () => SetLight(!_settings.LightOn))) failed.Add(_settings.HotkeyToggle);
        if (!_hotkeys.Register(_settings.HotkeyNextMode, NextMode)) failed.Add(_settings.HotkeyNextMode);
        return failed;
    }

    // ---------- strip ----------

    void StartDevice() => _device.Start(ResolvePort, _settings.BaudRate);

    /// <summary>Called by the device thread before each connection attempt.</summary>
    string? ResolvePort()
    {
        var ports = SerialPortCatalog.List();
        var found = SerialPortCatalog.Resolve(ports, _settings.PortDeviceId, _settings.PortHardwareKey, _settings.Port);
        if (found != null && found.PortName != _settings.Port)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Remember(found);
                RefreshPorts();
            });
        }
        return found?.PortName;
    }

    void Remember(SerialPortInfo port)
    {
        _settings.Port = port.PortName;
        if (port.DeviceId != null) _settings.PortDeviceId = port.DeviceId;
        if (port.HardwareKey != null) _settings.PortHardwareKey = port.HardwareKey;
        _settings.Save();
    }

    void RefreshPorts()
    {
        _ports = SerialPortCatalog.List();
        _loading = true;
        PortBox.Items.Clear();
        foreach (var p in _ports)
        {
            var text = string.IsNullOrEmpty(p.FriendlyName) ? p.PortName : Loc.T("port.item", p.PortName, p.FriendlyName);
            PortBox.Items.Add(new ComboBoxItem { Content = text, Tag = p });
        }

        var current = SerialPortCatalog.Resolve(_ports, _settings.PortDeviceId, _settings.PortHardwareKey, _settings.Port);
        if (current == null && _settings.Port == null)
        {
            // First start: pick the board if it is the only thing that looks like one.
            current = SerialPortCatalog.Guess(_ports);
            if (current != null) Remember(current);
        }
        if (_ports.Count == 0)
        {
            PortBox.Items.Add(new ComboBoxItem { Content = Loc.T("port.none.short"), ToolTip = Loc.T("port.none") });
            PortBox.SelectedIndex = 0;
        }
        else
            PortBox.SelectedIndex = current == null ? -1 : _ports.ToList().IndexOf(current);
        PortBox.IsEnabled = _ports.Count > 0;
        _loading = false;
        UpdateStatus();
    }

    void Port_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PortBox.SelectedItem is not ComboBoxItem { Tag: SerialPortInfo port }) return;
        // The user chose another board explicitly: forget the old identity.
        _settings.PortDeviceId = null;
        _settings.PortHardwareKey = null;
        Remember(port);
        if (_device.State != DeviceState.Disconnected) StartDevice();
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.Brightness = (int)Math.Round(e.NewValue);
        _settings.SaveSoon();
        ShowSliderValues();
    }

    void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_device.State != DeviceState.Disconnected)
        {
            _device.Blackout(OutputCount());
            _device.Stop();
            _settings.AutoConnect = false;
        }
        else
        {
            if (PortBox.SelectedItem is ComboBoxItem { Tag: SerialPortInfo port }) Remember(port);
            if (_settings.Port == null && _settings.PortDeviceId == null)
            {
                MessageWindow.Show(this, Loc.T("port.none"), MessageKind.Warning);
                return;
            }
            _settings.AutoConnect = true;
            StartDevice();
        }
        _settings.Save();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var state = _device.State;
        StatusText.Text = Loc.T("state." + state);
        // The port itself is shown in the list next to it; below the row only what went wrong.
        StatusDetail.Text = state == DeviceState.Error && !_device.PortBusy ? _device.LastError ?? "" : "";
        StatusDetail.Visibility = StatusDetail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BusyHint.Visibility = state == DeviceState.Error && _device.PortBusy ? Visibility.Visible : Visibility.Collapsed;

        var brushKey = state switch
        {
            DeviceState.Connected => "Rl.Status.Good",
            DeviceState.Connecting or DeviceState.Searching => "Rl.Status.Warn",
            DeviceState.Error => "Rl.Status.Bad",
            _ => "Rl.Status.Idle",
        };
        StatusDot.SetResourceReference(Shape.FillProperty, brushKey);
        StatusGlow.SetResourceReference(Shape.FillProperty, brushKey);
        ConnectButton.Content = Loc.T(state == DeviceState.Disconnected ? "device.connect" : "device.disconnect");
    }

    /// <summary>
    /// LEDs per packet. Some firmwares always read a fixed number of LEDs and swallow the next
    /// packet if we send fewer, so we always send at least as many as the strip has (padded black).
    /// </summary>
    int OutputCount() => Math.Max(_settings.Layout.Total, _settings.KnownLedCount);

    // ---------- light ----------

    void SetLight(bool on)
    {
        _settings.LightOn = on;
        _settings.Save();
        _loading = true;
        PowerToggle.IsChecked = on;
        _loading = false;
        PowerToggle.Content = Loc.T(on ? "power.on" : "power.off");
        _tray?.Refresh();
        UpdateLightInfo();
    }

    void Power_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) SetLight(PowerToggle.IsChecked == true);
    }

    void SetMode(LightMode mode)
    {
        _settings.Mode = mode;
        _settings.Save();
        _engine.Refresh();
        SelectModeTab();
        if (!_settings.LightOn) SetLight(true);
        _tray?.Refresh();
    }

    void NextMode()
    {
        var modes = Enum.GetValues<LightMode>();
        SetMode(modes[(Array.IndexOf(modes, _settings.Mode) + 1) % modes.Length]);
    }

    void SelectModeTab()
    {
        _loading = true;
        ModeScreen.IsChecked = _settings.Mode == LightMode.Screen;
        ModeColor.IsChecked = _settings.Mode == LightMode.Color;
        ModeRainbow.IsChecked = _settings.Mode == LightMode.Rainbow;
        ModeMusic.IsChecked = _settings.Mode == LightMode.Music;
        ModeCinema.IsChecked = _settings.Mode == LightMode.Cinema;
        ModeMic.IsChecked = _settings.Mode == LightMode.Mic;
        _loading = false;
        ScreenPanel.Visibility = Show(LightMode.Screen);
        ColorPanel.Visibility = Show(LightMode.Color);
        RainbowPanel.Visibility = Show(LightMode.Rainbow);
        MusicPanel.Visibility = Show(LightMode.Music);
        CinemaPanel.Visibility = Show(LightMode.Cinema);
        MicPanel.Visibility = Show(LightMode.Mic);
        if (_settings.Mode == LightMode.Mic) _micTimer.Start(); else _micTimer.Stop();
        UpdateLightInfo();

        Visibility Show(LightMode m) => _settings.Mode == m ? Visibility.Visible : Visibility.Collapsed;
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var mode = sender == ModeColor ? LightMode.Color : sender == ModeRainbow ? LightMode.Rainbow
            : sender == ModeMusic ? LightMode.Music : sender == ModeCinema ? LightMode.Cinema : sender == ModeMic ? LightMode.Mic : LightMode.Screen;
        SetMode(mode);
    }

    void UpdateLightInfo()
    {
        bool connected = _device.State == DeviceState.Connected;
        LightHint.Visibility = !connected && _settings.LightOn ? Visibility.Visible : Visibility.Collapsed;
        CaptureInfo.Text = connected && _settings.LightOn && _engine.CaptureName != null
            ? Loc.T("screen.status", _engine.CaptureName, Math.Round(_engine.Fps))
            : "";
        CaptureInfo.Visibility = CaptureInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MusicError.Text = _engine.AudioError != null ? Loc.T("music.error", _engine.AudioError) : "";
        MusicError.Visibility = MusicError.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MicError.Text = MusicError.Text;
        MicError.Visibility = MusicError.Visibility;
        CinemaError.Text = MusicError.Text;
        CinemaError.Visibility = MusicError.Visibility;

        // Cinema: where the picture is found and how the capture runs.
        if (connected && _settings.LightOn && _engine.CaptureName != null && _settings.Mode == LightMode.Cinema)
        {
            var bars = !_settings.CinemaBars ? Loc.T("cinema.bars.offinfo")
                : _engine.BarsVertical > 0 && _engine.BarsHorizontal > 0
                    ? Loc.T("cinema.bars.both", Percent(_engine.BarsVertical), Percent(_engine.BarsHorizontal))
                : _engine.BarsVertical > 0 ? Loc.T("cinema.bars.tb", Percent(_engine.BarsVertical))
                : _engine.BarsHorizontal > 0 ? Loc.T("cinema.bars.lr", Percent(_engine.BarsHorizontal))
                : Loc.T("cinema.bars.none");
            CinemaInfo.Text = bars + " · " + Loc.T("screen.status", _engine.CaptureName, Math.Round(_engine.Fps));
        }
        else
            CinemaInfo.Text = "";
        CinemaInfo.Visibility = CinemaInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        static string Percent(double share) => $"{Math.Round(share * 100)}%";
    }

    void ShowSliderValues()
    {
        BrightnessValue.Text = $"{_settings.Brightness}%";
        SmoothValue.Text = $"{_settings.Smoothing}%";
        SaturationValue.Text = $"{_settings.Saturation}%";
        MusicValue.Text = $"{_settings.MusicSensitivity}%";
        MicValue.Text = $"{_settings.MicSensitivity}%";
        CinemaSmoothValue.Text = $"{_settings.CinemaSmoothing}%";
        CinemaDepthValue.Text = $"{_settings.CinemaDepth}%";
        CinemaSoundValue.Text = $"{_settings.CinemaSound}%";
    }

    void Smooth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.Smoothing = v, e.NewValue);
    void Saturation_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.Saturation = v, e.NewValue);
    void Music_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.MusicSensitivity = v, e.NewValue);
    void CinemaSmooth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.CinemaSmoothing = v, e.NewValue);
    void CinemaSound_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.CinemaSound = v, e.NewValue);

    void CinemaDepth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.CinemaDepth = v, e.NewValue);

    void CinemaBars_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CinemaBars = CinemaBarsToggle.IsChecked == true;
        _settings.Save();
        UpdateLightInfo();
    }

    CinemaTestWindow? _cinemaTest;

    void CinemaTest_Click(object sender, RoutedEventArgs e)
    {
        if (_cinemaTest != null) { _cinemaTest.Activate(); return; }
        _cinemaTest = new CinemaTestWindow(_settings, _engine) { Owner = this };
        _cinemaTest.Closed += (_, _) => _cinemaTest = null;
        _cinemaTest.Show();
    }

    void MicSens_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SetValue(v => _settings.MicSensitivity = v, e.NewValue);

    // ---------- microphone ----------

    readonly DispatcherTimer _micTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    List<(string id, string name)> _mics = [];

    void FillMicrophones()
    {
        bool was = _loading;
        _loading = true;
        _mics = AudioAnalyzer.Microphones();
        MicBox.Items.Clear();
        MicBox.Items.Add(Loc.T("mic.default"));
        foreach (var (_, name) in _mics) MicBox.Items.Add(name);
        int index = _mics.FindIndex(m => m.id == _settings.MicDevice);
        MicBox.SelectedIndex = index < 0 ? 0 : index + 1;
        _loading = was;
    }

    void MicSettings_Click(object sender, RoutedEventArgs e)
    {
        new MicSettingsWindow(_settings, _engine) { Owner = this }.ShowDialog();
        _settings.Save();
    }

    void Mic_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicBox.SelectedIndex < 0) return;
        _settings.MicDevice = MicBox.SelectedIndex == 0 ? null : _mics[MicBox.SelectedIndex - 1].id;
        _settings.Save();
        _engine.Refresh();
    }

    /// <summary>Live loudness bar, so it is visible that the microphone hears.</summary>
    void UpdateMicMeter() =>
        MicMeter.Width = Math.Max(0, MicTrack.ActualWidth * Math.Clamp(_engine.MicLevel, 0, 1));

    void SetValue(Action<int> apply, double value)
    {
        if (_loading) return;
        apply((int)Math.Round(value));
        _settings.SaveSoon();
        ShowSliderValues();
    }

    void BuildSwatches()
    {
        foreach (var hex in SwatchColors)
        {
            var c = ColorMath.FromHex(hex, System.Drawing.Color.White);
            var swatch = new RadioButton
            {
                GroupName = "Swatch",
                Tag = hex,
                Background = new SolidColorBrush(MColor.FromRgb(c.R, c.G, c.B)),
            };
            swatch.SetResourceReference(StyleProperty, "Rl.Swatch");
            swatch.Checked += (_, _) =>
            {
                if (_loading) return;
                ShowColor(c, moveHue: true);
                SaveColor(c);
            };
            Swatches.Children.Add(swatch);
        }
    }

    void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var c = ColorMath.FromHsv(e.NewValue / 360, 1, 1);
        ShowColor(c, moveHue: false);
        SaveColor(c);
    }

    void ShowColor(System.Drawing.Color c, bool moveHue)
    {
        bool was = _loading;
        _loading = true;
        ColorPreview.Background = new SolidColorBrush(MColor.FromRgb(c.R, c.G, c.B));
        var hex = ColorMath.ToHex(c);
        foreach (RadioButton s in Swatches.Children) s.IsChecked = string.Equals((string)s.Tag, hex, StringComparison.OrdinalIgnoreCase);
        if (moveHue) HueSlider.Value = ColorMath.ToHsv(c).h * 360;
        _loading = was;
    }

    void SaveColor(System.Drawing.Color c)
    {
        _settings.SolidColor = ColorMath.ToHex(c);
        _settings.SaveSoon();
    }

    static readonly string[] CaptureMethods = ["auto", "dxgi", "gdi"];

    void FillCaptureMethods()
    {
        bool was = _loading;
        _loading = true;
        CaptureBox.Items.Clear();
        foreach (var m in CaptureMethods) CaptureBox.Items.Add(Loc.T("capture." + m));
        CaptureBox.SelectedIndex = Math.Max(0, Array.IndexOf(CaptureMethods, _settings.CaptureMethod));
        _loading = was;
    }

    void Capture_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CaptureBox.SelectedIndex < 0) return;
        _settings.CaptureMethod = CaptureMethods[CaptureBox.SelectedIndex];
        _settings.Save();
        _engine.Refresh();
    }

    void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.LightOn) SetLight(true);
        new ScreenTestWindow().ShowDialog();
    }

    // ---------- exclusions ----------

    readonly DispatcherTimer _exclusionTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _scanning;

    /// <summary>Every two seconds look (off the UI thread) for a running excluded program.</summary>
    async void ScanExclusions()
    {
        if (_scanning) { _scanAgain = true; return; }
        _scanning = true;
        try
        {
            do
            {
                _scanAgain = false;
                bool needed = _settings.Exclusions.Count > 0 || _settings.AutoUnity || _settings.AutoUnreal || _settings.AutoPlayers;
                var found = needed ? await Task.Run(() => ExclusionScanner.Find(_settings)) : null;
                if (found?.Action != _engine.Exclusion?.Action || found?.Program != _engine.Exclusion?.Program
                    || found?.Color != _engine.Exclusion?.Color || found?.Effect != _engine.Exclusion?.Effect)
                    _engine.Exclusion = found;
                ExclusionStatus.Text = found == null ? "" : Loc.T("ex.active", found.Program, Loc.T("exa." + found.Action));
                ExclusionStatus.Visibility = found == null ? Visibility.Collapsed : Visibility.Visible;
            } while (_scanAgain);
        }
        finally
        {
            _scanning = false;
        }
    }

    bool _scanAgain;

    // Another window came to the front: look at once, so a player switches to cinema without waiting for the timer.
    WinEventProc? _foregroundHook;
    IntPtr _foregroundHookHandle;

    void HookForeground()
    {
        _foregroundHook = (_, _, _, _, _, _, _) => ScanExclusions();
        _foregroundHookHandle = SetWinEventHook(3 /* EVENT_SYSTEM_FOREGROUND */, 3, IntPtr.Zero, _foregroundHook, 0, 0, 0 /* out of context */);
    }

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    void Exclusions_Click(object sender, RoutedEventArgs e)
    {
        // Changes work at once (the window calls back after each one); Cancel puts the old ones back.
        var window = new ExclusionsWindow(_settings) { Owner = this };
        window.Applied += ScanExclusions;
        if (window.ShowDialog() == true) _settings.Save();
        ScanExclusions();
    }

    // ---------- rainbow effects ----------

    sealed record EffectTile(EffectInfo Info, Border Root, EffectPreview Preview, TextBlock Name, CheckBox Check);

    readonly List<EffectTile> _tiles = [];

    void BuildEffectTiles()
    {
        foreach (var info in EffectCatalog.All)
        {
            var preview = new EffectPreview { Height = 54, Settings = _settings.EffectFor(info.Id) };
            var name = new TextBlock
            {
                FontSize = 11.5,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 2, 2, 4),
            };
            var check = new CheckBox { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5) };
            var gear = new Button
            {
                Content = "\uE713",
                Width = 24, Height = 24, MinHeight = 24, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3),
            };
            gear.SetResourceReference(StyleProperty, "Rl.Button.Icon");
            gear.SetResourceReference(ToolTipProperty, "Loc.fx.edit");
            gear.SetResourceReference(AutomationProperties.NameProperty, "Loc.fx.edit");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(name, 1);
            grid.Children.Add(preview);
            grid.Children.Add(name);
            grid.Children.Add(check);
            grid.Children.Add(gear);

            var root = new Border
            {
                Width = 110,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = grid,
            };
            var tile = new EffectTile(info, root, preview, name, check);
            _tiles.Add(tile);

            root.MouseEnter += (_, _) => PaintTile(tile);
            root.MouseLeave += (_, _) => PaintTile(tile);
            root.MouseLeftButtonUp += (_, _) => TileClicked(tile);
            check.Click += (_, _) => SetInPlaylist(info.Id, check.IsChecked == true);
            gear.Click += (_, _) => EditEffect(info);
            EffectTiles.Children.Add(root);
        }
    }

    void TileClicked(EffectTile tile)
    {
        if (_settings.PlaylistOn)
        {
            SetInPlaylist(tile.Info.Id, !_settings.Playlist.Contains(tile.Info.Id));
            return;
        }
        _settings.Effect = tile.Info.Id;
        _settings.Save();
        UpdateEffectTiles();
    }

    void SetInPlaylist(string id, bool include)
    {
        var list = _settings.Playlist.Where(x => x != id).ToList();
        if (include) list.Add(id);
        // Keep the catalog order so the playlist plays in the order the tiles are shown.
        _settings.Playlist = EffectCatalog.All.Select(e => e.Id).Where(list.Contains).ToList();
        _settings.Save();
        UpdateEffectTiles();
    }

    /// <summary>Anything ticked: untick everything (to pick a few from scratch); nothing ticked: tick all.</summary>
    void PlaylistAll_Click(object sender, RoutedEventArgs e)
    {
        _settings.Playlist = _settings.Playlist.Count > 0 ? [] : EffectCatalog.All.Select(x => x.Id).ToList();
        _settings.Save();
        UpdateEffectTiles();
    }

    void Playlist_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.PlaylistOn = PlaylistToggle.IsChecked == true;
        if (_settings.PlaylistOn && _settings.Playlist.Count == 0) _settings.Playlist = [_settings.Effect];
        _settings.Save();
        UpdateEffectTiles();
    }

    void EffectSummary_Click(object sender, RoutedEventArgs e)
    {
        EffectGallery.Visibility = EffectGallery.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateEffectTiles();
    }

    /// <summary>Folded view: the effect playing (or chosen) now, or the playlist size.</summary>
    void UpdateEffectSummary()
    {
        bool open = EffectGallery.Visibility == Visibility.Visible;
        var id = _settings.PlaylistOn ? _engine.Effects.CurrentId ?? _settings.Playlist.FirstOrDefault() ?? _settings.Effect : _settings.Effect;
        var info = EffectCatalog.Get(id);
        if (SummaryPreview.Settings?.Id != info.Id) SummaryPreview.Settings = _settings.EffectFor(info.Id);
        SummaryName.Text = _settings.PlaylistOn
            ? Loc.T("fx.summary.playlist", string.Join(" · ", _settings.Playlist.Select(p => Loc.T("fx." + p))))
            : Loc.T(info.NameKey);
        SummaryName.TextTrimming = TextTrimming.CharacterEllipsis;
        SummaryHint.Text = Loc.T(open ? "fx.summary.close" : "fx.summary.open");
        SummaryChevron.Text = open ? "" : "";
    }

    void UpdateEffectTiles()
    {
        UpdateEffectSummary();
        bool playlist = _settings.PlaylistOn;
        PlaylistTiming.Visibility = playlist ? Visibility.Visible : Visibility.Hidden;
        EffectHint.Text = Loc.T(playlist ? "fx.pick.playlist" : "fx.pick");
        PlaylistAllButton.Visibility = playlist ? Visibility.Visible : Visibility.Collapsed;
        var allTip = Loc.T(_settings.Playlist.Count > 0 ? "fx.none" : "fx.all");
        PlaylistAllButton.ToolTip = allTip;
        AutomationProperties.SetName(PlaylistAllButton, allTip);
        foreach (var t in _tiles)
        {
            t.Name.Text = Loc.T(t.Info.NameKey);
            t.Root.ToolTip = Loc.T(t.Info.HintKey);
            t.Check.Visibility = playlist ? Visibility.Visible : Visibility.Collapsed;
            t.Check.IsChecked = _settings.Playlist.Contains(t.Info.Id);
            PaintTile(t);
        }
    }

    /// <summary>Chosen tile gets the accent frame; in playlist mode the one playing now gets accent text.</summary>
    void PaintTile(EffectTile t)
    {
        bool chosen = _settings.PlaylistOn ? _settings.Playlist.Contains(t.Info.Id) : _settings.Effect == t.Info.Id;
        bool playing = _settings.Mode == LightMode.Rainbow && _engine.Effects.CurrentId == t.Info.Id && _device.State == DeviceState.Connected;
        t.Root.SetResourceReference(Border.BackgroundProperty, chosen ? "Rl.Button.Accent.Background" : "Rl.Input");
        t.Root.SetResourceReference(Border.BorderBrushProperty, chosen ? "Rl.Accent" : t.Root.IsMouseOver ? "Rl.Accent.Dim" : "Rl.Border");
        t.Name.SetResourceReference(TextBlock.ForegroundProperty, playing || (chosen && !_settings.PlaylistOn) ? "Rl.Accent" : "Rl.Text.Primary");
        t.Name.FontWeight = playing ? FontWeights.SemiBold : FontWeights.Normal;
    }

    void EditEffect(EffectInfo info)
    {
        var editor = new EffectEditorWindow(info, _settings.EffectFor(info.Id), _engine.Effects) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _settings.SetEffect(editor.Result);
            _settings.Save();
            var tile = _tiles.First(t => t.Info.Id == info.Id);
            tile.Preview.Settings = _settings.EffectFor(info.Id);
            SummaryPreview.Settings = null;
            UpdateEffectSummary();
        }
    }

    // ---------- screen and layout ----------

    void Screen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ScreenBox.SelectedIndex < 0) return;
        _settings.Monitor = ScreenHelper.List()[ScreenBox.SelectedIndex].screen.DeviceName;
        _settings.Save();
        _engine.Refresh();
    }

    void ShowLayout()
    {
        var l = _settings.Layout;
        Preview.Layout = new LedLayout(l);
        LayoutTotal.Text = Loc.T("layout.total", l.Total);
        LayoutSummary.Text = Loc.T("layout.summary", l.Top, l.Right, l.Bottom, l.Left);
        LayoutKnown.Text = _settings.KnownLedCount > 0 ? Loc.T("layout.known", _settings.KnownLedCount) : "";
        LayoutKnown.Visibility = _settings.KnownLedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Setup_Click(object sender, RoutedEventArgs e)
    {
        _engine.Suspend();
        try
        {
            new CalibrationWindow(_settings, _device, ScreenHelper.Find(_settings.Monitor)) { Owner = this }.ShowDialog();
        }
        finally
        {
            _settings.Save();
            _device.Blackout(OutputCount());
            _engine.Resume();
        }
        ShowLayout();
    }

    void Find_Click(object sender, RoutedEventArgs e)
    {
        _engine.Suspend();
        var w = new DotTestWindow(_settings, _device, ScreenHelper.Find(_settings.Monitor)) { Owner = this };
        try
        {
            w.ShowDialog();
        }
        finally
        {
            _settings.Save();
            _device.Blackout(Math.Max(OutputCount(), w.TestedCount));
            _engine.Resume();
        }
        ShowLayout();
    }

    // ---------- title bar, settings, tray ----------

    void ApplyTexts()
    {
        _loading = true;
        LangButton.Content = Loc.Language == "ru" ? "Ru" : "En";
        PowerToggle.Content = Loc.T(_settings.LightOn ? "power.on" : "power.off");

        ScreenBox.Items.Clear();
        var screens = ScreenHelper.List();
        foreach (var (_, label) in screens) ScreenBox.Items.Add(label);
        var current = ScreenHelper.Find(_settings.Monitor);
        ScreenBox.SelectedIndex = Math.Max(0, screens.ToList().FindIndex(s => s.screen.DeviceName == current.DeviceName));
        _loading = false;

        if (PortBox.Items.Count > 0) RefreshPorts();
        FillCaptureMethods();
        FillMicrophones();
        ShowLayout();
        UpdateStatus();
        UpdateLightInfo();
        UpdateEffectTiles();
        _tray?.Refresh();
    }

    public void ApplyTheme()
    {
        ThemeManager.Apply(_settings.Theme);
        _settings.Save();
        Preview.InvalidateVisual();
        UpdateStatus();
    }

    void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (LangButton.ContextMenu is not { } menu) return;
        LangRu.IsChecked = Loc.Language == "ru";
        LangEn.IsChecked = Loc.Language == "en";
        menu.PlacementTarget = LangButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void LangItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string lang } || lang == Loc.Language) return;
        _settings.Language = lang;
        _settings.Save();
        Loc.SetLanguage(lang);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        _hotkeys?.Clear(); // otherwise Windows swallows the shortcut being recorded
        var window = new SettingsWindow(_settings) { Owner = this };
        // Each change works at once; Cancel calls this again with the old values.
        // Frame rate, capture depth and color order are read by the engine on every frame by themselves.
        string theme = _settings.Theme, language = _settings.Language;
        int baud = _settings.BaudRate;
        window.Applied += () =>
        {
            if (_settings.Theme != theme) { theme = _settings.Theme; ApplyTheme(); }
            if (_settings.Language != language) { language = _settings.Language; Loc.SetLanguage(language); }
            if (_settings.BaudRate != baud)
            {
                baud = _settings.BaudRate;
                if (_device.State != DeviceState.Disconnected) StartDevice();
            }
        };
        bool saved = window.ShowDialog() == true;
        _settings.Save();
        if (!saved)
        {
            ApplyHotkeys();
            return;
        }
        var failed = ApplyHotkeys();
        if (failed.Count > 0)
            MessageWindow.Show(this, string.Join("\n", failed.Select(f => Loc.T("hk.busy", f))), MessageKind.Warning);
    }

    void About_Click(object sender, RoutedEventArgs e) => AboutWindow.ShowFor(this);

    void ToTray_Click(object sender, RoutedEventArgs e) => Hide();

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        Loc.Changed -= ApplyTexts;
        _statusTimer.Stop();
        _exclusionTimer.Stop();
        if (_foregroundHookHandle != IntPtr.Zero) UnhookWinEvent(_foregroundHookHandle);
        _micTimer.Stop();
        _engine.Dispose();
        _device.Blackout(OutputCount());
        _device.Stop();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _settings.Save();
        base.OnClosing(e);
    }
}
