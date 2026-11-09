namespace ReLight.Capture;

/// <summary>A small copy of the screen, 32-bit BGRA, rows top to bottom.</summary>
public sealed class ScreenFrame(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[width * height * 4];
}

/// <summary>Something that can hand out small copies of one monitor.</summary>
public interface IScreenSource : IDisposable
{
    /// <summary>Name shown in the status line ("DXGI", "GDI").</summary>
    string Name { get; }

    /// <summary>Latest picture of the screen; false when nothing could be captured this time.</summary>
    bool TryGrab(out ScreenFrame? frame);
}
