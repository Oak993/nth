using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 and 10 (web surfaces W16c; ED-NEW-web-w16c-1): a sanitised picture is drawn with Avalonia's own drawing context.
// These tests read the drawn pixels: fills and strokes where the picture puts them, scaling that fits and never stretches,
// currentColor following the theme, a model's black line art kept readable on dark, arrowheads that turn with their path,
// use, gradients, dashes, opacity, hidden elements and text.
[Collection("Headless")]
public sealed class NthSvgPainterTests(HeadlessSession headless)
{
    private static void Near(Color expected, Color actual, int tolerance, string what) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"{what}: expected {expected}, drew {actual}");

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_fill_lands_where_the_picture_puts_it_and_the_rest_is_the_thread() => headless.OnUiThread(() =>
    {
        using var rig = new SvgRig("""<svg viewBox="0 0 100 50"><rect width="50" height="50" fill="#ff0000"/></svg>""");
        var pixels = rig.Capture();
        Near(Color.Parse("#ff0000"), SvgRig.At(pixels, 25, 25), 2, "inside the rect");
        Near(rig.Thread, SvgRig.At(pixels, 75, 25), 2, "beside the rect");
        Assert.Equal(new Size(100, 50), rig.Canvas.Bounds.Size);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_wider_request_scales_the_picture_uniformly_and_a_narrower_room_shrinks_it() => headless.OnUiThread(() =>
    {
        using var wide = new SvgRig("""<svg viewBox="0 0 100 50"><rect width="50" height="50" fill="#ff0000"/></svg>""", width: 200);
        Assert.Equal(new Size(200, 100), wide.Canvas.Bounds.Size);
        var pixels = wide.Capture();
        Near(Color.Parse("#ff0000"), SvgRig.At(pixels, 90, 50), 2, "scaled rect");
        Near(wide.Thread, SvgRig.At(pixels, 110, 50), 2, "beyond the scaled rect");
        using var narrow = new SvgRig("""<svg viewBox="0 0 100 50"><rect width="50" height="50" fill="#ff0000"/></svg>""", window: 100);
        Assert.Equal(new Size(68, 34), narrow.Canvas.Bounds.Size); // 100 wide window less 2 x 16
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_fit_matrix_centres_a_picture_in_a_box_of_another_shape_without_stretching() => headless.OnUiThread(() =>
    {
        var doc = NthSvgSanitizer.Parse("""<svg viewBox="10 20 100 50"/>""").Document!;
        var fit = new NthSvgPainter(doc).Fit(new Rect(0, 0, 200, 200));
        Assert.Equal(new Point(0, 50), fit.Transform(new Point(10, 20)));
        Assert.Equal(new Point(200, 150), fit.Transform(new Point(110, 70)));
        var size = NthSvgCanvas.Fit(doc, natural: 100, width: 500);
        Assert.Equal(new Size(100, 50), size);
        var tall = NthSvgSanitizer.Parse("""<svg viewBox="0 0 20 2000"/>""").Document!;
        var capped = NthSvgCanvas.Fit(tall, natural: 20, width: 500);
        Assert.Equal(NthSvgCanvas.TallestPicture, capped.Height, 3);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_colour_is_the_threads_primary_text_in_light_and_dark(bool dark) => headless.OnUiThread(() =>
    {
        using var rig = new SvgRig(SvgSamples.LineArt, width: 100, dark: dark);
        var pixels = rig.Capture();
        // The circle's stroke passes through (10, 30) on its left edge.
        var stroke = SvgRig.At(pixels, 10, 30);
        var expected = NthUiTokens.Over(NthUiTokens.Text1.Resolve(dark ? ThemeVariant.Dark : ThemeVariant.Light), rig.Thread);
        Near(expected, stroke, 6, "currentColor stroke");
        Assert.True(NthUiTokens.Contrast(stroke, rig.Thread) >= 4.5);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(true)]
    [InlineData(false)]
    public void Black_line_art_is_adapted_on_dark_when_asked_and_left_alone_when_not(bool adapt) => headless.OnUiThread(() =>
    {
        const string art = """<svg viewBox="0 0 100 50"><path d="M0 25 H100" stroke="#000000" stroke-width="6" fill="none"/></svg>""";
        using var rig = new SvgRig(art, width: 100, dark: true, adapt: adapt);
        var line = SvgRig.At(rig.Capture(), 50, 25);
        if (adapt) Assert.True(NthUiTokens.Contrast(line, rig.Thread) >= NthSvgPainter.StrokeContrast - .1, $"adapted {line}");
        else Near(Color.Parse("#000000"), line, 3, "as drawn");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Text_on_a_light_card_inside_the_picture_is_not_flipped_against_the_dark_thread() => headless.OnUiThread(() =>
    {
        // Black text on a white card, drawn on the dark thread: the card is the surface the text sits on.
        const string art = """<svg viewBox="0 0 120 40"><rect width="120" height="40" fill="#ffffff"/><text x="60" y="26" text-anchor="middle" font-size="22" font-family="Segoe UI" fill="#000000">Hello</text></svg>""";
        using var rig = new SvgRig(art, width: 120, dark: true, adapt: true);
        var pixels = rig.Capture();
        var darkest = 255;
        for (var x = 20; x < 100; x++)
            for (var y = 8; y < 34; y++) darkest = Math.Min(darkest, new[] { SvgRig.At(pixels, x, y).R, SvgRig.At(pixels, x, y).G, SvgRig.At(pixels, x, y).B }.Max());
        Assert.True(darkest < 60, $"the text stayed dark on its white card (darkest pixel {darkest})");
        Near(Colors.White, SvgRig.At(pixels, 4, 4), 2, "the card");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Text_draws_with_its_fill_anchor_and_baseline() => headless.OnUiThread(() =>
    {
        const string art = """<svg viewBox="0 0 200 60"><text x="100" y="40" text-anchor="middle" font-size="30" font-family="Segoe UI" fill="#ff0000">MMMM</text></svg>""";
        using var rig = new SvgRig(art, width: 200);
        var pixels = rig.Capture();
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (var x = 0; x < 200; x++)
        {
            for (var y = 0; y < 60; y++)
            {
                var c = SvgRig.At(pixels, x, y);
                if (c.R > 180 && c.G < 90) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
        }
        Assert.True(right > left, "red ink drawn");
        Assert.InRange((left + right) / 2.0, 92, 108); // centred on x = 100
        Assert.InRange(bottom, 36, 44); // sits on the baseline at y = 40
        Assert.True(top < 25, "capital height reaches up from the baseline");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void An_arrowhead_marker_follows_the_direction_of_its_path() => headless.OnUiThread(() =>
    {
        const string art = """
            <svg viewBox="0 0 100 100">
              <defs><marker id="a" markerWidth="10" markerHeight="10" refX="0" refY="5" orient="auto" markerUnits="userSpaceOnUse"><path d="M0 0 L10 5 L0 10 z" fill="#ff0000"/></marker></defs>
              <path d="M10 25 H60" stroke="#000000" stroke-width="1" marker-end="url(#a)" fill="none"/>
              <path d="M30 40 V70" stroke="#000000" stroke-width="1" marker-end="url(#a)" fill="none"/>
            </svg>
            """;
        using var rig = new SvgRig(art, width: 100);
        var pixels = rig.Capture();
        Near(Color.Parse("#ff0000"), SvgRig.At(pixels, 64, 25), 6, "arrow after the horizontal line");
        Near(rig.Thread, SvgRig.At(pixels, 30, 15), 4, "above the horizontal line");
        Near(Color.Parse("#ff0000"), SvgRig.At(pixels, 30, 74), 6, "arrow after the vertical line, turned down");
        Near(rig.Thread, SvgRig.At(pixels, 36, 74), 4, "beside the turned arrow");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_renderers_flowchart_draws_its_boxes_labels_and_arrow() => headless.OnUiThread(() =>
    {
        using var rig = new SvgRig(SvgSamples.Flowchart, width: 320, limits: NthSvgLimits.Renderer, window: 360, height: 240);
        var pixels = rig.Capture();
        Near(Colors.White, SvgRig.At(pixels, 160 - 40, 30 - 12), 3, "the client box fill");
        Near(Color.Parse("#6f6f72"), SvgRig.At(pixels, 160, 80), 40, "the edge");
        Assert.True(rig.Canvas.Drawn >= 6, $"drawn {rig.Canvas.Drawn}");
        // The label is dark ink inside the white box.
        var ink = 0;
        for (var x = 130; x < 190; x++)
            for (var y = 20; y < 40; y++)
                if (SvgRig.At(pixels, x, y).R < 120) ink++;
        Assert.True(ink > 15, $"label ink {ink}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_use_draws_its_target_where_it_is_placed_and_the_defs_are_not_drawn_alone() => headless.OnUiThread(() =>
    {
        using var rig = new SvgRig("""<svg viewBox="0 0 100 50"><defs><rect id="r" width="10" height="10" fill="#00ff00"/></defs><use href="#r" x="30" y="10"/></svg>""", width: 100);
        var pixels = rig.Capture();
        Near(Color.Parse("#00ff00"), SvgRig.At(pixels, 35, 15), 2, "the used rect");
        Near(rig.Thread, SvgRig.At(pixels, 5, 5), 2, "where the def itself would be");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_gradient_runs_from_one_stop_to_the_other_and_transforms_move_things() => headless.OnUiThread(() =>
    {
        const string art = """
            <svg viewBox="0 0 100 40">
              <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient></defs>
              <rect width="100" height="20" fill="url(#g)"/>
              <g transform="translate(50 20)"><rect width="10" height="10" fill="#00ff00"/></g>
            </svg>
            """;
        using var rig = new SvgRig(art, width: 100);
        var pixels = rig.Capture();
        var left = SvgRig.At(pixels, 5, 10);
        var right = SvgRig.At(pixels, 95, 10);
        Assert.True(left.R > 200 && left.B < 60, $"left {left}");
        Assert.True(right.B > 200 && right.R < 60, $"right {right}");
        Near(Color.Parse("#00ff00"), SvgRig.At(pixels, 55, 25), 2, "the translated rect");
        Near(rig.Thread, SvgRig.At(pixels, 5, 25), 2, "where it would be untranslated");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Dashes_opacity_rounded_corners_and_hidden_shapes_draw_as_written() => headless.OnUiThread(() =>
    {
        const string art = """
            <svg viewBox="0 0 120 80">
              <path d="M0 5 H120" stroke="#000000" stroke-width="4" stroke-dasharray="8 8" fill="none"/>
              <g opacity=".5"><rect y="20" width="30" height="20" fill="#ff0000"/></g>
              <rect x="40" y="20" width="30" height="30" rx="14" fill="#0000ff"/>
              <rect x="80" y="20" width="30" height="20" fill="#00ff00" visibility="hidden"/>
              <path d="M0 70 H120" stroke="#000000" stroke-width="4" stroke-dasharray="1, 0" fill="none"/>
            </svg>
            """;
        using var rig = new SvgRig(art, width: 120);
        var pixels = rig.Capture();
        var on = 0;
        for (var x = 0; x < 120; x++) if (SvgRig.At(pixels, x, 5).R < 100) on++;
        Assert.InRange(on, 48, 72); // half of a 120 run is dash
        var half = SvgRig.At(pixels, 15, 30);
        Near(NthUiTokens.Over(Color.FromArgb(128, 255, 0, 0), rig.Thread), half, 6, "half-opaque red");
        Near(rig.Thread, SvgRig.At(pixels, 41, 21), 6, "the rounded corner is cut");
        Near(Color.Parse("#0000ff"), SvgRig.At(pixels, 55, 35), 2, "the rounded rect's middle");
        Near(rig.Thread, SvgRig.At(pixels, 95, 30), 2, "a hidden shape");
        var solid = 0;
        for (var x = 0; x < 120; x++) if (SvgRig.At(pixels, x, 70).R < 100) solid++;
        Assert.True(solid >= 118, "a dash of 1 and a gap of 0 is a solid line");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_same_picture_draws_in_the_palette_of_the_theme_it_sits_in() => headless.OnUiThread(() =>
    {
        using var light = new SvgRig(SvgSamples.LineArt, width: 100);
        using var dark = new SvgRig(SvgSamples.LineArt, width: 100, dark: true);
        var onLight = SvgRig.At(light.Capture(), 10, 30);
        var onDark = SvgRig.At(dark.Capture(), 10, 30);
        Assert.True(NthUiTokens.Luminance(onDark) > NthUiTokens.Luminance(onLight) + .3, $"light {onLight}, dark {onDark}");
        // A theme change on the same window redraws the canvas with the new colour.
        light.Window.RequestedThemeVariant = ThemeVariant.Dark;
        light.Pump();
        var switched = SvgRig.At(light.Capture(), 10, 30);
        Assert.True(NthUiTokens.Luminance(switched) > NthUiTokens.Luminance(onLight) + .3, $"switched {switched}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Drawing_a_big_document_stays_inside_its_budget() => headless.OnUiThread(() =>
    {
        var builder = new System.Text.StringBuilder("<svg viewBox=\"0 0 1000 1000\">");
        for (var i = 0; i < 1500; i++)
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"<g transform=\"translate({i % 40 * 25} {i / 40 * 25})\"><rect width=\"20\" height=\"20\" rx=\"4\" fill=\"#cccccc\" stroke=\"#888888\"/><text x=\"10\" y=\"14\" text-anchor=\"middle\" font-size=\"8\">{i}</text></g>");
        builder.Append("</svg>");
        using var rig = new SvgRig(builder.ToString(), width: 500, limits: NthSvgLimits.Renderer, window: 540, height: 540);
        rig.Capture(); // the first render builds the layouts
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) rig.Capture();
        var each = watch.Elapsed.TotalMilliseconds / 5;
        Assert.True(rig.Canvas.Drawn >= 3000, $"drawn {rig.Canvas.Drawn}");
        Assert.True(each < 400, $"a render of 3000 parts took {each:0} ms"); // a CPU-only headless frame; the live app draws on the GPU
    });

    // ED-NEW-secfinal-41 (final security gate, N5): one model-written svg could freeze the UI. Up to 2,000 <use> visits each draw a
    // whole group, so a few thousand nodes could mean millions of draws in one paint. A paint now stops after a budget of nodes, the
    // way it already stops at the depth and use limits: what is left is not drawn.
    private const int PaintBudget = 60_000;

    private static string Repeated(int cells, int uses)
    {
        var builder = new System.Text.StringBuilder("<svg viewBox=\"0 0 100 100\"><defs><g id=\"cell\">");
        for (var i = 0; i < cells; i++) builder.Append("<rect width=\"1\" height=\"1\" fill=\"#c33\"/>"); // short, so the document stays inside NthSvgLimits.Model's 262,144 characters
        builder.Append("</g></defs>");
        for (var i = 0; i < uses; i++) builder.Append("<use href=\"#cell\"/>");
        return builder.Append("</svg>").ToString();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-41")]
    public void A_group_used_two_thousand_times_paints_inside_the_node_budget_and_in_bounded_time() => headless.OnUiThread(() =>
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var rig = new SvgRig(Repeated(cells: 400, uses: 2000), width: 200, window: 260, height: 260);
        rig.Capture();
        watch.Stop();
        Assert.True(rig.Canvas.Drawn <= PaintBudget, $"one paint drew {rig.Canvas.Drawn} shapes");
        Assert.True(rig.Canvas.Drawn > 0, "the budget drew nothing at all");
        Assert.Equal(NthSvgLimits.MaxPaintNodes, rig.Canvas.Visited);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"painting took {watch.Elapsed.TotalSeconds:0.0} s");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-41")]
    public void A_marker_that_draws_itself_stops_at_the_depth_and_node_limits() => headless.OnUiThread(() =>
    {
        const string Path = """<path d="M0 0 L4 1 L0 2 L4 3" fill="#cc3333" stroke="#000000" marker-start="url(#m)" marker-mid="url(#m)" marker-end="url(#m)"/>""";
        var svg = $"""<svg viewBox="0 0 100 100"><defs><marker id="m" markerWidth="5" markerHeight="5" refX="2" refY="2" orient="auto" markerUnits="userSpaceOnUse">{Path}</marker></defs><path d="M10 10 L90 90 L10 90" stroke="#000000" fill="none" marker-start="url(#m)" marker-mid="url(#m)" marker-end="url(#m)"/></svg>""";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var rig = new SvgRig(svg, width: 200, window: 260, height: 260);
        rig.Capture();
        watch.Stop();
        Assert.True(rig.Canvas.Drawn <= PaintBudget, $"one paint drew {rig.Canvas.Drawn} shapes");
        Assert.InRange(rig.Canvas.Visited, 1, NthSvgLimits.MaxPaintNodes);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"painting took {watch.Elapsed.TotalSeconds:0.0} s");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-41")]
    public void The_largest_hostile_document_the_model_limit_allows_paints_inside_the_budget_and_in_bounded_time() => headless.OnUiThread(() =>
    {
        var svg = Repeated(cells: 5_000, uses: 2_000); // 7,001 nodes and about 243,000 characters: inside NthSvgLimits.Model, 10 million draws unbudgeted
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var rig = new SvgRig(svg, width: 200, window: 260, height: 260);
        rig.Capture();
        watch.Stop();
        Assert.Equal(NthSvgLimits.MaxPaintNodes, rig.Canvas.Visited);
        Assert.True(rig.Canvas.Drawn <= PaintBudget, $"one paint drew {rig.Canvas.Drawn} shapes");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"painting took {watch.Elapsed.TotalSeconds:0.0} s");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-41")]
    public void The_budget_is_never_smaller_than_the_largest_document_the_renderer_accepts_so_a_plain_document_is_not_cut() =>
        Assert.True(NthSvgLimits.MaxPaintNodes >= NthSvgLimits.Renderer.MaxNodes);

    // ED-NEW-secfinal-44 (final security gate, N5 residual): the node budget does not weigh what a node draws. One text of 60,000
    // characters (all NthSvgLimits.Model lets a document hold) used 2,000 times is 2,000 nodes and 120 million glyphs. A paint also has a
    // glyph budget: every text run drawn counts its characters, and what is left over the budget is not drawn.
    private const int GlyphBudget = 200_000;

    private static string LongTextUsedManyTimes(int characters, int uses)
    {
        var builder = new System.Text.StringBuilder("<svg viewBox=\"0 0 100 100\"><defs><g id=\"t\"><text x=\"0\" y=\"10\" font-size=\"4\">");
        builder.Append('a', characters).Append("</text></g></defs>");
        for (var i = 0; i < uses; i++) builder.Append("<use href=\"#t\"/>");
        return builder.Append("</svg>").ToString();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-44")]
    public void One_text_of_sixty_thousand_characters_used_two_thousand_times_paints_inside_the_glyph_budget_and_in_bounded_time() => headless.OnUiThread(() =>
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var rig = new SvgRig(LongTextUsedManyTimes(characters: 60_000, uses: 2_000), width: 200, window: 260, height: 260);
        rig.Capture();
        watch.Stop();
        Assert.True(rig.Canvas.Drawn <= GlyphBudget / 60_000, $"one paint drew {rig.Canvas.Drawn} texts of 60,000 characters");
        Assert.True(rig.Canvas.Drawn > 0, "the budget drew no text at all");
        Assert.Equal(3, rig.Canvas.Drawn); // 3 x 60,000 = 180,000 characters; a fourth would pass 200,000
        Assert.Equal(NthSvgLimits.MaxPaintGlyphs, rig.Canvas.Glyphs);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"painting took {watch.Elapsed.TotalSeconds:0.0} s");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-44")]
    public void Text_up_to_the_glyph_budget_is_drawn_whole_and_the_text_that_would_pass_it_is_not() => headless.OnUiThread(() =>
    {
        using (var exact = new SvgRig(LongTextUsedManyTimes(characters: 50_000, uses: 4), width: 200, window: 260, height: 260))
        {
            exact.Capture();
            Assert.Equal(4, exact.Canvas.Drawn);
            Assert.Equal(GlyphBudget, exact.Canvas.Glyphs);
        }

        using var over = new SvgRig(LongTextUsedManyTimes(characters: 50_000, uses: 5), width: 200, window: 260, height: 260);
        over.Capture();
        Assert.Equal(4, over.Canvas.Drawn);
        Assert.Equal(GlyphBudget, over.Canvas.Glyphs);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-secfinal-44")]
    public void The_glyph_budget_is_far_above_any_drawing_the_app_makes() => headless.OnUiThread(() =>
    {
        Assert.True(NthSvgLimits.MaxPaintGlyphs >= NthSvgLimits.Model.MaxTextLength * 3, "a Model document used three times fits");
        using var flowchart = new SvgRig(SvgSamples.Flowchart, width: 320, limits: NthSvgLimits.Renderer, window: 360, height: 240);
        flowchart.Capture();
        Assert.InRange(flowchart.Canvas.Glyphs, 1, GlyphBudget / 100);
    });
}
