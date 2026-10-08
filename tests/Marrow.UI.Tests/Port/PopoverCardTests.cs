using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0095 (owner ruling 2026-10-03): every popover the source presents as an NSPopover (SwiftUI .popover and the direct
// NSPopover sites) carries the Mac's arrow tip and sits at the Mac's distance from its button. SOURCE FACT, measured in
// the reference runtime frame by frame (study effort-title-2026-10-03/arrow): the model chip's card at 34.4 s, the
// diff panel's "..." menu at 22.2 s, the message menu at 24.3 s (all above their anchors, tip down) and a Hydra pair
// picker at 84.5 s of the settings tour (below its button, tip up): the tip stands 10-10.5 pt off the card's edge, its
// base is 27-29 pt wide where it meets the card, its sides run at 45 degrees (its outline narrows by 2 px a row,
// review/video_tips_x8_reviewer.png), its point is rounded (about 3 pt flat) and its shoulders blend into the edge; the
// point is 1-2 pt from the anchor (the card's edge 13 pt from it); the tip is centred on the anchor and part of the
// card's one outline. The card's corners and material stay
// with OD-0005; this draws the shape the tip needs, with ED-238's panel fill and edge.
[Collection("Headless")]
public sealed class PopoverCardTests(HeadlessSession headless)
{
    private static PopoverCard Card(PopoverTipEdge edge, double width = 200, double height = 100, double? offset = null)
    {
        var card = new PopoverCard
        {
            Child = new Border { Width = width, Height = height },
            TipEdge = edge,
            TipOffset = offset,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
        };
        card.Measure(Size.Infinity);
        card.Arrange(new Rect(card.DesiredSize));
        return card;
    }

    [Fact]
    [Trait("Decision", "ED-238")] // the popover panel, as ruled by OD-0095
    public void The_tip_has_the_Macs_size_and_stands_off_the_edge_facing_the_anchor() => headless.OnUiThread(() =>
    {
        Assert.InRange(PopoverCard.TipHeight, 10, 12);      // 10-10.5 pt seen past the rounding of the point
        var shoulder = PopoverCard.TipShoulderRadius * Math.Tan(Math.PI / 8); // where a shoulder's rounding leaves the edge
        Assert.InRange(2 * (PopoverCard.TipHalfWidth + shoulder), 27, 29); // 27-29 pt at the card's edge, shoulders included
        Assert.InRange(PopoverCard.TipHalfWidth / PopoverCard.TipHeight, 0.95, 1.05); // 45 degrees: 1 pt across per pt down
        Assert.InRange(PopoverCard.TipApexRadius, 2.5, 3.5); // about 3 pt flat at the point
        Assert.InRange(PopoverCard.TipGap, 0.5, 2);          // its point 1-2 pt from the anchor
        var below = Card(PopoverTipEdge.Bottom);
        Assert.Equal(new Size(202, 102 + PopoverCard.TipHeight + PopoverCard.TipGap), below.DesiredSize); // the 1 pt edge all round, then the tip
        var outline = below.Outline.Bounds;
        Assert.Equal(0.5, outline.Top, 1); // the 1 pt edge is stroked on the outline, half of it inside
        Assert.InRange(outline.Bottom, 102 + 9.5, 102 + PopoverCard.TipHeight); // the point, a rounding short of the sharp height (seen 10-10.5 pt)
        Assert.Equal(101, below.TipPoint.X, 1); // centred
        var right = Card(PopoverTipEdge.Left, offset: 40);
        Assert.Equal(new Size(202 + PopoverCard.TipHeight + PopoverCard.TipGap, 102), right.DesiredSize);
        Assert.Equal(40, right.TipPoint.Y, 1);
        Assert.Equal(PopoverCard.TipGap, right.TipPoint.X, 1);
    });

    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void The_tip_and_the_card_are_one_shape_with_no_seam_at_any_scale(double scale) => headless.OnUiThread(() =>
    {
        var card = Card(PopoverTipEdge.Bottom);
        var size = new PixelSize((int)Math.Ceiling(card.Bounds.Width * scale), (int)Math.Ceiling(card.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(card);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        using var decoded = SkiaSharp.SKBitmap.Decode(stream);
        SkiaSharp.SKColor At(double x, double y) => decoded.GetPixel((int)Math.Floor(x * scale), (int)Math.Floor(y * scale));
        var edge = 102.0; // the card's lower edge, in points
        var fill = At(101, 50);
        Assert.InRange((int)fill.Red, 0x1E - 2, 0x1E + 2); // the panel fill
        // Across the tip's base, the card's edge line does not run on: the tip's fill continues straight from the card's.
        foreach (var dy in new[] { -0.75, -0.25, 0.25, 0.75, 3, 6 })
        {
            var inside = At(101, edge + dy);
            Assert.True(Math.Abs(inside.Red - fill.Red) <= 3 && inside.Alpha >= 250, $"at {dy} pt by the tip's centre: {inside}");
        }
        // Beside the tip the edge is drawn, and past it only the card's faint black shadow (OD-0095: one shadow).
        Assert.True(At(40, edge - 0.5).Red > fill.Red + 8, $"the card's edge line away from the tip at {scale}: {At(40, edge - 0.5)}");
        foreach (var (x, y) in new[] { (40.0, edge + 4), (101 + PopoverCard.TipHalfWidth + 4, edge + 2) })
        {
            var past = At(x, y);
            Assert.True(past.Alpha <= PopoverCard.ShadowPeak * 255 + 2 && past.Red <= 2, $"past the outline at {x},{y}: {past}");
        }
    });

    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PopoverTipEdge.Bottom, 0, 0, 0, 50, PopoverTipEdge.Bottom)]   // the card above its anchor: tip down
    [InlineData(PopoverTipEdge.Bottom, 0, 200, 0, 50, PopoverTipEdge.Top)]   // flipped below it: tip up
    [InlineData(PopoverTipEdge.Left, 300, 0, 0, 0, PopoverTipEdge.Left)]     // to the anchor's right: tip left
    [InlineData(PopoverTipEdge.Left, -300, 0, 0, 0, PopoverTipEdge.Right)]   // flipped to its left: tip right
    [InlineData(PopoverTipEdge.Top, 0, 120, 0, 0, PopoverTipEdge.Top)]       // overlapping a little: still the axis it was placed on
    public void The_tip_faces_the_anchor_wherever_the_card_ended_up(PopoverTipEdge preferred, double cardX, double cardY, double anchorX, double anchorY, PopoverTipEdge expected)
    {
        var card = new Rect(cardX, cardY, 202, 113);
        var anchor = new Rect(anchorX + 80, anchorY + 130, 40, 20);
        if (preferred == PopoverTipEdge.Left) anchor = new Rect(anchorX + 250, anchorY + 40, 40, 20);
        var (edge, _) = PopoverCard.Aim(card, anchor, preferred);
        Assert.Equal(expected, edge);
    }

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void The_tip_points_at_the_anchors_centre_and_stays_on_the_straight_part_of_the_edge()
    {
        var card = new Rect(100, 0, 202, 113);
        Assert.Equal(60, PopoverCard.Aim(card, new Rect(140, 130, 40, 20), PopoverTipEdge.Bottom).Offset, 3); // the anchor's centre, 160, less the card's left
        var nearCorner = PopoverCard.Aim(card, new Rect(60, 130, 40, 20), PopoverTipEdge.Bottom).Offset;
        // Held off the corner: since ED-357 the corner is round (20 pt), so past it and the tip's half width and
        // shoulder (the square corner of OD-0095 let the tip come to TipHalfWidth + TipShoulderRadius).
        Assert.InRange(nearCorner, PopoverCard.BodyRadius + PopoverCard.TipHalfWidth, PopoverCard.BodyRadius + PopoverCard.TipHalfWidth + PopoverCard.TipShoulderRadius + 1);
    }

    [Fact]
    [Trait("Decision", "ED-107")] // PopoverAppear, as ruled by OD-0095
    public void The_card_grows_out_of_its_tip() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        // An offset on the straight part of the edge (since ED-357 the round 20 pt corner holds the tip at least
        // PopoverCard.TipLeast, 36 pt, from either end; OD-0095's square corner allowed the 30 this test used).
        var card = Card(PopoverTipEdge.Top, offset: 50);
        PopoverAppear.Play(card, PlacementMode.Bottom);
        var origin = card.RenderTransformOrigin;
        Assert.Equal(RelativeUnit.Relative, origin.Unit);
        Assert.Equal(50 / card.Bounds.Width, origin.Point.X, 3);
        Assert.Equal(PopoverCard.TipGap / card.Bounds.Height, origin.Point.Y, 3); // the tip's point, a point off the top
    });

    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PlacementMode.Top, PopoverTipEdge.Bottom)]
    [InlineData(PlacementMode.Bottom, PopoverTipEdge.Top)]
    [InlineData(PlacementMode.Right, PopoverTipEdge.Left)]
    [InlineData(PlacementMode.Left, PopoverTipEdge.Right)]
    public void A_presented_popover_is_drawn_on_a_card_with_its_tip_toward_the_anchor(PlacementMode placement, PopoverTipEdge edge) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var content = new Border { Width = 60, Height = 30 };
        var popup = new Popup { PlacementTarget = anchor, Placement = placement, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = content };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Same(content, card.Child);
            Assert.True(edge == card.TipEdge, $"tip {card.TipEdge}: card {card.TranslatePoint(default, window)} {card.Bounds.Size}, anchor {anchor.TranslatePoint(default, window)} {anchor.Bounds.Size}");
            popup.Child = new Border { Width = 50, Height = 20 }; // a site that swaps its content while it shows
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<PopoverCard>(popup.Child);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void Without_room_on_its_side_the_card_flips_and_its_tip_follows() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = Marrow.Core.Port.SwiftClock.Provider.Override(clock);
        var anchor = new Border { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = new Border { Width = 60, Height = 80 } };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); // no room above an anchor at the window's top
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs(); } // the grow-in done
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            var cardTop = card.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            var anchorBottom = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), window)!.Value.Y;
            Assert.True(cardTop >= anchorBottom - 0.5, $"flipped below: card top {cardTop}, anchor bottom {anchorBottom}");
            Assert.Equal(PopoverTipEdge.Top, card.TipEdge);
            var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
            Assert.True(Math.Abs(anchor.TranslatePoint(new Point(10, 0), window)!.Value.X - tip.X) < 0.5, $"tip {tip} offset {card.TipOffset}: card {card.TranslatePoint(default, window)} {card.Bounds.Size}, anchor {anchor.TranslatePoint(default, window)}"); // at the anchor's centre
            Assert.InRange(tip.Y - anchorBottom, 0, 2);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData("Dark")]
    [InlineData("Light")]
    public void The_card_wears_the_panel_and_its_top_level_is_clear_around_it(string name) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;
        application.RequestedThemeVariant = appearance;
        var anchor = new Border { Width = 20, Height = 20 };
        var scope = new ThemeVariantScope { RequestedThemeVariant = appearance, Child = anchor };
        var window = HeadlessPopupHost.OverlayWindow(scope);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = new Border { Width = 40, Height = 40 } };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.Child = null;
            using var root = HeadlessPopupHost.Open(popup, window, card);
            Dispatcher.UIThread.RunJobs();
            // ED-357: the card wears the glass's tint, hairline and rim, with the kit's panel (ED-238) as its fallback.
            Assert.Equal(PopoverHost.Fill.Resolve(appearance), Assert.IsAssignableFrom<ISolidColorBrush>(card.FallbackBrush).Color);
            Assert.Equal(PopoverHost.Tint.Resolve(appearance), Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);
            Assert.Equal(PopoverHost.Edge.Resolve(appearance), Assert.IsAssignableFrom<ISolidColorBrush>(card.BorderBrush).Color);
            Assert.Equal(PopoverHost.Rim.Resolve(appearance), Assert.IsAssignableFrom<ISolidColorBrush>(card.RimBrush).Color);
            Assert.Null(root.Background); // clear round the tip, and not hit-testable there (a Transparent fill is)
            Assert.Equal(new Thickness(0), root.BorderThickness);
            Assert.Contains(WindowTransparencyLevel.Transparent, root.TransparencyLevelHint);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
            application.RequestedThemeVariant = before;
            Dispatcher.UIThread.RunJobs();
        }
    });

    // OD-0095 gives the tip to "every popover the source presents with SwiftUI .popover (an NSPopover)"; a SwiftUI .sheet
    // (ChatView.swift:1456,1489, shown through ChatSheetPresentation) is not one, and a popup centred on its target faces no
    // edge of it. Neither hangs on a card: each keeps ED-238's panel on its own top-level.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(true, PlacementMode.Bottom)]
    [InlineData(false, PlacementMode.Center)]
    public void A_sheet_has_no_tip_and_its_top_level_wears_the_panel(bool sheet, PlacementMode placement) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 200, Height = 200 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var content = new Border { Width = 60, Height = 30 };
        var popup = new Popup { PlacementTarget = anchor, Placement = placement, ShouldUseOverlayLayer = true, Child = content };
        if (sheet) PopoverHost.SetIsSheet(popup, true);
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(content, popup.Child);
            Assert.Equal(default, PopoverHost.Room(popup));
            popup.Child = null;
            using var root = HeadlessPopupHost.Open(popup, window, content);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PopoverHost.Fill.Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color); // the panel, as before OD-0095
            Assert.Equal(new Thickness(1), root.BorderThickness);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // The card's first frame is drawn before any layout has told it where it ended up: its tip starts on the edge the
    // placement faces (above the anchor, the tip down), so the grow-in starts from the right place.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PlacementMode.Top, PopoverTipEdge.Bottom)]
    [InlineData(PlacementMode.TopEdgeAlignedLeft, PopoverTipEdge.Bottom)]
    [InlineData(PlacementMode.Bottom, PopoverTipEdge.Top)]
    [InlineData(PlacementMode.Right, PopoverTipEdge.Left)]
    [InlineData(PlacementMode.Left, PopoverTipEdge.Right)]
    public void Before_any_layout_the_tip_stands_on_the_edge_the_placement_faces(PlacementMode placement, PopoverTipEdge edge) => headless.OnUiThread(() =>
    {
        var popup = new Popup { PlacementTarget = new Border(), Placement = placement, Child = new Border { Width = 60, Height = 30 } };
        PopoverHost.Prepare(popup);
        Assert.Equal(edge, Assert.IsType<PopoverCard>(popup.Child).TipEdge);
        var room = PopoverHost.Room(popup);
        var shadow = PopoverCard.ShadowReach + PopoverCard.ShadowDrop; // the shadow's room, on the three sides away from the tip
        Assert.Equal(edge is PopoverTipEdge.Top or PopoverTipEdge.Bottom
            ? new Size(2 + 2 * shadow, 2 + PopoverCard.TipHeight + PopoverCard.TipGap + shadow)
            : new Size(2 + PopoverCard.TipHeight + PopoverCard.TipGap + shadow, 2 + 2 * shadow), room);
    });

    // A popup that closes and is given new content keeps it as the site set it until it is presented again (a site may
    // take it out and measure it in a window of its own in between: PromptEditor), and then hangs it on a new card.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_closed_popover_is_hung_on_a_new_card_only_when_it_opens_again() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var first = new Border { Width = 60, Height = 30 };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, ShouldUseOverlayLayer = true, Child = first };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            var second = new Border { Width = 50, Height = 20 };
            popup.Child = second;
            Assert.Same(second, popup.Child); // closed: left as the site set it
            popup.Child = null;
            Assert.Null(StyledParent(second)); // free to be measured in a window of its own
            popup.Child = second;
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var again = Assert.IsType<PopoverCard>(popup.Child);
            Assert.NotSame(card, again);
            Assert.Same(second, again.Child);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // PlainControls.Popover(buildBeforeOpen: false) reserves the popup with a placeholder and builds its content once the
    // popup has opened; the presenter has hung the placeholder on the card by then, and the built content takes its place.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void Content_built_once_the_popover_has_opened_hangs_on_the_card() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var state = new ShownFlag();
        var built = new Border { Width = 60, Height = 30 };
        var popup = PlainControls.Popover(anchor, new Binding<bool>(() => state.Value, value => state.Value = value), () => built, PlacementMode.Bottom, buildBeforeOpen: false);
        popup.ShouldUseOverlayLayer = true;
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { anchor, popup } });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            state.Value = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Same(built, card.Child);
            // On screen, not only the popup's Child: Avalonia 12.1.2 gives its host the child once, when the popup opens, so
            // the content a site sets while it is open has to be given to the host (PopoverHost.Follow).
            Assert.True(Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(card), "the card the content hangs on is not shown");
            Assert.True(Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(built), "the content built once the popup had opened is not shown");
            Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<PopoverCard>()); // and the placeholder's card is gone
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // OD-0095: the card and its tip cast one shadow (the panel has no edge in Light, "only the window manager's shadow").
    // It falls off with the distance from the outline, the same beside the tip as beside the card, in both appearances;
    // the presented card keeps room for it inside its popup on the three sides away from the tip, and on the tip's side
    // it fades to nothing at the popup's edge (1 pt past the point), never ending on a line.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_card_and_its_tip_cast_one_soft_shadow_inside_the_popup(string name) => headless.OnUiThread(() =>
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var card = new PopoverCard
        {
            Child = new Border { Width = 200, Height = 100 },
            TipEdge = PopoverTipEdge.Bottom,
            Background = new SolidColorBrush(PopoverHost.Fill.Resolve(appearance)),
            BorderBrush = new SolidColorBrush(PopoverHost.Border.Resolve(appearance)),
            ShadowRoom = true,
        };
        var host = new Border { Child = card }; // the popup's top-level, clear
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        // The room: the decided reach (10 pt) and drop (1 pt) of ED-238 round 2 (5), literal, not PopoverCard's own constants
        // (final review round 5: bounded by ShadowReach + ShadowDrop, a 6 pt reach, mutant R5-P4, cut the shadow on a line at
        // about 13 levels and passed).
        const double margin = 10 + 1;
        Assert.Equal(new Thickness(margin, margin, margin, 0), card.Margin);
        Assert.Equal(new Size(202 + 2 * margin, 102 + PopoverCard.TipHeight + PopoverCard.TipGap + margin), host.DesiredSize);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)host.Bounds.Width, (int)host.Bounds.Height));
        bitmap.Render(host);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        using var image = SkiaSharp.SKBitmap.Decode(stream);
        int Alpha(double x, double y) => image.GetPixel((int)(margin + x), (int)(margin + y)).Alpha;
        // Left of the card, halfway down, going out: darkest by the outline, gone by its reach.
        var left = Enumerable.Range(1, (int)margin).Select(d => Alpha(-d, 50)).ToList();
        // Against the decided numbers (ED-238 round 2 (5): 0.22 black at the outline, sigma 3.5), not PopoverCard.ShadowPeak
        // itself (final review round 4: bounded by the constant, a shadow twice as dark, mutant K8, passed): half a point out
        // 0.22 e^(-0.25 / 24.5) x 255 = 55.5, one sigma (3.5 pt) out 0.22 e^(-1/2) x 255 = 34.
        Assert.True(left[0] >= 45 && left[0] <= 0.22 * 255 + 2, $"by the outline: {left[0]} (decided 55.5)");
        Assert.InRange(left[3], 26, 42); // 3.5 pt out, decided 34
        for (var i = 1; i < left.Count; i++) Assert.True(left[i] <= left[i - 1], $"falls off: {string.Join(",", left)}");
        Assert.True(left[^1] <= 2, $"gone by its reach: {left[^1]}");
        // Out to the room's edge it falls off as the decided curve does, a point at a time (the pixel d points out spans d - 1
        // to d from the outline), never stopping on a line short of its reach: no step larger than the decided curve's over
        // that point, 3 levels for rounding.
        double Decided(double distance) => 0.22 * 255 * Math.Exp(-distance * distance / (2 * 3.5 * 3.5));
        for (var i = 1; i < left.Count; i++)
            Assert.True(left[i - 1] - left[i] <= Decided(i - 0.5) - Decided(i + 0.5) + 3, $"the shadow stops on a line {i} pt out: {string.Join(",", left)} (decided {string.Join(",", Enumerable.Range(0, left.Count).Select(d => Decided(d + 0.5).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))})");
        // Above the card the same (the drop makes it a little fainter), and beside the tip's side as beside the card.
        Assert.InRange(Alpha(100, -2), left[1] - 25, left[1] + 5);
        var tip = card.TipPoint;
        var besideTip = Alpha(tip.X + 6 + 2 * Math.Sqrt(2), 102 + 6); // 2 pt off the 45 degree side, 6 pt under the edge
        Assert.InRange(besideTip, left[1] - 20, left[1] + 20);
        // On the tip's side it has faded out by the popup's last row, under the point too.
        var lastRow = (int)host.Bounds.Height - 1;
        Assert.True(Enumerable.Range(0, image.Width).All(x => image.GetPixel(x, lastRow).Alpha <= 3), "the shadow is cut by the popup's edge under the tip");
        // And it gets there a row at a time, never ending on a hard line above the edge (final review round 4: read only on
        // the last row, a fade squeezed into a hundredth of a point, mutant K2, passed): 4, 6 and 8 pt either side of the point,
        // from 2 pt under the tip's side down to the last row, no step larger than the falloff's steepest (0.22 x 255 x e^-1/2
        // / 3.5 = 9.7 levels a point) and the 5 pt fade's (0.22 x 255 / 5 = 11.2) together, a level for rounding: 22.
        foreach (var beside in new[] { -8, -6, -4, 4, 6, 8 })
        {
            var columnX = (int)(margin + tip.X + beside);
            var top = (int)(margin + 102 + (PopoverCard.TipHeight - Math.Abs(beside)) + 2);
            var fade = Enumerable.Range(top, lastRow - top + 1).Select(y => (int)image.GetPixel(columnX, y).Alpha).ToList();
            for (var i = 1; i < fade.Count; i++) Assert.True(fade[i - 1] - fade[i] <= 22, $"the shadow {beside:+0;-0} pt beside the tip ends on a hard line: {string.Join(",", fade)}");
        }
    });

    // NSPopover.show(relativeTo: rect, of: view, ...) points its arrow at the rect, not at the view's middle: the composer's
    // suggestions at the caret (ComposerTextView.swift:81), a link's words (LinkPopover.swift:39). A popup given a
    // PlacementRect hangs its tip over that rect's middle.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_popover_shown_relative_to_a_rect_points_at_the_rect() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 300, Height = 60, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var caret = new Rect(220, 10, 2, 18); // near the anchor's right end
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.AnchorAndGravity, PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.Top,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.Top, PlacementRect = caret, ShouldUseOverlayLayer = true,
            Child = new Border { Width = 200, Height = 60 },
        };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Equal(PopoverTipEdge.Bottom, card.TipEdge);
            var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
            var target = anchor.TranslatePoint(caret.Center, window)!.Value;
            Assert.True(Math.Abs(tip.X - target.X) < 0.5, $"the tip at {tip.X}, the caret's middle at {target.X} (the anchor's at {anchor.TranslatePoint(new Point(150, 0), window)!.Value.X})");
            Assert.InRange(target.Y - caret.Height / 2 - tip.Y, 0, 2); // its point just over the caret's top
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // A card pushed over its anchor (a popup slid back on screen with no room on either side) keeps the edge it has: it
    // turns only once its body is wholly on the other side. Turning by the centres, the card's shadow room and tip room
    // moved with each turn, the centres crossed again, and the layout never settled ("Infinite layout loop detected",
    // found through the card-wrapping recording presenter: ComposerViewComponentsTests, MCPSettingsPageProofsMcpATests).
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PopoverTipEdge.Bottom)]
    [InlineData(PopoverTipEdge.Top)]
    [InlineData(PopoverTipEdge.Right)]
    public void A_card_over_its_anchor_settles_on_the_edge_it_has(PopoverTipEdge edge) => headless.OnUiThread(() =>
    {
        var anchor = new Border { Width = 40, Height = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var card = new PopoverCard { Child = new Border { Width = 120, Height = 50 }, TipEdge = edge, Anchor = anchor, ShadowRoom = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Width = 400, Height = 300, Content = new Panel { Children = { anchor, card } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 5; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(edge, card.TipEdge);
        }
        finally { window.Close(); }
    });

    // An anchor whose window is gone (closed, or never shown: a row a refresh rebuilt while its popover was open) leaves
    // the card as it is; following it must never throw out of a layout pass (found through the card-wrapping recording
    // presenter: MCPSettingsPageProofsMcpATests, "Visual does not belong to a visual tree" from PointToScreen).
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void An_anchor_out_of_any_shown_window_leaves_the_card_as_it_is() => headless.OnUiThread(() =>
    {
        var anchor = new Border { Width = 20, Height = 20 };
        var gone = new Window { Content = anchor };
        gone.Show();
        Dispatcher.UIThread.RunJobs();
        gone.Close(); // closed: still its top-level, but in no visual tree on screen
        var card = new PopoverCard { Child = new Border { Width = 60, Height = 30 }, TipEdge = PopoverTipEdge.Bottom, Anchor = anchor };
        var window = new Window { Width = 300, Height = 200, Content = card };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(PopoverTipEdge.Bottom, card.TipEdge);
            Assert.NotNull(TopLevel.GetTopLevel(anchor));
        }
        finally { window.Close(); }
    });

    // The clear surround beside the tip (and the shadow's room) is not the popover: the card takes a press only inside
    // its one outline, and a press anywhere else in the popover's top-level light-dismisses it, as a press outside an
    // NSPopover's panel does; a press on the card leaves it open, and a popover that is not light-dismissed stays.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(true)]
    [InlineData(false)]
    public void A_press_beside_the_tip_is_outside_the_popover(bool lightDismiss) => headless.OnUiThread(() =>
    {
        var manualFrames = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manualFrames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); manualFrames.Advance(TimeSpan.FromMilliseconds(ms)); }
        var anchor = new Border { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, IsLightDismissEnabled = lightDismiss, ShouldUseOverlayLayer = true, Child = new Border { Width = 120, Height = 60 } };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Step(300); // the pointer interacts with the settled panel
            window.UpdateLayout();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Equal(PopoverTipEdge.Bottom, card.TipEdge);
            var tip = card.TipPoint;
            var beside = new Point(tip.X + PopoverCard.TipHalfWidth + 10, tip.Y - 3); // past the card's body, clear of the tip
            var inside = new Point(tip.X, tip.Y - PopoverCard.TipHeight - 10);
            Assert.False(((Avalonia.Rendering.ICustomHitTest)card).HitTest(beside), "the card takes a press beside its tip");
            Assert.True(((Avalonia.Rendering.ICustomHitTest)card).HitTest(inside), "the card takes no press on its body");
            Assert.True(((Avalonia.Rendering.ICustomHitTest)card).HitTest(new Point(tip.X, tip.Y - 4)), "the card takes no press on its tip");
            var host = Assert.IsAssignableFrom<Control>(Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(card).First(ancestor => ancestor is PopupRoot or OverlayPopupHost));
            void Press(Avalonia.Interactivity.Interactive target, Point at)
            {
                var point = card.TranslatePoint(at, window)!.Value;
                var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
                var properties = new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed);
                target.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(target, pointer, window, point, 0, properties, Avalonia.Input.KeyModifiers.None));
                Dispatcher.UIThread.RunJobs();
            }
            Press(card.Child!, inside); // on the card: still open
            Assert.True(popup.IsOpen);
            Press(host, beside); // nothing of the card under it: the press reaches the top-level itself
            Assert.True(popup.IsOpen); // T45: dismissal preserves the visible close frames
            Step(80);
            if (lightDismiss) Assert.InRange(popup.Child!.Opacity, .1, .9);
            Step(100);
            Assert.Equal(!lightDismiss, popup.IsOpen);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // A presented card that flips (no room above its anchor) keeps its shadow's room on the three sides away from its
    // new tip, none on the tip's side, so the tip still ends 1 pt from the anchor.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_flipped_card_keeps_its_shadow_room_away_from_its_new_tip() => headless.OnUiThread(() =>
    {
        var side = PopoverCard.ShadowReach + PopoverCard.ShadowDrop;
        var card = new PopoverCard { TipEdge = PopoverTipEdge.Bottom, ShadowRoom = true };
        Assert.Equal(new Thickness(side, side, side, 0), card.Margin);
        card.TipEdge = PopoverTipEdge.Top;
        Assert.Equal(new Thickness(side, 0, side, side), card.Margin);
        card.TipEdge = PopoverTipEdge.Left;
        Assert.Equal(new Thickness(0, side, side, side), card.Margin);
        Assert.Equal(default, new PopoverCard { TipEdge = PopoverTipEdge.Top }.Margin); // a card drawn on its own keeps none
    });

    private static Avalonia.LogicalTree.ILogical? StyledParent(Control control) => ((Avalonia.LogicalTree.ILogical)control).LogicalParent;
}
