// REVIEW (WL-4 independent review): StaticWallpaperTests.Every_approved_static_shader_compiles_and_rasters asserts Contains(frame, b != 0)
// over RGBA bytes whose alpha is always 255, so it cannot fail for a black or flat picture. These look at the colour channels.
using Avalonia;
using Marrow.Testing;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
[Trait("Review", "WL-4")]
public sealed class REVIEW_StaticWallpaperRenderTests
{
    public static IEnumerable<object[]> Walls() => WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Static).Select(w => new object[] { w.Id });

    [Theory, MemberData(nameof(Walls))]
    public void REVIEW_every_static_wallpaper_renders_a_picture_that_is_neither_black_nor_flat_in_both_themes_and_with_every_option_on(string id)
    {
        var problems = new List<string>();
        foreach (var dark in new[] { true, false })
            foreach (var premium in new[] { false, true })
            {
                var options = premium ? new Dictionary<string, double> { ["dynamic"] = 1, ["focus"] = 1, ["grain"] = 1, ["persp"] = 1 } : null;
                using var renderer = new WallpaperRenderer(new(WallpaperKind.Static, id, premium ? "accent" : null, Options: options), dark,
                    new(new ManualTimeProvider(), () => "#D4501A", () => new Vector(1, 1)));
                using var surface = SKSurface.Create(new SKImageInfo(64, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
                renderer.Draw(surface.Canvas, 64, 40); using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
                int max = 0, min = 255;
                for (var y = 0; y < 40; y++) for (var x = 0; x < 64; x++)
                {
                    var c = bitmap.GetPixel(x, y); var v = Math.Max(c.Red, Math.Max(c.Green, c.Blue)); max = Math.Max(max, v); min = Math.Min(min, v);
                }
                if (max < 12) problems.Add($"{(dark ? "dark" : "light")}{(premium ? "+premium" : "")}: black (max channel {max})");
                else if (max - min < 2 && !premium) problems.Add($"{(dark ? "dark" : "light")}: flat (channel range {max - min})");
            }
        Assert.True(problems.Count == 0, id + ": " + string.Join("; ", problems));
    }
}
