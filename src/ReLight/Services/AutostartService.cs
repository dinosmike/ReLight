using Microsoft.Win32;

namespace ReLight.Services;

/// <summary>Start with Windows through the per-user Run key, as in ReFlow.</summary>
public static class AutostartService
{
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ReLight";

    /// <summary>Command-line flag added to the Run entry; the app starts in the tray then.</summary>
    public const string AutostartArg = "--autostart";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return !string.IsNullOrEmpty(key?.GetValue(ValueName) as string);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { Length: > 0 } exe)
                key?.SetValue(ValueName, $"\"{exe}\" {AutostartArg}", RegistryValueKind.String);
            else
                key?.DeleteValue(ValueName, false);
        }
        catch
        {
            // A locked-down registry just means no autostart.
        }
    }
}
