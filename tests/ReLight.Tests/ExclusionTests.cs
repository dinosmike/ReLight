using System.Collections.Concurrent;
using ReLight.Core;

namespace ReLight.Tests;

public class EngineDetectorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "relight-tests-" + Guid.NewGuid().ToString("N"));

    string MakeFile(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Unity_ByUnityPlayerDll()
    {
        var exe = MakeFile("UnityGame", "Game.exe");
        MakeFile("UnityGame", "UnityPlayer.dll");
        Assert.Equal(EngineDetector.Engine.Unity, EngineDetector.Detect(exe));
    }

    [Fact]
    public void Unreal_ByShippingExe()
    {
        var exe = MakeFile("UE", "MyGame", "Binaries", "Win64", "MyGame-Win64-Shipping.exe");
        Assert.Equal(EngineDetector.Engine.Unreal, EngineDetector.Detect(exe));
    }

    [Fact]
    public void Unreal_ByEngineFolder()
    {
        var exe = MakeFile("UE2", "MyGame", "Binaries", "Win64", "MyGame.exe");
        Directory.CreateDirectory(Path.Combine(_root, "UE2", "Engine", "Binaries"));
        Assert.Equal(EngineDetector.Engine.Unreal, EngineDetector.Detect(exe));
    }

    [Fact]
    public void OrdinaryProgram_IsNotAGame()
    {
        var exe = MakeFile("Tools", "notepad2.exe");
        Assert.Equal(EngineDetector.Engine.None, EngineDetector.Detect(exe));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}

[Collection("Screen capture")]
public class ExclusionEngineTests
{
    [Fact]
    public void Exclusion_ReplacesScreenModeWithColor()
    {
        var s = new AppSettings
        {
            Mode = LightMode.Screen,
            Layout = new StripLayout { Top = 4, Right = 2, Bottom = 4, Left = 2 },
            SolidColor = "#00FF00",
        };
        var device = new AdalightDevice();
        var packets = new ConcurrentQueue<byte[]>();
        device.PacketQueued += packets.Enqueue;
        device.SimulateConnected();
        var engine = new LightEngine(s, device) { Exclusion = new ActiveExclusion("game.exe", ExclusionAction.Color, "rule") };
        engine.Start();
        byte[]? p = null;
        for (int i = 0; i < 100 && !packets.TryDequeue(out p); i++) Thread.Sleep(20);
        engine.Dispose(); device.Dispose();

        Assert.NotNull(p);
        Assert.Equal(new byte[] { 0, 255, 0 }, p![6..9]);
    }

    static byte[] FirstPacket(AppSettings s, ActiveExclusion? exclusion)
    {
        var device = new AdalightDevice();
        var packets = new ConcurrentQueue<byte[]>();
        device.PacketQueued += packets.Enqueue;
        device.SimulateConnected();
        var engine = new LightEngine(s, device) { Exclusion = exclusion };
        engine.Start();
        byte[]? p = null;
        for (int i = 0; i < 100 && !packets.TryDequeue(out p); i++) Thread.Sleep(20);
        engine.Dispose(); device.Dispose();
        return p!;
    }

    [Fact]
    public void Exclusion_OwnColor_WinsOverTheProgramColor()
    {
        var s = new AppSettings { Mode = LightMode.Screen, Layout = new StripLayout { Top = 4, Right = 2, Bottom = 4, Left = 2 }, SolidColor = "#00FF00" };
        var p = FirstPacket(s, new ActiveExclusion("game.exe", ExclusionAction.Color, "rule", Color: "#0000FF"));
        Assert.Equal(new byte[] { 0, 0, 255 }, p[6..9]);
    }

    [Fact]
    public void Exclusion_WorksInAnyMode()
    {
        // A player in front switches to cinema from any mode, so exclusions are no longer limited to the screen mode.
        var s = new AppSettings { Mode = LightMode.Color, Layout = new StripLayout { Top = 4, Right = 2, Bottom = 4, Left = 2 }, SolidColor = "#00FF00" };
        var p = FirstPacket(s, new ActiveExclusion("game.exe", ExclusionAction.Color, "rule", Color: "#0000FF"));
        Assert.Equal(new byte[] { 0, 0, 255 }, p[6..9]);
    }

    [Fact]
    public void Exclusion_OwnEffect_WinsOverPlaylist()
    {
        var s = new AppSettings { PlaylistOn = true, Playlist = ["fire", "ocean"] };
        var player = new ReLight.Core.Effects.EffectPlayer(s);
        var frame = ReLight.Core.Effects.EffectFrame.For(new LedLayout(new StripLayout()));
        var output = new ReLight.Core.Effects.Rgb[frame.Count];
        player.Render(frame, 0.1, output, "comet");
        Assert.Equal("comet", player.CurrentId);
    }

    [Fact]
    public void Exclusion_Off_SendsBlackOnce()
    {
        var s = new AppSettings { Mode = LightMode.Screen, Layout = new StripLayout { Top = 4, Right = 2, Bottom = 4, Left = 2 } };
        var device = new AdalightDevice();
        var packets = new ConcurrentQueue<byte[]>();
        device.PacketQueued += packets.Enqueue;
        device.SimulateConnected();
        var engine = new LightEngine(s, device) { Exclusion = new ActiveExclusion("game.exe", ExclusionAction.Off, "rule") };
        engine.Start();
        Thread.Sleep(500);
        engine.Dispose(); device.Dispose();

        Assert.Single(packets);
        Assert.All(packets.First()[6..], b => Assert.Equal(0, b));
    }
}

public class ExclusionScannerTests
{
    /// <summary>A real running process that looks like a Unity game: a copy of ping.exe next to UnityPlayer.dll.</summary>
    [Fact]
    public void FindsARunningUnityGame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "relight-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "FakeUnityGame.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        File.WriteAllText(Path.Combine(dir, "UnityPlayer.dll"), "");
        using var game = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-n 30 127.0.0.1")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!;
        try
        {
            Thread.Sleep(300);
            var settings = new AppSettings { AutoUnity = true, UnityAction = ExclusionAction.Color };
            var found = ExclusionScanner.Find(settings);
            // Another Unity game may be running on this machine too (found first): any of them will do.
            Assert.NotNull(found);
            Assert.Equal("unity", found.Reason);
            Assert.Equal(ExclusionAction.Color, found.Action);
        }
        finally
        {
            game.Kill();
            game.WaitForExit();
        }
    }

    /// <summary>A player running in the background (not in front) must not switch to cinema; a plain rule counts while running.</summary>
    [Fact]
    public void PlayerInTheBackground_DoesNotCount()
    {
        var dir = Path.Combine(Path.GetTempPath(), "relight-player-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "vlc.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        using var player = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-n 30 127.0.0.1")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!;
        try
        {
            Thread.Sleep(300);
            Assert.Null(ExclusionScanner.Find(new AppSettings { AutoPlayers = true }));
            Assert.Null(ExclusionScanner.Find(new AppSettings
            {
                AutoPlayers = false,
                Exclusions = [new ExclusionRule { Exe = "vlc.exe", Action = ExclusionAction.Cinema }],
            }));
            var found = ExclusionScanner.Find(new AppSettings
            {
                AutoPlayers = false,
                Exclusions = [new ExclusionRule { Exe = "VLC.exe", Action = ExclusionAction.Off }],
            });
            Assert.Equal(ExclusionAction.Off, found?.Action);
        }
        finally
        {
            player.Kill();
            player.WaitForExit();
        }
    }
}
