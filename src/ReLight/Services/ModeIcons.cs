using System.Windows.Controls;
using System.Windows.Media;
using ReLight.Core;

namespace ReLight.Services;

/// <summary>
/// The glyphs of the modes, the same on the main window's tabs, in the tray menu and in the exclusions,
/// so a mode looks alike everywhere. The modes always go in the order of <see cref="LightMode"/>.
/// </summary>
public static class ModeIcons
{
    public static string Glyph(LightMode mode) => mode switch
    {
        LightMode.Screen => "",
        LightMode.Color => "",
        LightMode.Rainbow => "",
        LightMode.Music => "",
        LightMode.Cinema => "",
        _ => "",
    };

    /// <summary>Power glyph, for "turn the light off".</summary>
    public const string Off = "";

    /// <summary>A small pale icon for menus and lists.</summary>
    public static TextBlock Make(string glyph)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Opacity = 0.6,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Rl.Text.Muted");
        return icon;
    }
}
