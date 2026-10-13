using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 2.5 "Asturs on Armilla"; ED-NEW-dextra-as16-1): the six node shapes, their ids, and the tint each has on the dark and
// the light theme. The owner picks the shape; two Asturs never share one, so two never look alike.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-1")]
public sealed partial class AsturShapesTests
{
    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex Hex();

    [Fact]
    public void The_tints_are_the_canvas_table_for_both_themes()
    {
        Assert.Equal(("#8FD8B8", "#2E8B66"), AsturShapes.Tint(AsturShape.Squircle));
        Assert.Equal(("#F2A99E", "#C0574A"), AsturShapes.Tint(AsturShape.Hexagon));
        Assert.Equal(("#A9C1FA", "#4A6FD0"), AsturShapes.Tint(AsturShape.Diamond));
        Assert.Equal(("#EBCB82", "#9C7414"), AsturShapes.Tint(AsturShape.RoundedTriangle));
        Assert.Equal(("#C9B0F5", "#7E5BC4"), AsturShapes.Tint(AsturShape.Clover));
        Assert.Equal(("#86D3E3", "#2A8FA6"), AsturShapes.Tint(AsturShape.Star));
        foreach (var shape in AsturShapes.All)
        {
            Assert.Matches(Hex(), AsturShapes.Tint(shape).Dark);
            Assert.Matches(Hex(), AsturShapes.Tint(shape).Light);
        }
        Assert.Equal(6, AsturShapes.All.Select(s => AsturShapes.Tint(s).Dark).Distinct().Count());       // no two look alike
        Assert.Equal(6, AsturShapes.All.Select(s => AsturShapes.Tint(s).Light).Distinct().Count());
        Assert.Equal(6, AsturShapes.MostAsturs);
    }

    [Fact]
    public void Ids_round_trip_and_the_canvas_names_are_understood()
    {
        foreach (var shape in AsturShapes.All) Assert.Equal(shape, AsturShapes.Parse(AsturShapes.Id(shape)));
        Assert.Equal(AsturShape.Hexagon, AsturShapes.Parse("hex"));
        Assert.Equal(AsturShape.RoundedTriangle, AsturShapes.Parse("Triangle"));
        Assert.Equal(AsturShape.RoundedTriangle, AsturShapes.Parse("rounded triangle"));
        Assert.Equal(AsturShape.RoundedTriangle, AsturShapes.Parse(" Rounded-Triangle "));
        Assert.Null(AsturShapes.Parse("circle"));
        Assert.Null(AsturShapes.Parse(null));
        Assert.Equal("rounded_triangle", AsturShapes.Id(AsturShape.RoundedTriangle));
        Assert.Equal("Rounded triangle", AsturShapes.Title(AsturShape.RoundedTriangle));
    }

    [Fact]
    public void The_first_free_shape_follows_the_pickers_order_and_is_none_when_all_six_are_taken()
    {
        Assert.Equal(AsturShape.Squircle, AsturShapes.FirstFree([]));
        Assert.Equal(AsturShape.Diamond, AsturShapes.FirstFree([AsturShape.Squircle, AsturShape.Hexagon]));
        Assert.Equal(AsturShape.Squircle, AsturShapes.FirstFree([AsturShape.Hexagon]));
        Assert.Null(AsturShapes.FirstFree(AsturShapes.All));
    }
}
