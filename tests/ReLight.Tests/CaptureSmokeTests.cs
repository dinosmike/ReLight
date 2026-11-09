using ReLight.Capture;
using ReLight.Core;
using Xunit.Abstractions;

namespace ReLight.Tests;

/// <summary>
/// Needs a real desktop session; run with RELIGHT_CAPTURE_TESTS=1.
/// DXGI allows one duplication of a monitor per process at a time, so it shares a collection with the engine tests.
/// </summary>
[Collection("Screen capture")]
public class CaptureSmokeTests(ITestOutputHelper output)
{
    static bool Enabled => Environment.GetEnvironmentVariable("RELIGHT_CAPTURE_TESTS") == "1";

    [Fact]
    public void Gdi_And_Dxgi_GiveFrames()
    {
        if (!Enabled) return;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!;
        using (var gdi = new GdiScreenSource(screen.Bounds))
        {
            Assert.True(gdi.TryGrab(out var f));
            output.WriteLine($"GDI {f!.Width}x{f.Height}, first px {f.Pixels[0]},{f.Pixels[1]},{f.Pixels[2]}");
            var layout = new LedLayout(new StripLayout());
            var colors = new System.Drawing.Color[layout.Count];
            new ScreenSampler(layout, f.Width, f.Height, 0.15).Sample(f, colors);
            output.WriteLine($"LED 1 = {colors[0]}, LED 60 = {colors[59]}");
        }

        using var dxgi = DxgiScreenSource.TryCreate(screen.DeviceName);
        output.WriteLine(dxgi == null ? "DXGI: no output" : "DXGI: created");
        if (dxgi != null)
        {
            bool ok = false;
            for (int i = 0; i < 20 && !ok; i++) ok = dxgi.TryGrab(out _);
            output.WriteLine($"DXGI grab: {ok}");
        }
    }
}
