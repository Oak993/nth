using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Tests.Views.Settings;

// SK-15 (DL-17, OD-0076): every change of the pages is transitioned on the frame clock, with no single-frame jump, and Reduce Motion stills it. The frames and the clock are taken by hand.
[Collection("Headless")]
public sealed class LibraryPageMotionTests(HeadlessSession headless)
{
    /// <summary>Samples <paramref name="read"/> every frame of 16 ms for <paramref name="milliseconds"/>; the first sample is the value now.</summary>
    private static List<double> Frames(LibraryPageHarness harness, Func<double> read, double milliseconds)
    {
        var samples = new List<double> { read() };
        for (var spent = 0.0; spent < milliseconds; spent += 16)
        {
            harness.Step(16);
            samples.Add(read());
        }
        return samples;
    }

    private static void EaseOf(IReadOnlyList<double> samples, double from, double to)
    {
        Assert.Equal(from, samples[0], 6);
        Assert.Equal(to, samples[^1], 6);
        var sign = Math.Sign(to - from);
        for (var i = 1; i < samples.Count; i++) Assert.True((samples[i] - samples[i - 1]) * sign >= -1e-9, "the value turned back at frame " + i);
        Assert.True(samples.Distinct().Count() >= 6, "the change was a cut: " + string.Join(", ", samples.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))));
        var largest = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(largest <= Math.Abs(to - from) * 0.35, "a single frame moved " + largest);
    }

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: a row that starts downloading crossfades from its size to the ring
    public void A_row_that_starts_downloading_crossfades_its_status() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        var row = harness.Page.RowViews["S1"];
        var before = row.Mark.Live!;
        _ = harness.Controller.Download("S1");
        harness.Until(() => harness.Controller.Rows[0].State == ItemState.Downloading);
        Assert.Equal(StatusMark.Mark.Ring, row.Mark.Current);
        var incoming = row.Mark.Live!;
        Assert.NotSame(before, incoming);
        Assert.Equal(0, incoming.Opacity);
        Assert.Equal(1, before.Opacity);
        EaseOf(Frames(harness, () => incoming.Opacity, 260), 0, 1);
        Assert.Equal(0, before.Opacity);
        Assert.Null(before.GetVisualParent());
        harness.Install.Release("S1");
        harness.Until(() => harness.Controller.Rows.All(r => !r.Busy));
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: a detail swaps by crossfade, and the pane eases to the next one's height
    public void Selecting_another_row_crossfades_the_detail() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var first = harness.Page.Details["S1"];
        harness.Controller.Select("S4");
        harness.Settle();
        var next = harness.Page.Details["S4"];
        Assert.Same(next, harness.Page.DetailSlot.Live);
        Assert.Equal(0, next.Opacity);
        Assert.Equal(1, first.Opacity);
        EaseOf(Frames(harness, () => next.Opacity, 260), 0, 1);
        Assert.Equal(0, first.Opacity);
        Assert.Null(first.GetVisualParent());
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: the confirmation grows in from where it stands and shrinks away on Cancel, never cut
    public void The_dialog_grows_in_and_shrinks_away() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Press("download-all");
        var sheet = harness.Page.Sheet!;
        Assert.Equal(0, sheet.Opacity);
        var opening = Frames(harness, () => sheet.Opacity, 300);
        Assert.Equal(0, opening[0]);
        Assert.Equal(1, opening[^1]);
        Assert.True(opening.Distinct().Count() >= 2);
        for (var i = 1; i < opening.Count; i++) Assert.True(opening[i] >= opening[i - 1]);
        Assert.NotNull(harness.TryFind("dialog"));
        harness.Press("dialog-cancel");
        Assert.Null(harness.Page.Sheet);
        var layer = harness.Root.Children.OfType<WindowSheetLayer>().Single();
        Assert.True(layer.Sheet.Opacity > 0 || layer.Sheet.RenderTransform is not null, "the sheet was cut away");
        Frames(harness, () => layer.Sheet.Opacity, 400);
        Assert.Empty(harness.Root.Children.OfType<WindowSheetLayer>());
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: the confirmation's facts fold shut and the run's bar folds open when Continue is pressed
    public void The_dialog_phases_fold_open_and_shut() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        harness.Press("download-all");
        harness.Step(400);
        var dialog = harness.Page.Sheet!.Dialog;
        Assert.Equal(1, dialog.ConfirmFold.Fraction);
        Assert.Equal(0, dialog.RunFold.Fraction);
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S1"));
        Assert.False(dialog.ConfirmOpen);
        Assert.True(dialog.RunOpen);
        var shutting = Frames(harness, () => dialog.ConfirmFold.Fraction, 420);
        EaseOf(shutting, 1, 0);
        Assert.Equal(1, dialog.RunFold.Fraction, 6);
        harness.Install.Release("S1");
        harness.Wait(harness.Controller.RunCompletion);
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: the bar eases to each new progress and the button group swaps by crossfade
    public void The_bar_eases_to_each_new_progress() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        harness.Press("download-all");
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S1"));
        harness.Step(500);
        var bar = harness.Page.Sheet!.Dialog.Bar;
        harness.Install.Report("S1", 1);
        harness.Settle();
        var target = bar.Target;
        Assert.True(target > 0.2);
        // nth motion pack step 3: the fill springs to it (critically damped, stiffness 110): v(t) = target * (1 - (1 + w t) e^(-w t)), t seconds after the value was set; no overshoot, never backwards
        var series = Frames(harness, () => bar.Drawn, 400);
        Assert.Equal(0, series[0], 6);
        for (var i = 1; i < series.Count; i++)
        {
            Assert.True(series[i] >= series[i - 1] - 1e-9 && series[i] <= target + 1e-9, "the value turned back or overshot at frame " + i);
            var t = i * 0.016;
            Assert.True(Math.Abs(series[i] - target * (1 - (1 + NthSpring.Omega * t) * Math.Exp(-NthSpring.Omega * t))) <= 0.004, $"frame {i}: {series[i]:0.0000}");
        }
        Assert.True(series.Distinct().Count() >= 6, "the change was a cut");
        harness.Install.Release("S1");
        harness.Wait(harness.Controller.RunCompletion);
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: a changed title and a changed foot crossfade
    public void The_dialog_s_title_crossfades_when_the_phase_changes() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        harness.Press("download-all");
        harness.Step(500);
        var dialog = harness.Page.Sheet!.Dialog;
        var before = ((CzCrossfadeText)FindTitle(dialog)).IncomingOpacity;
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S1"));
        var title = (CzCrossfadeText)FindTitle(dialog);
        Assert.Equal("Downloading all skills", title.Text);
        Assert.Equal(0, title.IncomingOpacity);
        Assert.Equal(1, before);
        EaseOf(Frames(harness, () => title.IncomingOpacity, 400), 0, 1);
        harness.Install.Release("S1");
        harness.Wait(harness.Controller.RunCompletion);
    }, motion: true);

    private static Control FindTitle(DownloadAllDialog dialog) =>
        dialog.GetType().GetField("title", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog) as Control ?? throw new InvalidOperationException();

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: Reduce Motion stills every transition of the pages; the new state lands at once
    public void Reduce_Motion_lands_every_change_at_once() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        var row = harness.Page.RowViews["S1"];
        _ = harness.Controller.Download("S1");
        harness.Until(() => harness.Controller.Rows[0].State == ItemState.Downloading);
        Assert.Equal(1, row.Mark.Live!.Opacity);
        Assert.Single(((Panel)row.Mark.Child!).Children);
        harness.Controller.Select("S4");
        harness.Settle();
        Assert.Equal(1, harness.Page.Details["S4"].Opacity);
        Assert.Same(harness.Page.Details["S4"], harness.Page.DetailSlot.Live);
        Assert.Single(harness.Page.DetailSlot.Children);
        harness.Press("download-all");
        Assert.True(harness.TryFind("dialog-continue") is not null); // the sheet itself grows in by PopoverAppear, the app's shared popover motion, which has no Reduce Motion branch of its own
        Assert.Equal(1, harness.Page.Sheet!.Dialog.ConfirmFold.Fraction);
        harness.Press("dialog-cancel");
        harness.Install.Release("S1");
        harness.Until(() => harness.Controller.Rows.All(r => !r.Busy));
    }, motion: true, reduceMotion: true);
}
