using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReLight.Core;
using ReLight.Core.Effects;
using MColor = System.Windows.Media.Color;

namespace ReLight;

/// <summary>
/// Colors and speed of one rainbow effect. Every change plays at once on the big preview
/// and on the strip; Cancel drops the changes.
/// </summary>
public partial class EffectEditorWindow : Window
{
    readonly EffectInfo _info;
    readonly EffectPlayer _player;
    readonly EffectSettings _work;
    int _selected;
    bool _updating;

    /// <summary>The edited settings after Save.</summary>
    public EffectSettings Result => _work.Clone();

    public EffectEditorWindow(EffectInfo info, EffectSettings current, EffectPlayer player)
    {
        _info = info;
        _player = player;
        _work = current.Clone();
        InitializeComponent();

        TitleBarIcon.Source = AboutWindow.LargestIconFrame();
        TitleText.Text = Loc.T("fxe.title", Loc.T(info.NameKey));
        Description.Text = Loc.T(info.HintKey);
        ColorsHint.Text = info.MinColors == info.MaxColors
            ? Loc.T("fxe.colors.fixed")
            : Loc.T("fxe.colors.hint", info.MinColors, info.MaxColors);

        _updating = true;
        SpeedSlider.Value = _work.Speed;
        _updating = false;
        Closed += (_, _) => _player.Preview = null;
        Refresh();
    }

    /// <summary>Redraw swatches and sliders and push the working copy to the preview and the strip.</summary>
    void Refresh()
    {
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _work.Colors.Count - 1));
        Swatches.Children.Clear();
        for (int i = 0; i < _work.Colors.Count; i++)
        {
            int index = i;
            var c = ColorMath.FromHex(_work.Colors[i], System.Drawing.Color.Black);
            var swatch = new RadioButton
            {
                GroupName = "EditorSwatch",
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(MColor.FromRgb(c.R, c.G, c.B)),
                IsChecked = i == _selected,
            };
            swatch.SetResourceReference(StyleProperty, "Rl.Swatch");
            swatch.Checked += (_, _) => { _selected = index; ShowSelected(); };
            Swatches.Children.Add(swatch);
        }
        AddButton.IsEnabled = _work.Colors.Count < _info.MaxColors;
        RemoveButton.IsEnabled = _work.Colors.Count > _info.MinColors;
        AddButton.Visibility = RemoveButton.Visibility = _info.MinColors == _info.MaxColors ? Visibility.Collapsed : Visibility.Visible;
        SpeedValue.Text = $"{_work.Speed}%";
        ShowSelected();
        Apply();
    }

    void ShowSelected()
    {
        if (_work.Colors.Count == 0) return;
        var c = ColorMath.FromHex(_work.Colors[_selected], System.Drawing.Color.Black);
        var (h, s, v) = ColorMath.ToHsv(c);
        _updating = true;
        HueSlider.Value = h * 360;
        SatSlider.Value = s * 100;
        ValSlider.Value = v * 100;
        HexBox.Text = ColorMath.ToHex(c);
        ColorPreview.Background = new SolidColorBrush(MColor.FromRgb(c.R, c.G, c.B));
        _updating = false;
    }

    void Apply()
    {
        BigPreview.Settings = _work.Clone();
        _player.Preview = _work.Clone();
    }

    /// <summary>One color changed: update its swatch without rebuilding the row (keeps the drag smooth).</summary>
    void SetSelectedColor(System.Drawing.Color c, bool updateHex)
    {
        _work.Colors[_selected] = ColorMath.ToHex(c);
        var brush = new SolidColorBrush(MColor.FromRgb(c.R, c.G, c.B));
        ColorPreview.Background = brush;
        if (Swatches.Children[_selected] is RadioButton swatch) swatch.Background = brush;
        if (updateHex)
        {
            _updating = true;
            HexBox.Text = ColorMath.ToHex(c);
            _updating = false;
        }
        Apply();
    }

    void Color_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _work.Colors.Count == 0 || HexBox == null) return;
        SetSelectedColor(ColorMath.FromHsv(HueSlider.Value / 360, SatSlider.Value / 100, ValSlider.Value / 100), updateHex: true);
    }

    void Hex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating || _work.Colors.Count == 0) return;
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length != 7) return;
        var c = ColorMath.FromHex(text, System.Drawing.Color.Empty);
        if (c == System.Drawing.Color.Empty) return;
        var (h, s, v) = ColorMath.ToHsv(c);
        _updating = true;
        HueSlider.Value = h * 360;
        SatSlider.Value = s * 100;
        ValSlider.Value = v * 100;
        _updating = false;
        SetSelectedColor(c, updateHex: false);
    }

    void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || SpeedValue == null) return;
        _work.Speed = (int)Math.Round(e.NewValue);
        SpeedValue.Text = $"{_work.Speed}%";
        Apply();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_work.Colors.Count >= _info.MaxColors) return;
        // A new color next to the chosen one, shifted in hue so it is visibly different.
        var c = ColorMath.FromHex(_work.Colors.Count > 0 ? _work.Colors[_selected] : "#FF0000", System.Drawing.Color.Red);
        var (h, s, v) = ColorMath.ToHsv(c);
        _work.Colors.Insert(_selected + 1, ColorMath.ToHex(ColorMath.FromHsv(h + 0.12, Math.Max(s, 0.6), Math.Max(v, 0.6))));
        _selected++;
        Refresh();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_work.Colors.Count <= _info.MinColors) return;
        _work.Colors.RemoveAt(_selected);
        Refresh();
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = EffectSettings.Default(_info);
        _work.Colors = d.Colors;
        _work.Speed = d.Speed;
        _selected = 0;
        _updating = true;
        SpeedSlider.Value = _work.Speed;
        _updating = false;
        Refresh();
    }

    void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
