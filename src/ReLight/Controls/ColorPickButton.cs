using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ReLight.Core;

namespace ReLight.Controls;

/// <summary>
/// A round color swatch; a click opens a small panel with ready colors and a hue slider.
/// Null color means "as chosen in the program" and is shown as an empty ring.
/// </summary>
public sealed class ColorPickButton : Button
{
    static readonly string[] Swatches =
        ["#FFB45A", "#FFFFFF", "#FF0000", "#FF6A00", "#FFD000", "#00FF40", "#00E5FF", "#0040FF", "#8A2BFF", "#FF2D95"];

    readonly Border _dot = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1) };
    readonly Popup _popup;
    readonly Slider _hue = new() { Width = 220, Margin = new Thickness(0, 10, 0, 0) };
    string? _color;
    bool _updating;

    public event Action<string?>? ColorChanged;

    public string? Color
    {
        get => _color;
        set
        {
            _color = value;
            ShowDot();
        }
    }

    public ColorPickButton()
    {
        SetResourceReference(StyleProperty, "Rl.Button.Icon");
        Width = 32;
        Content = _dot;
        _dot.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");
        SetResourceReference(ToolTipProperty, "Loc.ex.color.own");
        SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "Loc.ex.color.own");

        var grid = new WrapPanel { Width = 220 };
        foreach (var hex in Swatches)
        {
            var c = ColorMath.FromHex(hex, System.Drawing.Color.White);
            var swatch = new Button
            {
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = SwatchTemplate(),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
            };
            swatch.Click += (_, _) => { Pick(hex); _popup!.IsOpen = false; };
            grid.Children.Add(swatch);
        }

        _hue.SetResourceReference(StyleProperty, "Rl.Slider.Hue");
        _hue.ValueChanged += (_, e) =>
        {
            if (!_updating) Pick(ColorMath.ToHex(ColorMath.FromHsv(e.NewValue / 360, 1, 1)));
        };

        var reset = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0), MinHeight = 30, Padding = new Thickness(8, 2, 8, 2) };
        reset.SetResourceReference(StyleProperty, "Rl.Button");
        reset.SetResourceReference(ContentProperty, "Loc.ex.color.default");
        reset.Click += (_, _) => { Pick(null); _popup!.IsOpen = false; };

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(_hue);
        panel.Children.Add(reset);

        var frame = new Border { Child = panel, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "Rl.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");

        _popup = new Popup
        {
            Child = frame,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        Click += (_, _) =>
        {
            _updating = true;
            if (_color != null) _hue.Value = ColorMath.ToHsv(ColorMath.FromHex(_color, System.Drawing.Color.Red)).h * 360;
            _updating = false;
            _popup.IsOpen = true;
        };
        ShowDot();
    }

    void Pick(string? hex)
    {
        Color = hex;
        ColorChanged?.Invoke(hex);
    }

    void ShowDot()
    {
        if (_color == null)
        {
            _dot.Background = Brushes.Transparent;
            _dot.BorderThickness = new Thickness(2);
            return;
        }
        var c = ColorMath.FromHex(_color, System.Drawing.Color.White);
        _dot.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        _dot.BorderThickness = new Thickness(1);
    }

    /// <summary>Round swatch with a thin ring that lights up on hover.</summary>
    static ControlTemplate SwatchTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var ring = new FrameworkElementFactory(typeof(Border));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        ring.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 0));
        ring.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        ring.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");
        t.VisualTree = ring;
        return t;
    }
}
