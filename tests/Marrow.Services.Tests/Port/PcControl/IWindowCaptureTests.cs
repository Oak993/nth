using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-5). The capture chain on fake sources and a recording encoder:
// order, blank frames, the model grid, zoom. No window is ever captured.
[Trait("Decision", "ED-NEW-pcctl-5")]
public sealed class IWindowCaptureTests
{
    private static PcFrame Solid(int width, int height, byte value)
    {
        var bytes = new byte[width * height * 4];
        Array.Fill(bytes, value);
        return PcFrame.Packed(bytes, width, height);
    }

    [Fact]
    public async Task The_first_source_with_a_real_frame_wins_and_is_named()
    {
        var failing = new PcFakeCapture("wgc", null, throws: true);
        var blank = new PcFakeCapture("printwindow", Solid(100, 50, 0));
        var screen = new PcFakeCapture("screen", Solid(2560, 1600, 200));
        var encoder = new PcFakeEncoder();
        var shot = await new PcCapturePipeline([failing, blank, screen], encoder).Shoot(1, new PcRect(0, 0, 2560, 1600), 1280, CancellationToken.None);
        Assert.NotNull(shot);
        Assert.Equal("screen", shot.Source);
        Assert.Equal((1, 1, 1), (failing.Calls, blank.Calls, screen.Calls));
        Assert.Equal((new PcRect(0, 0, 2560, 1600), 1264, 790, 75), encoder.Calls.Single());
        Assert.Equal(new PcGrid(1264, 790, shot.Grid.Scale), shot.Grid);
    }

    [Fact]
    public async Task No_usable_source_gives_no_shot()
    {
        var shot = await new PcCapturePipeline([new PcFakeCapture("wgc", null), new PcFakeCapture("printwindow", Solid(10, 10, 0))], new PcFakeEncoder()).Shoot(1, new PcRect(0, 0, 10, 10), 1280, CancellationToken.None);
        Assert.Null(shot);
    }

    [Fact]
    public async Task High_detail_uses_a_1600_pixel_grid()
    {
        var encoder = new PcFakeEncoder();
        await new PcCapturePipeline([new PcFakeCapture("wgc", Solid(3200, 1000, 9))], encoder).Shoot(1, new PcRect(0, 0, 3200, 1000), 1600, CancellationToken.None);
        Assert.Equal((1600, 500), (encoder.Calls[0].Width, encoder.Calls[0].Height));
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
    }

    [Fact]
    public void Zoom_doubles_a_small_region_up_to_the_frame_resolution()
    {
        var encoder = new PcFakeEncoder();
        var pipeline = new PcCapturePipeline([], encoder);
        // Model scale 0.5: a 100 x 50 frame region is 50 x 25 on the grid; zoom shows it at 100 x 50 (2x, = native).
        pipeline.Zoom(Solid(2000, 1000, 1), new PcRect(10, 10, 100, 50), 0.5, out var w, out var h);
        Assert.Equal((100, 50), (w, h));
        // Model scale 1.0 (a small window): the grid is already native, so the zoom stays at native size.
        pipeline.Zoom(Solid(800, 600, 1), new PcRect(0, 0, 100, 40), 1.0, out w, out h);
        Assert.Equal((100, 40), (w, h));
    }
}
