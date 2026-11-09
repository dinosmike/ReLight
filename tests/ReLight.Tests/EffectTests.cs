using ReLight.Core;
using ReLight.Core.Effects;

namespace ReLight.Tests;

public class EffectTests
{
    static readonly LedLayout Layout = new(new StripLayout { Top = 30, Right = 17, Bottom = 30, Left = 17 });

    public static IEnumerable<object[]> AllEffects() => EffectCatalog.All.Select(e => new object[] { e.Id });

    [Theory]
    [MemberData(nameof(AllEffects))]
    public void EveryEffect_GivesValidColors_WithAnyAllowedPalette(string id)
    {
        var info = EffectCatalog.Get(id);
        foreach (int colors in new[] { info.MinColors, info.MaxColors })
        {
            var settings = EffectSettings.Default(info);
            while (settings.Colors.Count < colors) settings.Colors.Add("#40C0FF");
            settings.Colors = settings.Colors.Take(colors).ToList();

            var runner = new EffectRunner(info);
            var frame = EffectFrame.For(Layout);
            var output = new Rgb[Layout.Count];
            bool lit = false;
            for (int step = 0; step < 120; step++)
            {
                runner.Render(frame, settings, 1 / 30.0, output);
                foreach (var c in output)
                {
                    Assert.True(float.IsFinite(c.R) && float.IsFinite(c.G) && float.IsFinite(c.B), $"{id}: not a number");
                    Assert.InRange(c.R, -0.001f, 1.001f);
                    Assert.InRange(c.G, -0.001f, 1.001f);
                    Assert.InRange(c.B, -0.001f, 1.001f);
                    lit |= c.R + c.G + c.B > 0.05f;
                }
            }
            Assert.True(lit, $"{id} with {colors} colors stayed dark for 4 seconds");
        }
    }

    [Fact]
    public void EveryEffect_HasNameAndDescription()
    {
        Loc.SetLanguage("ru");
        foreach (var e in EffectCatalog.All)
        {
            Assert.NotEqual(e.NameKey, Loc.T(e.NameKey));
            Assert.NotEqual(e.HintKey, Loc.T(e.HintKey));
        }
    }

    [Theory]
    [InlineData(150, 3840, 2160)]
    [InlineData(101, 1920, 1080)]
    [InlineData(57, 1080, 1920)]
    public void SpreadOver_UsesExactlyTheFoundCount(int total, int w, int h)
    {
        var l = new StripLayout();
        l.SpreadOver(total, w, h);
        Assert.Equal(total, l.Total);
        Assert.True(w >= h ? l.Top >= l.Left : l.Top <= l.Left);
    }

    [Fact]
    public void Playlist_SwitchesEffectsByTime()
    {
        var s = new AppSettings { PlaylistOn = true, Playlist = ["fire", "ocean"], PlaylistSeconds = 5 };
        var player = new EffectPlayer(s);
        var frame = EffectFrame.For(Layout);
        var output = new Rgb[Layout.Count];

        player.Render(frame, 0.1, output);
        Assert.Equal("fire", player.CurrentId);
        for (int i = 0; i < 60; i++) player.Render(frame, 0.1, output); // 6 seconds
        Assert.Equal("ocean", player.CurrentId);
    }

    [Fact]
    public void Preview_IsShownWhileEditing()
    {
        var s = new AppSettings { Effect = "rainbow" };
        var player = new EffectPlayer(s);
        var frame = EffectFrame.For(Layout);
        var output = new Rgb[Layout.Count];

        player.Preview = new EffectSettings { Id = "comet", Colors = ["#FF0000", "#000000"], Speed = 50 };
        player.Render(frame, 0.1, output);
        Assert.Equal("comet", player.CurrentId);

        player.Preview = null;
        player.Render(frame, 0.1, output);
        Assert.Equal("rainbow", player.CurrentId);
    }
}
