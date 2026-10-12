using System.Diagnostics;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a and 10, "Wallpaper chain", unit U2 (ED-NEW-inst-u2-1). The desktop
// preview's wallpaper, on the fake machine only: GetWallpaper for the window's monitor first, Windows' transcoded copy when
// that is Spotlight, missing, remote, too big, broken or too slow, then the background colour, then the theme gradient;
// local files only, at most 64 MB, decoded at twice the preview's pixel width (the whole desktop's width for Span) within
// 400 ms; every position mapped as Windows draws it; and ink labels exactly when the label areas are light (WCAG relative
// luminance above 0.6).
public sealed class WallpaperPreviewTests
{
    private const string Picture = @"C:\Users\Ana-María Østergård-Fernández\OneDrive\Pictures\Wallpapers\lake.jpg";
    private const int PreviewPixels = 288;

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_monitors_own_wallpaper_comes_first_decoded_at_twice_the_preview_width()
    {
        var source = new FakeWallpaperSource();
        source.AddImage(Picture, FakeWallpaperSource.Solid(1000, 625, 40, 90, 160));
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, Picture);
        source.SetTranscoded(FakeWallpaperSource.Solid(64, 40, 255, 0, 0));
        source.SetPosition(WallpaperPosition.Fit);

        var scene = await WallpaperPreview.ReadAsync(source, (100, 100), PreviewPixels);

        Assert.Equal(WallpaperOrigin.Wallpaper, scene.Origin);
        Assert.Equal(WallpaperPosition.Fit, scene.Position);
        Assert.Equal((Picture, PreviewPixels * 2), Assert.Single(source.Decoded));
        Assert.Equal(PreviewPixels * 2, scene.Image!.Width);
        Assert.Equal(1000, scene.Image.SourceWidth);
        Assert.Equal(new WallpaperRect(0, 0, 2560, 1600), scene.Monitor);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Spotlight_has_no_path_so_the_transcoded_copy_is_used()
    {
        var source = new FakeWallpaperSource();
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, null);
        source.SetTranscoded(FakeWallpaperSource.Solid(200, 125, 20, 30, 40));

