// ED-NEW-glassslots-1 (PORT-ONLY, no Swift counterpart: Skia programs): every glass container of 2 to 8 pieces draws with one merged program
// whose slots past its pieces are switched off, so a piece coming or going never links a program on the render thread. The merged program
// must draw exactly what the program of the container's own count drew.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialSlotsTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-180")] // ED-NEW-glassslots-1: one program for every container, one for a lone piece
    public void Every_container_of_two_to_eight_pieces_draws_with_one_merged_program() => headless.OnUiThread(() =>
    {
        var merged = GlassMaterial.WarmupProgram(2);
        Assert.NotSame(GlassMaterial.WarmupProgram(1), merged);
        Assert.DoesNotContain("pieces", GlassMaterial.WarmupProgram(1).Uniforms); // the lone piece's program is the one it always was
        Assert.Contains("pieces", merged.Uniforms);
        for (var count = 2; count <= GlassMaterial.WarmShapes; count++)
        {
            Assert.Same(merged, GlassMaterial.WarmupProgram(count));
            Assert.Equal(GlassMaterial.WarmShapes, GlassMaterial.Slots(count));
        }
        Assert.Contains($"optics{GlassMaterial.WarmShapes - 1}", merged.Uniforms);
        Assert.DoesNotContain($"optics{GlassMaterial.WarmShapes}", merged.Uniforms);
    });

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [Trait("Req", "PR-WIN-180")] // ED-NEW-glassslots-1: the merged program draws every pixel exactly as the program of the container's own count
    public void A_container_draws_the_same_pixels_with_the_merged_program_as_with_its_own_count(int count, bool overlapping) => headless.OnUiThread(() =>
    {
        var withMerged = Draw(count, overlapping, countPrograms: false);
        var withOwnCount = Draw(count, overlapping, countPrograms: true);
        Assert.Equal(withOwnCount.Length, withMerged.Length);
        var differing = Enumerable.Range(0, withMerged.Length).Where(i => withMerged[i] != withOwnCount[i]).ToArray();
        Assert.True(differing.Length == 0, $"{count} pieces ({(overlapping ? "overlapping" : "in a row")}): {differing.Length} pixels differ, first at "
            + string.Join(" ", differing.Take(5).Select(i => $"({i % 256},{i / 256}) {withMerged[i]} vs {withOwnCount[i]}")));
        // the scene draws glass: the pixels under the pieces are not the bare stripes
        Assert.NotEqual(withMerged[60 * 256 + 18], Bare()[60 * 256 + 18]);
    });

    private static uint[] Draw(int count, bool overlapping, bool countPrograms)
    {
        GlassMaterial.CountProgramsForTests = countPrograms;
        try
        {
            var container = new Panel();
            SwiftGlass.SetContainerSpacing(container, 8);
            for (var i = 0; i < count; i++)
            {
                var shape = (i % 3) switch
                {
                    0 => GlassShape.RoundedRectangle(10, RoundedCornerStyle.Continuous),
                    1 => GlassShape.Circle,
                    _ => GlassShape.Capsule(RoundedCornerStyle.Continuous),
                };
                var piece = new Border
                {
                    Background = Brushes.Transparent,
                    Width = overlapping ? 60 : 24,
                    Height = i % 3 == 1 ? (overlapping ? 60 : 24) : 40,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    // in a row: gaps of 4 and 10 pt, the first pieces joined by the container's spacing, the rest apart; overlapping: each
                    // over the last, so the nearest piece changes inside the union
                    Margin = overlapping ? new Thickness(8 + 22 * i, 30 + 9 * (i % 4), 0, 0) : new Thickness(6 + 31 * i - (i % 2) * 6, 40 + 6 * (i % 3), 0, 0),
                    Opacity = i == count - 1 ? 0.6 : 1,
                };
                SwiftGlass.SetEffect(piece, GlassEffect.Regular(shape));
                container.Children.Add(piece);
            }
            using var scene = new Scene(new Pattern(), container);
            return Pixels(scene);
        }
        finally { GlassMaterial.CountProgramsForTests = false; }
    }

    private static uint[] Bare()
    {
        using var scene = new Scene(new Pattern(), new Panel());
        return Pixels(scene);
    }

    private static uint[] Pixels(Scene scene)
    {
        var pixel = scene.Pixels();
        var all = new uint[256 * 160];
        for (var y = 0; y < 160; y++)
            for (var x = 0; x < 256; x++)
            {
                var c = pixel(x, y);
                all[y * 256 + x] = (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
            }
        return all;
    }
}
