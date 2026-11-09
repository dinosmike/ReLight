namespace ReLight.Core.Effects;

/// <summary>Colors as floats 0..1 so effects can mix and dim them without rounding steps.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static Rgb From(System.Drawing.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    public System.Drawing.Color ToColor() =>
        System.Drawing.Color.FromArgb(To255(R), To255(G), To255(B));

    public static Rgb operator *(Rgb c, float k) => new(c.R * k, c.G * k, c.B * k);
    public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static Rgb Lerp(Rgb a, Rgb b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    public Rgb Max(Rgb o) => new(Math.Max(R, o.R), Math.Max(G, o.G), Math.Max(B, o.B));

    static int To255(float v) => Math.Clamp((int)(v * 255 + 0.5f), 0, 255);
}

public static class EffectMath
{
    /// <summary>Point on a gradient through the palette; cyclic gradients wrap back to the first color.</summary>
    public static Rgb Gradient(IReadOnlyList<Rgb> palette, double t, bool cyclic)
    {
        int n = palette.Count;
        if (n == 0) return Rgb.Black;
        if (n == 1) return palette[0];
        if (cyclic)
        {
            t = Frac(t) * n;
            int i = (int)t;
            return Rgb.Lerp(palette[i % n], palette[(i + 1) % n], Smooth((float)(t - i)));
        }
        t = Math.Clamp(t, 0, 1) * (n - 1);
        int j = Math.Min((int)t, n - 2);
        return Rgb.Lerp(palette[j], palette[j + 1], Smooth((float)(t - j)));
    }

    public static double Frac(double x) => x - Math.Floor(x);

    public static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>Smooth 2D value noise, 0..1.</summary>
    public static float Noise(double x, double y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float fx = Smooth((float)(x - xi)), fy = Smooth((float)(y - yi));
        float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Two octaves of noise for a more natural look, 0..1.</summary>
    public static float Fbm(double x, double y) => Noise(x, y) * 0.65f + Noise(x * 2.3 + 17.1, y * 2.3 + 5.7) * 0.35f;

    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
