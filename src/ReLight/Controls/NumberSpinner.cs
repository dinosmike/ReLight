using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReLight.Controls;

/// <summary>Whole-number field with ▲▼ buttons and mouse-wheel support, styled like ReQuit.</summary>
public sealed class NumberSpinner : UserControl
{
    readonly TextBox _text;
    int _value;
    bool _syncing;

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 1000;

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            bool changed = v != _value;
            _value = v;
            _syncing = true;
            _text.Text = v.ToString();
            _syncing = false;
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NumberSpinner()
    {
        _text = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 4, 0),
            MinHeight = 0,
            Text = "0",
        };
        _text.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(c => char.IsDigit(c) || c == '-');
        _text.TextChanged += (_, _) =>
        {
            if (!_syncing && int.TryParse(_text.Text, out var v) && v >= Minimum && v <= Maximum && v != _value)
            {
                _value = v;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _text.LostKeyboardFocus += (_, _) => Value = _value; // put back a clean number
        _text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Value++; e.Handled = true; }
            else if (e.Key == Key.Down) { Value--; e.Handled = true; }
        };

        var up = new RepeatButton { Content = "" };
        var down = new RepeatButton { Content = "" };
        up.SetResourceReference(StyleProperty, "Rl.Spin.Button");
        down.SetResourceReference(StyleProperty, "Rl.Spin.Button");
        up.Click += (_, _) => Value++;
        down.Click += (_, _) => Value--;

        var arrows = new Grid { Width = 22 };
        arrows.RowDefinitions.Add(new RowDefinition());
        arrows.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(down, 1);
        arrows.Children.Add(up);
        arrows.Children.Add(down);
        var arrowsBorder = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = arrows };
        arrowsBorder.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(arrowsBorder, 1);
        grid.Children.Add(_text);
        grid.Children.Add(arrowsBorder);

        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = grid, ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "Rl.Input");
        frame.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");

        Content = frame;
        Height = 32;
        Width = 92;
        MouseWheel += (_, e) => { Value += e.Delta > 0 ? 1 : -1; e.Handled = true; };
    }
}
