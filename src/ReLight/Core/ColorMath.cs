using System.Drawing;
using System.Globalization;

namespace ReLight.Core;

public static class ColorMath
{
    /// <summary>Hue 0..1, saturation 0..1, value 0..1.</summary>
    public static Color FromHsv(double h, double s, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)h;
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return Color.FromArgb(To255(r), To255(g), To255(b));
    }

    public static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h / 6 + 1) % 1, max == 0 ? 0 : d / max, max);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color FromHex(string? hex, Color fallback)
    {
        if (hex is { Length: 7 } && hex[0] == '#' && int.TryParse(hex[1..], NumberStyles.HexNumber, null, out var v))
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return fallback;
    }

    static int To255(double x) => Math.Clamp((int)Math.Round(x * 255), 0, 255);
}
