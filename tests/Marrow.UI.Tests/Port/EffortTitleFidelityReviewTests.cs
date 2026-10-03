using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (adversarial review of unit/effort-title-fidelity, 2026-10-03). Each test states what the reference runtime or
// the owner's ruling shows and fails on the unit's head.
[Collection("Headless")]
public sealed class EffortTitleFidelityReviewTests(HeadlessSession headless)
{
    private static PopoverCard Card(PopoverTipEdge edge, IBrush fill, IBrush? border)
    {
        var card = new PopoverCard { Child = new Border { Width = 200, Height = 100 }, TipEdge = edge, Background = fill, BorderBrush = border };
        card.Measure(Size.Infinity);
        card.Arrange(new Rect(card.DesiredSize));
        return card;
    }

    private static SkiaSharp.SKBitmap Render(Control control, Size size, double scale)
    {
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(control);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return SkiaSharp.SKBitmap.Decode(stream);
    }

    // SOURCE FACT, measured by the reviewer at full resolution (2-chat-use.mov 22.2 s, 24.3 s, 34.4 s; the tip's dark
    // outline, row by row from 3 to 10 px under the card's edge): the outline narrows by exactly 2 px a row, 1 px a side
    // for each px down, in all three popovers (and the tip-up picker at 1-settings-tour.mov 84.5 s): sides at 45 degrees.
    // The unit's tip (half base 15 over a sharp height of 12) runs about 1.25 across per pt down, 51 degrees, and its test
    // asserts the misread "1.4" (TipHalfWidth / TipHeight in 1.2-1.6).
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_The_tips_sides_run_at_the_videos_45_degrees() => headless.OnUiThread(() =>
    {
        const double scale = 4; // fine enough to read the straight part of the side
        var card = Card(PopoverTipEdge.Bottom, Brushes.White, null);
        using var image = Render(card, card.Bounds.Size, scale);
        var edge = 102.0; // the card's lower edge, in points
        double Width(double depth)
        {
            var y = (int)Math.Round((edge + depth) * scale);
            var inside = Enumerable.Range(0, image.Width).Where(x => image.GetPixel(x, y).Alpha > 128).ToList();
            return inside.Count == 0 ? 0 : (inside[^1] - inside[0] + 1) / scale;
        }
        var across = (Width(3) - Width(8)) / 2 / 5; // per side, for each pt down, on the straight part
        Assert.True(across is >= 0.85 and <= 1.15, $"the tip's sides run {across:F2} pt across for each pt down; the video's run 1.0 (45 degrees)");
    });

