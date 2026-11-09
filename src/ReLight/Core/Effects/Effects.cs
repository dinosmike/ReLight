using static ReLight.Core.Effects.EffectMath;

namespace ReLight.Core.Effects;

/// <summary>Where the LEDs are and what time it is, as seen by an effect.</summary>
public sealed class EffectFrame
{
    /// <summary>Position along the strip, 0..1 (first LED = 0).</summary>
    public float[] Pos = [];
    /// <summary>Position on the screen, 0..1 from the left and from the top.</summary>
    public float[] X = [], Y = [];
    public int Count => Pos.Length;

    /// <summary>Animation time: seconds multiplied by the effect speed.</summary>
    public double Time;
    /// <summary>Same units as <see cref="Time"/>: the step since the last frame.</summary>
    public double Step;
    public IReadOnlyList<Rgb> Palette = [];

    public static EffectFrame For(LedLayout layout)
    {
        var f = new EffectFrame { Pos = new float[layout.Count], X = new float[layout.Count], Y = new float[layout.Count] };
        for (int i = 0; i < layout.Count; i++)
        {
            var r = layout.RegionOf(i, 0, 0);
            f.Pos[i] = (float)i / layout.Count;
            f.X[i] = (r.Left + r.Right) / 2;
            f.Y[i] = (r.Top + r.Bottom) / 2;
        }
        return f;
    }
}

/// <summary>One kind of animation. Every player makes its own instance, so effects may keep state.</summary>
public abstract class Effect
{
    public abstract void Render(EffectFrame f, Span<Rgb> output);
}

/// <summary>The whole palette as a ring running around the screen.</summary>
sealed class RainbowEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        for (int i = 0; i < f.Count; i++) o[i] = Gradient(f.Palette, f.Pos[i] - f.Time * 0.25, cyclic: true);
    }
}

/// <summary>The whole strip flows from one palette color to the next.</summary>
sealed class ColorCycleEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o) => o[..f.Count].Fill(Gradient(f.Palette, f.Time * 0.1, cyclic: true));
}

/// <summary>Slow breaths, every breath in the next palette color.</summary>
sealed class BreatheEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        if (f.Palette.Count == 0) { o[..f.Count].Clear(); return; }
        double t = f.Time * 0.25;
        float phase = (float)Frac(t);
        float level = MathF.Pow(MathF.Sin(phase * MathF.PI), 2) * 0.95f + 0.05f;
        o[..f.Count].Fill(f.Palette[(int)Math.Floor(t) % f.Palette.Count] * level);
    }
}

/// <summary>Waves running along the strip between the palette colors.</summary>
sealed class WaveEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        for (int i = 0; i < f.Count; i++)
        {
            double v = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (f.Pos[i] * 3 - f.Time * 0.3));
            o[i] = Gradient(f.Palette, v, cyclic: false);
        }
    }
}

/// <summary>A bright head with a fading tail flies around. Palette: head, sky.</summary>
sealed class CometEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        var head = f.Palette.Count > 0 ? f.Palette[0] : new Rgb(1, 1, 1);
        var sky = (f.Palette.Count > 1 ? f.Palette[1] : Rgb.Black) * 0.25f;
        double pos = Frac(f.Time * 0.2);
        for (int i = 0; i < f.Count; i++)
        {
            float behind = (float)Frac(pos - f.Pos[i]); // 0 at the head, grows along the tail
            float b = behind < 0.3f ? MathF.Pow(1 - behind / 0.3f, 2.2f) : 0;
            o[i] = Rgb.Lerp(sky, head, b);
        }
    }
}

/// <summary>Colored segments chasing each other around the screen.</summary>
sealed class ChaseEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        if (f.Palette.Count == 0) { o[..f.Count].Clear(); return; }
        int n = f.Palette.Count;
        const int segments = 12;
        for (int i = 0; i < f.Count; i++)
        {
            double s = f.Pos[i] * segments - f.Time * 0.8;
            int k = (int)Math.Floor(s);
            float edge = (float)Frac(s);
            // Darken the joints a little so the segments read as separate pieces.
            float shade = edge < 0.12f ? edge / 0.12f : edge > 0.88f ? (1 - edge) / 0.12f : 1;
            o[i] = f.Palette[((k % n) + n) % n] * (0.35f + 0.65f * shade);
        }
    }
}

