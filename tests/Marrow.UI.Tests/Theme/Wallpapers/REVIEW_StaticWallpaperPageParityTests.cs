// REVIEW (WL-4 independent review): the static engine against the approved page's own behaviour, where WL-4's own tests did not look.
using System.Text.Json;
using Avalonia;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
[Trait("Review", "WL-4")]
public sealed class REVIEW_StaticWallpaperPageParityTests
{
    private static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Marrow.sln"))) return d.FullName;
            throw new InvalidOperationException("Repository fixture root is missing.");
        }
    }

    // sw-gl.body.js target(): "a new one while a fade is still running ... starts from there and eases out in 0.3 s" (ease-out cubic).
    // WL-4 StaticWallpaperController.Advance divides by .55 and smoothsteps for every fade, interrupted or not.
    [Fact]
    public void REVIEW_a_palette_retarget_during_a_running_fade_follows_the_pages_point_three_second_ease_out()
    {
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tests/Marrow.UI.Tests/Fixtures/REVIEW_static-retarget.json"))).RootElement;
        var id = golden.GetProperty("id").GetString()!;
        var packs = golden.GetProperty("packs").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var options = new Dictionary<string, double> { ["grain"] = 0, ["option"] = 0, ["focus"] = 0, ["dynamic"] = 0, ["persp"] = 0 };
        WallpaperSelection Pick(int i) => new(WallpaperKind.Static, id, packs[i], Options: options);
        using var renderer = new WallpaperRenderer(Pick(0), true, new(new ManualTimeProvider(), () => "#005FB8", () => Vector.Zero));
        Frame(renderer);
        renderer.Set(Pick(1), true);
        var steps = golden.GetProperty("steps").EnumerateArray().ToArray();
        renderer.Advance(steps[0].GetProperty("dt").GetDouble()); Frame(renderer);
        Close(steps[0], renderer, "the first fade (0.55 s smoothstep) is the page's");
        renderer.Set(Pick(2), true);
        for (var i = 1; i < steps.Length; i++)
        {
            renderer.Advance(steps[i].GetProperty("dt").GetDouble()); Frame(renderer);
            Close(steps[i], renderer, $"step {i} after the retarget (the page eases out in 0.3 s)");
        }
    }

    // sw-page.js:26 reads now.getHours() + now.getMinutes() / 60: the page's clock has minute resolution.
    [Fact]
    public void REVIEW_the_dynamic_clock_has_the_pages_minute_resolution_not_seconds()
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, "riptide");
        var options = new Dictionary<string, double> { ["dynamic"] = 1, ["grain"] = 0, ["focus"] = 0, ["persp"] = 0 };
        var pick = new WallpaperSelection(WallpaperKind.Static, "riptide", wall.Packs[0].Id, Options: options);
        var at = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 15, 0, 59, TimeSpan.Zero));
        using var renderer = new WallpaperRenderer(pick, true, new(at, () => "#005FB8", () => Vector.Zero));
        Frame(renderer);
        var expected = StaticWallpaperPalette.Dynamic(wall.Static!, WallpaperPalette.For(wall.Packs[0], 1), 1, 15.0, 1).Palette;
        var actual = renderer.StaticState!.Palette;
        Assert.All(expected.Zip(actual), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 1e-5));
    }

    // OPTIONS-FORMAT: "never muddy: no brown", and Match accent "the dark-mode brown rules still apply". The approved shiftPal does not
    // reapply them, so 76 of 5400 Dynamic dark grounds are brown. WL-4's own test pins {17, 59} as the expected count.
    [Fact]
    public void REVIEW_no_dynamic_dark_ground_is_brown_at_any_key_hour()
    {
        var brown = new List<string>();
        foreach (var wall in WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Static))
            foreach (var pack in wall.Packs)
                foreach (var hour in new double[] { 0, 3, 6, 9, 12, 15, 18, 21, 23.5 })
                {
                    var palette = StaticWallpaperPalette.Dynamic(wall.Static!, WallpaperPalette.For(pack, 1), 1, hour, 1).Palette;
                    var (l, a, b) = (palette[15], palette[16], palette[17]);
                    var h = (Math.Atan2(b, a) * 180 / Math.PI + 360) % 360; var c = Math.Sqrt(a * a + b * b);
                    if (l < .46 && c > .01 && h > 20 && h < 120) brown.Add($"{wall.Id}/{pack.Id}@{hour}: L {l:F3} C {c:F3} h {h:F1}");
                }
        Assert.True(brown.Count == 0, $"{brown.Count} brown Dynamic dark grounds, e.g. {string.Join("; ", brown.Take(5))}");
    }

    // The two WL-4 test classes that carry the behaviour name no build unit: a reader cannot locate them with the Feature trait.
    [Fact]
    public void REVIEW_every_static_wallpaper_test_class_names_its_build_unit()
    {
        var missing = typeof(REVIEW_StaticWallpaperPageParityTests).Assembly.GetTypes()
            .Where(t => t.Namespace == "Marrow.UI.Tests.Theme.Wallpapers" && t.Name.StartsWith("StaticWallpaper", StringComparison.Ordinal)
                && t.Name.EndsWith("Tests", StringComparison.Ordinal))
            .Where(t => !t.GetCustomAttributesData().Any(a => a.AttributeType == typeof(TraitAttribute)
                && (string)a.ConstructorArguments[0].Value! == "Feature" && (string)a.ConstructorArguments[1].Value! == "WL-4"))
            .Select(t => t.Name).ToArray();
        Assert.True(missing.Length == 0, "no [Trait(\"Feature\", \"WL-4\")]: " + string.Join(", ", missing));
    }

    private static void Close(JsonElement step, WallpaperRenderer renderer, string what)
    {
        var expected = step.GetProperty("pal").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var actual = renderer.StaticState!.Palette;
        for (var i = 0; i < 18; i++) Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-5, $"{what}: palette[{i}] page {expected[i]:F6} port {actual[i]:F6}");
    }

    private static byte[] Frame(WallpaperRenderer renderer)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        renderer.Draw(surface.Canvas, 32, 20); using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }
}