        var scene = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);

        Assert.Equal(WallpaperOrigin.Transcoded, scene.Origin);
        Assert.Equal(FakeWallpaperSource.TranscodedPath, Assert.Single(source.Decoded).Path);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("network")]
    [InlineData("unc")]
    [InlineData("200 MB")]
    [InlineData("empty")]
    [InlineData("broken")]
    [InlineData("not an image")]
    [Trait("Decision", "ED-247")]
    public async Task A_wallpaper_that_fails_a_limit_falls_back_to_the_transcoded_copy(string trouble)
    {
        var source = new FakeWallpaperSource();
        var path = trouble == "unc" ? @"\\fileserver\share\lake.jpg" : Picture;
        var image = FakeWallpaperSource.Solid(100, 62, 10, 200, 10);
        switch (trouble)
        {
            case "network": source.AddImage(path, image, network: true); break;
            case "unc": source.AddImage(path, image); break;
            case "200 MB": source.AddImage(path, image, length: 200L * 1024 * 1024); break;
            case "empty": source.AddFile(path, 0); break;
            case "broken": source.AddImage(path, image); source.BreakDecode(path); break;
            case "not an image": source.AddFile(path, 4096); break;
        }
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, path);
        source.SetTranscoded(FakeWallpaperSource.Solid(200, 125, 20, 30, 40));

        var scene = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);

        Assert.Equal(WallpaperOrigin.Transcoded, scene.Origin);
        var decodedFirst = trouble is "broken" or "not an image";
        Assert.Equal(decodedFirst, source.Decoded.Any(d => d.Path == path));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Exactly_64_MB_is_still_read()
    {
        var source = new FakeWallpaperSource();
        source.AddImage(Picture, FakeWallpaperSource.Solid(100, 62, 10, 200, 10), length: WallpaperLimits.MaxFileBytes);
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, Picture);

        var scene = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);

        Assert.Equal(WallpaperOrigin.Wallpaper, scene.Origin);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_decode_past_the_400_ms_budget_is_cancelled_and_the_next_link_is_used()
    {
        var clock = new ManualTimeProvider();
        var source = new FakeWallpaperSource(clock);
        source.AddImage(Picture, FakeWallpaperSource.Solid(100, 62, 10, 200, 10));
        source.SlowDecode(Picture, TimeSpan.FromSeconds(30));
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, Picture);
        source.SetTranscoded(FakeWallpaperSource.Solid(200, 125, 20, 30, 40));
        var watch = Stopwatch.StartNew();

        var scene = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);

        Assert.Equal(WallpaperOrigin.Transcoded, scene.Origin);
        Assert.InRange(watch.Elapsed.TotalMilliseconds, 350, 5000);
        Assert.Contains(Picture, source.Cancelled);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task With_no_picture_at_all_the_background_colour_is_used_then_the_theme_gradient()
    {
        var source = new FakeWallpaperSource();
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, Picture);
        source.SetBackground(new WallpaperColor(0, 99, 177));

        var solid = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);
        Assert.Equal(WallpaperOrigin.Solid, solid.Origin);
        Assert.Equal(new WallpaperColor(0, 99, 177), solid.Background);
        var picture = WallpaperPreview.Compose(solid, 32, 20)!;
        Assert.All(Enumerable.Range(0, 32 * 20), i => Assert.Equal("177,99,0,255", string.Join(',', picture.Bgra.Skip(i * 4).Take(4))));

        source.SetBackground(null);
        var theme = await WallpaperPreview.ReadAsync(source, null, PreviewPixels);
        Assert.True(theme.UsesTheme);
        Assert.Null(WallpaperPreview.Compose(theme, 32, 20));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_source_that_throws_everywhere_still_gives_the_theme_gradient()
    {
        var source = new FakeWallpaperSource();
        foreach (var member in Enum.GetValues<FakeWallpaperSource.Member>()) source.Fail(member);

        var scene = await WallpaperPreview.ReadAsync(source, (10, 10), PreviewPixels);

        Assert.True(scene.UsesTheme);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_wallpaper_of_the_monitor_under_the_window_is_read()
    {
        var source = new FakeWallpaperSource();
        var left = new WallpaperMonitor(FakeWallpaperSource.PrimaryMonitor, new WallpaperRect(0, 0, 1920, 1200));
        var right = new WallpaperMonitor(FakeWallpaperSource.SecondMonitor, new WallpaperRect(1920, -200, 4480, 1400));
        source.SetMonitors(left, right);
        source.AddImage(Picture, FakeWallpaperSource.Solid(100, 62, 10, 200, 10));
        source.SetWallpaper(FakeWallpaperSource.SecondMonitor, Picture);

        var onRight = await WallpaperPreview.ReadAsync(source, (3000, 600), PreviewPixels);
        Assert.Equal(WallpaperOrigin.Wallpaper, onRight.Origin);
        Assert.Equal(right.Bounds, onRight.Monitor);
        Assert.Equal(new WallpaperRect(0, -200, 4480, 1400), onRight.VirtualDesktop);

        var onLeft = await WallpaperPreview.ReadAsync(source, (500, 600), PreviewPixels);
        Assert.Equal(WallpaperOrigin.Solid, onLeft.Origin);
        Assert.Equal(left.Bounds, onLeft.Monitor);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Span_decodes_at_the_whole_desktops_width()
    {
        var source = new FakeWallpaperSource();
        source.SetMonitors(
            new WallpaperMonitor(FakeWallpaperSource.PrimaryMonitor, new WallpaperRect(0, 0, 1600, 1000)),
            new WallpaperMonitor(FakeWallpaperSource.SecondMonitor, new WallpaperRect(1600, 0, 3200, 1000)));
        source.AddImage(Picture, FakeWallpaperSource.Solid(640, 200, 10, 200, 10));
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, Picture);
        source.SetPosition(WallpaperPosition.Span);

        await WallpaperPreview.ReadAsync(source, (10, 10), PreviewPixels);

        Assert.Equal(PreviewPixels * 2 * 2, Assert.Single(source.Decoded).Width);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_position_maps_the_monitor_as_Windows_draws_it()
    {
        var monitor = new WallpaperRect(0, 0, 1600, 1000);
        var image = new WallpaperImage(200, 100, new byte[200 * 100 * 4]);
        WallpaperScene At(WallpaperPosition position, WallpaperImage? picture = null) => new(WallpaperOrigin.Wallpaper, picture ?? image, position, default, monitor, monitor);
        string? Map(WallpaperPosition position, double x, double y, WallpaperImage? picture = null) =>
            WallpaperPreview.Map(At(position, picture), picture ?? image, x, y, 1600, 1000) is { } p ? FormattableString.Invariant($"{Math.Round(p.X, 6)},{Math.Round(p.Y, 6)}") : null;

        // Stretch: corner to corner
        Assert.Equal("0,0", Map(WallpaperPosition.Stretch, 0, 0));
        Assert.Equal("100,50", Map(WallpaperPosition.Stretch, 800, 500));
        Assert.Equal("200,100", Map(WallpaperPosition.Stretch, 1600, 1000));
        // Fill: covers (x10), cropped 200 on each side
        Assert.Equal("20,50", Map(WallpaperPosition.Fill, 0, 500));
        Assert.Equal("180,50", Map(WallpaperPosition.Fill, 1600, 500));
        // Fit: fits (x8), 100 of background above and below
        Assert.Null(Map(WallpaperPosition.Fit, 800, 50));
        Assert.Equal("100,50", Map(WallpaperPosition.Fit, 800, 500));
        Assert.Null(Map(WallpaperPosition.Fit, 800, 950));
        // Center: at its own size in the middle, background around it
        Assert.Equal("100,50", Map(WallpaperPosition.Center, 800, 500));
        Assert.Null(Map(WallpaperPosition.Center, 10, 10));
        // ... and at its source size when the decode was smaller (a 400 px picture decoded at 200)
        var halved = image with { SourceWidth = 400, SourceHeight = 200 };
        Assert.Equal("100,50", Map(WallpaperPosition.Center, 800, 500, halved));
        Assert.Equal("0,0", Map(WallpaperPosition.Center, 600, 400, halved));
        // Tile: repeats from the top left at its own size
        Assert.Equal("50,30", Map(WallpaperPosition.Tile, 250, 130));
        Assert.Equal("50,30", Map(WallpaperPosition.Tile, 1050, 530));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Span_shows_each_monitor_its_own_part_of_one_picture()
    {
        var left = new WallpaperRect(0, 0, 1600, 1000);
        var right = new WallpaperRect(1600, 0, 3200, 1000);
        var desktop = new WallpaperRect(0, 0, 3200, 1000);
        var image = new WallpaperImage(320, 100, new byte[320 * 100 * 4]);

        var onLeft = WallpaperPreview.Map(new WallpaperScene(WallpaperOrigin.Wallpaper, image, WallpaperPosition.Span, default, left, desktop), image, 0, 0, 1600, 1000);
        var onRight = WallpaperPreview.Map(new WallpaperScene(WallpaperOrigin.Wallpaper, image, WallpaperPosition.Span, default, right, desktop), image, 0, 0, 1600, 1000);

        Assert.Equal((0.0, 0.0), onLeft!.Value);
        Assert.Equal((160.0, 0.0), onRight!.Value);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Fit_letterboxes_with_the_background_colour_in_the_composed_picture()
    {
        var red = FakeWallpaperSource.Solid(200, 100, 255, 0, 0);
        var scene = new WallpaperScene(WallpaperOrigin.Wallpaper, red, WallpaperPosition.Fit, new WallpaperColor(0, 0, 255), new WallpaperRect(0, 0, 1600, 1000), new WallpaperRect(0, 0, 1600, 1000));

        var picture = WallpaperPreview.Compose(scene, 160, 100)!;

        Assert.Equal((255, 0, 0), Pixel(picture, 80, 50));
        Assert.Equal((0, 0, 255), Pixel(picture, 80, 3));
        Assert.Equal((0, 0, 255), Pixel(picture, 80, 96));
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(210, true)]
    [InlineData(200, false)]
    [InlineData(128, false)]
    [InlineData(0, false)]
    [Trait("Decision", "ED-247")]
    public void Labels_turn_ink_exactly_when_their_areas_are_light(byte grey, bool ink)
    {
        var image = FakeWallpaperSource.Solid(320, 200, grey, grey, grey);
        var scene = new WallpaperScene(WallpaperOrigin.Wallpaper, image, WallpaperPosition.Fill, default, new WallpaperRect(0, 0, 1600, 1000), new WallpaperRect(0, 0, 1600, 1000));

        var picture = WallpaperPreview.Compose(scene, 288, 180)!;

        Assert.Equal(ink, picture.InkLabels);
        Assert.Equal(WallpaperPreview.Luminance(grey, grey, grey), picture.LabelLuminance, 3);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Only_the_label_areas_decide_the_label_ink()
    {
        // light at the left (under the labels), dark everywhere else
        var pixels = new byte[400 * 250 * 4];
        for (var y = 0; y < 250; y++)
            for (var x = 0; x < 400; x++)
            {
                var light = x < 150;
                var i = (y * 400 + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = light ? (byte)250 : (byte)10;
                pixels[i + 3] = 255;
            }
        var scene = new WallpaperScene(WallpaperOrigin.Wallpaper, new WallpaperImage(400, 250, pixels), WallpaperPosition.Stretch, default, new WallpaperRect(0, 0, 1600, 1000), new WallpaperRect(0, 0, 1600, 1000));

        Assert.True(WallpaperPreview.Compose(scene, 288, 180)!.InkLabels);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Luminance_is_WCAG_relative_luminance()
    {
        Assert.Equal(1, WallpaperPreview.Luminance(255, 255, 255), 6);
        Assert.Equal(0, WallpaperPreview.Luminance(0, 0, 0), 6);
        Assert.Equal(.2158605, WallpaperPreview.Luminance(128, 128, 128), 5);
        Assert.Equal(.2126, WallpaperPreview.Luminance(255, 0, 0), 6);
        Assert.Equal(.7152, WallpaperPreview.Luminance(0, 255, 0), 6);
        Assert.Equal(.0722, WallpaperPreview.Luminance(0, 0, 255), 6);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Only_local_paths_are_read()
    {
        Assert.False(WallpaperPreview.IsRemote(Picture, new WallpaperFile(10, false)));
        Assert.False(WallpaperPreview.IsRemote(@"\\?\C:\Wallpapers\lake.jpg", new WallpaperFile(10, false)));
        Assert.True(WallpaperPreview.IsRemote(@"\\server\share\lake.jpg", new WallpaperFile(10, false)));
        Assert.True(WallpaperPreview.IsRemote(@"\\?\UNC\server\share\lake.jpg", new WallpaperFile(10, false)));
        Assert.True(WallpaperPreview.IsRemote(@"Z:\lake.jpg", new WallpaperFile(10, true)));
    }

    private static (int R, int G, int B) Pixel(WallpaperPicture picture, int x, int y)
    {
        var i = (y * picture.Width + x) * 4;
        return (picture.Bgra[i + 2], picture.Bgra[i + 1], picture.Bgra[i]);
    }
}
