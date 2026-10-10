using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// UI-CHAT-001 STOP App.Views.Chat.ThreadTimeline #10, ruled by the owner 2026-10-05 (OD-0105 (1), "Smooth fade"): where the Mac cuts (ThreadTimeline.swift:132-135, the
// loading branch is `Color.clear` and a thread opens in one frame, CATALOGUE.md C 164.452-164.640; :317-321, :339 a row's removal is `.identity`), the port by OD-0076
// "The loading mark fades out while the rows fade and rise in, in about 0.2 s. Rows that leave together fold shut together, in about 0.3 s." Each is sampled frame by frame.
// OD-0136 amendment 3 (ED-NEW-nth-motion) replaces the loading mark and its hand-over with the nth Flick's: the loader leaves in 0.3 s while the messages rise 10 px and
// fade in, 0.5 s, 150 ms later, 45 ms apart.
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
    [Trait("Req", "UI-CHAT-001")] // ThreadTimeline.swift:132-135; OD-0105 (1) a hand-over, not a cut; OD-0136 amendment 3 (ED-NEW-nth-motion) the Flick's hand-over
    public void The_history_loader_leaves_while_the_messages_rise_in_one_by_one() => ThreadTimelineRig.Run(headless, mount =>
    {
        SetLoading(mount, true);
        mount.Advance(600); // the loader has shown (it draws nothing for its first 400 ms)
        var mark = Assert.Single(mount.View.GetVisualDescendants().OfType<NthFlick>());
        Assert.Equal(48, mark.Width); Assert.Equal(48, mark.Height);
        Assert.False(mark.IsFinished);
        mount.SetEntries(TimelineMount.Turns(3));
        SetLoading(mount, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.True(mark.IsFinished, "the loader was told to leave (opacity 0.22 s, scale 0.9 in 0.3 s, drawn on the render thread)");
        var layer = (Control)mount.Scroll.GetVisualParent()!; // the timeline's branch (scrollOverlay)
        Assert.Equal(1, layer.Opacity);
        var rows = mount.View.DrawnRows.ToArray();
        Assert.True(rows.Length >= 2, $"{rows.Length} rows");
        double Rise(Control row) => row.RenderTransform is TranslateTransform rise ? rise.Y : 0;
        var samples = new List<(int Ms, double[] Opacity, double[] Rise, bool MarkShown)>();
        for (var ms = 10; ms <= 1000; ms += 10)
        {
            Frame(mount);
            samples.Add((ms, rows.Select(row => row.Opacity).ToArray(), rows.Select(Rise).ToArray(), TopLevel.GetTopLevel(mark) is not null));
        }
        // the messages wait 150 ms, then rise 10 px and fade in over 0.5 s on (.2,.8,.2,1), each 45 ms after the one above
        foreach (var sample in samples.Where(sample => sample.Ms <= 140)) Assert.All(sample.Opacity, opacity => Assert.Equal(0, opacity, 6));
        foreach (var (ms, opacity, rise, _) in samples)
            for (var i = 0; i < rows.Length; i++)
                Assert.True(Math.Abs(rise[i] - 10 * (1 - opacity[i])) < 0.05, $"+{ms} ms: row {i} is {rise[i]:F2} px low at opacity {opacity[i]:F3}");
        var at250 = samples.Single(sample => sample.Ms == 250);
        Assert.True(at250.Opacity[0] > at250.Opacity[1] && at250.Opacity[0] is > .2 and < 1, $"row 0 at {at250.Opacity[0]:F3} leads row 1 at {at250.Opacity[1]:F3}");
        // the loader is taken out once it has left (0.3 s)
        Assert.True(samples.Single(sample => sample.Ms == 200).MarkShown, "the loader is still leaving at 200 ms");
        Assert.False(samples.Single(sample => sample.Ms == 320).MarkShown, "the loader is still in the tree 320 ms after it began to leave");
        // all in place by 150 + 45 (n - 1) + 500 ms
        var end = samples.Single(sample => sample.Ms == Math.Min(1000, (int)Math.Ceiling((150 + 45 * (rows.Length - 1) + 500) / 10.0) * 10 + 10));
        Assert.All(end.Opacity, opacity => Assert.Equal(1, opacity, 6));
        Assert.All(rows, row => Assert.Null(row.RenderTransform));
    });

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // OD-0136 amendment 3: under reduce motion the hand-over is opacity only
    public void Under_reduce_motion_the_messages_fade_in_without_rising() => ThreadTimelineRig.Run(headless, mount =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(mount.Host, true);
        SetLoading(mount, true);
        mount.Advance(600);
        mount.SetEntries(TimelineMount.Turns(3));
        SetLoading(mount, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        var rows = mount.View.DrawnRows.ToArray();
        Frame(mount, 300);
        Assert.All(rows, row => Assert.Null(row.RenderTransform));
        for (var i = 0; i < 80; i++) Frame(mount);
        Assert.Empty(mount.View.GetVisualDescendants().OfType<NthFlick>());
        Assert.All(rows, row => Assert.Equal(1, row.Opacity, 6));
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
