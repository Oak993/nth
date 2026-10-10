using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views;

// OD-0133 (the providers' checks finish after the window shows) with OD-0076 (every change is transitioned, never cut): a
// provider row already on screen whose status changes crossfades its status line and its trailing control, the old fading
// out over the new fading in over ProviderStatusRow.ChangeSeconds; its first build and reduce motion change at once.
[Collection("Headless")]
public sealed class RootViewProviderRowTransitionTests(HeadlessSession headless)
{
    private static ProviderStatus Missing => new();
    private static ProviderStatus Checking => new() { IsChecking = true };

    private void OnShownRow(bool reduceMotion, Action<ProviderStatusRow, AppModelHarness, AppModel, ManualFrameSource> check) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var (row, host) = harness.Main.OnMain(() =>
        {
            RootViewRig.Seed(model, ProviderKind.Claude, Missing);
            var made = new ProviderStatusRow(ProviderKind.Claude);
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(made, true);
            return (made, RootViewRig.ShowRow(model, made));
        });
        try { check(row, harness, model, frames); }
        finally { harness.Main.OnMain(() => RootViewRig.Close(host)); }
    });

    private static void Change(AppModelHarness harness, AppModel model, ProviderStatus status)
    {
        harness.Main.OnMain(() => RootViewRig.Seed(model, ProviderKind.Claude, status));
        harness.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Advance(AppModelHarness harness, ManualFrameSource frames, double seconds)
    {
        harness.Main.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        harness.Main.RunUntilIdle();
    }

    private static TextBlock[] Lines(ProviderStatusRow row, string text) =>
        RootViewRig.All<TextBlock>(row).Where(block => block.Text == text).ToArray();

    [Fact]
    [Trait("Decision", "OD-0133")]
    [Trait("Decision", "OD-0076")]
    public void A_shown_rows_status_change_crossfades_its_line_and_its_trailing_control() => OnShownRow(false, (row, harness, model, frames) =>
    {
        Assert.Equal("Install", RootViewRig.Trailing(row)); // the first build: at once
        var first = Assert.Single(Lines(row, Missing.Summary));
        Assert.Equal(1, first.Opacity);

        Change(harness, model, Checking);
        var leaving = Assert.Single(Lines(row, Missing.Summary));
        var arriving = Assert.Single(Lines(row, "Checking…"));
        Assert.Equal(1, leaving.Opacity, 3);
        Assert.Equal(0, arriving.Opacity, 3);
        var button = Assert.Single(RootViewRig.All<Button>(row));
        var spinner = Assert.Single(RootViewRig.All<ProgressBar>(row));
        Assert.Contains(button.GetLogicalAncestors().OfType<Control>(), control => !control.IsHitTestVisible); // the leaving control takes no clicks

        var last = (Leaving: 1.0, Arriving: 0.0);
        for (var t = 0.0; t < ProviderStatusRow.ChangeSeconds - 0.02; t += 1 / 60.0)
        {
            Advance(harness, frames, 1 / 60.0);
            if (Lines(row, Missing.Summary).Length == 0) break;
            Assert.True(leaving.Opacity <= last.Leaving + 1e-9 && arriving.Opacity >= last.Arriving - 1e-9, "the crossfade went back");
            Assert.True(Math.Abs(leaving.Opacity + arriving.Opacity - 1) < 0.35, "the two lines are not crossfading");
            last = (leaving.Opacity, arriving.Opacity);
        }
        Assert.InRange(last.Arriving, 0.6, 1); // most of the way at the end of the curve, not a cut
        for (var t = 0.0; t < 0.2; t += 1 / 60.0) Advance(harness, frames, 1 / 60.0);
        Assert.Empty(Lines(row, Missing.Summary));
        Assert.Equal(1, arriving.Opacity);
        Assert.Equal(1, spinner.Opacity);
        Assert.Equal("spinner", RootViewRig.Trailing(row)); // the Install button has gone
    });

    [Fact]
    [Trait("Decision", "OD-0133")]
    [Trait("Decision", "OD-0076")]
    public void Under_reduce_motion_a_status_change_is_at_once() => OnShownRow(true, (row, harness, model, _) =>
    {
        Change(harness, model, Checking);
        Assert.Empty(Lines(row, Missing.Summary));
        Assert.Equal(1, Assert.Single(Lines(row, "Checking…")).Opacity);
        Assert.Equal("spinner", RootViewRig.Trailing(row));
    });

    [Fact]
    [Trait("Decision", "OD-0133")]
    public void A_rebuild_that_changes_nothing_leaves_the_row_as_it_is() => OnShownRow(false, (row, harness, model, _) =>
    {
        Change(harness, model, Missing);
        Assert.Equal(1, Assert.Single(Lines(row, Missing.Summary)).Opacity);
        Assert.Equal("Install", RootViewRig.Trailing(row));
    });
}
