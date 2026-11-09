namespace ReLight.Core;

/// <summary>
/// The soundtrack's part in the cinema mode: a slow loudness swell that gently breathes the
/// brightness of the screen colors — quiet scenes a little dimmer, loud ones full — without
/// following every beat. Pure math, fed with the loudness in dB a few dozen times a second.
/// </summary>
public sealed class CinemaSound
{
    /// <summary>Rise and fall time constants, seconds: swells in fast enough to feel, fades out slowly.</summary>
    public const double Attack = 0.35, Release = 1.8;

    /// <summary>Quieter than this is silence (a pause, the menu).</summary>
    public const double SilenceDb = -60;

    // The film's own typical loudness, followed over half a minute: the scale adapts to the
    // volume the film is mixed at, so a quiet film breathes as much as a loud one.
    double _typicalDb = -30;

    /// <summary>Current swell 0 (quiet for this film) .. 1 (loud for this film).</summary>
    public double Level { get; private set; }

    public double Update(double db, double dt)
    {
        dt = Math.Clamp(dt, 0, 0.5);
        if (db > SilenceDb) _typicalDb += (db - _typicalDb) * (1 - Math.Exp(-dt / 30));

        // 15 dB under the typical level is "quiet", 10 dB over it is "loud".
        double wanted = db <= SilenceDb ? 0 : Math.Clamp((db - (_typicalDb - 15)) / 25, 0, 1);
        double tau = wanted > Level ? Attack : Release;
        Level += (wanted - Level) * (1 - Math.Exp(-dt / tau));
        return Level;
    }

    /// <summary>
    /// Brightness multiplier for the strip: 1 at full swell, down to 1 - 0.6·<paramref name="amount"/>
    /// in silence. <paramref name="amount"/> 0..1 is the user's "sound" slider; 0 leaves the colors alone.
    /// </summary>
    public double Brightness(double amount) => 1 - Math.Clamp(amount, 0, 1) * 0.6 * (1 - Level);

    /// <summary>Saturation multiplier: loud moments get a little juicier, up to +25 %.</summary>
    public double Saturation(double amount) => 1 + Math.Clamp(amount, 0, 1) * 0.25 * Level;
}
