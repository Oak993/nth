using System.Security.Cryptography;
using SkiaSharp;

namespace Marrow.App.Tests.Wallpapers;

public sealed class WallpaperReferenceIntegrityTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Approved_inventory_and_every_required_compact_look_have_an_offline_reference()
    {
        var cases = WallpaperReferences.Cases;
        Assert.Equal(1, WallpaperReferences.Manifest.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(160, WallpaperReferences.Manifest.GetProperty("width").GetInt32());
        Assert.Equal(100, WallpaperReferences.Manifest.GetProperty("height").GetInt32());
        Assert.Equal("ED-NEW-wl-reconcile", WallpaperReferences.Manifest.GetProperty("decision").GetString());
        Assert.Equal(cases.Count, WallpaperReferences.Manifest.GetProperty("counts").GetProperty("renders").GetInt32());
        foreach (var (collection, count) in new[] { ("static", 90), ("animated", 72) })
        {
            var walls = cases.Where(c => string.Equals(c.Collection, collection, StringComparison.Ordinal)).GroupBy(c => c.Wallpaper).ToArray();
            Assert.Equal(count, walls.Length);
            foreach (var wall in walls)
            {
                Assert.Single(wall.Select(c => c.Inputs.GetProperty("name").GetString()).Distinct(StringComparer.Ordinal));
                foreach (var dark in new[] { true, false })
                {
                    var defaults = wall.Where(c => string.Equals(c.Variant, "default", StringComparison.Ordinal) && c.Inputs.GetProperty("dark").GetBoolean() == dark).ToArray();
                    Assert.Contains(defaults, c => c.Inputs.GetProperty("time").GetDouble() == 0);
                    if (string.Equals(collection, "animated", StringComparison.Ordinal))
                        Assert.Contains(defaults, c => c.Inputs.GetProperty("time").GetDouble() == c.Inputs.GetProperty("period").GetDouble() / 2);
                }
                var animated = string.Equals(collection, "animated", StringComparison.Ordinal);
                var period = wall.First().Inputs.GetProperty("period").GetDouble();
                foreach (var variant in new[] { "default", "quiet", "rich", "dynamic-dusk", "match-accent", "full-focus", "perspective", "intermediate-focus", "authored-focus" })
                {
                    var entries = wall.Where(c => string.Equals(c.Variant, variant, StringComparison.Ordinal)).ToArray();
                    var times = animated ? new[] { 0.0, period / 2 } : new[] { 0.0 };
                    var samples = variant is "dynamic-dusk" or "match-accent" ? 2 : 1;
                    Assert.Equal(2 * times.Length * samples, entries.Length);
                    foreach (var dark in new[] { true, false }) foreach (var time in times)
                        Assert.Equal(samples, entries.Count(c => c.Inputs.GetProperty("dark").GetBoolean() == dark && c.Inputs.GetProperty("time").GetDouble() == time));
                }
                foreach (var c in wall)
                {
                    var input = c.Inputs;
                    Assert.Contains(input.GetProperty("clockHours").GetDouble(), new[] { 18.0, 15.25 });
                    Assert.Contains(input.GetProperty("accent").GetString(), new[] { "#005FB8", "#D4501A" });
                    Assert.Equal(string.Equals(c.Variant, "dynamic-dusk", StringComparison.Ordinal), input.GetProperty("dynamic").GetBoolean());
                    var expectedFocus = c.Variant switch
                    {
                        "full-focus" => 1.0,
                        "intermediate-focus" => .37,
                        "authored-focus" when !animated && c.Wallpaper is "fogbow" or "brume" => .1,
                        "authored-focus" when !animated && c.Wallpaper is "lenticular" or "pillar" => .05,
                        _ => 0.0,
                    };
                    Assert.Equal(expectedFocus, input.GetProperty("focus").GetDouble());
                    if (c.Variant == "dynamic-dusk")
                        Assert.Equal(new[] { 15.25, 18.0 }, wall.Where(x => x.Variant == c.Variant).Select(x => x.Inputs.GetProperty("clockHours").GetDouble()).Distinct().Order());
                    if (c.Variant == "match-accent")
                        Assert.Equal(new[] { "#005FB8", "#D4501A" }, wall.Where(x => x.Variant == c.Variant).Select(x => x.Inputs.GetProperty("accent").GetString()).Distinct().Order());
                    if (c.Variant == "perspective")
                    {
                        Assert.True(input.GetProperty("perspective").GetBoolean());
                        Assert.Equal(new[] { .65, -.4 }, input.GetProperty("tilt").EnumerateArray().Select(t => t.GetDouble()));
                    }
                    else Assert.Equal(new[] { 0.0, 0.0 }, input.GetProperty("tilt").EnumerateArray().Select(t => t.GetDouble()));
                    Assert.Equal(18, input.GetProperty("uniforms").GetProperty("palette").GetArrayLength());
                    Assert.Matches("^[a-f0-9]{64}\\.png$", c.File);
                    Assert.Equal(c.File[..^4], input.GetProperty("sha256").GetString());
                }
            }
        }
        Assert.Equal(cases.Count, cases.Select(c => c.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(162, cases.Select(c => c.Inputs.GetProperty("name").GetString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Pngs_are_lossless_hash_bound_opaque_and_within_the_fixture_budget()
    {
        var unique = WallpaperReferences.Cases.GroupBy(c => c.File, StringComparer.Ordinal).ToArray();
        Assert.Equal(unique.Length, WallpaperReferences.Manifest.GetProperty("counts").GetProperty("uniquePngs").GetInt32());
        long pngBytes = 0;
        foreach (var group in unique)
        {
            var bytes = File.ReadAllBytes(Path.Combine(WallpaperReferences.Folder, group.Key));
            pngBytes += bytes.Length;
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            foreach (var reference in group)
            {
                Assert.Equal(hash, reference.Inputs.GetProperty("sha256").GetString());
                Assert.Equal(bytes.Length, reference.Inputs.GetProperty("bytes").GetInt32());
            }
            using var bitmap = SKBitmap.Decode(bytes);
            Assert.NotNull(bitmap); Assert.Equal(160, bitmap.Width); Assert.Equal(100, bitmap.Height);
            var rgba = new byte[160 * 100 * 4];
            for (var y = 0; y < 100; y++) for (var x = 0; x < 160; x++)
            {
                var p = bitmap.GetPixel(x, y); var i = (y * 160 + x) * 4;
                Assert.Equal(255, p.Alpha);
                rgba[i] = p.Red; rgba[i + 1] = p.Green; rgba[i + 2] = p.Blue; rgba[i + 3] = p.Alpha;
            }
            var rgbaHash = Convert.ToHexStringLower(SHA256.HashData(rgba));
            foreach (var reference in group) Assert.Equal(rgbaHash, reference.Inputs.GetProperty("rgbaSha256").GetString());
        }
        Assert.Equal(pngBytes, WallpaperReferences.Manifest.GetProperty("counts").GetProperty("pngBytes").GetInt64());
        Assert.True(pngBytes + new FileInfo(Path.Combine(WallpaperReferences.Folder, "manifest.json")).Length < 100_000_000);
        Assert.Equal(unique.Length + 1, Directory.GetFiles(WallpaperReferences.Folder).Length);
    }
}
