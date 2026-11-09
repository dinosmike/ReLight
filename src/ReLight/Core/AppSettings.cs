using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReLight.Core;

public enum StartCorner { TopLeft, TopRight, BottomRight, BottomLeft }

public enum LightMode { Screen, Color, Rainbow, Music, Cinema, Mic }

/// <summary>How the strip is glued around the screen.</summary>
public sealed class StripLayout
{
    public int Top { get; set; } = 40;
    public int Right { get; set; } = 25;
    public int Bottom { get; set; } = 40;
    public int Left { get; set; } = 25;

    /// <summary>Corner nearest to the first LED of the strip.</summary>
    public StartCorner Start { get; set; } = StartCorner.BottomLeft;

    /// <summary>Direction the strip runs from its first LED, looking at the screen.</summary>
    public bool Clockwise { get; set; } = true;

    /// <summary>How many LEDs the real start is shifted from the corner (can be negative).</summary>
    public int Offset { get; set; }

    [JsonIgnore]
    public int Total => Top + Right + Bottom + Left;

    public StripLayout Clone() => (StripLayout)MemberwiseClone();

    /// <summary>Split a total LED count over the four sides in proportion to the screen shape.</summary>
    public void SpreadOver(int total, double screenWidth, double screenHeight)
    {
        int horizontal = (int)Math.Round(total * screenWidth / (2 * (screenWidth + screenHeight)));
        int vertical = (total - 2 * horizontal) / 2;
        int rest = total - 2 * horizontal - 2 * vertical;
        Top = horizontal + rest;
        Bottom = horizontal;
        Left = vertical;
        Right = vertical;
    }
}

/// <summary>User's colors and speed for one rainbow effect.</summary>
public sealed class EffectSettings
{
    public string Id { get; set; } = "";
    public List<string> Colors { get; set; } = [];
    public int Speed { get; set; } = 40;

    public EffectSettings Clone() => new() { Id = Id, Colors = [.. Colors], Speed = Speed };

    public static EffectSettings Default(Effects.EffectInfo info) =>
        new() { Id = info.Id, Colors = [.. info.DefaultColors], Speed = info.DefaultSpeed };
}

/// <summary>How the microphone mode reacts. Hues are in degrees 0..360, the rest 0..100.</summary>
public sealed class MicOptions
{
    /// <summary>Silence threshold: 0 = -70 dB (hears everything) .. 100 = -20 dB (only loud sounds).</summary>
    public int Gate { get; set; } = 30;

    /// <summary>The threshold follows the room noise by itself (<see cref="SilenceGate"/>); <see cref="Gate"/> is then only the start.</summary>
    public bool AutoGate { get; set; } = true;

    public bool Voice { get; set; } = true;
    public int VoiceLowHue { get; set; } = 15;
    public int VoiceHighHue { get; set; } = 220;
    public int WaveSpeed { get; set; } = 50;

    public bool Claps { get; set; } = true;
    /// <summary>"random", "white" or "voice".</summary>
    public string RingColors { get; set; } = "random";
    public int RingSpeed { get; set; } = 50;

    /// <summary>How long the light takes to fade after a sound.</summary>
    public int Afterglow { get; set; } = 50;

    /// <summary>Silence threshold in dBFS.</summary>
    public double GateDb => -70 + Math.Clamp(Gate, 0, 100) * 0.5;

    public MicOptions Clone() => (MicOptions)MemberwiseClone();
}

public sealed class AppSettings
{
    /// <summary>Last COM name of the strip; only a fallback, the USB identity below is what counts.</summary>
    public string? Port { get; set; }

    /// <summary>Windows device instance of the strip's USB-serial adapter.</summary>
    public string? PortDeviceId { get; set; }

    /// <summary>USB model of the adapter, e.g. VID_1A86&amp;PID_7523 (CH340).</summary>
    public string? PortHardwareKey { get; set; }

    /// <summary>Connect on start: true once the user has connected the strip at least once.</summary>
    public bool AutoConnect { get; set; }
    public int BaudRate { get; set; } = 115200;
    public string ColorOrder { get; set; } = "RGB";
    public int Brightness { get; set; } = 100;

    // ----- light -----

    /// <summary>The strip is lit (false = switched off by the power button or hotkey).</summary>
    public bool LightOn { get; set; } = true;

    public LightMode Mode { get; set; } = LightMode.Screen;

    /// <summary>Color for the single-color mode, #RRGGBB.</summary>
    public string SolidColor { get; set; } = "#FFB45A";

    /// <summary>Rainbow mode: the effect shown when the playlist is off.</summary>
    public string Effect { get; set; } = "rainbow";

    /// <summary>Rainbow mode: colors and speed the user changed, per effect.</summary>
    public List<EffectSettings> Effects { get; set; } = [];

    /// <summary>Rainbow mode: switch between the effects in <see cref="Playlist"/> by itself.</summary>
    public bool PlaylistOn { get; set; }

