using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, unit U0 (ED-NEW-inst-u0-1). The wallpaper contract the desktop
// preview's chain (unit U2) is built on: Windows' own position numbers and colour layout, the read limits, and the fake
// that stands in for IDesktopWallpaper: a Spotlight monitor has no path, a slow decode waits on the test's clock and is
// cancelled at the budget, a 200 MB or network file is reported as such, a broken source throws, and decodes come back at
// the width asked for. Nothing here reads the real wallpaper.
public sealed class WallpaperSourceTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Positions_and_colours_use_Windows_own_numbers()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, new[] { WallpaperPosition.Center, WallpaperPosition.Tile, WallpaperPosition.Stretch, WallpaperPosition.Fit, WallpaperPosition.Fill, WallpaperPosition.Span }.Select(p => (int)p));
        Assert.Equal(new WallpaperColor(0x11, 0x22, 0x33), WallpaperColor.FromColorRef(0x00332211)); // COLORREF is 0x00BBGGRR
        Assert.Equal((1920, 1080), (new WallpaperRect(-1920, 0, 0, 1080).Width, new WallpaperRect(-1920, 0, 0, 1080).Height));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_read_limits_are_the_planned_ones()
    {
        Assert.Equal(64L * 1024 * 1024, WallpaperLimits.MaxFileBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(400), WallpaperLimits.DecodeBudget);
        Assert.Equal(2, WallpaperLimits.DecodeWidthFactor);
        Assert.Equal(0.6, WallpaperLimits.InkLabelLuminance);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_fake_has_one_monitor_with_no_wallpaper_until_one_is_set_and_spotlight_gives_no_path()
    {
        var source = new FakeWallpaperSource();
        var monitor = Assert.Single(source.Monitors());

        Assert.Equal(FakeWallpaperSource.PrimaryMonitor, monitor.DevicePath);
        Assert.Null(source.WallpaperPath(monitor.DevicePath));
        Assert.Null(source.TranscodedWallpaperPath());
        Assert.Equal(WallpaperPosition.Fill, source.Position());
        Assert.Equal(new WallpaperColor(0, 0, 0), source.BackgroundColor());

        source.SetMonitors(new WallpaperMonitor(FakeWallpaperSource.PrimaryMonitor, new WallpaperRect(0, 0, 2560, 1600)), new WallpaperMonitor(FakeWallpaperSource.SecondMonitor, new WallpaperRect(2560, 0, 4480, 1080)));
        source.SetWallpaper(FakeWallpaperSource.PrimaryMonitor, @"C:\Wallpapers\lake.jpg");
        source.SetWallpaper(FakeWallpaperSource.SecondMonitor, null); // Windows Spotlight
        source.SetTranscoded(FakeWallpaperSource.Solid(8, 5, 200, 210, 220));

        Assert.Equal(2, source.Monitors().Count);
        Assert.Equal(@"C:\Wallpapers\lake.jpg", source.WallpaperPath(FakeWallpaperSource.PrimaryMonitor));
        Assert.Null(source.WallpaperPath(FakeWallpaperSource.SecondMonitor));
        Assert.Equal(FakeWallpaperSource.TranscodedPath, source.TranscodedWallpaperPath());
        Assert.Contains("Ana-María Østergård-Fernández", FakeWallpaperSource.TranscodedPath, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Files_report_their_size_and_whether_they_are_on_the_network_without_decoding()
    {
        var source = new FakeWallpaperSource();
        source.AddFile(@"C:\Wallpapers\huge.png", 200L * 1024 * 1024);
        source.AddImage(@"\\server\share\wall.jpg", FakeWallpaperSource.Solid(4, 4, 0, 0, 0), network: true);

        Assert.Equal(new WallpaperFile(200L * 1024 * 1024, IsNetwork: false), source.Inspect(@"C:\Wallpapers\huge.png"));
        Assert.Equal(new WallpaperFile(64, IsNetwork: true), source.Inspect(@"\\server\share\wall.jpg"));
        Assert.Null(source.Inspect(@"C:\Wallpapers\gone.jpg"));
        Assert.Equal(3, source.Inspected.Count);
        Assert.Empty(source.Decoded);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_decode_comes_back_at_the_width_asked_for_and_a_non_image_as_null()
    {
        var source = new FakeWallpaperSource();
        source.AddImage(@"C:\Wallpapers\lake.jpg", FakeWallpaperSource.Solid(32, 20, 250, 250, 250));
        source.AddFile(@"C:\Wallpapers\notes.txt", 12);

        var image = await source.DecodeAsync(@"C:\Wallpapers\lake.jpg", 16, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal((16, 10), (image.Width, image.Height));
        Assert.Equal(16 * 10 * 4, image.Bgra.Length);
        Assert.Equal(new byte[] { 250, 250, 250, 255 }, image.Bgra[..4]);
        Assert.Null(await source.DecodeAsync(@"C:\Wallpapers\notes.txt", 16, CancellationToken.None));
        Assert.Equal(new[] { (@"C:\Wallpapers\lake.jpg", 16), (@"C:\Wallpapers\notes.txt", 16) }, source.Decoded);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_slow_decode_waits_on_the_test_clock_and_is_cancelled_at_the_budget()
    {
        var clock = new ManualTimeProvider();
        var source = new FakeWallpaperSource(clock);
        source.AddImage(@"C:\Wallpapers\slow.jpg", FakeWallpaperSource.Solid(4, 4, 9, 9, 9));
        source.SlowDecode(@"C:\Wallpapers\slow.jpg", TimeSpan.FromSeconds(2));
        using var budget = new CancellationTokenSource(WallpaperLimits.DecodeBudget, clock);

        var decode = source.DecodeAsync(@"C:\Wallpapers\slow.jpg", 4, budget.Token);
        Assert.False(decode.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.False(decode.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decode);
        Assert.Equal(new[] { @"C:\Wallpapers\slow.jpg" }, source.Cancelled);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_broken_source_or_a_corrupt_image_throws_so_the_chain_moves_on()
    {
        var source = new FakeWallpaperSource();
        source.AddImage(@"C:\Wallpapers\corrupt.jpg", FakeWallpaperSource.Solid(4, 4, 1, 2, 3));
        source.BreakDecode(@"C:\Wallpapers\corrupt.jpg");
        source.Fail(FakeWallpaperSource.Member.Monitors);

        Assert.Throws<InvalidOperationException>(() => source.Monitors());
        await Assert.ThrowsAsync<InvalidDataException>(() => source.DecodeAsync(@"C:\Wallpapers\corrupt.jpg", 4, CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() => source.DecodeAsync(@"C:\Wallpapers\gone.jpg", 4, CancellationToken.None));
        Assert.Equal(1, source.CallCount(FakeWallpaperSource.Member.Monitors));
        Assert.Equal(2, source.CallCount(FakeWallpaperSource.Member.Decode));
    }
}
