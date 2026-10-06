using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// UI-CHAT-001 STOP App.Views.Chat.ThreadTimeline #10, ruled by the owner 2026-10-05 (OD-0105 (1), "Smooth fade"): where the Mac cuts (ThreadTimeline.swift:132-135, the
// loading branch is `Color.clear` and a thread opens in one frame, CATALOGUE.md C 164.452-164.640; :317-321, :339 a row's removal is `.identity`), the port by OD-0076
// "The loading mark fades out while the rows fade and rise in, in about 0.2 s. Rows that leave together fold shut together, in about 0.3 s." Each is sampled frame by frame.
[Collection("Headless")]
public sealed class ThreadTimelineHandOverTests(HeadlessSession headless)
{
    private static void Frame(TimelineMount mount, int milliseconds = 10)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // the clock first, then the frame: a frame drawn at a time reads that time
        mount.Store.Main.Advance(TimeSpan.FromMilliseconds(milliseconds));
        mount.Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        mount.Window.UpdateLayout();
    }

    private static void SetLoading(TimelineMount mount, bool loading) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(mount.Runtime, loading);

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // ThreadTimeline.swift:132-135; OD-0105 (1) the loading mark fades out while the rows fade and rise in, in about 0.2 s
    public void The_loading_mark_fades_out_while_the_rows_fade_and_rise_in_together_in_a_fifth_of_a_second() => ThreadTimelineRig.Run(headless, mount =>
    {
        SetLoading(mount, true);
        mount.Advance(600); // the mark has shown (it draws nothing for its first 400 ms, ED-097)
        var mark = Assert.Single(mount.View.GetVisualDescendants().OfType<AuroraLoader>()); // OD-0113 supersedes the Comet icon
        mount.SetEntries(TimelineMount.Turns(3));
        SetLoading(mount, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        var layer = (Control)mount.Scroll.GetVisualParent()!; // the timeline's branch (scrollOverlay)
        var samples = new List<(int Ms, double Mark, double Rows, double Rise, bool MarkShown)>();
        double Rise() => layer.RenderTransform is TranslateTransform rise ? rise.Y : 0;
        for (var ms = 10; ms <= 300; ms += 10)
        {
            Frame(mount);
            samples.Add((ms, ((Visual)mark).Opacity, layer.Opacity, Rise(), TopLevel.GetTopLevel(mark) is not null));
        }
        // together, on one curve: what the mark has lost the rows have gained, and the rise is the rest of the 4 points
        foreach (var (ms, markOpacity, rows, rise, shown) in samples.Where(sample => sample.MarkShown))
        {
            Assert.True(Math.Abs(markOpacity + rows - 1) < 0.03, $"+{ms} ms: the mark at {markOpacity:F3} and the rows at {rows:F3} are not one hand-over");
            Assert.True(Math.Abs(rise - 4 * (1 - rows)) < 0.15, $"+{ms} ms: the rows are {rise:F2} points low at opacity {rows:F3}");
        }
        // never a cut: the first frame is part way, and so is 100 ms
        Assert.True(samples[0].Rows is > 0 and < 0.6, $"10 ms in, the rows are at {samples[0].Rows:F3}");
        var at100 = samples.Single(sample => sample.Ms == 100);
        Assert.True(at100.Rows is > 0.4 and < 0.98 && at100.Rise > 0.05, $"100 ms in, the rows are at {at100.Rows:F3}, {at100.Rise:F2} points low");
        Assert.All(samples.Zip(samples.Skip(1)), pair => Assert.True(pair.Second.Rows >= pair.First.Rows - 1e-9, $"the rows fell back at +{pair.Second.Ms} ms"));
        // about 0.2 s: by 220 ms the mark has gone, the rows are whole and in place
        var after = samples.Single(sample => sample.Ms == 220);
        Assert.False(after.MarkShown, "the mark is still in the tree 220 ms after the hand-over began");
        Assert.Equal(1, after.Rows, 6);
        Assert.Equal(0, after.Rise, 6);
        Assert.True(samples.Single(sample => sample.Ms == 170).Rows < 1, "the hand-over was over in well under 0.2 s");
    });

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // OD-0105 (1); ED-084 Reduce Motion: the end state at once
    public void Under_reduce_motion_the_loading_mark_hands_over_at_once() => ThreadTimelineRig.Run(headless, mount =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(mount.Host, true);
        SetLoading(mount, true);
        mount.Advance(600);
        mount.SetEntries(TimelineMount.Turns(3));
        SetLoading(mount, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Frame(mount);
        Assert.Empty(mount.View.GetVisualDescendants().OfType<AuroraLoader>());
        Assert.Equal(1, ((Control)mount.Scroll.GetVisualParent()!).Opacity);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // ThreadTimeline.swift:317-321, :339; OD-0105 (1) rows that leave together fold shut together, in about 0.3 s
    public void Rows_that_leave_together_fold_shut_together_in_about_three_tenths_of_a_second() => ThreadTimelineRig.Run(headless, mount =>
    {
        var turns = TimelineMount.Turns(4).ToList();
        mount.SetEntries(turns); mount.Settle();
        var before = mount.Blocks();
        Assert.Equal(4, before.Length);
        var goers = before.Skip(2).Select(block => SwiftTransition.Container(block.Row)).ToArray();
        var heights = goers.Select(container => container.Bounds.Height).ToArray();
        Assert.All(heights, height => Assert.True(height > 20, $"a row {height} high"));
        mount.SetEntries(turns.Take(6)); // the last two turns leave the thread in one change
        var samples = new List<(int Ms, double[] Open, bool[] Shown)>();
        for (var ms = 10; ms <= 400; ms += 10)
        {
            Frame(mount);
            samples.Add((ms, goers.Select((container, index) => container.Bounds.Height / heights[index]).ToArray(), goers.Select(container => TopLevel.GetTopLevel(container) is not null).ToArray()));
        }
        // together: the two rows are as far shut as each other in every frame, and they shut steadily
        foreach (var (ms, open, shown) in samples.Where(sample => sample.Shown.All(on => on)))
            Assert.True(Math.Abs(open[0] - open[1]) < 0.01, $"+{ms} ms: one row is {open[0]:F3} open and the other {open[1]:F3}");
        Assert.True(samples[0].Shown.All(on => on) && samples[0].Open[0] is > 0.4 and < 1, $"10 ms in, the rows are {samples[0].Open[0]:F3} open: cut, or not moving");
        var at150 = samples.Single(sample => sample.Ms == 150);
        Assert.True(at150.Shown.All(on => on) && at150.Open[0] is > 0.01 and < 0.6, $"150 ms in, the rows are {at150.Open[0]:F3} open");
        var shownSamples = samples.Where(sample => sample.Shown.All(on => on)).ToArray();
        Assert.All(shownSamples.Zip(shownSamples.Skip(1)), pair => Assert.True(pair.Second.Open[0] <= pair.First.Open[0] + 1e-9, $"the rows opened again at +{pair.Second.Ms} ms"));
        // about 0.3 s: still there at 250 ms, gone by 320 ms, and the two that stay are where they were
        Assert.True(samples.Single(sample => sample.Ms == 250).Shown.All(on => on), "the rows were gone well before 0.3 s");
        Assert.True(samples.Single(sample => sample.Ms == 320).Shown.All(on => !on), "the rows are still in the tree 320 ms after they left");
        Assert.Equal(2, mount.Blocks().Length);
    });
}
