using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Git;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class AuroraDiffLoadingStateTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void Empty_files_empty_before_the_first_arrival_finishes_does_not_reuse_a_dimmed_body() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"), null);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var first = new TaskCompletionSource<ValueList<DiffFile>>();
        rig.SeedDiff(first.Task, rig.Turns[0].Id);
        rig.Store.Main.Loop.Run(() => rig.Runtime.ParsedDiff(rig.Turns[1].Id), AppModelHarness.Bound);
        rig.SeedDiff(Task.FromResult(ValueList<DiffFile>.Empty), rig.Turns[2].Id);
        var inspector = rig.NewInspector();
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[0].Id);
        rig.Present(inspector);
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(600)); frames.Advance(TimeSpan.FromMilliseconds(600)); Dispatcher.UIThread.RunJobs();
        first.SetResult(ValueList<DiffFile>.Empty);
        Assert.True(rig.PumpUntil(() => DiffInspectorTree.Text(inspector, "No changes yet") is not null));
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[1].Id);
        Assert.True(rig.PumpUntil(() => DiffInspectorTree.Descendants<Marrow.App.Views.Diff.DiffFileCard>(inspector).Any()));
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[2].Id);
        Assert.True(rig.PumpUntil(() => DiffInspectorTree.Text(inspector, "No changes yet") is not null));
        var root = Assert.IsType<Grid>(inspector.Child);
        Assert.Equal(1, Assert.IsType<Border>(root.Children[1]).Opacity);
    });

    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_compositor_activity_guard_tracks_visibility_completion_and_detach_without_allocating() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var loader = new AuroraLoader(AuroraLoaderKind.FlowArc); loader.BeginWait();
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = loader };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(AuroraLoader.AnyAnimating());
            loader.IsVisible = false; Assert.False(AuroraLoader.AnyAnimating());
            loader.IsVisible = true; Assert.True(AuroraLoader.AnyAnimating());
            loader.Complete(); time.Advance(TimeSpan.FromMilliseconds(150)); Assert.False(AuroraLoader.AnyAnimating());
            loader.BeginWait(); Assert.True(AuroraLoader.AnyAnimating());
            for (var i = 0; i < 180; i++) AuroraLoader.AnyAnimating();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 180; i++) AuroraLoader.AnyAnimating();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        finally { window.Close(); }
        Assert.False(AuroraLoader.AnyAnimating());
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    public void A_mounted_diff_keeps_its_wait_exit_and_content_fade_across_unrelated_runtime_updates() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var pending = new TaskCompletionSource<ValueList<DiffFile>>();
        rig.SeedDiff(pending.Task, rig.Turns[0].Id);
        var inspector = rig.NewInspector();
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[0].Id);
        rig.Present(inspector);
        var state = Assert.Single(DiffInspectorTree.Descendants<AuroraDiffLoadingState>(inspector));
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(500)); frames.Advance(TimeSpan.FromMilliseconds(500)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(.5, state.Label.Opacity, 8);
        rig.Store.Main.OnMain(() => rig.Runtime.FollowUpsCollapsed = !rig.Runtime.FollowUpsCollapsed);
        Assert.Same(state, Assert.Single(DiffInspectorTree.Descendants<AuroraDiffLoadingState>(inspector)));
        Assert.Equal(.5, state.Label.Opacity, 8);
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
        pending.SetResult(ValueList<DiffFile>.Empty);
        Assert.True(rig.PumpUntil(() => DiffInspectorTree.Text(inspector, "No changes yet") is not null));
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(.5, state.Label.Opacity, 8);
        rig.Store.Main.OnMain(() => rig.Runtime.FollowUpsCollapsed = !rig.Runtime.FollowUpsCollapsed);
        Assert.Same(state, Assert.Single(DiffInspectorTree.Descendants<AuroraDiffLoadingState>(inspector)));
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
        Assert.Empty(DiffInspectorTree.Descendants<AuroraDiffLoadingState>(inspector));
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(200)); frames.Advance(TimeSpan.FromMilliseconds(200)); Dispatcher.UIThread.RunJobs();
        var root = Assert.IsType<Grid>(inspector.Child);
        Assert.Equal(1, Assert.IsType<Border>(root.Children[1]).Opacity);
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    public void The_kept_diff_label_waits_four_hundred_ms_fades_with_the_skeleton_and_leaves_after_the_exit_fade() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        using var clock = SwiftClock.Provider.Override(time);
        using var source = FrameClock.Source.Override(frames);
        var state = new AuroraDiffLoadingState();
        var window = new Avalonia.Controls.Window { Width = 300, Height = 200, Content = state };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal("Reading the diff\u2026", state.Label.Text);
            // OD-0136 amendment 2, step 5: the diff skeleton stands where the sheen loader stood (88 tall, stretched, 10 in at the sides), the header's words have the light through them
            Assert.Equal(NthSkeletonKind.Diff, state.Skeleton.Kind);
            Assert.True(state.Label.IsShining);
            Assert.Equal(0, state.Label.Opacity);
            var place = Assert.IsType<Avalonia.Controls.Panel>(state.Children[0]);
            Assert.Equal(88, place.Height); Assert.Equal(new Avalonia.Thickness(10, 0), place.Margin); Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, place.HorizontalAlignment);
            time.Advance(TimeSpan.FromMilliseconds(399)); frames.Advance(TimeSpan.FromMilliseconds(399)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, state.Label.Opacity);
            Assert.Null(state.Skeleton.Parent); // ED-097: nothing is drawn for 400 ms, so a fast load never flashes
            time.Advance(TimeSpan.FromMilliseconds(101)); frames.Advance(TimeSpan.FromMilliseconds(101)); Dispatcher.UIThread.RunJobs();
            Assert.Same(place, state.Skeleton.Parent); // it comes in then, so its rows fade in from that moment
            Assert.Equal(.5, state.Label.Opacity, 8);
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, state.Label.Opacity);
            var removed = 0; state.Complete(() => removed++);
            // the hand-over is the skeleton's own 0.2 s fade (NthSkeleton.Finish); the label goes with it
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.5, state.Label.Opacity, 8); Assert.Equal(0, removed);
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, state.Label.Opacity); Assert.Equal(1, removed); Assert.Equal(0, frames.Pending);
        }
        finally { window.Close(); }
    });
}
