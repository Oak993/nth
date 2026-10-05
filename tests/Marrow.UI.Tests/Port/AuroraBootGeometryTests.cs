using Avalonia;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// OD-0113; locked loading-aurora.html:576-665. Screen origin and scaling are test inputs.
public sealed class AuroraBootGeometryTests
{
    [Theory]
    [InlineData(1, 1320, 860)]
    [InlineData(1.25, 1320, 860)]
    [InlineData(1.5, 1320, 860)]
    [InlineData(2, 1320, 860)]
    [InlineData(1, 900, 700)]
    [InlineData(1.25, 900, 700)]
    [InlineData(1.5, 900, 700)]
    [InlineData(2, 900, 700)]
    [Trait("Decision", "ED-103")]
    public void The_tile_and_clip_end_at_the_exact_visible_window_with_its_corner_and_title_band(double scale, double width, double height)
    {
        var visible = new PixelRect(-1720, 75, (int)(width * scale), (int)(height * scale));
        var sequence = AuroraBootSequence.FromPixels(visible, new PixelPoint(-1920, 0), scale, false);
        var last = sequence.At(3.4, 2.4);
        var expected = new Rect(200 / scale, 75 / scale, width, height);
        Assert.Equal(expected, last.Tile);
        Assert.Equal(new Rect(0, 0, width, height), last.ContentClip);
        Assert.Equal(8, last.CornerRadius);
        Assert.Equal(new Rect(expected.X, expected.Y, width, 38), last.TitleBand);
        Assert.Equal(1, last.ContentOpacity);
        Assert.Equal(1, last.Progress);
        Assert.Equal(visible.X, -1920 + last.Tile.X * scale, 10);
        Assert.Equal(visible.Y, last.Tile.Y * scale, 10);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void One_cubic_progress_drives_rectangle_radius_clip_and_content()
    {
        var frame = new Rect(200, 75, 1320, 860);
        var sequence = new AuroraBootSequence(frame, false);
        // At curve parameter .5: x=.245, y=.77 from cubic-bezier(.32,.72,0,1).
        var sample = sequence.At(2.645, 2.4);
        const double p = .77, side = .88 * 440;
        var width = side + (1320 - side) * p;
        var height = side + (860 - side) * p;
        Assert.InRange(sample.Progress, p - 1e-5, p + 1e-5); // locked JS solver stops at x error 1e-5
        Assert.Equal(frame.Center.X - width / 2, sample.Tile.X, 4);
        Assert.Equal(frame.Center.Y - height / 2, sample.Tile.Y, 4);
        Assert.Equal(width, sample.Tile.Width, 4);
        Assert.Equal(height, sample.Tile.Height, 4);
        Assert.Equal(.225 * side + (8 - .225 * side) * p, sample.CornerRadius, 4);
        Assert.Equal(sample.Tile.Translate(-frame.Position), sample.ContentClip);
        var u = (p - .35) / .65;
        Assert.Equal(u * u * (3 - 2 * u), sample.ContentOpacity, 5);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Readiness_and_the_floor_gate_the_window_and_the_glow_closes_after_twelve_hundred_ms()
    {
        var sequence = new AuroraBootSequence(new Rect(10, 20, 900, 700), false);
        Assert.False(sequence.At(100, null).MainWindowShown);
        Assert.False(sequence.At(1.799, .2).MainWindowShown);
        Assert.True(sequence.At(1.8, .2).MainWindowShown);
        Assert.False(sequence.At(6.999, 7).MainWindowShown);
        Assert.True(sequence.At(7, 7).MainWindowShown);
        Assert.Equal(0, sequence.At(7, 7).ContentOpacity);
        Assert.Equal(1, sequence.At(8, 7).ContentOpacity);
        Assert.False(sequence.At(9.199, 7).StageClosed);
        Assert.True(sequence.At(9.2, 7).StageClosed);
        Assert.Equal(0, sequence.At(9.2, 7).GlowOpacity);
        Assert.Equal(0, sequence.At(9.2, 7).ScrimOpacity);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Launch_is_twelve_hundred_ms_from_one_hundred_twenty_and_then_idle_sways()
    {
        var sequence = new AuroraBootSequence(new Rect(10, 20, 900, 700), false);
        Assert.Equal(0, sequence.At(.12, null).Launch);
        Assert.Equal(.5, sequence.At(.72, null).Launch, 10);
        Assert.Equal(1, sequence.At(1.32, null).Launch);
        Assert.Equal(.9, sequence.At(2.22, null).Idle, 10);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Reduce_motion_holds_a_still_tile_and_only_crossfades_at_the_real_window_frame()
    {
        var frame = new Rect(10, 20, 900, 700);
        var sequence = new AuroraBootSequence(frame, true);
        Assert.False(sequence.At(50, null).MainWindowShown);
        Assert.False(sequence.At(1.299, 0).MainWindowShown);
        Assert.True(sequence.At(1.3, 0).MainWindowShown);
        Assert.Equal(.5, sequence.At(1.5, 0).ContentOpacity, 10);
        Assert.Equal(0, sequence.At(1.5, 0).TileOpacity);
        Assert.Equal(1, sequence.At(1.7, 0).ContentOpacity, 10);
        Assert.Equal(0, sequence.At(1.5, 0).Idle);
        Assert.Equal(new Rect(0, 0, 900, 700), sequence.At(1.5, 0).ContentClip);
        Assert.True(sequence.At(1.9, 0).StageClosed);
    }
}
