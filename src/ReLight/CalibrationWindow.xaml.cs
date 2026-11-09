using System.Windows;
using System.Windows.Input;
using ReLight.Controls;
using ReLight.Core;
using ReLight.Services;
using WinScreen = System.Windows.Forms.Screen;

namespace ReLight;

/// <summary>
/// Full-screen test picture on the strip's screen: each edge has its own color, and the strip
/// shows the same colors according to the layout being edited, so mistakes are visible at a glance.
/// </summary>
public partial class CalibrationWindow : Window
{
    readonly AppSettings _settings;
    readonly AdalightDevice _device;
    readonly StripLayout _work;
    readonly string _originalOrder;
    readonly WinScreen _screen;
    LedLayout _layout;
    bool _updating = true;

    public CalibrationWindow(AppSettings settings, AdalightDevice device, WinScreen screen)
    {
        _settings = settings;
        _device = device;
        _screen = screen;
        _work = settings.Layout.Clone();
        _originalOrder = settings.ColorOrder;
        _layout = new LedLayout(_work);
        InitializeComponent();
        ScreenHelper.CoverScreen(this, screen);

        TopBox.Value = _work.Top;
        RightBox.Value = _work.Right;
        BottomBox.Value = _work.Bottom;
        LeftBox.Value = _work.Left;
        OffsetBox.Value = _work.Offset;
        SpreadBox.Value = settings.KnownLedCount > 0 ? settings.KnownLedCount : Math.Max(1, _work.Total);

        foreach (var c in Enum.GetValues<StartCorner>()) StartBox.Items.Add(Loc.T("corner." + c));
        StartBox.SelectedIndex = (int)_work.Start;
        DirBox.Items.Add(Loc.T("cal.cw"));
        DirBox.Items.Add(Loc.T("cal.ccw"));
        DirBox.SelectedIndex = _work.Clockwise ? 0 : 1;
        foreach (var o in ColorOutput.Orders) OrderBox.Items.Add(o);
        OrderBox.SelectedItem = ColorOutput.Orders.Contains(settings.ColorOrder) ? settings.ColorOrder : "RGB";

        foreach (var n in new[] { TopBox, RightBox, BottomBox, LeftBox, OffsetBox })
            n.ValueChanged += (_, _) => Changed();
        StartBox.SelectionChanged += (_, _) => Changed();
        DirBox.SelectionChanged += (_, _) => Changed();
        OrderBox.SelectionChanged += (_, _) =>
        {
            if (OrderBox.SelectedItem is string o) _settings.ColorOrder = o;
            SendFrame();
        };

        _updating = false;
        Changed();
    }

    void Changed()
    {
        if (_updating) return;
        _work.Top = TopBox.Value;
        _work.Right = RightBox.Value;
        _work.Bottom = BottomBox.Value;
        _work.Left = LeftBox.Value;
        _work.Offset = OffsetBox.Value;
        _work.Start = (StartCorner)Math.Max(0, StartBox.SelectedIndex);
        _work.Clockwise = DirBox.SelectedIndex != 1;
        _layout = new LedLayout(_work);

        TotalText.Text = _settings.KnownLedCount > 0
            ? Loc.T("cal.total.known", _work.Total, _settings.KnownLedCount)
            : Loc.T("cal.total", _work.Total);
        Picture.Layout = _layout;
        SendFrame();
    }

    /// <summary>Split a total count over the four sides in proportion to the screen shape.</summary>
    void Spread_Click(object sender, RoutedEventArgs e)
    {
        var spread = new StripLayout();
        spread.SpreadOver(SpreadBox.Value, _screen.Bounds.Width, _screen.Bounds.Height);

        _updating = true;
        TopBox.Value = spread.Top;
        BottomBox.Value = spread.Bottom;
        LeftBox.Value = spread.Left;
        RightBox.Value = spread.Right;
        _updating = false;
        Changed();
    }

    void SendFrame()
    {
        int count = Math.Max(_layout.Count, _settings.KnownLedCount);
        var colors = new System.Drawing.Color[count];
        for (int i = 0; i < count; i++)
            colors[i] = i < _layout.Count ? LedColors.Of(_layout, i) : System.Drawing.Color.Black;
        _device.Send(ColorOutput.ToBytes(colors, _settings.ColorOrder, _settings.Brightness));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        else if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
    }

    void Done_Click(object sender, RoutedEventArgs e) => Finish(true);

    void Cancel_Click(object sender, RoutedEventArgs e) => Finish(false);

    void Finish(bool keep)
    {
        if (keep)
        {
            _settings.Layout = _work.Clone();
            if (_settings.KnownLedCount == 0) _settings.KnownLedCount = _work.Total;
        }
        else
        {
            _settings.ColorOrder = _originalOrder;
        }
        DialogResult = keep;
    }
}
