using Marrow.Core.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :247-260 blurred(_:softness:radiusScale:). CIGaussianBlur and
// the shared CIContext are macOS-only (platform boundary, PR-THEME-066 windows_path): what is observable on Windows is the
// radius the softness asks for, the unchanged return below the 0.01 threshold, and the clamped edge handling.
public sealed class WallpaperBlurTests
{
    private static byte Channel(DecodedBitmap bitmap, int x, int y, int channel = 0) => bitmap.Pixels[(y * bitmap.Width + x) * 4 + channel];

    // A Gaussian of standard deviation `sigma` over a step at pixel `edge` leaves, at pixel x, 255 * CDF((x - (edge - 0.5)) / sigma).
    private static double Expected(int x, int edge, double sigma) => 255 * WallpaperPictures.NormalCdf((x - (edge - 0.5)) / sigma);

    [Fact]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:250 `let radius = softness * 28 * radiusScale`, a Gaussian of that radius
    public void The_blur_radius_is_softness_times_28_times_the_radius_scale()
    {
        var wide = WallpaperStore.Blurred(WallpaperPictures.Step(200, 3, 100), 0.25)!; // radius 7
        var halved = WallpaperStore.Blurred(WallpaperPictures.Step(200, 3, 100), 0.5, 0.5)!; // 0.5 * 28 * 0.5 = 7 as well
        foreach (var x in new[] { 80, 90, 95, 99, 100, 105, 110, 120 })
        {
            Assert.InRange(Channel(wide, x, 1), Expected(x, 100, 7) - 3, Expected(x, 100, 7) + 3);
            Assert.InRange(Channel(halved, x, 1), Expected(x, 100, 7) - 3, Expected(x, 100, 7) + 3);
        }
        Assert.Equal(200, wide.Width);
        Assert.Equal(3, wide.Height);

        // A different softness gives a different spread: radius 14 for softness 0.5.
        var softer = WallpaperStore.Blurred(WallpaperPictures.Step(200, 3, 100), 0.5)!;
        foreach (var x in new[] { 80, 90, 95, 105, 110, 120 })
            Assert.InRange(Channel(softer, x, 1), Expected(x, 100, 14) - 3, Expected(x, 100, 14) + 3);
    }

    [Fact]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:252-259 the blur acts along both axes
    public void The_blur_acts_vertically_as_well()
    {
        var blurred = WallpaperStore.Blurred(WallpaperPictures.Step(3, 200, 100, alongY: true), 0.25)!;
        foreach (var y in new[] { 80, 95, 99, 100, 105, 120 })
            Assert.InRange(Channel(blurred, 1, y), Expected(y, 100, 7) - 3, Expected(y, 100, 7) + 3);
    }

    [Fact]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:253-259 clamped to the extent, cropped back to it: the frame stays solid
    public void The_edges_stay_opaque_and_unfaded_and_the_frame_keeps_its_size()
    {
        var flat = WallpaperPictures.Flat(120, 40, 200);

        var blurred = WallpaperStore.Blurred(flat, 1)!; // radius 28, wider than half the frame

        Assert.Equal(120, blurred.Width);
        Assert.Equal(40, blurred.Height);
        Assert.Equal(120 * 4, blurred.BytesPerRow);
        for (var y = 0; y < blurred.Height; y++)
            for (var x = 0; x < blurred.Width; x++)
            {
                Assert.Equal(255, Channel(blurred, x, y, 3)); // opaque everywhere, corners included
                for (var channel = 0; channel < 3; channel++) Assert.InRange(Channel(blurred, x, y, channel), 199, 201); // no fade toward the edge
            }
    }

    [Fact]
    [Trait("Req", "PR-THEME-066")] // UI/Theme/Wallpaper.swift:251 `guard radius > 0.01 else { return base }`
    public void A_radius_of_0_01_or_less_returns_the_base_unchanged()
    {
        var step = WallpaperPictures.Step(40, 3, 20);
        Assert.Same(step, WallpaperStore.Blurred(step, 0)); // softness 0 costs nothing
        Assert.Same(step, WallpaperStore.Blurred(step, 0.0003)); // 0.0084
        Assert.Same(step, WallpaperStore.Blurred(step, 0.5, 0.0005)); // 0.007, through the radius scale

        var justOver = WallpaperStore.Blurred(step, 0.0004)!; // 0.0112: a real blur, a new picture
        Assert.NotSame(step, justOver);
        Assert.Equal(40, justOver.Width);
        Assert.Equal(3, justOver.Height);
    }
}
