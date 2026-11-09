using System.Windows;
using System.Windows.Controls;

namespace ReLight.Controls;

/// <summary>
/// One row that fills its width: every child gets its own width plus an equal share of the
/// free space, so the labels stand at equal distances whatever their length
/// (a UniformGrid would give "Микрофон" as little room as "Кино" and clip it).
/// </summary>
public sealed class SpreadPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double width = 0, height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double natural = children.Sum(c => c.DesiredSize.Width);
        // Too narrow: shrink everyone in proportion rather than push the last one out.
        double scale = natural > final.Width && natural > 0 ? final.Width / natural : 1;
        double extra = children.Count > 0 ? Math.Max(0, final.Width - natural) / children.Count : 0;
        double x = 0;
        foreach (var child in children)
        {
            double w = child.DesiredSize.Width * scale + extra;
            child.Arrange(new Rect(x, 0, w, final.Height));
            x += w;
        }
        return final;
    }
}
