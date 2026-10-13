using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Settings;

// The Settings records whose only open clause was the corner (ED-181, closed by ED-275): the continuous curve on the rendered element.
// Each test mounts the production control, renders it through Avalonia's renderer at 24 x (RenderedCorner) and reads the opacity at the
// points between Apple's curve and the circle, so the test fails if the element drew a circular corner (the negative controls below
// run the same reading on a circular twin and on the same element with its cut removed, and it fails there).
// Source: SettingsView.swift:148-164 (rows), UI/Chrome/Chrome.swift:681-757 (SidebarRow: RoundedRectangle(cornerRadius: 7, style: .continuous));
// UI/Chrome/ChromeVisualPicker.swift:86-105 (stage clipShape r 6 .continuous, ring r 6 + 3 .continuous strokeBorder 2).
[Collection("Headless")]
public sealed class SettingsLeftoverCornerTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static Border Stage(Control tile) => Assert.Single(Nodes<Border>(tile), b => b.Width == 76 && b.Height == 48);
    private static SwiftShapeBorder Ring(Control tile) => Assert.Single(Nodes<SwiftShapeBorder>(tile), b => b.BorderThickness == new Thickness(2));

    // A circular twin of a shape, laid out at the size the real one has: the reading must fail on it.
    private static T Twin<T>(T twin, Size size) where T : Control
    {
        twin.Measure(size); twin.Arrange(new Rect(size));
        return twin;
    }

    private static double Alpha(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color.A / 255.0;

    // The reading fails, and (unless said otherwise) because the circle's sliver is drawn, not for some other reason.
    private static void Throws(RenderedCorner corner, CornerRadius radii, double full, string because = "a circular corner") =>
        Assert.Contains(because, Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => corner.AssertCorners(radii, expectApple: true, full)).Message, StringComparison.Ordinal);

    // ---- UI-SETTINGS-004: the sidebar row, 28 pt high, r 7 .continuous (Chrome.swift:722) ------------------------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-004")] // App/Views/Settings/SettingsView.swift:148-164; UI/Chrome/Chrome.swift:722,739-745 (RoundedRectangle(cornerRadius: 7, style: .continuous).fill(fill)); ED-181, ED-275 (2): 28 >= 2 * 1.528665 * 7 = 21.4, Apple's corner
    public void Every_sidebar_row_wash_draws_Apple_s_r7_corner_on_its_pixels_selected_and_hovered_and_not_the_circle()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            Tick(h, 150);
            var selected = Assert.IsType<Border>(Assert.Single(Nodes<Button>(Nodes<SidebarRow>(view).First())).Content); // General, selected on opening
            var hovered = RowButton(view, "Models"); Reveal(w, h, hovered); /* ED-NEW-final-settings-1: Models lies below the 580 pt window's fold, so the sidebar is scrolled to it first */ Hover(w, hovered); Tick(h, 150); var wash = Assert.IsType<Border>(hovered.Content);
            foreach (var (name, shape, alpha) in new[] { ("selected, overlay 0.12", selected, 0.12), ("hovering, overlay 0.06", wash, 0.06) })
            {
                Assert.Equal(new CornerRadius(7), shape.CornerRadius); Assert.Equal(Chrome.SettingsNavRowHeight, shape.Bounds.Height, 3);
                Assert.True(ContinuousCornerChecks.Drawn(shape, expectApple: true), name);
                using var drawn = RenderedCorner.Of(shape);
                var read = drawn.AssertCorners(shape.CornerRadius, expectApple: true, Alpha(shape.Background));
                Assert.True(read.Length >= 16, $"{read.Length} points read"); // four corners, both edges of each, at several places along them
                Assert.All(read, r => Assert.Equal(0, r.Sliver, 0.01)); // the circle's sliver is not drawn ...
                Assert.Equal(alpha, Alpha(shape.Background), 0.02); // the wash is the theme's overlay at 0.12 / 0.06 ...
                Assert.All(read, r => Assert.InRange(r.Solid, 0.6 * Alpha(shape.Background), 1)); // ... and the points just inside Apple's curve are drawn at it
            }
            // The reading is two-sided: the row's Apple corner fails the circular reading (so the capsule's circular arcs below cannot pass as Apple's).
            using (var apple = RenderedCorner.Of(selected)) Assert.Contains("not the circular regime", Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => apple.AssertCorners(selected.CornerRadius, expectApple: false, Alpha(selected.Background))).Message, StringComparison.Ordinal);
            // The negative controls: a circular twin of the row's size draws the sliver, and so does the real wash with its cut removed.
            var circularTwin = Twin(new Border { Width = 192, Height = Chrome.SettingsNavRowHeight, Background = selected.Background }.RoundedRectangle(7, RoundedCornerStyle.Circular), new Size(192, Chrome.SettingsNavRowHeight));
            using var twin = RenderedCorner.Of(circularTwin); Throws(twin, circularTwin.CornerRadius, Alpha(selected.Background));
            Assert.All(twin.AssertCorners(circularTwin.CornerRadius, expectApple: false, Alpha(selected.Background)), r => Assert.Equal(r.Solid, r.Sliver, 0.01));
            var cut = selected.Clip; selected.Clip = null;
            try { using var uncut = RenderedCorner.Of(selected); Throws(uncut, selected.CornerRadius, Alpha(selected.Background)); } finally { selected.Clip = cut; }
        });
    }

    // ---- UI-SETTINGS-014, 015 and 065: the choice tiles, stage r 6 and ring r 6 + 3 (ChromeVisualPicker.swift:86-105) -------------------

    private static void TilesDrawApple(Control row, string picker, int expectedTiles)
    {
        var tiles = Nodes<ChromeVisualTile>(row);
        Assert.Equal(expectedTiles, tiles.Length);
        var selectedRings = 0;
        foreach (var tile in tiles)
        {
            var stage = Stage(tile); var ring = Ring(tile); var name = picker + " / " + Avalonia.Automation.AutomationProperties.GetName(tile);
            // :92-93 the stage: 76 x 48, r 6, .continuous: 2 * 1.528665 * 6 = 18.3 <= 48, Apple's corner.
            Assert.Equal(new CornerRadius(6), stage.CornerRadius); Assert.Equal(76, stage.Bounds.Width, 3); Assert.Equal(48, stage.Bounds.Height, 3);
            Assert.True(ContinuousCornerChecks.Drawn(stage, expectApple: true), name + " stage");
            using (var drawn = RenderedCorner.Of(stage))
            {
                var read = drawn.AssertCorners(stage.CornerRadius, expectApple: true, Alpha(stage.Background));
                Assert.All(read, r => Assert.Equal(0, r.Sliver, 0.01));
            }
            // :96-102 the ring: r 6 + 3 = 9 on the 82 x 54 padded stage, strokeBorder 2, .continuous (27.5 <= 54): its outer edge is Apple's corner.
            Assert.Equal(new CornerRadius(9), ring.CornerRadius); Assert.Equal(82, ring.Bounds.Width, 3); Assert.Equal(54, ring.Bounds.Height, 3);
            Assert.True(ContinuousCornerChecks.Drawn(ring, expectApple: true), name + " ring");
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush);
            if (!tile.IsSelected) { Assert.Equal(0, brush.Color.A); continue; } // :101 the unselected ring is Color.clear: nothing to draw
            selectedRings++; Assert.True(brush.Color.A > 0, name);
            using var rendered = RenderedCorner.Of(ring);
            var ringRead = rendered.AssertCorners(ring.CornerRadius, expectApple: true, brush.Color.A / 255.0);
            Assert.True(ringRead.Length >= 16, $"{ringRead.Length} points read");
            Assert.All(ringRead, r => Assert.Equal(0, r.Sliver, 0.01)); // the stroke's outer edge leaves the circle's sliver bare
            
        }
        Assert.Equal(1, selectedRings);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-014")] // App/Views/Settings/SettingsView.swift:327-331; UI/Chrome/ChromeVisualPicker.swift:86-105; ED-275 (3) (the ring is a SwiftShapeBorder, the stage a clipped Border)
    [Trait("Req", "UI-SETTINGS-015")] // App/Views/Settings/SettingsView.swift:344-348; the same tiles
    public void The_Workspace_and_Working_line_tiles_draw_Apple_s_r6_stage_and_r9_ring_on_their_pixels()
    {
        With(() => new GeneralSettingsPage(), (view, _, h, w) =>
        {
            Tick(h, 150);
            TilesDrawApple(Row(view, "Workspace"), "Workspace", 2);
            TilesDrawApple(Row(view, "Working line"), "Working line", 2);
            // The second tile selected: the ring moves to it, and it is Apple's corner there too (not only the tile the page opens on).
            foreach (var title in new[] { "Workspace", "Working line" })
            {
                Click(w, Assert.Single(Nodes<Button>(Nodes<ChromeVisualTile>(Row(view, title))[1]))); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150);
                TilesDrawApple(Row(view, title), title + " (second tile)", 2);
                Assert.True(Nodes<ChromeVisualTile>(Row(view, title))[1].IsSelected);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-065")] // SettingsView.swift:395-399,432-442; HydraSettingsPage.swift:50-59,76-89; ChromeVisualPicker.swift:86-105
    public void The_six_other_visual_pickers_draw_Apple_s_r6_stage_and_r9_ring_on_their_pixels()
    {
        With(() => new GeneralSettingsPage(), (view, _, h, _) => { Tick(h, 150); TilesDrawApple(Row(view, "Done with a thread"), "Done with a thread", 2); });
        With(() => new AppearanceSettingsPage(), (view, _, h, _) => { Tick(h, 150); TilesDrawApple(Row(view, "Sidebar"), "Sidebar", 3); TilesDrawApple(Row(view, "Activity list"), "Activity list", 2); });
        With(() => new HydraSettingsPage(), (view, _, h, _) =>
        {
            Tick(h, 150);
            TilesDrawApple(Row(view, "Checkout"), "Checkout", 2); TilesDrawApple(Row(view, "Head panel"), "Head panel", 2); TilesDrawApple(Row(view, "Heads' panels"), "Heads' panels", 2);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-014")] // the negative controls of the tile readings: the reading fails on a circular r 6 stage and a circular r 9 ring, and on the real ones with their cut removed
    [Trait("Req", "UI-SETTINGS-065")]
    public void The_tile_reading_fails_on_a_circular_stage_a_circular_ring_and_on_the_real_ones_without_their_cut()
    {
        With(() => new GeneralSettingsPage(), (view, _, h, _) =>
        {
            Tick(h, 150);
            var tile = Nodes<ChromeVisualTile>(Row(view, "Workspace")).Single(t => t.IsSelected); var stage = Stage(tile); var ring = Ring(tile);
            var circularStage = Twin(new Border { Width = 76, Height = 48, Background = stage.Background }.RoundedRectangle(6, RoundedCornerStyle.Circular), new Size(76, 48));
            using (var a = RenderedCorner.Of(circularStage)) Throws(a, circularStage.CornerRadius, Alpha(stage.Background));
            // The ring as the deferral drew it: circular. A circular r 9 shape filled in the accent draws the sliver (the reading fails on it for that reason);
            // a stroked circular one (a plain Border, or the SwiftShapeBorder that draws what a Border draws in the circular regime) centres its 2 pt stroke on the
            // inset rect, so its outer edge is an r 10 arc that cuts Apple's r 9 run-in and fails the reading just inside the curve instead.
            var circularRing = Twin(new SwiftShapeBorder { Width = 82, Height = 54, Background = ring.BorderBrush }.RoundedRectangle(9, RoundedCornerStyle.Circular), new Size(82, 54));
            using (var b = RenderedCorner.Of(circularRing)) Throws(b, circularRing.CornerRadius, Alpha(ring.BorderBrush));
            var strokedBorder = Twin(new Border { Width = 82, Height = 54, BorderThickness = new Thickness(2), BorderBrush = ring.BorderBrush, CornerRadius = new CornerRadius(9) }, new Size(82, 54));
            using (var d = RenderedCorner.Of(strokedBorder)) Throws(d, strokedBorder.CornerRadius, Alpha(ring.BorderBrush), because: "just inside Apple's curve");
            var strokedShape = Twin(new SwiftShapeBorder { Width = 82, Height = 54, BorderThickness = new Thickness(2), BorderBrush = ring.BorderBrush }.RoundedRectangle(9, RoundedCornerStyle.Circular), new Size(82, 54));
            using (var e = RenderedCorner.Of(strokedShape)) Throws(e, strokedShape.CornerRadius, Alpha(ring.BorderBrush), because: "just inside Apple's curve");
            var cut = stage.Clip; Assert.NotNull(cut); stage.Clip = null;
            try { using var c = RenderedCorner.Of(stage); Throws(c, stage.CornerRadius, Alpha(stage.Background)); } finally { stage.Clip = cut; }
        });
    }

    // ---- UI-SETTINGS-017: the Theme capsule, 28 pt high, Capsule(style: .continuous) (SettingsView.swift:1166-1185) ----------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-017")] // App/Views/Settings/SettingsView.swift:1182-1190,1195 (.frame(height: Chrome.capsuleContentHeight), .contentShape(Capsule(style: .continuous)), .chromeGlassCapsule()); UI/Chrome/Chrome.swift:126-131,164-165; ED-275 (2): 28 < 2 * 1.528665 * 14 = 42.8, so the capsule's ends are circular arcs
    public void The_Theme_capsule_draws_the_circular_regime_on_its_pixels_its_glass_and_its_hit_shape_agree_and_the_page_no_longer_mounts_it()
    {
        With(ThemePickerHost.Page, (view, _, h, _) =>
        {
            Tick(h, 150);
            var picker = Assert.Single(Nodes<ThemePickerButton>(view)); Assert.Equal(28, picker.Bounds.Height, 3);
            // The material: Capsule(style: .continuous) glass (Chrome.swift:164-165, :180), on the fill behind the label.
            var fill = Assert.Single(Nodes<Border>(picker), b => SwiftGlass.GetEffect(b) is not null);
            var effect = SwiftGlass.GetEffect(fill)!; Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);
            // ED-275 (2): every corner of a 28 pt capsule is the circular arc (a capsule never reaches Apple's run-in), and the Border that fills it says so.
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(fill)); Assert.Equal(new CornerRadius(14), fill.CornerRadius);
            var outline = SwiftShapes.Outline(new Rect(picker.Bounds.Size), fill.CornerRadius, RoundedCornerStyle.Continuous);
            Assert.All(outline.Corners, k => Assert.Equal(CornerKind.Arc, k)); Assert.False(SwiftShapes.HasContinuousCorner(picker.Bounds.Size, fill.CornerRadius, RoundedCornerStyle.Continuous));
            Assert.False(ContinuousCornerChecks.Drawn(fill, expectApple: false)); // the circular regime: no Apple corner, and no clip on a plain Border
            // :1185 .contentShape(Capsule(style: .continuous)): the button's hit shape is the same capsule.
            var hit = Assert.Single(picker.GetVisualDescendants().OfType<Border>(), b => b != fill && SwiftShapes.GetCornerStyle(b) == RoundedCornerStyle.Continuous); // the button template's Border
            Assert.Equal(new CornerRadius(14), hit.CornerRadius); Assert.False(ContinuousCornerChecks.Drawn(hit, expectApple: false));
            // On the pixels: the fill (the source's own tint, Chrome.glassTint at 0.3, under the glass) draws the circular arc to the edge, the sliver Apple's
            // curve would cut off is drawn.
            var tint = Alpha(fill.Background); Assert.Equal(0.3, tint, 0.02);
            using var drawn = RenderedCorner.Of(fill);
            var read = drawn.AssertCorners(fill.CornerRadius, expectApple: false, tint);
            Assert.True(read.Length >= 16, $"{read.Length} points read");
            // The same reading with Apple's corner would fail here: the reading above asked for the circular arc, and the Apple reading is the one that throws.
            Throws(drawn, fill.CornerRadius, tint);
        }, nativePopup: true, height: 1500);
        // OD-0088 (ED-250): the Appearance page the user sees holds the Look card and no capsule, so the capsule is mounted in no screen: OD-0076's width ease
        // and label crossfade on a theme change belong to the Look card's own Theme row (its segment label, SegmentButton.SetLabel), not to this control.
        With(() => new AppearanceSettingsPage(), (view, _, _, _) =>
        {
            Assert.Empty(Nodes<ThemePickerButton>(view)); Assert.Single(Nodes<Marrow.App.Port.LookCard>(view));
        });
    }
}
