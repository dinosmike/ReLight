using System.Drawing;
using ReLight.Core;

namespace ReLight.Capture;

/// <summary>Average color of the screen strip behind every LED.</summary>
public sealed class ScreenSampler
{
    readonly LedLayout _layout;
    readonly int _width, _height;
    readonly Rectangle[] _areas;

    /// <param name="depth">How far into the picture each LED looks, 0..1 of the shorter side.</param>
    /// <param name="content">The part of the screen with the picture, 0..1 (cinema: without the black bars); null = all of it.</param>
    public ScreenSampler(LedLayout layout, int width, int height, double depth, RectangleF? content = null)
    {
        _layout = layout;
        _width = width;
        _height = height;
        Content = content ?? new RectangleF(0, 0, 1, 1);
        _areas = new Rectangle[layout.Count];
        float cw = Math.Max(1e-3f, Content.Width) * width, ch = Math.Max(1e-3f, Content.Height) * height;
        float dx = (float)(depth * Math.Min(cw, ch) / cw);
        float dy = (float)(depth * Math.Min(cw, ch) / ch);
        for (int i = 0; i < layout.Count; i++)
        {
            var r = layout.RegionOf(i, dx, dy);
            r = RectangleF.FromLTRB(Content.Left + r.Left * Content.Width, Content.Top + r.Top * Content.Height,
                Content.Left + r.Right * Content.Width, Content.Top + r.Bottom * Content.Height);
            int x0 = (int)Math.Floor(r.Left * width), x1 = (int)Math.Ceiling(r.Right * width);
            int y0 = (int)Math.Floor(r.Top * height), y1 = (int)Math.Ceiling(r.Bottom * height);
            x0 = Math.Clamp(x0, 0, width - 1); y0 = Math.Clamp(y0, 0, height - 1);
            x1 = Math.Clamp(Math.Max(x1, x0 + 1), 1, width); y1 = Math.Clamp(Math.Max(y1, y0 + 1), 1, height);
            _areas[i] = Rectangle.FromLTRB(x0, y0, x1, y1);
        }
    }

    /// <summary>The part of the screen the LEDs read, 0..1.</summary>
    public RectangleF Content { get; }

    /// <summary>Screen pixels each LED averages, in frame pixels; index = strip index.</summary>
    public IReadOnlyList<Rectangle> Areas => _areas;

    public bool Fits(LedLayout layout, ScreenFrame frame) =>
        ReferenceEquals(layout, _layout) && frame.Width == _width && frame.Height == _height;

    public void Sample(ScreenFrame frame, Span<Color> output)
    {
        var px = frame.Pixels;
        int stride = frame.Width * 4;
        for (int i = 0; i < _areas.Length; i++)
        {
            var a = _areas[i];
            long r = 0, g = 0, b = 0;
            for (int y = a.Top; y < a.Bottom; y++)
            {
                int row = y * stride;
                for (int x = a.Left; x < a.Right; x++)
                {
                    int o = row + x * 4;
                    b += px[o];
                    g += px[o + 1];
                    r += px[o + 2];
                }
            }
            int n = Math.Max(1, a.Width * a.Height);
            output[i] = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
        }
    }
}
