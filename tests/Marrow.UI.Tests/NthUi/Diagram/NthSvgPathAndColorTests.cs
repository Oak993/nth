using Avalonia;
using Avalonia.Media;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Diagram;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): nth reads path data and colours itself. A path becomes a short list of
// absolute segments (so the painter, the markers and the writer share one form), a bad number stops the path where it is, and
// a colour that would be lost on its surface is moved along its own hue until it reads.
public sealed class NthSvgPathAndColorTests
{
    private static IReadOnlyList<NthSvgPathSegment> Segments(string d) => NthSvgPathData.Parse(d).Segments;

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Every_command_becomes_an_absolute_move_line_cubic_quad_arc_or_close()
    {
        var segments = Segments("m10 10 20 0 v10 h-20 z M0 0 C1 1 2 2 3 3 s1 1 2 2 Q4 4 5 5 t1 1 A5 5 0 1 0 9 9");
        Assert.Equal(
            [NthSvgPathKind.Move, NthSvgPathKind.Line, NthSvgPathKind.Line, NthSvgPathKind.Line, NthSvgPathKind.Close,
             NthSvgPathKind.Move, NthSvgPathKind.Cubic, NthSvgPathKind.Cubic, NthSvgPathKind.Quad, NthSvgPathKind.Quad, NthSvgPathKind.Arc],
            segments.Select(s => s.Kind));
        Assert.Equal((10, 10), (segments[0].X, segments[0].Y));
        Assert.Equal((30, 10), (segments[1].X, segments[1].Y)); // the pair after m is a relative line
        Assert.Equal((30, 20), (segments[2].X, segments[2].Y));
        Assert.Equal((10, 20), (segments[3].X, segments[3].Y));
        Assert.Equal((10, 10), (segments[4].X, segments[4].Y)); // z goes back to the subpath start
        // s reflects the previous cubic's second control point about the current point: (3,3) + (3,3) - (2,2) = (4,4).
        Assert.Equal((4, 4), (segments[7].X1, segments[7].Y1));
        Assert.Equal((5, 5), (segments[7].X, segments[7].Y));
        // t reflects the previous quad's control point: (5,5) + (5,5) - (4,4) = (6,6).
        Assert.Equal((6, 6), (segments[9].X1, segments[9].Y1));
        var arc = segments[10];
        Assert.True(arc.Large);
        Assert.False(arc.Sweep);
        Assert.Equal((9, 9), (arc.X, arc.Y));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Numbers_run_together_the_way_minifiers_write_them()
    {
        var segments = Segments("M1.5.5-2-3.25e1.5L-1-2");
        Assert.Equal(2, segments.Count);
        Assert.Equal((1.5, .5), (segments[0].X, segments[0].Y));
        // After the move the pair "-2 -3" is an implicit line, then "25e1 .5" would be another; the last L closes the thought.
        var flags = Segments("M0 0a1 1 0 011 1");
        Assert.Equal(2, flags.Count);
        Assert.True(flags[1].Sweep);
        Assert.False(flags[1].Large);
        Assert.Equal((1, 1), (flags[1].X, flags[1].Y));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_bad_number_or_command_stops_the_path_where_it_is()
    {
        var cut = NthSvgPathData.Parse("M0 0 L10 10 L20 oops L30 30");
        Assert.False(cut.IsComplete);
        Assert.Equal(2, cut.Segments.Count);
        Assert.False(NthSvgPathData.Parse("L10 10").IsComplete); // a path starts with a move
        Assert.True(NthSvgPathData.Parse("").IsEmpty);
        Assert.False(NthSvgPathData.Parse("M0 0 L1e99 1").IsComplete);
        Assert.False(NthSvgPathData.Parse("M0 0 L1 1 X 5").IsComplete);
        Assert.False(NthSvgPathData.Parse("M0 0 L1").IsComplete);
        var longPath = "M0 0" + string.Concat(Enumerable.Repeat(" L1 1", NthSvgPathData.MaxSegments + 10));
        var parsed = NthSvgPathData.Parse(longPath);
        Assert.False(parsed.IsComplete);
        Assert.True(parsed.Segments.Count <= NthSvgPathData.MaxSegments);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_text_form_reads_back_to_the_same_segments()
    {
        var path = NthSvgPathData.Parse("M10 10 C 20 0, 30 0, 40 10 S 60 20 70 10 q5 5 10 0 a4 4 30 0 1 8 0 z");
        var again = NthSvgPathData.Parse(path.ToText());
        Assert.Equal(path.Segments, again.Segments);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Vertices_carry_the_direction_the_path_arrives_and_leaves_by()
    {
        var vertices = NthSvgPathData.Parse("M0 0 L10 0 L10 10").Vertices();
        Assert.Equal(3, vertices.Count);
        Assert.Null(vertices[0].Arrive);
        Assert.Equal(new Vector(10, 0), vertices[0].Leave);
        Assert.Equal(new Vector(10, 0), vertices[1].Arrive);
        Assert.Equal(new Vector(0, 10), vertices[1].Leave);
        Assert.Equal(new Vector(0, 10), vertices[2].Arrive);
        Assert.Null(vertices[2].Leave);
        // A curve arrives along its last control handle.
        var curve = NthSvgPathData.Parse("M0 0 C 0 10, 10 10, 20 0").Vertices();
        Assert.Equal(new Vector(10, -10), curve[1].Arrive);
        Assert.Equal(new Vector(0, 10), curve[0].Leave);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_path_spans_the_box_of_its_points_and_control_points()
    {
        var path = NthSvgPathData.Parse("M10 20 L50 20 L50 60 Z");
        Assert.Equal(new Rect(10, 20, 40, 40), path.Bounds());
        Assert.Equal(new Rect(0, 0, 20, 10), NthSvgPathData.Parse("M0 0 C 0 10, 10 10, 20 0").Bounds());
        Assert.Equal(default, NthSvgPathData.Empty.Bounds());
    }

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData("#abc", 0xFF, 0xAA, 0xBB, 0xCC)]
    [InlineData("#abcd", 0xDD, 0xAA, 0xBB, 0xCC)]
    [InlineData("#112233", 0xFF, 0x11, 0x22, 0x33)]
    [InlineData("#11223344", 0x44, 0x11, 0x22, 0x33)]
    [InlineData("rgb(10, 20, 30)", 0xFF, 10, 20, 30)]
    [InlineData("rgb(10 20 30 / 50%)", 128, 10, 20, 30)]
    [InlineData("rgba(255,0,0,.5)", 128, 255, 0, 0)]
    [InlineData("rgb(100%, 0%, 50%)", 0xFF, 255, 0, 128)]
    [InlineData("hsl(0, 100%, 50%)", 0xFF, 255, 0, 0)]
    [InlineData("hsla(240, 100%, 50%, .25)", 64, 0, 0, 255)]
    [InlineData("Navy", 0xFF, 0, 0, 128)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void Colours_are_read_in_the_forms_svg_writes_them(string text, int a, int r, int g, int b)
    {
        Assert.True(NthSvgColor.TryParse(text, out var color), text);
        Assert.Equal(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData("")]
    [InlineData("url(#a)")]
    [InlineData("var(--x)")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#ggg")]
    [InlineData("rgb(1,2)")]
    [InlineData("rgb(a,b,c)")]
    [InlineData("notacolour")]
    [InlineData("calc(1+2)")]
    [InlineData("expression(alert(1))")]
    public void Anything_else_is_not_a_colour(string text) => Assert.False(NthSvgColor.TryParse(text, out _), text);

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData("#000000", "Dark")]
    [InlineData("#1c1c1e", "Dark")]
    [InlineData("#ffffff", "Light")]
    [InlineData("#f5f5f7", "Light")]
    [InlineData("#808080", "Dark")]
    [InlineData("#808080", "Light")]
    [InlineData("#1e90ff", "Dark")]
    [InlineData("#ffd700", "Light")]
    public void A_colour_that_would_be_lost_moves_along_its_hue_until_it_reads(string hex, string theme)
    {
        var backdrop = theme == "Dark" ? NthUiRig.DarkThread : NthUiRig.LightThread;
        Assert.True(NthSvgColor.TryParse(hex, out var color));
        foreach (var minimum in new[] { NthSvgPainter.StrokeContrast, NthSvgPainter.TextContrast })
        {
            var adapted = NthSvgColor.Adapt(color, backdrop, minimum);
            Assert.True(NthUiTokens.Contrast(adapted, backdrop) >= minimum - .001, $"{hex} on {theme} at {minimum}: {adapted}");
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_colour_that_already_reads_is_left_alone_and_a_see_through_one_keeps_its_alpha()
    {
        var dark = NthUiRig.DarkThread;
        var white = Color.FromRgb(255, 255, 255);
        Assert.Equal(white, NthSvgColor.Adapt(white, dark, 4.5));
        var faint = Color.FromArgb(40, 0, 0, 0);
        var moved = NthSvgColor.Adapt(Color.FromArgb(200, 10, 10, 10), dark, 4.5);
        Assert.Equal(200, moved.A);
        Assert.True(NthUiTokens.Luminance(NthUiTokens.Over(moved, dark)) > NthUiTokens.Luminance(dark));
        Assert.Equal(faint, NthSvgColor.Adapt(faint, dark, 0)); // no minimum, nothing to do
        Assert.Equal(Color.FromArgb(0, 1, 2, 3), NthSvgColor.Adapt(Color.FromArgb(0, 1, 2, 3), dark, 4.5));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void An_adapted_colour_keeps_its_hue()
    {
        Assert.True(NthSvgColor.TryParse("#0a3d91", out var blue));
        var adapted = NthSvgColor.Adapt(blue, NthUiRig.DarkThread, 4.5);
        var (hueBefore, _, _) = NthSvgColor.ToHsl(blue);
        var (hueAfter, _, lightness) = NthSvgColor.ToHsl(adapted);
        Assert.InRange(Math.Abs(hueBefore - hueAfter), 0, 6);
        Assert.True(lightness > NthSvgColor.ToHsl(blue).Lightness);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Colours_write_as_short_text_and_read_back()
    {
        foreach (var color in new[] { Color.FromRgb(1, 2, 3), Color.FromArgb(128, 10, 20, 30), Colors.White })
        {
            Assert.True(NthSvgColor.TryParse(NthSvgColor.ToText(color), out var back));
            Assert.Equal(color.R, back.R);
            Assert.InRange(Math.Abs(color.A - back.A), 0, 1);
        }
    }
}
