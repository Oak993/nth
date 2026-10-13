using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-13")]
public sealed class WebReviewSplitterAllocationTests(HeadlessSession headless)
{
    [Fact]
    public void Pane_workspace_frames_reuse_the_same_callback() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        var frames = new QuietFrameSource { ObservedOwner = rig.Workspace };
        using var source = FrameClock.Source.Override(frames);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        var first = frames.ObservedFrame;
        Assert.NotNull(first);
        frames.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(rig.Workspace.IsMoving);
        Assert.Same(first, frames.ObservedFrame);
    });

    [Fact]
    public void Splitter_grip_frames_reuse_the_same_callback() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        var grip = new SplitterGrip(SplitterOrientation.Vertical);
        rig.Window.Content = grip;
        rig.Layout();
        var frames = new QuietFrameSource { ObservedOwner = grip };
        using var source = FrameClock.Source.Override(frames);
        grip.SetShown(true);
        var first = frames.ObservedFrame;
        Assert.NotNull(first);
        rig.Runtime.Time.Advance(TimeSpan.FromMilliseconds(1));
        frames.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(frames.Pending > 0);
        Assert.Same(first, frames.ObservedFrame);
    });

    [Fact]
    public void A_moving_splitter_reuses_its_frame_callback_without_per_frame_allocation() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        var frames = new QuietFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var value = new SplitterValue(rig.Workspace, SplitterOrientation.Vertical,
            new SplitterLimits(0, 10000, 100), new SplitterState(100), static _ => { });
        value.StepTo(true);
        // Warm the actual frame path, then measure only that path; the manual clock's own
        // LINQ work happens before each allocation sample.
        for (var i = 0; i < 10; i++) { time.Advance(TimeSpan.FromTicks(100)); frames.Advance(TimeSpan.FromTicks(100)); }
        long allocated = 0;
        for (var i = 0; i < 100; i++)
        {
            time.Advance(TimeSpan.FromTicks(100));
            var before = GC.GetAllocatedBytesForCurrentThread();
            frames.Advance(TimeSpan.FromTicks(100));
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(value.IsAnimating);
        Assert.Equal(1, frames.Pending);
        value.Finish();
        Assert.Equal(0, frames.Pending);
        Assert.Equal(0, allocated);
    });
}
