using System.Drawing;

namespace ReLight.Core;

/// <summary>Turns colors into the byte stream the strip expects: channel order and brightness.</summary>
public static class ColorOutput
{
    public static readonly string[] Orders = ["RGB", "RBG", "GRB", "GBR", "BRG", "BGR"];

    public static byte[] ToBytes(IReadOnlyList<Color> colors, string order, int brightness)
    {
        var map = ChannelMap(order);
        float k = Math.Clamp(brightness, 0, 100) / 100f;
        var bytes = new byte[colors.Count * 3];
        for (int i = 0; i < colors.Count; i++)
        {
            var c = colors[i];
            Span<byte> rgb = [c.R, c.G, c.B];
            for (int ch = 0; ch < 3; ch++)
                bytes[i * 3 + ch] = (byte)(rgb[map[ch]] * k + 0.5f);
        }
        return bytes;
    }

    /// <summary>For every output byte, which of R=0, G=1, B=2 goes there.</summary>
    static int[] ChannelMap(string order)
    {
        if (order is not { Length: 3 }) order = "RGB";
        return order.ToUpperInvariant().Select(ch => ch switch { 'R' => 0, 'G' => 1, _ => 2 }).ToArray();
    }
}
