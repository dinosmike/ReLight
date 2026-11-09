using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ReLight.Services;

/// <summary>System-wide hotkeys (RegisterHotKey) bound to a window handle.</summary>
public sealed class HotkeyService : IDisposable
{
    const int WmHotkey = 0x0312;
    const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    readonly IntPtr _hwnd;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 0x5200;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    /// <summary>Drop all registrations, e.g. before applying new shortcuts.</summary>
    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    /// <summary>Register "Ctrl+Alt+L"-style text. Returns false when the shortcut is taken or invalid.</summary>
    public bool Register(string? text, Action action)
    {
        if (!TryParse(text, out var modifiers, out var key)) return string.IsNullOrWhiteSpace(text);
        int id = _nextId++;
        uint mods = ModNoRepeat
                    | (modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0)
                    | (modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0)
                    | (modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0)
                    | (modifiers.HasFlag(ModifierKeys.Windows) ? ModWin : 0);
        if (!RegisterHotKey(_hwnd, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse(name, true, out key)) return false;
                    break;
            }
        }
        // A shortcut needs at least one modifier, except function keys.
        return key != Key.None && (modifiers != ModifierKeys.None || key is >= Key.F1 and <= Key.F24);
    }

    static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Clear();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
