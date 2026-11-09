using ReLight.Audio;
using ReLight.Core;
using ReLight.Core.Effects;

namespace ReLight.Tests;

public class MicTests
{
    static readonly LedLayout Layout = new(new StripLayout { Top = 30, Right = 17, Bottom = 30, Left = 17, Start = StartCorner.BottomLeft });

    static float Brightness(Rgb[] c) => c.Sum(x => x.R + x.G + x.B);

    static Rgb[] Run(MicVisualizer v, MicOptions o, SoundFeatures s, int frames)
    {
        var frame = EffectFrame.For(Layout);
        var output = new Rgb[Layout.Count];
        for (int i = 0; i < frames; i++) v.Render(frame, i == 0 ? s : s with { Hit = 0 }, o, 1 / 30.0, output);
        return output;
    }

    [Fact]
    public void Silence_IsCompletelyBlack()
    {
        var o = Run(new MicVisualizer(), new MicOptions(), default, 30);
        Assert.All(o, c => Assert.Equal(0f, c.R + c.G + c.B));
    }

    [Fact]
    public void AfterSounds_SilenceFadesToBlack()
    {
        var v = new MicVisualizer();
        var options = new MicOptions();
        Run(v, options, new SoundFeatures(0.8f, 0.8f, 0.4f, 1f), 20);
        var o = Run(v, options, default, 150); // 5 seconds of silence
        Assert.True(Brightness(o) < 0.01f, $"still {Brightness(o)} after silence");
    }

    [Fact]
    public void Clap_MakesARingThatFades()
    {
        var v = new MicVisualizer();
        var options = new MicOptions();
        var flash = Run(v, options, new SoundFeatures(0.3f, 0, 0.5f, 1f), 1);
        float first = Brightness(flash);
        var running = Run(v, options, new SoundFeatures(0.3f, 0, 0.5f, 0), 10);
        Assert.True(Brightness(running) > 0.5f, "the ring should still be running");
        var later = Run(v, options, default, 90);
        Assert.True(Brightness(later) < first, "the ring should fade out");
    }

    [Fact]
    public void Voice_RisesFromTheBottom()
    {
        var frame = EffectFrame.For(Layout);
        var o = Run(new MicVisualizer(), new MicOptions(), new SoundFeatures(0.6f, 0.8f, 0.3f, 0), 6);
        float bottom = 0, top = 0;
        for (int i = 0; i < Layout.Count; i++)
        {
            if (frame.Y[i] > 0.95f) bottom += o[i].R + o[i].G + o[i].B;
            if (frame.Y[i] < 0.05f) top += o[i].R + o[i].G + o[i].B;
        }
        Assert.True(bottom > top * 2, $"bottom {bottom} should be brighter than top {top} right after speaking");
    }

    [Fact]
    public void VoiceOff_MakesNoWaves()
    {
        var o = Run(new MicVisualizer(), new MicOptions { Voice = false }, new SoundFeatures(0.6f, 0.8f, 0.3f, 0), 20);
        Assert.Equal(0f, Brightness(o));
    }

    [Fact]
    public void AutoGate_SitsAboveTheRoomNoise()
    {
        // A room at -58 dB with speech at -25 dB now and then: the threshold ends up just above the noise.
        var gate = new SilenceGate(-45);
        var random = new Random(1);
        for (int i = 0; i < 50 * 12; i++)
        {
            bool speaking = i % 100 < 40;
            gate.Update(speaking ? -25 + random.NextDouble() * 5 : -58 + random.NextDouble() * 2, 0.02);
        }
        Assert.InRange(gate.GateDb, -54, -48);
    }

    [Fact]
    public void AutoGate_FollowsALouderRoom()
    {
        var gate = new SilenceGate(-60);
        for (int i = 0; i < 50 * 10; i++) gate.Update(-60, 0.02);
        double quiet = gate.GateDb;
        for (int i = 0; i < 50 * 12; i++) gate.Update(-40, 0.02); // a fan was switched on
        Assert.InRange(quiet, -55, -53);
        Assert.InRange(gate.GateDb, -35, -33);
    }

    [Fact]
    public void AutoGate_StaysInTheSliderRange()
    {
        var gate = new SilenceGate(-50);
        for (int i = 0; i < 50 * 12; i++) gate.Update(-120, 0.02); // digital silence
        Assert.Equal(SilenceGate.MinDb, gate.GateDb, 1);
    }

    [Fact]
    public void Gate_MapsToDecibels()
    {
        Assert.Equal(-70, new MicOptions { Gate = 0 }.GateDb);
        Assert.Equal(-20, new MicOptions { Gate = 100 }.GateDb);
    }
}
