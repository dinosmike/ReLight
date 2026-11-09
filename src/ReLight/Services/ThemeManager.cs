using System.Windows;
using Microsoft.Win32;

namespace ReLight.Services;

/// <summary>Switches between the dark and light palettes; "system" follows Windows.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(string setting)
    {
        IsDark = setting switch
        {
            "dark" => true,
            "light" => false,
            _ => !SystemUsesLightTheme(),
        };
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = palette;
        else merged.Add(palette);
    }

    static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }
}
