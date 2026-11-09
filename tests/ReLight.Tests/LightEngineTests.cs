using System.Collections.Concurrent;
using ReLight.Core;

namespace ReLight.Tests;

/// <summary>DXGI allows one duplication of a monitor per process at a time: keep these in one collection.</summary>
[Collection("Screen capture")]
public class LightEngineTests
{
    static (LightEngine engine, AdalightDevice device, ConcurrentQueue<byte[]> packets) Start(AppSettings s)
    {
        var device = new AdalightDevice();
        var packets = new ConcurrentQueue<byte[]>();
        device.PacketQueued += packets.Enqueue;
        device.SimulateConnected();
        var engine = new LightEngine(s, device);
        engine.Start();
        return (engine, device, packets);
    }

    static AppSettings Settings(LightMode mode) => new()
    {
        Mode = mode,
        Layout = new StripLayout { Top = 10, Right = 5, Bottom = 10, Left = 5 },
        KnownLedCount = 40,   // strip longer than the layout: the rest must be padded black
        Brightness = 50,
        SolidColor = "#FF8000",
        Monitor = null,
    };

    static byte[] WaitPacket(ConcurrentQueue<byte[]> packets)
    {
        for (int i = 0; i < 100; i++)
        {
            if (packets.TryDequeue(out var p)) return p;
            Thread.Sleep(20);
        }
        throw new TimeoutException("no packet");
    }

    [Fact]
    public void ColorMode_SendsSolidColorWithBrightnessAndPadding()
    {
        var (engine, device, packets) = Start(Settings(LightMode.Color));
        var p = WaitPacket(packets);
        engine.Dispose(); device.Dispose();

        Assert.Equal(6 + 40 * 3, p.Length);
        Assert.Equal(new byte[] { 128, 64, 0 }, p[6..9]);             // #FF8000 at 50 %
        Assert.Equal(new byte[] { 128, 64, 0 }, p[(6 + 29 * 3)..(6 + 30 * 3)]); // last layout LED
        Assert.Equal(new byte[] { 0, 0, 0 }, p[(6 + 30 * 3)..(6 + 31 * 3)]);     // padding
    }

    [Fact]
    public void Rainbow_GivesDifferentColorsAlongTheStrip()
    {
        var (engine, device, packets) = Start(Settings(LightMode.Rainbow));
        var p = WaitPacket(packets);
        engine.Dispose(); device.Dispose();

        var first = p[6..9];
        var middle = p[(6 + 15 * 3)..(6 + 16 * 3)];
        Assert.NotEqual(first, middle);
    }

    [Fact]
    public void LightOff_SendsBlackOnce()
    {
        var s = Settings(LightMode.Color);
        s.LightOn = false;
        var (engine, device, packets) = Start(s);
        var p = WaitPacket(packets);
        Thread.Sleep(400);
        engine.Dispose(); device.Dispose();

        Assert.All(p[6..], b => Assert.Equal(0, b));
        Assert.True(packets.IsEmpty); // no stream of black frames
    }

    [Fact]
    public void ScreenMode_CapturesAndStreams()
    {
        if (Environment.GetEnvironmentVariable("RELIGHT_CAPTURE_TESTS") != "1") return;
        var (engine, device, packets) = Start(Settings(LightMode.Screen));
        Thread.Sleep(1500);
        engine.Dispose(); device.Dispose();
        Assert.True(packets.Count > 10, $"only {packets.Count} frames");
    }
}
