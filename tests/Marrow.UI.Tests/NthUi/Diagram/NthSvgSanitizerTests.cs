using Avalonia;
using Avalonia.Media;
using Marrow.UI.NthUi.Diagram;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): the SVG a page or a model hands over is sanitised from an allow-list:
// shapes, paths, text, gradients and markers only; no script, no foreign object, no stylesheet, no event handler, no address.
// These tests hold the allow-list from both sides: what a hostile document loses, what a real one keeps, that the result writes
// back to markup that holds none of it, that reading that markup again changes nothing, and that nothing a stranger types
// can cost more than the limits allow.
public sealed class NthSvgSanitizerTests
{
    private static NthSvgDocument Read(string markup, NthSvgLimits? limits = null)
    {
        var result = NthSvgSanitizer.Parse(markup, limits);
        Assert.True(result.Ok, result.Error);
        return result.Document!;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_flowchart_from_the_renderer_keeps_its_shapes_text_marker_and_title()
    {
        var doc = Read(SvgSamples.Flowchart, NthSvgLimits.Renderer);
        Assert.Equal(new Rect(0, 0, 320, 180), doc.ViewBox);
        Assert.Equal("Request flow", doc.Title);
        Assert.Contains("m_pointEnd", doc.Markers.Keys);
        var marker = doc.Markers["m_pointEnd"];
        Assert.True(marker.Auto);
        Assert.True(marker.UserSpace);
        Assert.Equal(new Rect(0, 0, 10, 10), marker.ViewBox);
        Assert.Single(marker.Content.Children);
        var all = Flatten(doc.Root).ToList();
        Assert.Equal(2, all.OfType<NthSvgRect>().Count());
        Assert.Equal(2, all.OfType<NthSvgText>().Count());
        var edge = Assert.Single(all.OfType<NthSvgPathShape>());
        Assert.Equal("m_pointEnd", edge.Style.MarkerEnd);
        Assert.Equal(NthSvgPaintKind.None, edge.Style.Fill!.Value.Kind);
        Assert.Equal(2, edge.Style.StrokeWidth);
        var client = all.OfType<NthSvgText>().First(t => t.Content == "Client");
        Assert.Equal(NthSvgAnchor.Middle, client.Style.Anchor);
        Assert.Equal(NthSvgBaseline.Central, client.Style.Baseline);
        Assert.Equal(14, client.Style.FontSize);
        Assert.Equal("Segoe UI Variable, Segoe UI, sans-serif", client.Style.FontFamily);
        Assert.Empty(doc.Removed);
    }

    private static IEnumerable<NthSvgNode> Flatten(NthSvgGroup group)
    {
        foreach (var child in group.Children)
        {
            yield return child;
            if (child is NthSvgGroup inner)
            {
                foreach (var each in Flatten(inner)) yield return each;
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_hostile_document_loses_everything_outside_the_allow_list_and_says_what_it_lost()
    {
        var doc = Read(SvgSamples.Hostile);
        var markup = NthSvgWriter.ToMarkup(doc);
        foreach (var forbidden in SvgSamples.Forbidden)
            Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
        foreach (var note in new[] { "<script> removed", "<style> removed", "<foreignObject> removed", "<image> removed", "<animate> removed", "<set> removed",
            "<filter> removed", "<clipPath> removed", "<mask> removed", "<pattern> removed", "<switch> removed", "external reference", "event handler onclick",
            "event handler onmouseover", "event handler onload" })
            Assert.Contains(note, doc.Removed);

        // What stays: the rects (the second keeps its attribute fill because the style's address was refused), the path, the
        // local use, the text with its tspan.
        var all = Flatten(doc.Root).ToList();
        var blue = all.OfType<NthSvgRect>().Single(r => r.X == 50);
        Assert.Equal(Color.Parse("#0000ff"), blue.Style.Fill!.Value.Color);
        Assert.Equal(Color.Parse("#ff0000"), blue.Style.Stroke!.Value.Color);
        var use = Assert.Single(all.OfType<NthSvgUse>());
        Assert.Equal("r1", use.Target);
        Assert.Equal((20, 40), (use.X, use.Y));
        var text = Assert.Single(all.OfType<NthSvgText>());
        Assert.Equal("Hithere", text.Content);
        Assert.Null(text.Style.FontFamily); // a family list with an address in it is refused whole
        var path = Assert.Single(all.OfType<NthSvgPathShape>());
        Assert.Null(path.Style.MarkerEnd);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Entities_and_doctypes_expand_nothing()
    {
        const string laughs = """
            <?xml version="1.0"?>
            <!DOCTYPE svg [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
            ]>
            <svg viewBox="0 0 10 10"><text x="0" y="5">&c;</text></svg>
            """;
        var result = NthSvgSanitizer.Parse(laughs);
        Assert.False(result.Ok);
        const string external = """
            <!DOCTYPE svg [<!ENTITY x SYSTEM "file:///C:/Windows/win.ini">]>
            <svg viewBox="0 0 10 10"><text x="0" y="5">&x;</text></svg>
            """;
        var file = NthSvgSanitizer.Parse(external);
        Assert.False(file.Ok);
        Assert.DoesNotContain("[fonts]", file.Error ?? "", StringComparison.OrdinalIgnoreCase);
        // A plain doctype with no entity use is tolerated (it is skipped, never fetched).
        var plain = NthSvgSanitizer.Parse("""
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg viewBox="0 0 10 10"><rect width="5" height="5"/></svg>
            """);
        Assert.True(plain.Ok, plain.Error);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Reading_what_was_written_changes_nothing()
    {
        foreach (var sample in new[] { SvgSamples.Flowchart, SvgSamples.LineArt, SvgSamples.Hostile })
        {
            var first = NthSvgWriter.ToMarkup(Read(sample, NthSvgLimits.Renderer));
            var second = NthSvgWriter.ToMarkup(Read(first, NthSvgLimits.Renderer));
            Assert.Equal(first, second);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Limits_stop_a_document_that_asks_for_too_much()
    {
        Assert.False(NthSvgSanitizer.Parse(SvgSamples.Many(NthSvgLimits.Model.MaxNodes + 1)).Ok);
        Assert.True(NthSvgSanitizer.Parse(SvgSamples.Many(500)).Ok);
        var deep = NthSvgSanitizer.Parse(SvgSamples.Deep(NthSvgLimits.Model.MaxDepth + 4));
        Assert.False(deep.Ok);
        Assert.Contains("deeper", deep.Error, StringComparison.Ordinal);
        Assert.True(NthSvgSanitizer.Parse(SvgSamples.Deep(10)).Ok);
        var huge = "<svg viewBox=\"0 0 10 10\">" + new string(' ', NthSvgLimits.Model.MaxLength) + "</svg>";
        Assert.False(NthSvgSanitizer.Parse(huge).Ok);
        Assert.False(NthSvgSanitizer.Parse("").Ok);
        Assert.False(NthSvgSanitizer.Parse("<html><body/></html>").Ok);
        Assert.False(NthSvgSanitizer.Parse("not xml at all").Ok);
        var text = "<svg viewBox=\"0 0 10 10\"><text x=\"0\" y=\"5\">" + new string('a', NthSvgLimits.Model.MaxTextLength + 1) + "</text></svg>";
        Assert.False(NthSvgSanitizer.Parse(text).Ok);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_view_box_and_the_declared_size_are_read_and_a_missing_box_takes_the_svg_default()
    {
        var both = Read("""<svg width="200" height="100" viewBox="0 0 400 200"/>""");
        Assert.Equal(new Size(400, 200), both.Size);
        Assert.Equal(new Size(200, 100), both.DeclaredSize);
        var sized = Read("""<svg width="120px" height="60px"/>""");
        Assert.Equal(new Rect(0, 0, 120, 60), sized.ViewBox);
        var none = Read("<svg/>");
        Assert.Equal(new Rect(0, 0, 300, 150), none.ViewBox);
        var percent = Read("""<svg width="100%" height="100%" viewBox="0 0 50 25"/>""");
        Assert.Null(percent.DeclaredSize);
        var absurd = Read("""<svg viewBox="0 0 0 -4" width="30" height="30"/>""");
        Assert.Equal(new Rect(0, 0, 30, 30), absurd.ViewBox);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Style_attributes_and_declarations_are_read_with_the_declaration_winning_and_bad_values_ignored()
    {
        var doc = Read("""
            <svg viewBox="0 0 10 10" fill="#112233">
              <g stroke="red" stroke-width="3" opacity=".5">
                <rect width="4" height="4" fill="blue" style="fill:#abc; stroke-width: 2px; stroke-dasharray: 4 2; stroke-linecap: round !important; font-size: huge; fill-opacity: 50%"/>
                <circle r="2" cx="5" cy="5" fill="nonsense" stroke="rgb(10, 20, 30)" stroke-opacity="2"/>
                <path d="M0 0 L5 5" stroke="hsl(120, 100%, 25%)" stroke-width="-4" stroke-miterlimit="0"/>
              </g>
            </svg>
            """);
        Assert.Equal(Color.Parse("#112233"), doc.Root.Style.Fill!.Value.Color);
        var group = Assert.IsType<NthSvgGroup>(doc.Root.Children[0]);
        Assert.Equal(.5, group.Style.Opacity);
        var rect = Assert.IsType<NthSvgRect>(group.Children[0]);
        Assert.Equal(Color.Parse("#aabbcc"), rect.Style.Fill!.Value.Color);
        Assert.Equal(2, rect.Style.StrokeWidth);
        Assert.Equal([4.0, 2.0], rect.Style.DashArray!);
        Assert.Equal(PenLineCap.Round, rect.Style.LineCap);
        Assert.Null(rect.Style.FontSize);
        Assert.Equal(.5, rect.Style.FillOpacity);
        var circle = Assert.IsType<NthSvgEllipse>(group.Children[1]);
        Assert.Null(circle.Style.Fill);
        Assert.Equal(Color.FromRgb(10, 20, 30), circle.Style.Stroke!.Value.Color);
        Assert.Equal(1, circle.Style.StrokeOpacity);
        var path = Assert.IsType<NthSvgPathShape>(group.Children[2]);
        Assert.Equal(Color.FromRgb(0, 128, 0), path.Style.Stroke!.Value.Color);
        Assert.Null(path.Style.StrokeWidth);
        Assert.Null(path.Style.MiterLimit);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Transforms_compose_left_to_right_and_a_malformed_list_is_dropped()
    {
        var doc = Read("""
            <svg viewBox="0 0 10 10">
              <rect id="a" width="1" height="1" transform="translate(10 20) scale(2)"/>
              <rect id="b" width="1" height="1" transform="rotate(90 5 5)"/>
              <rect id="c" width="1" height="1" transform="matrix(1 0 0 1 7 8)"/>
              <rect id="d" width="1" height="1" transform="translate(1,2) wobble(3)"/>
              <rect id="e" width="1" height="1" style="transform: translate(4px)"/>
            </svg>
            """);
        NthSvgNode Get(string id) => doc.Targets[id];
        var a = Get("a").Transform.Transform(new Point(1, 1)); // scale first, then translate
        Assert.Equal(new Point(12, 22), a);
        var b = Get("b").Transform.Transform(new Point(5, 0));
        Assert.Equal(10, b.X, 6);
        Assert.Equal(5, b.Y, 6);
        Assert.Equal(new Point(8, 9), Get("c").Transform.Transform(new Point(1, 1)));
        Assert.False(Get("d").HasTransform);
        Assert.Contains("transform not read", doc.Removed);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Text_collapses_white_space_keeps_tspan_positions_and_resolves_em_offsets()
    {
        var doc = Read("""
            <svg viewBox="0 0 100 100">
              <text x="10" y="20">  Hello
                 <tspan x="10" dy="1.2em" font-weight="bold">brave   new</tspan>  world  </text>
              <text x="0" y="0" xml:space="preserve">a  b</text>
              <text x="0" y="0">   </text>
            </svg>
            """);
        var texts = doc.Root.Children.OfType<NthSvgText>().ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal("Hello brave new world", texts[0].Content);
        var run = texts[0].Runs.First(r => r.Text.Contains("brave", StringComparison.Ordinal));
        Assert.Equal(10, run.X);
        Assert.Equal(1.2, run.Dy);
        Assert.True(run.DyEm);
        Assert.Equal(700, run.Style.FontWeight);
        Assert.Equal("a  b", texts[1].Content);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Gradients_are_read_by_id_and_an_address_is_never_a_paint()
    {
        var doc = Read("""
            <svg viewBox="0 0 10 10">
              <defs>
                <linearGradient id="g1" x1="0%" y1="0%" x2="100%" y2="0%">
                  <stop offset="0" stop-color="#ff0000"/>
                  <stop offset="50%" style="stop-color: blue; stop-opacity: .5"/>
                  <stop offset="1" stop-color="rgb(0,255,0)"/>
                </linearGradient>
                <radialGradient id="g2" gradientUnits="userSpaceOnUse" cx="5" cy="5" r="4"><stop offset="0" stop-color="white"/></radialGradient>
              </defs>
              <rect id="a" width="10" height="10" fill="url(#g1)"/>
              <rect id="b" width="10" height="10" fill="url('#g2') green"/>
              <rect id="c" width="10" height="10" fill="url(https://evil.example/g.svg#g1)"/>
            </svg>
            """);
        var g1 = doc.Gradients["g1"];
        Assert.False(g1.Radial);
        Assert.Equal(3, g1.Stops.Count);
        Assert.Equal(.5, g1.Stops[1].Offset);
        Assert.InRange((int)g1.Stops[1].Color.A, 127, 128);
        Assert.Equal((0.0, 1.0), (g1.X1, g1.X2));
        var g2 = doc.Gradients["g2"];
        Assert.True(g2.Radial && g2.UserSpace);
        Assert.Equal(4, g2.R);
        var a = (NthSvgRect)doc.Targets["a"];
        Assert.Equal(NthSvgPaintKind.Reference, a.Style.Fill!.Value.Kind);
        Assert.Equal("g1", a.Style.Fill.Value.Reference);
        var b = (NthSvgRect)doc.Targets["b"];
        Assert.Equal(Color.Parse("#008000"), b.Style.Fill!.Value.Color);
        Assert.Null(((NthSvgRect)doc.Targets["c"]).Style.Fill);
        Assert.Contains("external reference", doc.Removed);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Elements_with_display_none_and_empty_shapes_are_not_kept()
    {
        var doc = Read("""
            <svg viewBox="0 0 10 10">
              <rect width="5" height="5" display="none"/>
              <g style="display:none"><circle r="3"/></g>
              <rect width="0" height="5"/>
              <circle r="-1"/>
              <path d="Z"/>
              <polyline points="1,1"/>
              <rect id="kept" width="5" height="5" visibility="hidden"/>
            </svg>
            """);
        Assert.Single(doc.Root.Children);
        var kept = (NthSvgRect)doc.Targets["kept"];
        Assert.False(kept.Style.Visible);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void An_unfinished_document_is_closed_where_it_stands_and_every_prefix_is_safe()
    {
        var markup = SvgSamples.Flowchart;
        var lastCount = 0;
        NthSvgDocument? last = null;
        for (var length = 1; length <= markup.Length; length++)
        {
            var result = NthSvgSanitizer.ParsePartial(markup[..length], NthSvgLimits.Renderer);
            if (!result.Ok) continue;
            Assert.True(result.Document!.NodeCount >= lastCount - 1, $"a longer prefix lost parts at {length}");
            lastCount = result.Document.NodeCount;
            last = result.Document;
        }
        Assert.NotNull(last);
        Assert.Equal(NthSvgWriter.ToMarkup(Read(markup, NthSvgLimits.Renderer)), NthSvgWriter.ToMarkup(last!));
        Assert.False(NthSvgSanitizer.ParsePartial("<svg viewBox=\"0 0 10 10\" wid").Ok);
        Assert.True(NthSvgSanitizer.ParsePartial("<svg viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"/><circle r=\"3\"").Ok);
        Assert.Single(NthSvgSanitizer.ParsePartial("<svg viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"/><circle r=\"3\"").Document!.Root.Children);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Mutated_documents_never_throw_and_never_write_back_anything_forbidden()
    {
        var random = new Random(16);
        var pieces = new[] { "<script>", "</g>", "<foreignObject>", "onclick=\"x\"", "url(http://evil.example)", "javascript:", "<!--", "]]>", "&#x0;", "<![CDATA[", "\"", "'", "<", ">", "&", "<use href=\"#a\"/>", "<style>" };
        var sources = new[] { SvgSamples.Flowchart, SvgSamples.LineArt, SvgSamples.Hostile };
        for (var i = 0; i < 600; i++)
        {
            var text = sources[i % sources.Length];
            for (var edits = random.Next(1, 5); edits > 0; edits--)
            {
                var at = random.Next(text.Length);
                text = random.Next(3) switch
                {
                    0 => text.Remove(at, Math.Min(random.Next(1, 12), text.Length - at)),
                    1 => text.Insert(at, pieces[random.Next(pieces.Length)]),
                    _ => text[..at],
                };
            }
            foreach (var result in new[] { NthSvgSanitizer.Parse(text, NthSvgLimits.Renderer), NthSvgSanitizer.ParsePartial(text, NthSvgLimits.Renderer) })
            {
                if (result.Document is not { } doc) continue;
                var markup = NthSvgWriter.ToMarkup(doc);
                foreach (var forbidden in SvgSamples.Structural)
                    Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Ids_that_could_carry_anything_but_a_name_are_not_kept()
    {
        var doc = Read("""
            <svg viewBox="0 0 10 10">
              <rect id="ok_1.a-b:c" width="1" height="1"/>
              <rect id="bad id" width="1" height="1"/>
              <rect id="9starts-with-digit" width="1" height="1"/>
              <rect id="x&quot;onload=&quot;y" width="1" height="1"/>
            </svg>
            """);
        Assert.Equal(["ok_1.a-b:c"], doc.Targets.Keys);
    }
}
