using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.3: model grid, model-to-screen mapping at every common scale, negative
// monitor origins, mixed DPI desktops, SendInput's absolute values, moved versus resized windows.
[Trait("Decision", "ED-NEW-pcctl-1")]
public sealed class CoordinateMapperTests
{
    [Theory]
    [InlineData(1000, 800, 1000, 800, 1.0)]
    [InlineData(2560, 1600, 1264, 790, 0.494)]
    [InlineData(3840, 2160, 1280, 720, 0.333)]
    [InlineData(1280, 400, 1280, 400, 1.0)]
    [InlineData(400, 3000, 171, 1280, 0.427)]
    public void The_grid_caps_the_long_edge_and_the_pixel_count_and_never_upscales(int width, int height, int gridWidth, int gridHeight, double scale)
    {
        var grid = CoordinateMapper.Grid(width, height);
        Assert.Equal(gridWidth, grid.Width);
        Assert.Equal(gridHeight, grid.Height);
        Assert.Equal(scale, grid.Scale, 3);
        Assert.True(grid.Width <= 1280 && grid.Height <= 1280);
        Assert.True((double)grid.Width * grid.Height <= 1_000_000);
    }

    [Fact]
    public void A_high_detail_grid_uses_a_1600_pixel_long_edge()
    {
        var grid = CoordinateMapper.Grid(3200, 1000, longEdge: 1600, maxPixels: 2_000_000);
        Assert.Equal(1600, grid.Width);
        Assert.Equal(500, grid.Height);
    }

    [Fact]
    public void An_empty_window_has_an_empty_grid()
    {
        Assert.Equal(new PcGrid(0, 0, 1.0), CoordinateMapper.Grid(0, 500));
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(1.75)]
    [InlineData(2.00)]
    [InlineData(2.25)]
    public void A_model_point_maps_back_into_the_same_physical_pixel_at_every_display_scale(double dpiScale)
    {
        // A 1100 x 700 DIP window: physical size grows with the display scale, the grid stays within its caps.
        var frame = new PcRect(300, 200, (int)(1100 * dpiScale), (int)(700 * dpiScale));
        var grid = CoordinateMapper.Grid(frame.Width, frame.Height);
        for (var x = 0; x < grid.Width; x += 37)
        {
            for (var y = 0; y < grid.Height; y += 41)
            {
                var screen = CoordinateMapper.ToScreen(frame, grid.Scale, x, y);
                Assert.True(frame.Contains(screen), $"({x}, {y}) left the frame at {dpiScale}");
                var back = CoordinateMapper.ToModel(frame, grid.Scale, screen);
                Assert.InRange(back.X, x - 1, x + 1);
                Assert.InRange(back.Y, y - 1, y + 1);
            }
        }
    }

    [Fact]
    public void A_window_on_a_monitor_left_of_the_primary_maps_to_negative_screen_points()
    {
        var frame = new PcRect(-1920, -200, 1600, 800);
        var grid = CoordinateMapper.Grid(frame.Width, frame.Height);
        Assert.Equal(0.8, grid.Scale, 6);
        Assert.Equal(new PcPoint(-1920 + 500, -200 + 250), CoordinateMapper.ToScreen(frame, grid.Scale, 400, 200));
    }

    [Fact]
    public void Absolute_values_land_on_the_intended_pixel_over_a_mixed_dpi_desktop_with_negative_origin()
    {
        // A 150 % 4K monitor left of a 100 % 1080p primary, top-aligned differently: the virtual desktop is their union.
        var left = new PcRect(-3840, -400, 3840, 2160);
        var primary = new PcRect(0, 0, 1920, 1080);
        var desk = PcRect.Union([left, primary]);
        Assert.Equal(new PcRect(-3840, -400, 5760, 2160), desk);
        foreach (var point in new[] { new PcPoint(-3840, -400), new PcPoint(-1, 0), new PcPoint(0, 0), new PcPoint(1919, 1079), new PcPoint(-2000, 1759), new PcPoint(777, 333) })
        {
            var (dx, dy) = CoordinateMapper.ToAbsolute(point, desk);
            Assert.InRange(dx, 0, 65535);
            Assert.InRange(dy, 0, 65535);
            Assert.Equal(point, CoordinateMapper.FromAbsolute(dx, dy, desk));
        }
    }

    [Fact]
    public void Every_pixel_of_a_small_desktop_round_trips_through_absolute_values()
    {
        var desk = new PcRect(-7, 3, 97, 61);
        for (var x = desk.X; x < desk.Right; x++)
        {
            for (var y = desk.Y; y < desk.Bottom; y++)
            {
                var (dx, dy) = CoordinateMapper.ToAbsolute(new PcPoint(x, y), desk);
                Assert.Equal(new PcPoint(x, y), CoordinateMapper.FromAbsolute(dx, dy, desk));
            }
        }
    }

    [Fact]
    public void A_window_that_moved_is_told_apart_from_one_that_resized()
    {
        var seen = new PcRect(100, 100, 800, 600);
        Assert.Equal(PcFrameChange.Same, CoordinateMapper.Compare(seen, seen));
        Assert.Equal(PcFrameChange.Moved, CoordinateMapper.Compare(seen, seen.Offset(40, -10)));
        Assert.Equal(PcFrameChange.Resized, CoordinateMapper.Compare(seen, seen with { Width = 801 }));
        Assert.Equal(PcFrameChange.Resized, CoordinateMapper.Compare(seen, new PcRect(0, 0, 800, 601)));
    }

    [Fact]
    public void Points_off_the_grid_are_reported()
    {
        var grid = new PcGrid(100, 50, 0.5);
        Assert.True(CoordinateMapper.OnGrid(grid, 0, 0));
        Assert.True(CoordinateMapper.OnGrid(grid, 99.4, 49.9));
        Assert.False(CoordinateMapper.OnGrid(grid, 100, 10));
        Assert.False(CoordinateMapper.OnGrid(grid, -1, 10));
        Assert.False(CoordinateMapper.OnGrid(grid, 10, 50));
    }

    [Fact]
    public void A_zoom_region_is_mapped_to_physical_pixels_and_clipped_to_the_frame()
    {
        var frame = new PcRect(1000, 500, 2000, 1000);
        Assert.Equal(new PcRect(1200, 600, 400, 200), CoordinateMapper.RegionToScreen(frame, 0.5, 100, 50, 200, 100));
        Assert.Equal(new PcRect(2800, 1400, 200, 100), CoordinateMapper.RegionToScreen(frame, 0.5, 900, 450, 500, 500));
    }
}
