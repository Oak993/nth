using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-5, ED-NEW-pcctl-7). The capture chain on fake sources and a
// recording encoder: order, blank frames, the per-model grid (1568 px, 1.15 MP by default; critique C27), marks, native
// crops, the crop around a point click and the tiles of a weak tree. No window is ever captured.
[Trait("Decision", "ED-NEW-pcctl-5")]
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class IWindowCaptureTests
{
    private static PcFrame Solid(int width, int height, byte value) => PcFakeCapture.Solid(width, height, value);

    [Fact]
    public async Task The_first_source_with_a_real_frame_wins_and_is_named()
    {
        var failing = new PcFakeCapture("wgc", null, throws: true);
        var blank = new PcFakeCapture("printwindow", Solid(100, 50, 0));
        var screen = new PcFakeCapture("screen", Solid(2560, 1600, 200));
        var encoder = new PcFakeEncoder();
        var shot = await new PcCapturePipeline([failing, blank, screen], encoder).Shoot(1, new PcRect(0, 0, 2560, 1600), PcGridProfile.Standard, [], CancellationToken.None);
        Assert.NotNull(shot);
        Assert.Equal("screen", shot.Source);
        Assert.Equal((1, 1, 1), (failing.Calls, blank.Calls, screen.Calls));
        var grid = CoordinateMapper.Grid(2560, 1600, 1568, 1_150_000);
        Assert.Equal((new PcRect(0, 0, 2560, 1600), grid.Width, grid.Height, 75), encoder.Calls.Single());
        Assert.True(grid.Width <= 1568 && (long)grid.Width * grid.Height <= 1_150_000);
        Assert.Equal(grid, shot.Grid);
    }

    [Fact]
    public async Task No_usable_source_gives_no_shot()
    {
        var shot = await new PcCapturePipeline([new PcFakeCapture("wgc", null), new PcFakeCapture("printwindow", Solid(10, 10, 0))], new PcFakeEncoder())
            .Shoot(1, new PcRect(0, 0, 10, 10), PcGridProfile.Standard, [], CancellationToken.None);
        Assert.Null(shot);
    }

    [Fact]
    public async Task The_high_profile_uses_a_2048_pixel_grid_and_marks_reach_the_encoder()
    {
        var encoder = new PcFakeEncoder();
        PcMark[] marks = [new(7, new PcRect(10, 10, 40, 20))];
        await new PcCapturePipeline([new PcFakeCapture("wgc", Solid(4096, 1000, 9))], encoder).Shoot(1, new PcRect(0, 0, 4096, 1000), PcGridProfile.High, marks, CancellationToken.None);
        Assert.Equal((2048, 500), (encoder.Calls[0].Width, encoder.Calls[0].Height));
        Assert.Same(marks, encoder.Marks[0]);
    }

    [Fact]
    public void Blank_detection_sees_a_single_lit_pixel_on_the_sample_grid()
    {
        var frame = Solid(640, 480, 0);
        Assert.True(PcFrames.IsBlank(frame));
        // 640 x 480 is sampled every 10 px across and every 7 px down: (20, 14) is on the grid.
        frame.Bgra[(14 * frame.Stride) + (20 * 4) + 2] = 255;
        Assert.False(PcFrames.IsBlank(frame));
        Assert.True(PcFrames.IsBlank(PcFrame.Packed([], 0, 0)));
    }

    [Fact]
    public void Crop_copies_the_rows_of_the_rectangle_and_clips_to_the_frame()
    {
        var frame = PcFrame.Packed(Enumerable.Range(0, 4 * 3 * 4).Select(i => (byte)i).ToArray(), 4, 3);
        var crop = PcFrames.Crop(frame, new PcRect(1, 1, 2, 5));
        Assert.Equal((2, 2), (crop.Width, crop.Height));
        Assert.Equal(new byte[] { 20, 21, 22, 23, 24, 25, 26, 27, 36, 37, 38, 39, 40, 41, 42, 43 }, crop.Bgra);
        Assert.Equal(new PcRect(0, 0, 2, 3), PcFrames.Clip(frame, new PcRect(-5, -1, 7, 9)));
    }

    [Fact]
    public void Zoom_and_region_go_up_to_native_pixels_and_never_upscale()
    {
        var encoder = new PcFakeEncoder();
        var pipeline = new PcCapturePipeline([], encoder);
        // A 100 x 50 region of a 4K frame comes back 1:1, whatever the screenshot's scale was.
        var crop = pipeline.Native(Solid(3840, 2160, 1), new PcRect(10, 10, 100, 50), PcGridProfile.Standard);
        Assert.Equal((100, 50), (crop.Width, crop.Height));
        // A region larger than the grid is shrunk only to the profile.
        var wide = pipeline.Native(Solid(3840, 2160, 1), new PcRect(0, 0, 3000, 400), PcGridProfile.Standard);
        Assert.Equal(1568, wide.Width);
        // Clipped to the frame.
        Assert.Equal(new PcRect(700, 500, 100, 100), pipeline.Native(Solid(800, 600, 1), new PcRect(700, 500, 400, 400), PcGridProfile.Standard).Rect);
    }

    [Fact]
    public void A_point_click_gets_a_256_pixel_native_crop_around_it()
    {
        var pipeline = new PcCapturePipeline([], new PcFakeEncoder());
        var crop = pipeline.AroundPoint(Solid(1920, 1080, 1), new PcPoint(1000, 500), PcGridProfile.Standard);
        Assert.Equal((new PcRect(872, 372, 256, 256), 256, 256), (crop.Rect, crop.Width, crop.Height));
        Assert.Equal(new PcRect(0, 0, 138, 128), pipeline.AroundPoint(Solid(1920, 1080, 1), new PcPoint(10, 0), PcGridProfile.Standard).Rect);
    }

    [Fact]
    public void A_weak_tree_region_is_split_into_native_tiles_that_each_fit_the_grid()
    {
        var pipeline = new PcCapturePipeline([], new PcFakeEncoder());
        var tiles = pipeline.Tiles(Solid(3000, 1600, 1), new PcRect(0, 0, 3000, 1600), PcGridProfile.High);
        Assert.Equal(4, tiles.Count);
        Assert.All(tiles, t => Assert.Equal((1500, 800, 1500, 800), (t.Rect.Width, t.Rect.Height, t.Width, t.Height)));
        Assert.Equal(3000L * 1600, tiles.Sum(t => (long)t.Rect.Width * t.Rect.Height));
        // A region that fits the grid as it is stays one native piece.
        Assert.Equal((1200, 700), (pipeline.Tiles(Solid(1200, 700, 1), new PcRect(0, 0, 1200, 700), PcGridProfile.Standard).Single().Width, 700));
        // A region that would need more than four tiles comes back as one downscaled crop.
        var one = pipeline.Tiles(Solid(7680, 4320, 1), new PcRect(0, 0, 7680, 4320), PcGridProfile.Standard);
        Assert.Single(one);
        Assert.True(one[0].Width <= 1568);
    }
}
