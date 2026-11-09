namespace ReLight.Core;

/// <summary>
/// Automatic silence threshold for the microphone mode. The quiet moments between sounds over the
/// last 10 seconds are the room's own noise; the threshold sits a little above it and follows it slowly.
/// Nearly free: it reuses the loudness the analyser has already measured, one comparison per frame
/// and a sort of 40 numbers four times a second.
/// </summary>
public sealed class SilenceGate
{
    const double SlotSeconds = 0.25;
    const int Slots = 40;           // 40 × 0.25 s = the last 10 seconds
    public const double Margin = 6; // dB above the room noise
    public const double MinDb = -70, MaxDb = -20; // the same range as the manual threshold

    readonly double[] _quietest = new double[Slots]; // the quietest moment of each quarter second
    readonly double[] _sorted = new double[Slots];
    int _filled, _next;
    double _slotQuietest = double.MaxValue, _slotTime;

    public SilenceGate(double startDb) => GateDb = startDb;

    /// <summary>The threshold now, in dBFS.</summary>
    public double GateDb { get; private set; }

    /// <summary>Start over (another microphone), from <paramref name="startDb"/>.</summary>
    public void Reset(double startDb)
    {
        GateDb = startDb;
        _filled = _next = 0;
        _slotQuietest = double.MaxValue;
        _slotTime = 0;
    }

    /// <summary>Loudness of the current frame in dBFS and the time since the previous one.</summary>
    public void Update(double db, double dt)
    {
        if (db < _slotQuietest) _slotQuietest = db;
        _slotTime += dt;
        if (_slotTime < SlotSeconds) return;

        _quietest[_next] = _slotQuietest;
        _next = (_next + 1) % Slots;
        if (_filled < Slots) _filled++;
        double elapsed = _slotTime;
        _slotQuietest = double.MaxValue;
        _slotTime = 0;

        // The median of the quiet moments: a single dip or a long word does not move it.
        Array.Copy(_quietest, _sorted, _filled);
        Array.Sort(_sorted, 0, _filled);
        double noise = _sorted[_filled / 2];
        double target = Math.Clamp(noise + Margin, MinDb, MaxDb);
        GateDb += (target - GateDb) * (1 - Math.Exp(-elapsed / 1.5)); // glides over a couple of seconds
    }
}
