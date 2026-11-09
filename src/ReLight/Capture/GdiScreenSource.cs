using System.Runtime.InteropServices;

namespace ReLight.Capture;

/// <summary>
/// Fallback capture through GDI: one StretchBlt of the whole monitor into a tiny bitmap.
/// Works everywhere (remote desktop, virtual machines), but costs more than DXGI.
/// </summary>
public sealed class GdiScreenSource : IScreenSource
{
    readonly System.Drawing.Rectangle _bounds;
    readonly ScreenFrame _frame;
    readonly IntPtr _memDc, _bitmap, _oldBitmap, _bits;

    public string Name => "GDI";

    public GdiScreenSource(System.Drawing.Rectangle bounds, int width = 160)
    {
        _bounds = bounds;
        int height = Math.Max(1, width * bounds.Height / Math.Max(1, bounds.Width));
        _frame = new ScreenFrame(width, height);

        var bmi = new BitmapInfo
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = width,
            Height = -height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var screen = GetDC(IntPtr.Zero);
        _memDc = CreateCompatibleDC(screen);
        _bitmap = CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
        _oldBitmap = SelectObject(_memDc, _bitmap);
        SetStretchBltMode(_memDc, Halftone);
        SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
    }

    public bool TryGrab(out ScreenFrame? frame)
    {
        frame = null;
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (!StretchBlt(_memDc, 0, 0, _frame.Width, _frame.Height,
                    screen, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SrcCopy))
                return false;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
        GdiFlush();
        Marshal.Copy(_bits, _frame.Pixels, 0, _frame.Pixels.Length);
        frame = _frame;
        return true;
    }

    public void Dispose()
    {
        SelectObject(_memDc, _oldBitmap);
        DeleteObject(_bitmap);
        DeleteDC(_memDc);
    }

    const int Halftone = 4;
    const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        public int Colors; // one RGBQUAD, unused for 32 bpp
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
}
