using System.Drawing;
using ReLight.Core;

namespace ReLight.Tests;

public class LedLayoutTests
{
    static StripLayout Small(StartCorner start, bool clockwise, int offset = 0) => new()
    {
        Top = 4, Right = 2, Bottom = 4, Left = 2, Start = start, Clockwise = clockwise, Offset = offset,
    };

    [Fact]
    public void BottomLeftClockwise_GoesUpTheLeftSideFirst()
    {
        var l = new LedLayout(Small(StartCorner.BottomLeft, true));
        Assert.Equal((Side.Left, 0, 2), l.SideOf(0));   // bottom-most on the left
        Assert.Equal((Side.Left, 1, 2), l.SideOf(1));
        Assert.Equal((Side.Top, 0, 4), l.SideOf(2));    // then left end of the top
        Assert.Equal((Side.Bottom, 3, 4), l.SideOf(11)); // ends at the left end of the bottom
    }

    [Fact]
    public void TopLeftCounterClockwise_GoesDownTheLeftSideFirst()
    {
        var l = new LedLayout(Small(StartCorner.TopLeft, false));
        Assert.Equal((Side.Left, 1, 2), l.SideOf(0));   // top-most on the left
        Assert.Equal((Side.Left, 0, 2), l.SideOf(1));
        Assert.Equal((Side.Bottom, 3, 4), l.SideOf(2)); // left end of the bottom
        Assert.Equal((Side.Top, 0, 4), l.SideOf(11));
    }

    [Fact]
    public void BottomRightCounterClockwise_GoesUpTheRightSide()
    {
        var l = new LedLayout(Small(StartCorner.BottomRight, false));
        Assert.Equal((Side.Right, 1, 2), l.SideOf(0));  // bottom-most on the right
        Assert.Equal((Side.Right, 0, 2), l.SideOf(1));
        Assert.Equal((Side.Top, 3, 4), l.SideOf(2));    // right end of the top
    }

    [Fact]
    public void Offset_ShiftsTheStart()
    {
        var l = new LedLayout(Small(StartCorner.BottomLeft, true, offset: 2));
        Assert.Equal((Side.Top, 0, 4), l.SideOf(0));
        var back = new LedLayout(Small(StartCorner.BottomLeft, true, offset: -2));
        Assert.Equal((Side.Bottom, 2, 4), back.SideOf(0)); // middle of the bottom edge
    }

    [Theory]
    [InlineData(StartCorner.TopLeft, true)]
    [InlineData(StartCorner.TopRight, false)]
    [InlineData(StartCorner.BottomRight, true)]
    [InlineData(StartCorner.BottomLeft, false)]
    public void EveryPlaceGetsExactlyOneLed(StartCorner start, bool cw)
    {
        var l = new LedLayout(new StripLayout { Top = 37, Right = 21, Bottom = 36, Left = 21, Start = start, Clockwise = cw, Offset = 5 });
        var seen = new HashSet<(Side, int)>();
        for (int i = 0; i < l.Count; i++)
        {
            var (side, k, _) = l.SideOf(i);
            Assert.True(seen.Add((side, k)));
        }
        Assert.Equal(115, seen.Count);
    }

    [Fact]
    public void Regions_FollowScreenEdges()
    {
        var l = new LedLayout(new StripLayout { Top = 4, Right = 2, Bottom = 4, Left = 2, Start = StartCorner.TopLeft, Clockwise = true });
        Assert.Equal(RectangleF.FromLTRB(0, 0, 0.25f, 0.1f), l.RegionOf(0, 0.1f, 0.1f));      // top-left
        Assert.Equal(RectangleF.FromLTRB(0.9f, 0, 1, 0.5f), l.RegionOf(4, 0.1f, 0.1f));      // top of the right edge
        Assert.Equal(RectangleF.FromLTRB(0.75f, 0.9f, 1, 1), l.RegionOf(6, 0.1f, 0.1f));     // right end of the bottom
        Assert.Equal(RectangleF.FromLTRB(0, 0.5f, 0.1f, 1), l.RegionOf(10, 0.1f, 0.1f));     // bottom of the left edge
    }

    [Fact]
    public void EmptyLayout_DoesNotCrash()
    {
        var l = new LedLayout(new StripLayout { Top = 0, Right = 0, Bottom = 0, Left = 0 });
        Assert.Equal(0, l.Count);
    }
}

public class OutputTests
{
    [Theory]
    [InlineData("RGB", 255, 0, 0)]
    [InlineData("GRB", 0, 255, 0)]
    [InlineData("BGR", 0, 0, 255)]
    public void ColorOrder_PutsRedWhereTheStripExpectsIt(string order, byte a, byte b, byte c)
    {
        var bytes = ColorOutput.ToBytes([Color.FromArgb(255, 0, 0)], order, 100);
        Assert.Equal(new[] { a, b, c }, bytes);
    }

    [Fact]
    public void Brightness_ScalesChannels()
    {
        var bytes = ColorOutput.ToBytes([Color.FromArgb(200, 100, 0)], "RGB", 50);
        Assert.Equal(new byte[] { 100, 50, 0 }, bytes);
    }

    [Fact]
    public void Packet_HasAdalightHeader()
    {
        var p = AdalightDevice.BuildPacket(new byte[150 * 3]);
        Assert.Equal(6 + 450, p.Length);
        Assert.Equal((byte)'A', p[0]);
        Assert.Equal((byte)'d', p[1]);
        Assert.Equal((byte)'a', p[2]);
        Assert.Equal(0, p[3]);          // 149 = 0x0095
        Assert.Equal(0x95, p[4]);
        Assert.Equal(0x00 ^ 0x95 ^ 0x55, p[5]);
    }
}