/// <summary>A light swinging back and forth along the strip. Palette: light, background.</summary>
sealed class PendulumEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        var light = f.Palette.Count > 0 ? f.Palette[0] : new Rgb(1, 0, 0);
        var bg = (f.Palette.Count > 1 ? f.Palette[1] : Rgb.Black) * 0.6f;
        double x = 0.5 - 0.5 * Math.Cos(Frac(f.Time * 0.15) * 2 * Math.PI); // eases at the ends like a pendulum
        for (int i = 0; i < f.Count; i++)
        {
            double d = (f.Pos[i] - x) / 0.06;
            o[i] = Rgb.Lerp(bg, light, (float)Math.Exp(-d * d));
        }
    }
}

/// <summary>Flames rising from the bottom of the screen, palette from embers to the tips.</summary>
sealed class FireEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        double t = f.Time * 0.8;
        for (int i = 0; i < f.Count; i++)
        {
            float up = 1 - f.Y[i]; // 0 at the bottom of the screen
            float n = Fbm(f.X[i] * 7, f.Y[i] * 4 + t * 2.2);
            float flicker = Noise(i * 0.7, t * 6) * 0.25f;
            float heat = Math.Clamp(n * 1.25f + flicker - up * 0.55f, 0, 1);
            o[i] = Gradient(f.Palette, heat, cyclic: false) * (0.15f + 0.85f * heat);
        }
    }
}

/// <summary>Slowly drifting clouds of the palette colors (ocean, lava lamp).</summary>
sealed class NoiseFlowEffect(double scale, double drift, float dim) : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        for (int i = 0; i < f.Count; i++)
        {
            float v = Fbm(f.Pos[i] * scale + f.Time * drift, f.Time * 0.15);
            o[i] = Gradient(f.Palette, v * 1.2 - 0.1, cyclic: false) * (1 - dim + dim * v);
        }
    }
}

/// <summary>Swaying glowing curtains.</summary>
sealed class AuroraEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        for (int i = 0; i < f.Count; i++)
        {
            double wobble = Fbm(f.Pos[i] * 3, f.Time * 0.2) * 4;
            double v = 0.5 + 0.5 * Math.Sin(f.Pos[i] * 9 + wobble + f.Time * 0.4);
            float glow = Fbm(f.Pos[i] * 5 + 9, f.Time * 0.3);
            o[i] = Gradient(f.Palette, v, cyclic: false) * Math.Min(1, 0.2f + 1.3f * glow * glow);
        }
    }
}

/// <summary>
/// Random sparks that flash and fade. With a glow the first palette color is the night sky
/// and the rest are the stars; without it every color is a spark on black.
/// </summary>
sealed class SparkleEffect(float rate, float fade, float glow) : Effect
{
    readonly Random _random = new();
    float[] _level = [];
    Rgb[] _color = [];

    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        if (_level.Length != f.Count) { _level = new float[f.Count]; _color = new Rgb[f.Count]; }
        int first = glow > 0 && f.Palette.Count > 1 ? 1 : 0;
        int n = f.Palette.Count - first;
        var sky = glow > 0 && f.Palette.Count > 0 ? f.Palette[0] * glow : Rgb.Black;
        float step = (float)Math.Max(0, f.Step);
        for (int i = 0; i < f.Count; i++)
        {
            _level[i] = Math.Max(0, _level[i] - step * fade);
            if (n > 0 && _random.NextDouble() < rate * step)
            {
                _level[i] = 1;
                _color[i] = f.Palette[first + _random.Next(n)];
            }
            o[i] = sky.Max(_color[i] * _level[i]);
        }
    }
}

/// <summary>Sky colors from the top of the screen to the bottom, slowly shifting.</summary>
sealed class SunsetEffect : Effect
{
    public override void Render(EffectFrame f, Span<Rgb> o)
    {
        for (int i = 0; i < f.Count; i++)
            o[i] = Gradient(f.Palette, f.Y[i] * 0.9 + 0.5 + 0.5 * Math.Sin(f.Time * 0.12 + f.X[i]), cyclic: true);
    }
}
