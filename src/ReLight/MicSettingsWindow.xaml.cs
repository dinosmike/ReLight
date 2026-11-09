using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ReLight.Core;

namespace ReLight;

/// <summary>
/// How the microphone mode reacts. Changes go to the strip at once; Cancel restores the old options.
/// </summary>
public partial class MicSettingsWindow : Window
{
    static readonly string[] RingColorKeys = ["random", "white", "voice"];

    readonly AppSettings _settings;
    readonly LightEngine _engine;
    readonly MicOptions _original;
    readonly MicOptions _work;
    readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool _loading = true;

    public MicSettingsWindow(AppSettings settings, LightEngine engine)
    {
        _settings = settings;
        _engine = engine;
        _original = settings.Mic.Clone();
        _work = settings.Mic.Clone();
        InitializeComponent();

        foreach (var k in RingColorKeys) RingColorsBox.Items.Add(Loc.T("micset.rings." + k));
        ShowValues();
        _loading = false;

        _meterTimer.Tick += (_, _) => UpdateMeter();
        _meterTimer.Start();
        Closed += (_, _) => _meterTimer.Stop();
        Closing += (_, _) => { if (DialogResult != true) _settings.Mic = _original; }; // Alt+F4 is a cancel too
    }

    void ShowValues()
    {
        _loading = true;
        GateSlider.Value = _work.Gate;
        GlowBox.IsChecked = _work.Glow;
        GlowSlider.Value = _work.GlowLevel;
        VoiceToggle.IsChecked = _work.Voice;
        LowHue.Value = _work.VoiceLowHue;
        HighHue.Value = _work.VoiceHighHue;
        WaveSlider.Value = _work.WaveSpeed;
        ClapsToggle.IsChecked = _work.Claps;
        RingColorsBox.SelectedIndex = Math.Max(0, Array.IndexOf(RingColorKeys, _work.RingColors));
        RingSlider.Value = _work.RingSpeed;
        AfterSlider.Value = _work.Afterglow;
        _loading = false;
        ShowLabels();
    }

    void ShowLabels()
    {
        GateValue.Text = $"{_work.GateDb:0} dB";
        GlowValue.Text = $"{_work.GlowLevel}%";
        WaveValue.Text = $"{_work.WaveSpeed}%";
        RingValue.Text = $"{_work.RingSpeed}%";
        AfterValue.Text = $"{_work.Afterglow}%";
        VoiceToggle.Content = Loc.T(_work.Voice ? "power.on" : "power.off");
        ClapsToggle.Content = Loc.T(_work.Claps ? "power.on" : "power.off");
        GlowSlider.IsEnabled = _work.Glow;
        LowSwatch.Background = Hue(_work.VoiceLowHue);
        HighSwatch.Background = Hue(_work.VoiceHighHue);
        UpdateMeter();
    }

    static Brush Hue(int degrees)
    {
        var c = ColorMath.FromHsv(degrees / 360.0, 0.9, 1);
        return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    }

    /// <summary>Loudness bar on the -70..-10 dB scale with the threshold mark on the same scale.</summary>
    void UpdateMeter()
    {
        double w = MeterArea.ActualWidth;
        if (w <= 0) return;
        Meter.Width = w * Math.Clamp(_engine.MicLevel, 0, 1);
        bool above = _engine.MicLevel * 60 - 70 >= _work.GateDb;
        Meter.SetResourceReference(Border.BackgroundProperty, above ? "Rl.Accent" : "Rl.Text.Muted");
        GateMark.Margin = new Thickness(w * (_work.GateDb + 70) / 60 - 1, 0, 0, 0);
    }

    /// <summary>Copy the controls into the working options and play them on the strip.</summary>
    void Apply()
    {
        if (_loading) return;
        _work.Gate = (int)Math.Round(GateSlider.Value);
        _work.Glow = GlowBox.IsChecked == true;
        _work.GlowLevel = (int)Math.Round(GlowSlider.Value);
        _work.Voice = VoiceToggle.IsChecked == true;
        _work.VoiceLowHue = (int)Math.Round(LowHue.Value);
        _work.VoiceHighHue = (int)Math.Round(HighHue.Value);
        _work.WaveSpeed = (int)Math.Round(WaveSlider.Value);
        _work.Claps = ClapsToggle.IsChecked == true;
        _work.RingColors = RingColorKeys[Math.Max(0, RingColorsBox.SelectedIndex)];
        _work.RingSpeed = (int)Math.Round(RingSlider.Value);
        _work.Afterglow = (int)Math.Round(AfterSlider.Value);
        _settings.Mic = _work.Clone();
        ShowLabels();
    }

    void Any_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();

    void Any_Click(object sender, RoutedEventArgs e) => Apply();

    void Rings_Changed(object sender, SelectionChangedEventArgs e) => Apply();

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = new MicOptions();
        _loading = true;
        foreach (var p in typeof(MicOptions).GetProperties().Where(p => p.CanWrite))
            p.SetValue(_work, p.GetValue(d));
        ShowValues();
        _settings.Mic = _work.Clone();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Mic = _work.Clone();
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _settings.Mic = _original;
        DialogResult = false;
    }
}
