using System.Diagnostics;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// ED-NEW-perf-memory-2: Blurred is the same Gaussian as before, in whole rows with SIMD and in parallel. It must give the old result to within one level
// of one channel (float rounding), for every shape of picture, and must no longer take seconds of one core on a 3200 px picture.
public sealed class WallpaperBlurFastTests
{
    private static DecodedBitmap Noise(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 4];
        new Random(seed).NextBytes(pixels);
        return new DecodedBitmap(width, height, width * 4, pixels);
    }

    private static int LargestDifference(DecodedBitmap a, DecodedBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.BytesPerRow, b.BytesPerRow);
        var largest = 0;
        for (var at = 0; at < a.Pixels.Length; at++) largest = Math.Max(largest, Math.Abs(a.Pixels[at] - b.Pixels[at]));
        return largest;
    }

    [Theory]
    [InlineData(64, 48, 0.4)]     // radius 11.2, wider than the picture's half
    [InlineData(200, 3, 0.25)]    // a strip: the vertical taps all clamp
    [InlineData(3, 200, 0.25)]    // a column: the horizontal taps all clamp
    [InlineData(1, 1, 0.5)]       // one pixel
    [InlineData(17, 13, 0.2)]     // not a multiple of the vector width, extent 17 beyond both sides
    [InlineData(301, 197, 0.9)]   // odd sizes, radius 25
    [InlineData(480, 270, 1.0)]   // the largest softness, extent 84
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:247-260 the same Gaussian, edge pixels repeated
    public void The_fast_blur_matches_the_first_ports_result_within_one_level(int width, int height, double softness)
    {
        var picture = Noise(width, height, width * 31 + height);

        var fast = WallpaperStore.Blurred(picture, softness)!;
        var reference = WallpaperBlurReference.Blurred(picture, softness * 28);

        Assert.True(LargestDifference(fast, reference) <= 1);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 0.8)]
    [InlineData(0.3, 0.25)]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:250 the radius scale of the live preview
    public void The_radius_scale_of_the_live_preview_gives_the_same_result_as_the_first_port(double softness, double scale)
    {
        var picture = Noise(220, 130, 9);

        var fast = WallpaperStore.Blurred(picture, softness, scale)!;
        var reference = WallpaperBlurReference.Blurred(picture, softness * 28 * scale);

        Assert.True(LargestDifference(fast, reference) <= 1);
    }

    [Fact]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:251 the base is returned as it is, never changed in place
    public void The_blur_leaves_its_input_untouched_and_gives_the_same_answer_twice()
    {
        var picture = Noise(90, 60, 3);
        var before = (byte[])picture.Pixels.Clone();

        var first = WallpaperStore.Blurred(picture, 0.6)!;
        var second = WallpaperStore.Blurred(picture, 0.6)!;

        Assert.True(picture.Pixels.AsSpan().SequenceEqual(before));
        Assert.True(first.Pixels.AsSpan().SequenceEqual(second.Pixels)); // rows are independent: no race between the parallel workers
    }

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:164-176 the full render: a 3200 px picture at the largest softness
    public void A_full_size_render_at_the_largest_softness_takes_well_under_the_seconds_the_first_port_needed()
    {
        var picture = Noise(3200, 1800, 5);
        _ = WallpaperStore.Blurred(Noise(64, 48, 1), 1.0); // warm-up: JIT and the worker threads
        var clock = Stopwatch.StartNew();

        var blurred = WallpaperStore.Blurred(picture, 1.0)!;

        clock.Stop();
        Assert.Equal(3200, blurred.Width);
        // The first port took about eight billion multiply-adds, many seconds on one core. This is a coarse bound with a wide margin for a busy PC.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"the blur took {clock.Elapsed.TotalSeconds:0.0} s");
    }
}
