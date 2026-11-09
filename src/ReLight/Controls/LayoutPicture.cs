using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ReLight.Core;

namespace ReLight.Controls;

/// <summary>
/// Draws the screen with the LEDs around it. Full size it is the calibration test picture
/// (wide colored bands with LED numbers); small it is the preview on the main window.
/// </summary>
public sealed class LayoutPicture : FrameworkElement
{
    LedLayout? _layout;

    /// <summary>Band width as a share of the shorter screen side.</summary>
    public double BandShare { get; set; } = 0.12;

    /// <summary>Draw LED numbers (test picture) or not (small preview).</summary>
    public bool ShowNumbers { get; set; } = true;

    /// <summary>Small preview: round dots on a dark "TV" instead of solid bands.</summary>
    public bool Compact { get; set; }

    public LedLayout? Layout
    {
        get => _layout;
        set { _layout = value; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (Compact) RenderCompact(dc, w, h);
        else RenderFull(dc, w, h);
    }

    void RenderFull(DrawingContext dc, double w, double h)
    {
        if (_layout is not { Count: > 0 } layout) return;
        double band = Math.Min(w, h) * BandShare;
        float dx = (float)(band / w), dy = (float)(band / h);
        var gap = new Pen(new SolidColorBrush(Color.FromRgb(17, 17, 17)), 2);
        gap.Freeze();
        double fontSize = Math.Max(10, band * 0.17);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        for (int i = 0; i < layout.Count; i++)
        {
            var rect = RectOf(layout, i, dx, dy, w, h);
            var brush = new SolidColorBrush(LedColors.Of(layout, i).ToMedia());
            dc.DrawRectangle(brush, gap, rect);
            if (!ShowNumbers) continue;

            var side = layout.SideOf(i).side;
            double along = side is Side.Top or Side.Bottom ? rect.Width : rect.Height;
            double need = fontSize * 2.2;
            int every = along >= need ? 1 : along * 5 >= need ? 5 : 10;
            if (i != 0 && (i + 1) % every != 0) continue;

            var text = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, pixelsPerDip);
            dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
        }
    }

    void RenderCompact(DrawingContext dc, double w, double h)
    {
        // Keep 16:9-ish proportions inside the given box.
        double pad = 10;
        double sw = w - 2 * pad, sh = sw * 9 / 16;
        if (sh > h - 2 * pad) { sh = h - 2 * pad; sw = sh * 16 / 9; }
        var screen = new Rect((w - sw) / 2, (h - sh) / 2, sw, sh);

        var tv = TryFindResource("Rl.Preview.Screen") as Brush ?? Brushes.Black;
        var border = TryFindResource("Rl.Border") as Brush ?? Brushes.Gray;
        dc.DrawRoundedRectangle(tv, new Pen(border, 1), screen, 4, 4);

        if (_layout is not { Count: > 0 } layout) return;
        double inset = 6;
        var ring = new Rect(screen.X + inset, screen.Y + inset, screen.Width - 2 * inset, screen.Height - 2 * inset);
        double dot = Math.Clamp(Math.Min(ring.Width, ring.Height) / Math.Max(layout.Settings.Left, Math.Max(layout.Settings.Right, 1)) * 0.55, 1.6, 4);
        double step = Math.Min(ring.Width / Math.Max(1, Math.Max(layout.Settings.Top, layout.Settings.Bottom)),
                               ring.Height / Math.Max(1, Math.Max(layout.Settings.Left, layout.Settings.Right)));
        dot = Math.Min(dot, Math.Max(1.2, step * 0.42));

        for (int i = layout.Count - 1; i >= 0; i--)
        {
            var r = layout.RegionOf(i, 0, 0);
            var (side, _, _) = layout.SideOf(i);
            var c = new Point(
                ring.X + (side == Side.Left ? 0 : side == Side.Right ? ring.Width : (r.Left + r.Right) / 2 * ring.Width),
                ring.Y + (side == Side.Top ? 0 : side == Side.Bottom ? ring.Height : (r.Top + r.Bottom) / 2 * ring.Height));
            var color = LedColors.Of(layout, i).ToMedia();
            var brush = new SolidColorBrush(color);
            double radius = i == 0 ? dot * 1.8 : dot;
            if (i == 0)
            {
                var glow = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
                dc.DrawEllipse(glow, null, c, radius * 1.9, radius * 1.9);
            }
            dc.DrawEllipse(brush, null, c, radius, radius);
        }
    }

    static Rect RectOf(LedLayout layout, int i, float dx, float dy, double w, double h)
    {
        var r = layout.RegionOf(i, dx, dy);
        // Top and bottom bands take the corners; left and right are squeezed between them.
        if (layout.SideOf(i).side is Side.Left or Side.Right)
            r = System.Drawing.RectangleF.FromLTRB(r.Left, dy + r.Top * (1 - 2 * dy), r.Right, dy + r.Bottom * (1 - 2 * dy));
        return new Rect(r.X * w, r.Y * h, r.Width * w, r.Height * h);
    }
}
