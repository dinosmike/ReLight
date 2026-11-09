using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ReLight.Core;

/// <summary>What the light does instead of screen capture while an excluded program runs.</summary>
/// <summary>What an exclusion switches to: off, or one of the modes in the order of <see cref="LightMode"/>.</summary>
public enum ExclusionAction { Off, Screen, Color, Rainbow, Music, Cinema, Mic }

/// <summary>A program that switches the screen mode to something else while it runs.</summary>
public sealed class ExclusionRule
{
    /// <summary>File name, e.g. "game.exe" — matched without regard to case or folder.</summary>
    public string Exe { get; set; } = "";

    /// <summary>Full path where it was picked, for the icon and the tooltip.</summary>
    public string? Path { get; set; }

    public ExclusionAction Action { get; set; } = ExclusionAction.Rainbow;

    /// <summary>For <see cref="ExclusionAction.Color"/>: its own color, #RRGGBB; null = the color chosen in the program.</summary>
    public string? Color { get; set; }

    /// <summary>For <see cref="ExclusionAction.Rainbow"/>: its own effect id; null = the effect or playlist chosen in the program.</summary>
    public string? Effect { get; set; }
}

/// <summary>Why the screen mode is replaced right now.</summary>
public sealed record ActiveExclusion(string Program, ExclusionAction Action, string Reason, string? Color = null, string? Effect = null);

/// <summary>Recognises games made with Unity or Unreal Engine by the engine files next to the exe.</summary>
public static class EngineDetector
{
    public enum Engine { None, Unity, Unreal }

    static readonly Dictionary<string, Engine> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Engine Detect(string exePath)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(exePath, out var known)) return known;
        }
        var engine = DetectUncached(exePath);
        lock (Cache) Cache[exePath] = engine;
        return engine;
    }

    static Engine DetectUncached(string exePath)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(exePath);
            if (dir == null) return Engine.None;
            var name = System.IO.Path.GetFileNameWithoutExtension(exePath);

            // Unity: UnityPlayer.dll next to the exe, or the "<Game>_Data" folder with Unity's files.
            if (File.Exists(System.IO.Path.Combine(dir, "UnityPlayer.dll"))
                || File.Exists(System.IO.Path.Combine(dir, name + "_Data", "globalgamemanagers"))
                || Directory.Exists(System.IO.Path.Combine(dir, name + "_Data", "il2cpp_data")))
                return Engine.Unity;

            // Unreal: "<Game>-Win64-Shipping.exe" in "...\Binaries\Win64", with an "Engine" folder higher up.
            if (name.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-Win64-Test", StringComparison.OrdinalIgnoreCase))
                return Engine.Unreal;
            var d = new DirectoryInfo(dir);
            if (d.Name.Equals("Win64", StringComparison.OrdinalIgnoreCase)
                && d.Parent?.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase) == true)
            {
                for (var up = d.Parent.Parent; up != null && up.Parent != null; up = up.Parent)
                    if (Directory.Exists(System.IO.Path.Combine(up.FullName, "Engine", "Binaries")))
                        return Engine.Unreal;
            }
        }
        catch
        {
            // No access to the folder: just not recognised.
        }
        return Engine.None;
    }
}

/// <summary>Popular video players, recognised by the exe name.</summary>
public static class VideoPlayers
{
    public static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc.exe", "mpc-hc.exe", "mpc-hc64.exe", "mpc-be.exe", "mpc-be64.exe",
        "PotPlayer.exe", "PotPlayer64.exe", "PotPlayerMini.exe", "PotPlayerMini64.exe",
        "mpv.exe", "mpvnet.exe", "smplayer.exe", "KMPlayer.exe", "KMPlayer64.exe", "GOM.exe",
        "kodi.exe", "Plex.exe", "PlexMediaPlayer.exe", "JellyfinMediaPlayer.exe", "stremio.exe",
        "wmplayer.exe", "Video.UI.exe", "Microsoft.Media.Player.exe", "bsplayer.exe", "5KPlayer.exe",
    };
}