    // OD-0095: the tip is "part of the card's own shape and material (one outline, one fill, one shadow)". The card draws
    // a fill and an edge and no shadow at all, and the top-level round it is now clear, so nothing casts one: in Light
    // (the panel has no edge, "only the window manager's shadow") a white card and its tip have no boundary over a white
    // surface. NSPopover's panel casts a soft shadow that follows the arrow.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_The_card_and_its_tip_cast_one_shadow() => headless.OnUiThread(() =>
    {
        var light = ThemeVariant.Light;
        var card = Card(PopoverTipEdge.Bottom, new SolidColorBrush(PopoverHost.Fill.Resolve(light)), new SolidColorBrush(PopoverHost.Border.Resolve(light)));
        using var image = Render(card, card.Bounds.Size, 1);
        var edge = 102;
        // Just past the card's lower edge away from the tip, and just past the tip's side half way down.
        var belowEdge = image.GetPixel(40, edge + 1).Alpha;
        var besideTip = image.GetPixel((int)(101 + PopoverCard.TipHalfWidth / 2 + 3), edge + 5).Alpha;
        Assert.True(belowEdge > 0 && besideTip > 0, $"no shadow round the card ({belowEdge}) or its tip ({besideTip}): a Light popover has no boundary over a light window");
    });

    // A presented popover's top-level is now clear round the card (Background Transparent, so hit-testable), and the room
    // the tip takes is part of the popup: a click in the clear corners beside the tip (or in the point's 1 pt gap) lands
    // on the popover's own top-level instead of what is under it, and so neither reaches the window nor light-dismisses
    // the popover. On the Mac a click on the clear part of a popover's window goes to the window under it.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_The_clear_surround_beside_the_tip_takes_no_clicks() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = new Border { Width = 200, Height = 100 } };
        try
        {
            PopoverHost.Prepare(popup); // what the presenter does before it opens a popover
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.Child = null;
            using var root = HeadlessPopupHost.Open(popup, window, card);
            root.Measure(Size.Infinity);
            root.Arrange(new Rect(root.DesiredSize));
            Dispatcher.UIThread.RunJobs();
            // The clear corner beside the tip: past the card's body, well clear of the tip's side.
            var tip = card.TipPoint;
            var corner = new Point(tip.X < card.Bounds.Width / 2 ? tip.X + PopoverCard.TipHalfWidth + 10 : tip.X - PopoverCard.TipHalfWidth - 10, tip.Y - 3);
            Assert.False(card.Outline.FillContains(corner), $"the probe {corner} lies outside the card's outline {card.Outline.Bounds} ({card.TipEdge}, {card.Bounds})");
            // A headless PopupRoot cannot hit-test (no rendered scene), so the cause is read instead: Avalonia hit-tests a
            // Transparent background (it is drawn), and only a null one lets a press fall through. The real-window check
            // (a press beside the tip reaching the window and light-dismissing) is owed to the quiet-PC gate.
            var background = root.Background;
            Assert.True(background is null, $"the popover's top-level is hit-testable round its tip ({background}), so a click beside the tip is swallowed");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    private static MorphingTextBlock Title(string text, IBrush brush) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeight.Medium,
        Foreground = brush,
        MaxLines = 1,
    };

    // The letters a change keeps are now drawn from their own outlines (geometry), while the same letters at rest are drawn
    // by the text renderer; for a plain colour the two meet on the frame the change starts and the frame it ends. The
    // kept "202" must look the same on both sides of that line (no snap of weight or edge at 1, 1.25, 1.5, 1.75).
    [Theory]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111's morph or OD-0094's drift (ED-238), re-traited in final round 2
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void REVIEW_A_plain_title_keeps_its_drawn_letters_as_a_change_takes_them_over(double scale) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = Title("2024", Brushes.White);
        var host = new Border { Background = Brushes.Black, Child = label, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        var size = host.Bounds.Size;
        using var rest = Render(host, size, scale);
        label.SetText("2025", Animation.Snappy(0.2));
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        using var taken = Render(host, size, scale);
        // The kept "202": the label's left 60 %.
        var worst = 0;
        for (var y = 0; y < rest.Height; y++)
            for (var x = 0; x < (int)(rest.Width * 0.6); x++)
            {
                var a = rest.GetPixel(x, y);
                var b = taken.GetPixel(x, y);
                worst = Math.Max(worst, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
            }
        Assert.True(worst <= 24, $"at {scale}x the kept letters change by {worst} levels the moment the change takes them over");
    });

    // SOURCE FACT, measured by the reviewer every frame (2-chat-use.mov High -> Extra High at 34.84 s, the title at
    // 1540,1168): the kept "High"'s right edge and the chevron's left edge, px (about pt): 161/170 at rest (gap 9);
    // 34.865 169/170 (gap 1); 34.887 171/174 (3); 34.907 176/180 (4); 34.928 179/185 (6); 34.950 181/188 (7); settled
    // 183/191 (8). The kept letters run ahead of the label's frame: "High" has made 36 % of its way while the chevron
    // has not moved, and touches it. In the port the kept letters glide on exactly the frame's curve, so the gap never
    // changes (the builder's own sbs_inc_after.png, 34.865-34.907: video "High>" against the port's "High >").
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111's morph or OD-0094's drift (ED-238), re-traited in final round 2
    public void REVIEW_The_kept_letters_run_ahead_of_the_labels_frame_as_in_the_video() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = Title("High", Brushes.White);
        label.GrowthAnchor = 0.5;
        var chevron = new Border { Width = 6, Height = 12, Background = Brushes.Red, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { label, chevron } };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            int Gap()
            {
                var pixels = RenderedPixels.Capture(window);
                int? white = null, red = null;
                for (var x = 0; x < pixels.Width; x++)
                    for (var y = 0; y < pixels.Height; y++)
                    {
                        var (r, g, b) = pixels.At(x, y);
                        if (g > 120 && b > 120) white = x; // the rightmost white column: the "h"
                        if (red is null && r > 200 && g < 60) red = x;
                    }
                return (red ?? 0) - (white ?? 0);
            }
            var restGap = Gap();
            label.SetText("Extra High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            var least = restGap;
            for (var ms = 0; ms < 120; ms += 20)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                least = Math.Min(least, Gap());
            }
            Assert.True(restGap - least >= 4, $"the gap between the kept \"High\" and the chevron goes {restGap} -> at least {least}; the video closes it by 8 of 9 pt in the first frame");
        }
        finally { window.Close(); }
    });

    private static void Pump(ManualFrameSource frames, ManualTimeProvider clock, int count)
    {
        for (var i = 0; i < count; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(16));
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static LinearGradientBrush Pair() => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.OrangeRed, 0), new GradientStop(Colors.DeepPink, 0.5), new GradientStop(Colors.MediumPurple, 1) },
    };

    // OD-0094: the drift is "cheap (a shader or brush transform on the frame clock, no per-frame allocation, frames only
    // while the title is visible)".
    // RULING (final review, round 1, 2026-10-03) on what this test measures. Its first form (the label against the same
    // label held still, everything on this thread counted) charged the drift with two costs that are not the drift's:
    // (1) the renderer's frame, which the headless platform runs on the test thread (ForceRenderTimerTick ->
    // ServerCompositor.Render; on Windows the render thread's): about 4.6 KB a frame for ANY changed pixel, the same for a
    // stock TextBlock whose gradient moves and about 4.2 KB for a Border changing colour; (2) Avalonia's dispatch and
    // commit of a changed brush on the UI thread (MediaContext, Compositor.Commit, GradientBrush.SerializeChanges builds an
    // ImmutableGradientStop per stop per commit): about 2.2 KB a frame, which a brush transform on the frame clock (the
    // ruling's own example) pays as well. The round-2 cost test held the renderer still instead, and then Avalonia
    // throttles every commit after the first (MediaContext.CommitCompositorsWithThrottling: a batch the renderer never takes
    // stays pending), so it leaves out the commit the app pays every frame. Measured right: the renderer ticks every
    // frame (so every commit happens, as in the app), only its own tick is left out of the UI thread's count, and the
    // drift is set against stock Avalonia making the same visible change, a TextBlock with the same text and gradient whose
    // two ends the test moves each frame. The drift may add at most 256 B a frame to that floor on the UI thread (measured
    // over 600 frames on 2026-10-03: drifting 2.25-2.32 KB, the floor 2.21-2.29 KB), cost the renderer no more than the
    // floor does (4.57-4.65 KB against 4.58-4.82 KB), and never draw the label again while its colours move.
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111's morph or OD-0094's drift (ED-238), re-traited in final round 2
    public void REVIEW_The_drift_allocates_nothing_frame_by_frame() => headless.OnUiThread(() =>
    {
        const int Frames = 600;
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        (long Ui, long Renderer) PerFrame(Control content, Action? each, Action? measuring = null)
        {
            var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = content };
            window.Show();
            try
            {
                long ui = 0, renderer = 0;
                void Step(bool count)
                {
                    var a = GC.GetAllocatedBytesForCurrentThread();
                    each?.Invoke();
                    clock.Advance(TimeSpan.FromMilliseconds(16));
                    frames.Advance(TimeSpan.FromMilliseconds(16));
                    Dispatcher.UIThread.RunJobs();
                    var b = GC.GetAllocatedBytesForCurrentThread();
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the renderer's frame (the render thread's on Windows)
                    var c = GC.GetAllocatedBytesForCurrentThread();
                    Dispatcher.UIThread.RunJobs();
                    var d = GC.GetAllocatedBytesForCurrentThread();
                    if (count) { ui += b - a + (d - c); renderer += c - b; }
                }
                for (var i = 0; i < 30; i++) Step(false);
                measuring?.Invoke();
                for (var i = 0; i < Frames; i++) Step(true);
                return (ui / Frames, renderer / Frames);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        CountedMorphingTextBlock Drifting() => new() { Text = "Extra High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Pair(), MaxLines = 1, DriftPeriod = 7 };
        (long Ui, long Renderer) Floor()
        {
            var brush = Pair();
            var moved = 0;
            var stock = new TextBlock { Text = "Extra High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = brush, MaxLines = 1 };
            return PerFrame(stock, () =>
            {
                moved = (moved + 1) % 160;
                brush.StartPoint = new RelativePoint(moved, 0, RelativeUnit.Absolute);
                brush.EndPoint = new RelativePoint(moved + 80, 0, RelativeUnit.Absolute);
            });
        }
        PerFrame(Drifting(), null); // warm both paths
        Floor();
        var title = Drifting();
        var drawn = -1;
        var phase = 0.0;
        var drift = PerFrame(title, null, () => { drawn = title.Renders; phase = title.DriftPhase; });
        var floor = Floor();
        Assert.True(title.DriftPhase != phase, "the colours did not move");
        Assert.True(drawn == title.Renders, $"the label was drawn {title.Renders - drawn} times again over {Frames} frames of drift");
        Assert.True(drift.Ui - floor.Ui <= 256, $"the drift allocates {drift.Ui - floor.Ui} bytes a frame on the UI thread beyond stock Avalonia moving the same gradient ({drift.Ui} against {floor.Ui})");
        Assert.True(drift.Renderer - floor.Renderer <= 512, $"the drift costs the renderer {drift.Renderer - floor.Renderer} bytes a frame beyond stock Avalonia moving the same gradient ({drift.Renderer} against {floor.Renderer})");
    });

    // Owner bar "zero stutter" (and OD-0094's "cheap" for the colours passing along the letters): a gradient title's
    // change now builds a new LinearGradientBrush and GradientStops for every letter, for each of its seventeen blur
    // copies, every frame (Pinned); measured about 430 KB a frame for "Extra High" -> "Max" against about 40 KB for the
    // same change in a plain colour, some 12 MB of garbage per change on the UI thread.
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111's morph or OD-0094's drift (ED-238), re-traited in final round 2
    public void REVIEW_A_gradient_change_costs_about_what_a_plain_one_does() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        long PerFrame(IBrush brush)
        {
            var label = Title("Extra High", brush);
            var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = label };
            window.Show();
            try
            {
                Pump(frames, clock, 10);
                var before = GC.GetAllocatedBytesForCurrentThread();
                label.SetText("Max", Animation.Snappy(0.2));
                var count = 0;
                while (label.IsMorphing && count < 100) { Pump(frames, clock, 1); count++; }
                return (GC.GetAllocatedBytesForCurrentThread() - before) / Math.Max(1, count);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        var plain = PerFrame(Brushes.White);
        var gradient = PerFrame(Pair());
        Assert.True(gradient <= 2 * plain, $"a gradient change allocates {gradient} bytes a frame, a plain one {plain}");
    });

    // OD-0095: "a small tip centred on the anchor". A site that shows its Popup again while it is still open (the presenter
    // keeps a closing popover open for the 0.17 s it shrinks into its tip, PopoverAppear.Dismiss; AttachmentPreviewPanel
    // re-shows in place; DiffPopoverCoordinator.Reopen hides and shows at once) sets its new content first and its new
    // anchor after: SetFixedContent, then PlacementTarget, then SetPresented (AttachmentPreviewPanel.cs:91-95,
    // DiffInspector.cs:1072-1079, HydraHeadHoverPopover.cs:120-128, ChatBadge.cs:245-258). Avalonia 12.1.2 hosts a Popup's
    // child once, when it opens (Popup.Open: popupHost.SetChild(Child); ChildChanged only re-parents it logically), so the
    // card on screen stays the first one (that the new content is never shown is older than this unit), and Avalonia moves
    // the popup to the new anchor (Popup.OnPropertyChanged, PlacementTarget). The card on screen aims its tip at the anchor
    // it was hung with (PopoverHost.Wrap reads PlacementTarget once into PopoverCard.Anchor), not at the one the popup now
    // hangs from. NSPopover.show(relativeTo:of:preferredEdge:) aims the arrow at the view it is shown from.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_A_popover_shown_again_from_another_anchor_aims_its_tip_at_that_anchor() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var first = new Border { Width = 20, Height = 20 };
        var second = new Border { Width = 20, Height = 20 };
        Canvas.SetLeft(first, 40);
        Canvas.SetTop(first, 220);
        Canvas.SetLeft(second, 300);
        Canvas.SetTop(second, 220);
        var window = HeadlessPopupHost.OverlayWindow(new Canvas { Children = { first, second } });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = first, Placement = PlacementMode.Top, ShouldUseOverlayLayer = true, Child = new Border { Width = 120, Height = 60 } };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            // The site's show while it is still shown: its new content, then its new anchor, then presented.
            popup.Child = new Border { Width = 120, Height = 60 };
            popup.PlacementTarget = second;
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var card = Assert.Single(window.GetVisualDescendants().OfType<PopoverCard>()); // the card on screen
            var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
            var middle = second.TranslatePoint(new Point(10, 10), window)!.Value;
            var old = first.TranslatePoint(new Point(10, 10), window)!.Value;
            Assert.True(Math.Abs(tip.X - middle.X) < 0.5, $"the tip at x {tip.X:F1}; the anchor it is shown from has its middle at {middle.X:F1}, the one it was shown from before at {old.X:F1}");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });
}
