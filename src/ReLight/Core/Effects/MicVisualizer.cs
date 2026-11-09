using ReLight.Audio;

namespace ReLight.Core.Effects;

/// <summary>
/// Microphone mode picture, three layers mixed together:
/// voice waves rising from the bottom centre up both sides, colored by the pitch;
/// rings of light that burst out from claps, knocks and other sudden sounds;
/// and, if wanted, a soft glow from the overall noise. Silence is black.
/// </summary>
public sealed class MicVisualizer
{
    sealed class Ring
    {
        public float Center, Radius, Strength;
        public Rgb Color;
    }

    const int TrailLength = 96;

    readonly Random _random = new();
    readonly List<Ring> _rings = [];
    readonly Rgb[] _trail = new Rgb[TrailLength];  // voice colors, newest at 0, moving outwards
    float[] _distance = [];                          // distance of each LED from the bottom centre, 0..1
    float _bottomCenter;
    double _trailCarry;
    float _hue, _glow;

    public void Render(EffectFrame f, SoundFeatures s, MicOptions o, double seconds, Span<Rgb> output)
    {
        if (_distance.Length != f.Count) Prepare(f);
        float dt = (float)seconds;
        _hue = (float)EffectMath.Frac(_hue + seconds * (0.02 + s.Level * 0.08));
        // Afterglow 0..100: how slowly light fades once the sound is gone.
        float fade = 0.6f + (100 - Math.Clamp(o.Afterglow, 0, 100)) / 100f * 3.4f;

        var voiceColor = VoiceColor(s.Pitch, o);

        // Voice: push a new color into the trail; the trail runs outwards along the strip.
        double speed = 0.3 + Math.Clamp(o.WaveSpeed, 1, 100) / 100.0 * 1.5;
        _trailCarry += seconds * TrailLength * speed;
        while (_trailCarry >= 1)
        {
            _trailCarry -= 1;
            Array.Copy(_trail, 0, _trail, 1, TrailLength - 1);
            _trail[0] = o.Voice ? voiceColor * Math.Clamp(s.Voice * 1.4f, 0, 1) : Rgb.Black;
        }
        // The far end of the trail also dims with time, so the afterglow setting affects voice too.
        for (int i = 0; i < TrailLength; i++) _trail[i] *= MathF.Max(0, 1 - dt * fade * 0.25f);

        // Hits: a ring of light bursts out; strong ones start at the bottom centre, others anywhere.
        if (o.Claps && s.Hit > 0)
        {
            _rings.Add(new Ring
            {
                Center = s.Hit > 0.7f ? _bottomCenter : (float)_random.NextDouble(),
                Strength = 0.6f + s.Hit * 0.4f,
                Color = o.RingColors switch
                {
                    "white" => new Rgb(1, 1, 1),
                    "voice" => voiceColor,
                    _ => Hsv((float)_random.NextDouble(), 0.85f, 1),
                },
            });
            if (_rings.Count > 12) _rings.RemoveAt(0);
        }
        float ringSpeed = 0.3f + Math.Clamp(o.RingSpeed, 1, 100) / 100f * 1.2f;
        foreach (var r in _rings)
        {
            r.Radius += dt * ringSpeed * (0.7f + r.Strength * 0.5f);
            r.Strength -= dt * fade * 0.35f;
        }
        _rings.RemoveAll(r => r.Strength <= 0 || r.Radius > 0.6f);

        // Glow follows the loudness and fades with the afterglow; with the option off it stays black.
        float glowTarget = o.Glow ? s.Level * Math.Clamp(o.GlowLevel, 0, 100) / 100f : 0;
        _glow = glowTarget > _glow ? glowTarget : MathF.Max(glowTarget, _glow - dt * fade * 0.3f);
        var glow = Hsv(_hue, 0.8f, _glow);

        for (int i = 0; i < f.Count; i++)
        {
            var c = glow;
            c = c.Max(_trail[(int)(_distance[i] * (TrailLength - 1))]);
            foreach (var r in _rings)
            {
                float d = MathF.Abs(f.Pos[i] - r.Center);
                d = MathF.Min(d, 1 - d); // around the strip both ways
                float edge = MathF.Abs(d - r.Radius) / 0.045f;
                if (edge < 1) c = c.Max(r.Color * ((1 - edge * edge) * r.Strength));
                if (r.Radius < 0.06f && d < 0.06f) c = c.Max(new Rgb(1, 1, 1) * (r.Strength * (1 - d / 0.06f))); // flash at the start
            }
            output[i] = new Rgb(MathF.Min(1, c.R), MathF.Min(1, c.G), MathF.Min(1, c.B));
        }
    }

    /// <summary>Between the low-voice and high-voice hues, going the short way round the color wheel.</summary>
    static Rgb VoiceColor(float pitch, MicOptions o)
    {
        float a = o.VoiceLowHue / 360f, b = o.VoiceHighHue / 360f;
        float d = b - a;
        if (d > 0.5f) d -= 1;
        if (d < -0.5f) d += 1;
        return Hsv(a + d * pitch, 0.9f, 1);
    }

    /// <summary>Each LED's distance from the bottom centre of the screen along the strip, 0 there and 1 at the top centre.</summary>
    void Prepare(EffectFrame f)
    {
        _distance = new float[f.Count];
        int bottom = 0;
        float best = float.MaxValue;
        for (int i = 0; i < f.Count; i++)
        {
            float score = MathF.Abs(f.X[i] - 0.5f) + (1 - f.Y[i]) * 2;
            if (score < best) { best = score; bottom = i; }
        }
        _bottomCenter = f.Count > 0 ? f.Pos[bottom] : 0;
        for (int i = 0; i < f.Count; i++)
        {
            float d = MathF.Abs(f.Pos[i] - _bottomCenter);
            _distance[i] = MathF.Min(d, 1 - d) * 2;
        }
    }

    static Rgb Hsv(float h, float s, float v)
    {
        var c = ColorMath.FromHsv(h, s, v);
        return new Rgb(c.R / 255f, c.G / 255f, c.B / 255f);
    }
}
