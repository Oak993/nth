using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.5 "monochrome line art with the accent only for the current value and focus", 10 "text at least 4.5:1 ... controls
// 3:1" and "High contrast" (web surfaces W16b; ED-NEW-web-w16b-1): a series line, a bar and the accent keep 3:1 on the card in
// both themes; no series is told from another by tone alone; the charts render where the design puts them (a line through its
// points, hollow rings, the canvas's bars with only the largest in the accent, a ring of slices from strong to pale, stacked
// bands, markers of different shapes), and every sample is saved as a PNG next to the test binaries (nthui-snapshots) to review.
[Collection("Headless")]
public sealed class NthUiChartThemeTests(HeadlessSession headless)
{
    public static TheoryData<string> Themes => new(["Light", "Dark"]);

    private static ThemeVariant Theme(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static Color Card(string name)
    {
        var thread = name == "Dark" ? NthUiRig.DarkThread : NthUiRig.LightThread;
        return NthUiTokens.Over(NthUiTokens.CardFill.Resolve(Theme(name)), thread);
    }

    private static Color Pixel(RenderedPixels pixels, NthUiRig rig, Control control, Point at)
    {
        var p = rig.InWindow(control, at);
        var (r, g, b) = pixels.At((int)Math.Round(p.X), (int)Math.Round(p.Y));
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    /// <summary>The pixel in the box around <paramref name="at"/> that differs most from <paramref name="ground"/> (antialiasing and a
    /// pixel of offset do not decide a test of where ink is).</summary>
    private static Color Inkiest(RenderedPixels pixels, NthUiRig rig, Control control, Point at, Color ground, int reach = 2)
    {
        var best = ground;
        var far = 0.0;
        var origin = rig.InWindow(control, at);
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                var (r, g, b) = pixels.At((int)Math.Round(origin.X) + dx, (int)Math.Round(origin.Y) + dy);
                var c = Color.FromRgb((byte)r, (byte)g, (byte)b);
                var d = Math.Abs(c.R - ground.R) + Math.Abs(c.G - ground.G) + Math.Abs(c.B - ground.B);
                if (d <= far) continue;
                far = d;
                best = c;
            }
        }
        return best;
    }

