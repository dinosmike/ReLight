using ReLight.Capture;
using ReLight.Core;

namespace ReLight.Tests;

public class CinemaTests
{
    /// <summary>160×90 frame: black bars of <paramref name="bar"/> rows above and below, gray picture between.</summary>
    static ScreenFrame Letterboxed(int bar, byte picture = 120, int sideBar = 0, int subtitleRows = 0)
    {
        var f = new ScreenFrame(160, 90);
        for (int y = 0; y < 90; y++)
            for (int x = 0; x < 160; x++)
            {
                bool inPicture = y >= bar && y < 90 - bar && x >= sideBar && x < 160 - sideBar;
                bool subtitle = y >= 90 - bar + 2 && y < 90 - bar + 2 + subtitleRows && x is > 50 and < 110;
                byte v = inPicture ? picture : subtitle ? (byte)255 : (byte)0;
                int o = (y * 160 + x) * 4;
                f.Pixels[o] = f.Pixels[o + 1] = f.Pixels[o + 2] = v;
            }
        return f;
    }

    [Fact]
    public void Measure_FindsBarsAboveAndBelow()
    {
        var m = LetterboxDetector.Measure(Letterboxed(12).Pixels, 160, 90);
        Assert.NotNull(m);
        Assert.Equal(12 / 90.0, m.Value.vertical, 3);
        Assert.Equal(0, m.Value.horizontal, 3);
    }

    [Fact]
    public void Measure_FindsBarsAtTheSides()
    {
        var m = LetterboxDetector.Measure(Letterboxed(0, sideBar: 20).Pixels, 160, 90);
        Assert.Equal(20 / 160.0, m!.Value.horizontal, 3);
    }

    [Fact]
    public void Measure_BlackFrameTeachesNothing() =>
        Assert.Null(LetterboxDetector.Measure(new ScreenFrame(160, 90).Pixels, 160, 90));

    [Fact]
    public void Subtitles_InTheBottomBar_DoNotShrinkIt()
    {
        var d = new LetterboxDetector();
        d.Update(Letterboxed(12, subtitleRows: 5), 0);
        Assert.Equal(12 / 90.0, d.Vertical, 3);
        Assert.Equal(1 - 12 / 90.0, d.Content.Bottom, 3);
    }

    [Fact]
    public void DarkScene_CannotGrowTheBars_ButABrighterPictureShrinksThemAtOnce()
    {
        var d = new LetterboxDetector(memorySeconds: 3);
        d.Update(Letterboxed(12), 0);
        d.Update(Letterboxed(25), 1);           // dark top of a night scene looks like a wider bar
        Assert.Equal(12 / 90.0, d.Vertical, 3);
        d.Update(new ScreenFrame(160, 90), 2);  // fade to black: ignored
        Assert.Equal(12 / 90.0, d.Vertical, 3);
        d.Update(Letterboxed(0), 2.5);          // full-screen picture: bars gone right away
        Assert.Equal(0, d.Vertical);
        Assert.Equal(new System.Drawing.RectangleF(0, 0, 1, 1), d.Content);
    }

    [Fact]
    public void WiderBars_AreTakenAfterTheMemoryPasses()
    {
        var d = new LetterboxDetector(memorySeconds: 3);
        d.Update(Letterboxed(5), 0);
        for (double t = 0.5; t <= 4; t += 0.5) d.Update(Letterboxed(15), t);
        Assert.Equal(15 / 90.0, d.Vertical, 3);
    }

    [Fact]
    public void Sampler_ReadsInsideThePicture()
    {
        var layout = new LedLayout(new StripLayout { Top = 10, Right = 6, Bottom = 10, Left = 6 });
        var frame = Letterboxed(15, picture: 200);
        var content = System.Drawing.RectangleF.FromLTRB(0, 15 / 90f, 1, 75 / 90f);

        var colors = new System.Drawing.Color[layout.Count];
        new ScreenSampler(layout, 160, 90, 0.2, content).Sample(frame, colors);
        Assert.All(colors, c => Assert.Equal(200, c.R));     // nothing of the bars mixed in

        new ScreenSampler(layout, 160, 90, 0.2).Sample(frame, colors);
        Assert.Contains(colors, c => c.R < 100);            // the plain screen mode reads the bars
    }

    [Fact]
    public void Sound_SwellsSlowlyAndSettles()
    {
        var s = new CinemaSound();
        for (int i = 0; i < 400; i++) s.Update(-30, 0.05);   // 20 s of the film's usual level
        double usual = s.Level;
        s.Update(-12, 0.05);                                // explosion: one frame moves it only a little
        Assert.InRange(s.Level - usual, 0.001, 0.15);
        for (int i = 0; i < 20; i++) s.Update(-12, 0.05);   // a second of it: clearly brighter
        Assert.True(s.Level > usual + 0.2);
        for (int i = 0; i < 200; i++) s.Update(-200, 0.05); // silence: fades away
        Assert.True(s.Level < 0.01);
        Assert.Equal(0.4, s.Brightness(1), 2);              // full slider: silence dims to 40 %
        Assert.Equal(1, s.Brightness(0), 3);                // slider at 0: colors untouched
    }
}