/// <summary>
/// Looks every couple of seconds (and at once when another window comes to the front) for
/// programs from the exclusion list, video players in front and, if asked, Unity and Unreal games.
/// Order: a player (or a "cinema" rule) whose window is in front, then rules for running
/// programs, then games found by their engine.
/// </summary>
/// <remarks>
/// It must not disturb the game it is looking for: the process list comes from one system
/// snapshot, and the exe path of each process is asked once, with the weakest right Windows
/// has (PROCESS_QUERY_LIMITED_INFORMATION) — no reading of the game's memory or modules,
/// which anti-cheat systems watch for and which costs far more.
/// </remarks>
public static class ExclusionScanner
{
    /// <summary>Exe path per process id (null = not available); the name guards against reused ids.</summary>
    static readonly Dictionary<int, (string name, string? path)> Paths = [];

    public static ActiveExclusion? Find(AppSettings settings)
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return null; }

        try
        {
            var rules = settings.Exclusions.ToList();
            int self = Environment.ProcessId;

            // In front: watching a film in a player.
            if (ForegroundProgram() is { } front)
            {
                var own = rules.FirstOrDefault(r => string.Equals(r.Exe, front, StringComparison.OrdinalIgnoreCase));
                if (own is { Action: ExclusionAction.Cinema })
                    return new ActiveExclusion(front, own.Action, "rule", own.Color, own.Effect);
                if (own == null && settings.AutoPlayers && VideoPlayers.Known.Contains(front))
                    return new ActiveExclusion(front, settings.PlayersAction, "player", settings.PlayersColor, settings.PlayersEffect);
            }

            // Running: games and other programs listed by the user ("cinema" ones count only in front).
            foreach (var p in processes)
            {
                string name = p.ProcessName + ".exe";
                var rule = rules.FirstOrDefault(r => r.Action != ExclusionAction.Cinema
                                                     && string.Equals(r.Exe, name, StringComparison.OrdinalIgnoreCase));
                if (rule != null) return new ActiveExclusion(name, rule.Action, "rule", rule.Color, rule.Effect);
            }

            if (!settings.AutoUnity && !settings.AutoUnreal) return null;
            lock (Paths)
            {
                var alive = processes.Select(p => p.Id).ToHashSet();
                foreach (var gone in Paths.Keys.Where(id => !alive.Contains(id)).ToList()) Paths.Remove(gone);
            }
            foreach (var p in processes)
            {
                if (p.Id == self || p.Id <= 4) continue;
                var path = PathOf(p.Id, p.ProcessName);
                if (path == null) continue;
                var engine = EngineDetector.Detect(path);
                if (engine == EngineDetector.Engine.Unity && settings.AutoUnity)
                    return new ActiveExclusion(p.ProcessName + ".exe", settings.UnityAction, "unity", settings.UnityColor, settings.UnityEffect);
                if (engine == EngineDetector.Engine.Unreal && settings.AutoUnreal)
                    return new ActiveExclusion(p.ProcessName + ".exe", settings.UnrealAction, "unreal", settings.UnrealColor, settings.UnrealEffect);
            }
            return null;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    static string? _lastFront;

    /// <summary>
    /// Exe name of the program whose window is in front. While ReLight itself is in front (its
    /// sliders tuned over a playing film) the program in front before it still counts.
    /// </summary>
    static string? ForegroundProgram()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return _lastFront;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return _lastFront;
        if (pid == 0) return _lastFront = null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return _lastFront = p.ProcessName + ".exe";
        }
        catch
        {
            return _lastFront = null; // closed in the meantime
        }
    }

    static string? PathOf(int id, string name)
    {
        lock (Paths)
            if (Paths.TryGetValue(id, out var known) && known.name == name) return known.path;
        string? path = null;
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, id);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size)) path = buffer.ToString(0, size);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        lock (Paths) Paths[id] = (name, path);
        return path;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