    private static double Luminance(Color c) => NthUiTokens.Luminance(c);

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void A_series_line_a_bar_and_the_accent_keep_three_to_one_on_the_card(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        var card = Card(name);
        var problems = new List<string>();
        for (var series = 0; series < 8; series++)
        {
            var line = NthUiTokens.Over(NthUiChartInk.Stroke(series).Resolve(theme), card);
            var ratio = NthUiTokens.Contrast(line, card);
            if (ratio < 3) problems.Add($"series {series}: {ratio:0.00}");
        }
        var accent = NthUiTokens.Contrast(NthUiTokens.Accent.Resolve(theme), card);
        if (accent < 3) problems.Add($"accent: {accent:0.00}");
        var strong = NthUiTokens.Strong.Resolve(theme);
        foreach (var tone in new[] { .5, .55 })
        {
            var start = NthUiTokens.Over(Color.FromArgb((byte)Math.Round(255 * tone), strong.R, strong.G, strong.B), card);
            var ratio = NthUiTokens.Contrast(start, card);
            if (ratio < 3) problems.Add($"bar start at {tone}: {ratio:0.00}");
        }
        var axis = NthUiTokens.Over(NthUiChartInk.Axis.Resolve(theme), card);
        if (NthUiTokens.Contrast(axis, card) < 1.3) problems.Add("the baseline is not drawn clearly");
        Assert.Empty(problems);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void No_series_is_told_from_another_by_tone_alone_and_a_slice_is_paler_than_the_one_before() => headless.OnUiThread(() =>
    {
        var looks = Enumerable.Range(0, 8).Select(NthUiChartInk.Series).ToList();
        Assert.Equal(8, looks.Select(l => (l.Tone, l.Dash is null ? "solid" : string.Join(",", l.Dash), l.Marker)).Distinct().Count());
        Assert.Equal(4, looks.Select(l => l.Marker).Distinct().Count());
        Assert.Null(looks[0].Dash);
        Assert.NotNull(looks[2].Dash);
        for (var count = 2; count <= 9; count++)
        {
            var tones = Enumerable.Range(0, count).Select(i => NthUiChartInk.FillTone(i, count)).ToList();
            Assert.Equal(tones.OrderByDescending(t => t), tones);
            Assert.InRange(tones[0], .85, .95);
            Assert.InRange(tones[^1], .25, .35);
        }
        Assert.Equal(.86, NthUiChartInk.FillTone(0, 1));
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void An_editable_line_is_drawn_through_its_hollow_rings_over_hairline_gridlines(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English), theme: theme);
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var pixels = rig.Capture();
        var card = Card(name);
        var p0 = plot.PointOf(0, 0)!.Value;
        var p1 = plot.PointOf(0, 1)!.Value;
        var middle = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
        var stroke = Inkiest(pixels, rig, plot, middle, card, 1);
        Assert.True(NthUiTokens.Contrast(stroke, card) >= 3, $"the line between two points is ink: {stroke} on {card}");
        var ring = Inkiest(pixels, rig, plot, new Point(p0.X, p0.Y - 4.5), card, 1);
        Assert.True(NthUiTokens.Contrast(ring, card) >= 6, $"a ring is drawn in the strong tone: {ring}");
        var hole = Pixel(pixels, rig, plot, p0);
        Assert.True(Math.Abs(hole.R - NthUiChartInk.Face.Resolve(theme).R) <= 6 && Math.Abs(hole.G - NthUiChartInk.Face.Resolve(theme).G) <= 6, $"a ring is hollow: {hole}");
        var area = plot.PlotArea;
        var grid = Inkiest(pixels, rig, plot, new Point(area.Left + 12, plot.HeightOf(40)), card, 1);
        Assert.NotEqual(card, grid);
        Assert.True(NthUiTokens.Contrast(grid, card) < 2, $"a gridline is a hairline: {grid}");
        var baseline = Inkiest(pixels, rig, plot, new Point(area.Left + 12, plot.HeightOf(0)), card, 1);
        Assert.True(NthUiTokens.Contrast(baseline, card) > NthUiTokens.Contrast(grid, card), "the baseline is stronger than a gridline");
        var empty = Pixel(pixels, rig, plot, new Point(area.Left + 12, plot.HeightOf(50)));
        Assert.True(NthUiTokens.Contrast(empty, card) < 1.05, $"nothing is drawn between gridlines: {empty}");
        Assert.Equal("units", rig.Chart().Model.Data.Unit);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void The_canvas_bars_are_monochrome_with_the_accent_only_on_the_largest(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English), theme: theme);
        rig.Settle();
        var bars = rig.Chart().Bars!;
        var pixels = rig.Capture();
        var accent = NthUiTokens.Accent.Resolve(theme);
        var card = Card(name);
        var first = Pixel(pixels, rig, bars.Bars[0], new Point(bars.Bars[0].Bounds.Width * .9, 4));
        Assert.True(Math.Abs(first.R - accent.R) < 26 && Math.Abs(first.G - accent.G) < 26 && Math.Abs(first.B - accent.B) < 26, $"the largest bar is the accent {accent}, drew {first}");
        foreach (var bar in bars.Bars.Skip(1))
        {
            var width = bar.Bounds.Width * bar.Shown;
            var color = Pixel(pixels, rig, bar, new Point(Math.Max(2, width - 6), 4));
            Assert.True(Math.Abs(color.R - color.B) < 14 || Math.Abs(color.R - color.G) < 14, $"a smaller bar is monochrome: {color}");
            Assert.True(NthUiTokens.Contrast(color, card) >= 3, $"a bar keeps 3:1 on the card: {color} on {card}");
        }
        Assert.Equal(["Compute", "Egress", "Storage"], bars.Shown.Select(r => r.Name));
        var ratios = bars.Ratios(bars.Model);
        Assert.Equal(1, ratios[0], 6);
        Assert.All(bars.Bars.Zip(ratios), pair => Assert.Equal(pair.Second, pair.First.Shown, 6));
        Assert.True(bars.Bars[2].Shown * bars.Bars[2].Bounds.Width >= 8);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void A_pie_is_a_ring_of_slices_from_strong_to_pale_and_its_key_swatches_match(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English), theme: theme);
        rig.Settle();
        var chart = rig.Chart();
        var pie = chart.Pie!;
        var pixels = rig.Capture();
        var card = Card(name);
        var (center, outer, inner) = pie.Geometry;
        var middle = (outer + inner) / 2;
        var slices = new[] { .25, .65, .9 }.Select(turn => Pixel(pixels, rig, pie, NthUiPieGeometry.At(center, middle, turn * 2 * Math.PI))).ToList();
        var contrasts = slices.Select(c => NthUiTokens.Contrast(c, card)).ToList();
        Assert.True(contrasts[0] > contrasts[1] && contrasts[1] > contrasts[2], $"slices go from strong to pale: {string.Join(", ", contrasts.Select(c => c.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
        Assert.All(slices, c => Assert.True(Math.Abs(c.R - c.B) < 6, $"a slice is monochrome: {c}"));
        var hole = Pixel(pixels, rig, pie, new Point(center.X, center.Y - inner + 4));
        Assert.True(NthUiTokens.Contrast(hole, card) < 1.06, $"the middle of the ring is empty: {hole}");
        var swatches = chart.PieKey!.Children.Select(row => (NthUiLegendSwatch)((Grid)((Border)row).Child!).Children[0]).ToList();
        for (var i = 0; i < 3; i++)
        {
            var key = Pixel(pixels, rig, swatches[i], new Point(swatches[i].Bounds.Width / 2, swatches[i].Bounds.Height / 2));
            Assert.True(Math.Abs(key.R - slices[i].R) <= 8 && Math.Abs(key.G - slices[i].G) <= 8, $"swatch {i} matches its slice: {key} and {slices[i]}");
        }
        Assert.Equal(3, swatches.Count);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void Stacked_areas_fill_their_bands_and_scatter_series_use_different_marker_shapes(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        using var area = new NthUiRig(NthUiView.Show(NthUiSamples.Area, culture: NthUiRig.English), theme: theme);
        area.Settle();
        var plot = area.Chart().Plot!;
        var pixels = area.Capture();
        var ground = Pixel(pixels, area, plot, new Point(plot.PlotArea.Left + 10, plot.PlotArea.Top + 6));
        var q2 = plot.PointOf(0, 1)!.Value;
        var top = plot.PointOf(1, 1)!.Value;
        var lower = Pixel(pixels, area, plot, new Point(q2.X + 20, (q2.Y + plot.PlotArea.Bottom) / 2));
        var upper = Pixel(pixels, area, plot, new Point(q2.X + 20, q2.Y - (q2.Y - top.Y) / 2));
        var above = Pixel(pixels, area, plot, new Point(q2.X + 20, top.Y - 14));
        Assert.True(Luminance(lower) != Luminance(upper) && Luminance(upper) != Luminance(above), $"three bands: {lower}, {upper}, {above}");
        Assert.True(Math.Abs(Luminance(above) - Luminance(ground)) < .01, $"nothing is drawn above the stack: {above} and {ground}");
        Assert.True(NthUiTokens.Contrast(lower, ground) > NthUiTokens.Contrast(upper, ground), "the lower band is the stronger");

        using var scatter = new NthUiRig(NthUiView.Show(NthUiSamples.Scatter, culture: NthUiRig.English), theme: theme);
        scatter.Settle();
        var dots = scatter.Chart().Plot!;
        var shot = scatter.Capture();
        var back = Pixel(shot, scatter, dots, new Point(dots.PlotArea.Left + 6, dots.PlotArea.Top + 6));
        var phone = dots.PointOf(0, 1)!.Value;
        var tablet = dots.PointOf(1, 1)!.Value;
        Assert.True(NthUiTokens.Contrast(Pixel(shot, scatter, dots, phone), back) >= 3, "a phone marker is drawn");
        Assert.True(NthUiTokens.Contrast(Pixel(shot, scatter, dots, tablet), back) >= 3, "a tablet marker is drawn");
        Assert.NotEqual(Pixel(shot, scatter, dots, phone), Pixel(shot, scatter, dots, tablet));
        var phoneCorner = Pixel(shot, scatter, dots, new Point(phone.X + 3.4, phone.Y + 3.4));
        var tabletCorner = Pixel(shot, scatter, dots, new Point(tablet.X + 3.4, tablet.Y + 3.4));
        Assert.True(NthUiTokens.Contrast(tabletCorner, back) > NthUiTokens.Contrast(phoneCorner, back), "a square marker reaches its corner where a circle does not");
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [MemberData(nameof(Themes))]
    public void Every_chart_sample_renders_and_is_saved_for_review(string name) => headless.OnUiThread(() =>
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "nthui-snapshots");
        Directory.CreateDirectory(folder);
        var samples = new (string, string)[]
        {
            ("chart-hosting", NthUiSamples.CanvasHosting), ("chart-editable", NthUiSamples.EditableChart), ("chart-pie", NthUiSamples.Pie),
            ("chart-scatter", NthUiSamples.Scatter), ("chart-area", NthUiSamples.Area), ("chart-mortgage", NthUiSamples.Mortgage),
        };
        foreach (var (label, text) in samples)
        {
            using var rig = new NthUiRig(NthUiView.Show(text, culture: NthUiRig.English), theme: Theme(name));
            rig.Settle();
            var roots = rig.View.Document.Roots.ToList();
            for (var i = 0; i < roots.Count; i++)
            {
                var png = rig.View.ExportPng(roots[i].Key, 2);
                Assert.NotNull(png);
                File.WriteAllBytes(Path.Combine(folder, $"{label}-{name.ToLowerInvariant()}-{i}.png"), png!);
            }
            Assert.NotEmpty(rig.Chart().Summary);
        }
    });
}
