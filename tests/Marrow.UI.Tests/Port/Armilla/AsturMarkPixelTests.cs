using Marrow.Core.Port.Assistant;
using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-17 (DESIGN.md rev 5, 2.5 "An Astur's own mark", "Never clipped"; ED-NEW-dextra-as17-2): the mark drawn for real with Skia into a bitmap. Every pose of every shape stays
// inside its slot (paint past the slot would show in the margin), each shape is its tint in both themes, the strength is one group opacity (a rounded triangle's outline has no
// seam at partial strength), and the badge's clear ring is cut from the shape and nothing else.
public sealed class AsturMarkPixelTests
{
    private static readonly ArmillaShape[] Shapes = Enum.GetValues<ArmillaShape>();
    private static readonly AsturMarkState[] States = Enum.GetValues<AsturMarkState>();

    private static AsturMarkEngine Mark(ArmillaShape shape, AsturMarkState state, double size = 30, bool dark = true, bool reduce = true) =>
        new(new AsturMarkLook { Size = size, Shape = shape, State = state, Dark = dark, Reduce = reduce });

    [Theory]
    [InlineData(20, 1)]
    [InlineData(30, 1)]
    [InlineData(30, 2)]
    [InlineData(44, 1.5)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Every_pose_of_every_shape_paints_inside_its_slot_and_nothing_past_it(double size, double scale)
    {
        foreach (var shape in Shapes)
        foreach (var state in States)
        foreach (var dark in new[] { true, false })
        {
            using var bitmap = AsturDrive.Render(Mark(shape, state, size, dark), scale);
            Assert.True(AsturDrive.PaintedOutsideSlot(bitmap, size, scale) == 0, $"{size} px at x{scale}, {shape} {state} dark={dark}: paint runs past the slot");
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_moves_between_states_paint_inside_the_slot_too_including_the_overshoot_of_the_springs()
    {
        foreach (var shape in Shapes)
        foreach (var from in States)
        foreach (var to in States)
        {
            if (from == to) continue;
            var look = new AsturMarkLook { Size = 30, Shape = shape, State = from, Dark = false };
            var mark = new AsturMarkEngine(look);
            mark.Set(look with { State = to });
            for (var frame = 0; frame < 48; frame++)
            {
                mark.Tick(AsturDrive.Frame);
                if (frame % 3 != 0) continue;
                using var bitmap = AsturDrive.Render(mark, 2);
                Assert.True(AsturDrive.PaintedOutsideSlot(bitmap, 30, 2) == 0, $"{shape} {from} to {to}, frame {frame}: paint runs past the slot");
            }
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void At_30_px_each_shape_is_its_own_tint_in_the_dark_theme_and_in_the_light_one()
    {
        foreach (var shape in Shapes)
        foreach (var dark in new[] { true, false })
        {
            using var bitmap = AsturDrive.Render(Mark(shape, AsturMarkState.Still, 30, dark));
            var (darkHex, lightHex) = AsturShapes.Tint((AsturShape)(int)shape);
            var want = SKColor.Parse(dark ? darkHex : lightHex);
            var got = AsturDrive.At(bitmap, 30, 4, 0);                                    // inside every shape: nothing is cut there and Still has no badge
            Assert.True(Math.Abs(got.Red - want.Red) <= 2 && Math.Abs(got.Green - want.Green) <= 2 && Math.Abs(got.Blue - want.Blue) <= 2 && got.Alpha == 255,
                $"{shape} dark={dark}: {got} is not {want}");
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_two_tint_tables_Armilla_draws_with_and_the_profile_holds_are_the_same()
    {
        for (var i = 0; i < Shapes.Length; i++)
        {
            var (dark, light) = AsturShapes.Tint((AsturShape)i);
            Assert.Equal(dark, ArmillaPoses.TintsDark[i]);
            Assert.Equal(light, ArmillaPoses.TintsLight[i]);
        }
    }

    [Theory]
    [InlineData(AsturMarkState.Still, 255)]
    [InlineData(AsturMarkState.Idle, 184)]                                                // 72 %
    [InlineData(AsturMarkState.Paused, 101)]                                              // 72 % dimmed to 55 %
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_strength_is_one_group_opacity_so_a_rounded_triangles_outline_shows_no_seam(AsturMarkState state, int alpha)
    {
        using var bitmap = AsturDrive.Render(Mark(ArmillaShape.Triangle, state, 44), 2);
        var scale = state == AsturMarkState.Still ? 1 : .8;
        var r = 22 * AsturMarkEngine.ShapeRadius * scale;
        // the middle of the fill, and a point just inside the triangle's outline, where the stroke overlaps the fill
        var inner = AsturDrive.At(bitmap, 44, 0, 0, 2);
        var band = AsturDrive.At(bitmap, 44, 0, -r * .93, 2);
        Assert.InRange(inner.Alpha, alpha - 3, alpha + 3);
        Assert.InRange(band.Alpha, alpha - 3, alpha + 3);
        Assert.True(Math.Abs(inner.Alpha - band.Alpha) <= 2, $"a seam: {inner.Alpha} inside, {band.Alpha} on the outline");
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_badge_is_an_ink_disc_with_a_clear_ring_cut_from_the_shape_and_only_from_the_shape()
    {
        const double size = 30, hb = 15;
        var radius = Math.Max(9, .4 * size) / 2;
        var centre = hb - 1.5 - radius;                                                    // the badge's centre, from the slot's
        using var withBadge = AsturDrive.Render(Mark(ArmillaShape.Hexagon, AsturMarkState.Done, size), 4);
        using var without = AsturDrive.Render(Mark(ArmillaShape.Hexagon, AsturMarkState.Still, size), 4);

        // a point of the ring, towards the middle of the mark: there the shape is cut away
        var step = (radius + .6) / Math.Sqrt(2);
        var ring = AsturDrive.At(withBadge, size, centre - step, centre - step, 4);
        var whole = AsturDrive.At(without, size, centre - step, centre - step, 4);
        Assert.Equal(255, whole.Alpha);
        Assert.True(ring.Alpha < 20, $"the ring still holds paint ({ring.Alpha})");
        // the disc is the ink (light on dark), well clear of the check inside it
        var disc = AsturDrive.At(withBadge, size, centre + radius * .8, centre, 4);
        Assert.True(disc.Alpha > 250 && disc.Red >= 230 && disc.Green >= 236 && disc.Blue >= 240, $"the disc is {disc}");
        // and the cut takes nothing from what is behind the slot's own corner: far from the badge the shape is whole
        Assert.Equal(255, AsturDrive.At(withBadge, size, -6, -6, 4).Alpha);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void On_a_light_theme_the_badge_disc_is_dark_ink_with_a_light_glyph()
    {
        const double size = 30;
        using var bitmap = AsturDrive.Render(Mark(ArmillaShape.Star, AsturMarkState.Stopped, size, dark: false), 4);
        var radius = 6.0;
        var centre = 15 - 1.5 - radius;
        var disc = AsturDrive.At(bitmap, size, centre + radius * .8, centre, 4);
        var glyph = AsturDrive.At(bitmap, size, centre, centre, 4);                          // the filled square of Stopped
        Assert.True(disc.Alpha > 250 && disc.Red <= 40, $"the disc is {disc}");
        Assert.True(glyph.Alpha > 250 && glyph.Red >= 230, $"the glyph is {glyph}");
    }
}
