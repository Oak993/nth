using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3a: how the chat column follows a width spring (work/design/web-surfaces/DESIGN.md 2.6 and spike
// (h)). Live re-wraps the transcript every frame. Clip, the fallback if (h) fails, keeps the start layout under a
// springing clip, lays out once at the end under a 120 ms cross-fade, and ends the reshape so the timeline anchors its
// reading position. Both are built; the workspace uses Clip until (h) has run.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3a-2")]
public sealed class ChatReflowTests(HeadlessSession headless)
{
    [Fact]
    public void Until_spike_h_decides_every_workspace_uses_the_clip_reflow() => headless.OnUiThread(() =>
    {
        Assert.Equal(ChatReflow.Clip, PaneWorkspace.DefaultChatReflow);
        Assert.Equal(ChatReflow.Clip, new PaneWorkspace(new Avalonia.Controls.Border(), _ => null).Reflow);
    });

    [Fact]
    public void Clip_keeps_the_start_layout_through_the_spring_and_lays_out_once_under_a_cross_fade() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Clip);
        rig.ChatColumn.Widths.Clear();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        Assert.Equal(1400, rig.Workspace.Chat.ReservedWidth);
        Assert.True(rig.Workspace.Chat.ClipToBounds);
        var slot = new List<double>();
        while (rig.Workspace.IsMoving)
        {
            rig.Frame();
            if (!rig.Workspace.IsMoving) break;
            slot.Add(rig.Workspace.Chat.Bounds.Width);
            Assert.Equal(1400, rig.ChatColumn.Bounds.Width, 1); // the transcript keeps its start layout
            Assert.True(rig.Resize.IsWorkspaceResizing);
            Assert.All(rig.ChatColumn.Widths, width => Assert.Equal(1400, width, 1)); // and is never laid out at another width
        }
        Assert.True(slot.Count > 8 && slot.Zip(slot.Skip(1)).All(pair => pair.Second <= pair.First + 1e-6), "the clip edge springs in");
        var final = rig.Expected().Chat.Width;
        Assert.Single(rig.ChatColumn.Widths, width => Math.Abs(width - final) < 1); // one layout at the end
        Assert.Equal(final, rig.ChatColumn.Bounds.Width, 0);
        Assert.Null(rig.Workspace.Chat.ReservedWidth);
        Assert.False(rig.Resize.IsWorkspaceResizing); // the reshape ends with that layout, so the timeline anchors
        Assert.Equal(1, rig.Snapshots);
        Assert.True(rig.Workspace.Chat.IsCrossFading);
        var opacity = rig.Workspace.Chat.Overlay.Opacity;
        Assert.Equal(1, opacity, 3);
        var frames = 0;
        while (rig.Workspace.Chat.IsCrossFading)
        {
            rig.Frame();
            frames++;
            if (!rig.Workspace.Chat.IsCrossFading) break;
            Assert.True(rig.Workspace.Chat.Overlay.Opacity < opacity);
            opacity = rig.Workspace.Chat.Overlay.Opacity;
        }
        Assert.InRange(frames, 7, 9); // 120 ms at 60 Hz
        Assert.False(rig.Workspace.Chat.ClipToBounds);
        Assert.Null(rig.Workspace.Chat.Overlay.Source);
    });

    [Fact]
    public void Live_lays_the_chat_out_at_every_frame_of_the_spring_with_no_snapshot() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Live);
        rig.ChatColumn.Widths.Clear();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        Assert.Null(rig.Workspace.Chat.ReservedWidth);
        var frames = 0;
        while (rig.Workspace.IsMoving)
        {
            rig.Frame();
            frames++;
            if (rig.Workspace.IsMoving) Assert.True(rig.Resize.IsWorkspaceResizing);
            Assert.Equal(rig.Workspace.Chat.Bounds.Width, rig.ChatColumn.Bounds.Width, 1);
        }
        Assert.True(rig.ChatColumn.Widths.Distinct().Count() >= frames / 2, "the transcript re-wraps as the column moves");
        Assert.Equal(0, rig.Snapshots);
        Assert.False(rig.Workspace.Chat.IsCrossFading);
        Assert.False(rig.Resize.IsWorkspaceResizing);
        Assert.Contains(true, rig.ResizingSeen);
    });

    [Fact]
    public void Without_a_snapshot_the_clip_reflow_lays_out_at_the_end_without_a_cross_fade() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Clip);
        rig.SnapshotsWork = false;
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        Assert.Equal(1, rig.Snapshots);
        Assert.False(rig.Workspace.Chat.IsCrossFading);
        Assert.False(rig.Workspace.Chat.ClipToBounds);
        Assert.Equal(rig.Expected().Chat.Width, rig.ChatColumn.Bounds.Width, 0);
        Assert.False(rig.Resize.IsWorkspaceResizing);
    });

    [Fact]
    public void A_spring_that_turns_mid_way_keeps_the_layout_it_started_from() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Clip);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        rig.Frame();
        rig.Frame();
        rig.Frame();
        rig.Workspace.Close(PaneKind.Browser);
        rig.Layout();
        Assert.Equal(1400, rig.Workspace.Chat.ReservedWidth);
        rig.ChatColumn.Widths.Clear();
        rig.Settle();
        Assert.Empty(rig.ChatColumn.Widths); // it ended where it started: no layout at all
        Assert.Equal(1400, rig.ChatColumn.Bounds.Width, 1);
        Assert.Null(rig.Workspace.Chat.ReservedWidth);
    });

    [Fact]
    public void A_window_resize_during_a_clip_reflow_ends_it_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Clip);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        rig.Frame();
        Assert.NotNull(rig.Workspace.Chat.ReservedWidth);
        rig.Window.Width = 1200;
        rig.Layout();
        Assert.Null(rig.Workspace.Chat.ReservedWidth);
        Assert.False(rig.Workspace.IsMoving);
        Assert.Equal(rig.Expected().Chat.Width, rig.ChatColumn.Bounds.Width, 0);
        rig.Settle();
        Assert.False(rig.Resize.IsWorkspaceResizing);
    });
}
