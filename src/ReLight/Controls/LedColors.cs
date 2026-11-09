using ReLight.Core;
using DColor = System.Drawing.Color;
using MColor = System.Windows.Media.Color;

namespace ReLight.Controls;

/// <summary>Colors of the test picture: each screen edge has its own, the first LED is white.</summary>
public static class LedColors
{
    public static readonly DColor Top = DColor.FromArgb(255, 0, 0);
    public static readonly DColor Right = DColor.FromArgb(0, 255, 0);
    public static readonly DColor Bottom = DColor.FromArgb(0, 0, 255);
    public static readonly DColor Left = DColor.FromArgb(255, 255, 0);
    public static readonly DColor First = DColor.White;

    public static DColor Of(LedLayout layout, int stripIndex) =>
        stripIndex == 0 ? First : layout.SideOf(stripIndex).side switch
        {
            Side.Top => Top,
            Side.Right => Right,
            Side.Bottom => Bottom,
            _ => Left,
        };

    public static MColor ToMedia(this DColor c) => MColor.FromRgb(c.R, c.G, c.B);
}
