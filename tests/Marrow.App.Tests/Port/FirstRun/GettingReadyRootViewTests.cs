using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the sheet in RootView. Look I: Welcome waits behind, blurred and dimmed; the sheet hangs from the top of the detail pane;
// at the end it lifts away while the pane comes back to focus, in one hand-over (the transition into Welcome, OD-0076).
// RootView.cs lines :14-51 (the HStack, the detail column) are the source of the pane the sheet is over; the look is OD-0096's.
[Collection("Headless")]
public sealed class GettingReadyRootViewTests(HeadlessSession headless)
{
    private const double Frame = 1 / GettingReadyRig.Hz;

    private static void Mounted(HeadlessSession headless, Action<GettingReadyRootRig> body, bool reduceMotion = false, bool show = true) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            using var rig = new GettingReadyRootRig(model, reduceMotion, show);
            body(rig);
        });
    });

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void Welcome_waits_behind_the_sheet_blurred_dimmed_and_still() => Mounted(headless, rig =>
    {
        Assert.Same(rig.Sheet, rig.Root.GettingReady);
        Assert.Contains(rig.Root.DetailPane.GetVisualDescendants(), visual => visual is WelcomeView); // no projects: the detail pane is WelcomeView
        Assert.Contains(rig.Sheet, rig.Root.GetVisualDescendants()); // the sheet is in the window
        Assert.Equal(GettingReadyLook.BlurSigma, rig.PaneBlurSigma, 2);
        Assert.Equal(GettingReadyLook.PaneOpacityRest, rig.Root.DetailPane.Opacity, 6);
        Assert.False(rig.Root.DetailPane.IsHitTestVisible); // nothing in the waiting Welcome can be pressed until the sheet has gone
        var pane = rig.Root.DetailPane.TranslatePoint(new Point(0, 0), rig.Window)!.Value;
        var sheet = rig.Sheet.TranslatePoint(new Point(0, 0), rig.Window)!.Value;
        Assert.Equal(0, sheet.Y, 3); // hangs from the top edge of the window
        Assert.Equal(pane.X + (rig.Root.DetailPane.Bounds.Width - GettingReadyLook.SheetWidth) / 2, sheet.X, 2); // centred on the detail pane, not the window
        var slot = Assert.IsAssignableFrom<Panel>(rig.Sheet.GetVisualParent());
        Assert.Contains(rig.Root.DetailPane, slot.Children); // the sheet and the pane share a slot,
        Assert.True(slot.Children.IndexOf(rig.Sheet) > slot.Children.IndexOf(rig.Root.DetailPane)); // the sheet drawn above it
    });

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void A_window_with_no_sheet_is_untouched() => Mounted(headless, rig =>
    {
        Assert.Null(rig.Root.GettingReady);
        Assert.Null(rig.Root.DetailPane.Effect);
        Assert.Equal(1, rig.Root.DetailPane.Opacity);
        Assert.True(rig.Root.DetailPane.IsHitTestVisible);
    }, show: false);

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void The_pane_comes_back_along_the_fixtures_frames_as_the_sheet_lifts() => Mounted(headless, rig =>
    {
        rig.AllDone();
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var at = 0.0;
        var frames = GettingReadyFixture.HandFrames();
        for (var index = 0; index < frames.Count - 1; index++) // the last instant is the end: the sheet has been taken out of the tree
        {
            var instant = GettingReadyLook.Hold + GettingReadyLook.HandOverSeconds * index / 20;
            rig.Frame(instant - at);
            at = instant;
            var paneStyle = frames[index].GetProperty("paneStyle");
            var blur = GettingReadyFixture.BlurPx(paneStyle.GetProperty("filter").GetString()!);
            var opacity = double.Parse(paneStyle.GetProperty("opacity").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(rig.PaneBlurSigma - blur) <= (blur < 0.5 ? 0.5 : 0.1), $"{index}/20: pane blur {rig.PaneBlurSigma:0.000}, fixture {blur:0.000}");
            Assert.True(Math.Abs(rig.Root.DetailPane.Opacity - opacity) <= 0.005, $"{index}/20: pane opacity {rig.Root.DetailPane.Opacity:0.000}, fixture {opacity:0.000}");
        }
    });

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void At_60_Hz_the_pane_never_jumps_and_the_sheet_leaves_the_tree_only_once_it_is_out_of_sight_and_the_pane_whole() => Mounted(headless, rig =>
    {
        rig.AllDone();
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var sigma = new List<double> { rig.PaneBlurSigma };
        var opacity = new List<double> { rig.Root.DetailPane.Opacity };
        var times = new List<double> { 0 };
        double? removed = null;
        double handWhenRemoved = 0;
        double lastBlurred = 0;
        rig.Run(1.6, time =>
        {
            times.Add(time);
            sigma.Add(rig.PaneBlurSigma);
            opacity.Add(rig.Root.DetailPane.Opacity);
            if (rig.PaneBlurSigma > 0) lastBlurred = rig.PaneBlurSigma;
            if (removed is null && !rig.Root.GetVisualDescendants().Contains(rig.Sheet)) { removed = time; handWhenRemoved = rig.Sheet.Hand; }
        });
        for (var index = 1; index < sigma.Count; index++)
        {
            Assert.True(sigma[index] <= sigma[index - 1] + 1e-9, $"the blur came back at {times[index]:0.000}");
            Assert.True(opacity[index] >= opacity[index - 1] - 1e-9, $"the pane dimmed again at {times[index]:0.000}");
            // the steepest 60 Hz step of the page's curve is 0.73 px; the frame that takes the effect off steps from what is left of it (under 1 px of blur)
            var taken = sigma[index] == 0 && sigma[index - 1] > 0;
            Assert.True(sigma[index - 1] - sigma[index] <= (taken ? 1.0 : 0.8), $"the blur jumped {sigma[index - 1] - sigma[index]:0.00} px at {times[index]:0.000}");
            Assert.True(opacity[index] - opacity[index - 1] <= 0.05, $"the pane brightened {opacity[index] - opacity[index - 1]:0.000} at {times[index]:0.000}");
        }
        Assert.True(lastBlurred <= 1.3, $"the blur was still {lastBlurred:0.00} px on the frame before it was lifted: a cut"); // below 0.5 px the effect is taken off; it is a hair's blur
        Assert.NotNull(removed);
        Assert.True(handWhenRemoved >= GettingReadyLook.HiSpan - 1e-9, $"the sheet was taken out at hand-over {handWhenRemoved:0.000}, before it was out of sight"); // hi = 1: translated off the window, 0.4 opaque
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Null(rig.Root.DetailPane.Effect);
        Assert.Equal(1, rig.Root.DetailPane.Opacity);
        Assert.True(rig.Root.DetailPane.IsHitTestVisible); // Welcome is usable once the sheet has gone
        Assert.Null(rig.Root.GettingReady);
    });

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void Under_reduce_motion_the_hold_still_runs_then_the_pane_is_whole_in_the_one_frame() => Mounted(headless, rig =>
    {
        rig.AllDone();
        var run = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        rig.Run(GettingReadyLook.Hold - 2 * Frame);
        Assert.Equal(GettingReadyLook.BlurSigma, rig.PaneBlurSigma, 2);
        Assert.False(run.IsCompleted);
        rig.Run(3 * Frame);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Null(rig.Root.DetailPane.Effect);
        Assert.Equal(1, rig.Root.DetailPane.Opacity);
        Assert.Null(rig.Root.GettingReady);
    }, reduceMotion: true);

    [Fact] // GR-2.rootview
    [Trait("Decision", "ED-288")]
    public void Welcome_is_blurred_in_the_drawn_frame_and_sharp_again_after_the_lift() => Mounted(headless, rig =>
    {
        // a hard vertical edge in the detail pane stands in for Welcome's content: black on the left half, white on the right
        var edge = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star) } };
        var black = new Border { Background = Avalonia.Media.Brushes.Black };
        var white = new Border { Background = Avalonia.Media.Brushes.White };
        Grid.SetColumn(white, 1);
        edge.Children.Add(black);
        edge.Children.Add(white);
        rig.Root.DetailPane.Child = edge;
        rig.Settle();
        var origin = rig.Root.DetailPane.TranslatePoint(new Point(0, 0), rig.Window)!.Value;
        var middle = (int)(origin.X + rig.Root.DetailPane.Bounds.Width / 2);
        var y = 600; // below the sheet
        var near = FramePixels.Levels(rig.Window, (middle - 1, y), (middle + 1, y), (middle - 12, y), (middle + 12, y));
        Assert.True(near[2] > 0.0 && near[3] < 1.0 && near[1] - near[0] < 0.5, $"the edge is soft while the sheet hangs: {string.Join(", ", near.Select(v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
        rig.AllDone();
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        rig.Run(1.5);
        var sharp = FramePixels.Levels(rig.Window, (middle - 3, y), (middle + 3, y));
        Assert.True(sharp[1] - sharp[0] > 0.9, $"the edge is hard again after the lift: {sharp[0]:0.00} to {sharp[1]:0.00}");
    });
}
