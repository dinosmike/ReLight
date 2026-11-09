using System.Drawing;

namespace ReLight.Capture;

/// <summary>
/// Finds the black bars of a film (above/below a wide picture, beside a narrow one) so the
/// cinema mode reads the colors at the edge of the picture, not of the bars.
/// </summary>
/// <remarks>
/// A dark scene makes the bars look bigger than they are, never smaller, so the bars are the
/// smallest seen over the last few seconds: a bright frame corrects them at once, a dark one
/// cannot grow them. The top bar is trusted for the bottom one too: subtitles are often drawn
/// over the bottom bar and would otherwise shrink it. Frames that are black as a whole
/// (fades, pauses between scenes) teach nothing and are skipped.
/// </remarks>
public sealed class LetterboxDetector
{
    /// <summary>A pixel brighter than this in any channel is picture, not bar.</summary>
    public const int Threshold = 28;

    /// <summary>Bars never take more than this share of the height (each) or the width (each).</summary>
    public const double MaxVertical = 0.35, MaxHorizontal = 0.3;

    /// <summary>Bars thinner than this are noise: a dark first row, a mip-blurred edge.</summary>
    const double MinBar = 0.015;

    readonly double _memorySeconds;
    readonly Queue<(double time, double vertical, double horizontal)> _seen = new();

    /// <summary>Picture area in 0..1 of the screen; the whole screen until bars are found.</summary>
    public RectangleF Content { get; private set; } = new(0, 0, 1, 1);

    /// <summary>Bar above (= below) the picture, 0..1 of the height.</summary>
    public double Vertical { get; private set; }

    /// <summary>Bar left (= right) of the picture, 0..1 of the width.</summary>
    public double Horizontal { get; private set; }

    public LetterboxDetector(double memorySeconds = 3) => _memorySeconds = memorySeconds;

    /// <summary>Look at a new frame; <paramref name="now"/> in seconds. Returns true when the bars changed.</summary>
    public bool Update(ScreenFrame frame, double now)
    {
        var measured = Measure(frame.Pixels, frame.Width, frame.Height);
        if (measured is { } m) _seen.Enqueue((now, m.vertical, m.horizontal));
        while (_seen.Count > 1 && now - _seen.Peek().time > _memorySeconds) _seen.Dequeue();
        if (_seen.Count == 0) return false;

        double v = _seen.Min(s => s.vertical), h = _seen.Min(s => s.horizontal);
        if (v < MinBar) v = 0;
        if (h < MinBar) h = 0;
        // Whole frame rows/columns only: tiny jitter must not rebuild the LED areas every frame.
        v = Math.Round(v * frame.Height) / frame.Height;
        h = Math.Round(h * frame.Width) / frame.Width;
        if (v == Vertical && h == Horizontal) return false;
        Vertical = v;
        Horizontal = h;
        Content = RectangleF.FromLTRB((float)h, (float)v, (float)(1 - h), (float)(1 - v));
        return true;
    }

    /// <summary>Forget everything: next frames decide anew.</summary>
    public void Reset()
    {
        _seen.Clear();
        Vertical = Horizontal = 0;
        Content = new RectangleF(0, 0, 1, 1);
    }

    /// <summary>
    /// Bars of one frame (BGRA): the top bar and the narrower of the side bars, as shares
    /// of the height and the width. Null when the frame has no picture at all.
    /// </summary>
    public static (double vertical, double horizontal)? Measure(byte[] px, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        int maxRows = (int)(height * MaxVertical), maxCols = (int)(width * MaxHorizontal);

        int top = 0;
        while (top < height && !RowLit(px, width, top, 0, width)) top++;
        if (top == height) return null; // black from top to bottom
        top = Math.Min(top, maxRows);

        int bottom = height - 1;
        while (bottom > top && !RowLit(px, width, bottom, 0, width)) bottom--;

        int left = 0;
        while (left < maxCols && !ColumnLit(px, width, left, top, bottom + 1)) left++;
        int right = 0;
        while (right < maxCols && !ColumnLit(px, width, width - 1 - right, top, bottom + 1)) right++;

        return ((double)top / height, (double)Math.Min(left, right) / width);
    }

    static bool RowLit(byte[] px, int width, int y, int x0, int x1)
    {
        int need = Math.Max(1, (x1 - x0) / 50), lit = 0, row = y * width * 4;
        for (int x = x0; x < x1; x++)
            if (Bright(px, row + x * 4) && ++lit >= need) return true;
        return false;
    }

    static bool ColumnLit(byte[] px, int width, int x, int y0, int y1)
    {
        int need = Math.Max(1, (y1 - y0) / 50), lit = 0;
        for (int y = y0; y < y1; y++)
            if (Bright(px, (y * width + x) * 4) && ++lit >= need) return true;
        return false;
    }

    static bool Bright(byte[] px, int o) => px[o] > Threshold || px[o + 1] > Threshold || px[o + 2] > Threshold;
}
