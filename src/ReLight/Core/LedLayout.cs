using System.Drawing;

namespace ReLight.Core;

public enum Side { Top, Right, Bottom, Left }

/// <summary>
/// Maps strip LEDs to places on the screen edge.
/// Internally every LED gets a "perimeter position": counting clockwise from the top-left corner,
/// top edge left→right, right edge top→bottom, bottom edge right→left, left edge bottom→top.
/// </summary>
public sealed class LedLayout
{
    readonly int[] _perimeterOfStrip;
    readonly int[] _stripOfPerimeter;

    public StripLayout Settings { get; }
    public int Count { get; }

    public LedLayout(StripLayout s)
    {
        Settings = s;
        Count = Math.Max(0, s.Top) + Math.Max(0, s.Right) + Math.Max(0, s.Bottom) + Math.Max(0, s.Left);
        _perimeterOfStrip = new int[Count];
        _stripOfPerimeter = new int[Count];
        if (Count == 0) return;

        int corner = s.Start switch
        {
            StartCorner.TopLeft => 0,
            StartCorner.TopRight => s.Top,
            StartCorner.BottomRight => s.Top + s.Right,
            _ => s.Top + s.Right + s.Bottom,
        };

        for (int i = 0; i < Count; i++)
        {
            int p = s.Clockwise ? corner + s.Offset + i : corner - 1 - s.Offset - i;
            p = ((p % Count) + Count) % Count;
            _perimeterOfStrip[i] = p;
            _stripOfPerimeter[p] = i;
        }
    }

    /// <summary>Strip index (0-based) of the LED at a perimeter position.</summary>
    public int StripIndexAt(int perimeter) => _stripOfPerimeter[perimeter];

    public (Side side, int index, int count) SideOf(int stripIndex)
    {
        var s = Settings;
        int p = _perimeterOfStrip[stripIndex];
        if (p < s.Top) return (Side.Top, p, s.Top);
        p -= s.Top;
        if (p < s.Right) return (Side.Right, p, s.Right);
        p -= s.Right;
        if (p < s.Bottom) return (Side.Bottom, p, s.Bottom);
        p -= s.Bottom;
        return (Side.Left, p, s.Left);
    }

    /// <summary>
    /// Screen area that belongs to an LED, in 0..1 coordinates of the screen.
    /// <paramref name="depth"/> is how far the area reaches into the picture.
    /// </summary>
    public RectangleF RegionOf(int stripIndex, float depthX, float depthY)
    {
        var (side, k, n) = SideOf(stripIndex);
        float a = (float)k / n, b = (float)(k + 1) / n;
        return side switch
        {
            Side.Top => RectangleF.FromLTRB(a, 0, b, depthY),
            Side.Right => RectangleF.FromLTRB(1 - depthX, a, 1, b),
            Side.Bottom => RectangleF.FromLTRB(1 - b, 1 - depthY, 1 - a, 1),
            _ => RectangleF.FromLTRB(0, 1 - b, depthX, 1 - a),
        };
    }
}