    public List<string> Playlist { get; set; } = ["rainbow", "aurora", "ocean", "fire"];

    /// <summary>How long each playlist effect plays, seconds.</summary>
    public int PlaylistSeconds { get; set; } = 60;

    /// <summary>Settings of an effect: the user's if they changed it, otherwise its defaults.</summary>
    public EffectSettings EffectFor(string id)
    {
        lock (Effects)
        {
            var found = Effects.FirstOrDefault(e => e.Id == id);
            if (found != null && found.Colors.Count > 0) return found;
            return EffectSettings.Default(Core.Effects.EffectCatalog.Get(id));
        }
    }

    public void SetEffect(EffectSettings value)
    {
        lock (Effects)
        {
            Effects.RemoveAll(e => e.Id == value.Id);
            Effects.Add(value.Clone());
        }
    }

    /// <summary>Screen mode: frames per second asked from the capture (the strip may limit it further).</summary>
    public int ScreenFps { get; set; } = 30;

    /// <summary>Screen mode: 0 = instant, 100 = very slow fades.</summary>
    public int Smoothing { get; set; } = 40;

    /// <summary>Screen mode: 100 = colors as on screen, more = juicier.</summary>
    public int Saturation { get; set; } = 120;

    /// <summary>Screen mode: "auto" (DXGI, GDI if it fails), "dxgi" or "gdi".</summary>
    public string CaptureMethod { get; set; } = "auto";

    /// <summary>Screen mode: how deep into the picture each LED looks, % of the shorter side.</summary>
    public int CaptureDepth { get; set; } = 15;

    /// <summary>Music mode sensitivity 0..100.</summary>
    public int MusicSensitivity { get; set; } = 50;

    /// <summary>Cinema mode: 0 = instant, 100 = very slow fades (calmer than the screen mode by default).</summary>
    public int CinemaSmoothing { get; set; } = 70;

    /// <summary>Cinema mode: how deep into the picture each LED looks, % of the picture's shorter side.</summary>
    public int CinemaDepth { get; set; } = 30;

    /// <summary>Cinema mode: how much the soundtrack breathes the brightness, 0 = not at all.</summary>
    public int CinemaSound { get; set; } = 40;

    /// <summary>Cinema mode: skip the black bars above/below (and beside) the picture.</summary>
    public bool CinemaBars { get; set; } = true;

    /// <summary>Microphone mode: Windows id of the microphone, null = the default one.</summary>
    public string? MicDevice { get; set; }

    /// <summary>Microphone mode sensitivity 0..100.</summary>
    public int MicSensitivity { get; set; } = 60;

    public MicOptions Mic { get; set; } = new();

    // ----- exclusions -----

    /// <summary>Programs that replace the screen mode while they run (mostly games).</summary>
    public List<ExclusionRule> Exclusions { get; set; } = [];

    public bool AutoUnity { get; set; }
    public ExclusionAction UnityAction { get; set; } = ExclusionAction.Color;
    public string? UnityColor { get; set; } = "#FF2D95";
    public string? UnityEffect { get; set; }
    public bool AutoUnreal { get; set; }
    public ExclusionAction UnrealAction { get; set; } = ExclusionAction.Color;
    public string? UnrealColor { get; set; } = "#00FF40";
    public string? UnrealEffect { get; set; }

    /// <summary>Known video players switch the light while their window is in front; off until switched on.</summary>
    public bool AutoPlayers { get; set; }
    public ExclusionAction PlayersAction { get; set; } = ExclusionAction.Cinema;
    public string? PlayersColor { get; set; }
    public string? PlayersEffect { get; set; }

    // ----- program -----

    public string HotkeyToggle { get; set; } = "Ctrl+Alt+L";
    public string HotkeyNextMode { get; set; } = "Ctrl+Alt+M";
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; }

    /// <summary>Windows device name of the screen the strip is attached to, e.g. \\.\DISPLAY1.</summary>
    public string? Monitor { get; set; }

    /// <summary>"system", "dark" or "light".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"auto", "ru" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>LED count found with the running-dot test, 0 when unknown.</summary>
    public int KnownLedCount { get; set; }

    public StripLayout Layout { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReLight");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch
        {
            // A broken settings file must never stop the app from starting.
        }
        return new();
    }

    System.Threading.Timer? _saveTimer;

    /// <summary>Save a moment later; while a slider is dragged this writes the file once, not on every step.</summary>
    public void SaveSoon()
    {
        _saveTimer ??= new System.Threading.Timer(_ => Save());
        _saveTimer.Change(400, System.Threading.Timeout.Infinite);
    }

    static readonly object SaveLock = new();

    public void Save()
    {
        try
        {
            lock (SaveLock)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
        }
        catch
        {
            // Not being able to save is not worth crashing over.
        }
    }
}
