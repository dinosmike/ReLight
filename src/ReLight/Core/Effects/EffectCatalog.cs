namespace ReLight.Core.Effects;

/// <summary>A kind of rainbow effect: its default colors, how many colors it accepts and how to make it.</summary>
public sealed record EffectInfo(
    string Id,
    string[] DefaultColors,
    int MinColors,
    int MaxColors,
    int DefaultSpeed,
    Func<Effect> Create)
{
    /// <summary>Localization key of the effect name, "fx.fire".</summary>
    public string NameKey => "fx." + Id;

    /// <summary>Localization key of the one-line description, "fxd.fire".</summary>
    public string HintKey => "fxd." + Id;
}

public static class EffectCatalog
{
    public static readonly IReadOnlyList<EffectInfo> All =
    [
        new("rainbow", ["#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00FFFF", "#0040FF", "#C000FF"], 2, 10, 35, () => new RainbowEffect()),
        new("cycle", ["#FF0040", "#FF8000", "#00FF80", "#0080FF"], 2, 10, 30, () => new ColorCycleEffect()),
        new("breathe", ["#00C8FF", "#FF2D95", "#FFB45A"], 1, 8, 35, () => new BreatheEffect()),
        new("wave", ["#0030FF", "#00FFC8"], 2, 6, 40, () => new WaveEffect()),
        new("comet", ["#FFFFFF", "#0028FF"], 2, 2, 45, () => new CometEffect()),
        new("chase", ["#FF0000", "#FFFFFF", "#00C000"], 2, 6, 35, () => new ChaseEffect()),
        new("pendulum", ["#FF0000", "#200000"], 2, 2, 50, () => new PendulumEffect()),
        new("fire", ["#200000", "#FF2000", "#FF8000", "#FFE060"], 2, 6, 50, () => new FireEffect()),
        new("ocean", ["#000830", "#0040C0", "#00A0D0", "#80FFF0"], 2, 6, 30, () => new NoiseFlowEffect(2.5, 0.08, 0.6f)),
        new("aurora", ["#00FF60", "#00C0FF", "#A000FF"], 2, 6, 30, () => new AuroraEffect()),
        new("lava", ["#300000", "#FF2000", "#FF8000", "#FFD000"], 2, 6, 25, () => new NoiseFlowEffect(1.4, 0.04, 0.5f)),
        new("stars", ["#1A1030", "#FFFFFF", "#80C0FF", "#FFE0A0"], 2, 8, 40, () => new SparkleEffect(0.35f, 0.9f, 0.6f)),
        new("confetti", ["#FF0040", "#FFD000", "#00FF60", "#00C0FF", "#A000FF", "#FF8000"], 1, 10, 55, () => new SparkleEffect(1.6f, 1.6f, 0f)),
        new("sunset", ["#FF6000", "#FF0060", "#6000C0", "#FFB000"], 2, 8, 20, () => new SunsetEffect()),
    ];

    public static EffectInfo Get(string? id) => All.FirstOrDefault(e => e.Id == id) ?? All[0];

    /// <summary>Speed setting 1..100 to animation time per second.</summary>
    public static double TimeScale(int speed) => 0.15 + Math.Pow(Math.Clamp(speed, 1, 100) / 100.0, 1.6) * 3.5;
}
