using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;
using Xunit;

namespace Marrow.App.Tests.Nth;

// OD-0136 amendment 2, group A, as Calculi WU6 (ED-NEW-calculi-wu6-1) changes it: the sidebar's working marks and the subagent panel's helper box carry the
// working beads (quiet, 12 DIP; the helper capsule's own at 14 DIP) in place of the nth tile-trace spinner; the ghost's still mark is the beads' pose drawn
// with ordinary rendering.
[Collection("Headless")]
public sealed class NthSitesATests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Host(Control content)
    {
        var window = new Avalonia.Controls.Window { Content = content, Width = 200, Height = 100 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_working_mark_is_a_12_point_quiet_beads_slot_named_Working() => headless.OnUiThread(() =>
    {
        var mark = NthWorkingMarks.Working("thread-a");
        var window = Host(mark);
        try
        {
            Assert.Equal(new Size(12, 12), mark.Bounds.Size);
            Assert.Equal("Working", Avalonia.Automation.AutomationProperties.GetName(mark));
            Assert.Equal(Avalonia.Automation.AccessibilityView.Content, Avalonia.Automation.AutomationProperties.GetAccessibilityView(mark));
            // the quiet family at 12 DIP and 30 Hz; it runs from the moment it is made
            var beads = Assert.Single(mark.GetVisualDescendants().OfType<NthBeadsControl>());
            Assert.Same(mark.Beads, beads);
            Assert.Equal(12, beads.GlyphSize); Assert.True(beads.Quiet); Assert.True(beads.Ambient);
            Assert.Equal(NthBeadsFrame.EveryFor(12, true), beads.Current.Every); Assert.Equal(2, beads.Current.Every);
            Assert.Equal(1, beads.Current.Turn); Assert.Equal(NthBeadsExit.None, beads.Current.Exit);
            Assert.Equal(NthBeadsState.Tool, beads.Current.State);
            // the beads' own box is the motion envelope, centred on the 12 DIP slot (it overhangs, nothing clips it)
            var envelope = NthBeadsPainter.EnvelopeSize(12);
            Assert.InRange(beads.Bounds.Width, envelope.Width, envelope.Width + 1); Assert.InRange(beads.Bounds.Height, envelope.Height, envelope.Height + 1); // layout rounding takes it up to a whole pixel
            Assert.Equal(6, beads.Bounds.X + beads.Bounds.Width / 2, 0);
            Assert.Equal(6 + .04 * NthBeadsPainter.Unit(12), beads.Bounds.Y + beads.Bounds.Height / 2, 0);
            Assert.False(mark.ClipToBounds);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_mark_for_a_hydra_merge_holds_Tripus() => headless.OnUiThread(() =>
    {
        var mark = NthWorkingMarks.Working("thread-b", hydra: true);
        Assert.Equal(NthBeadsState.Hydra, mark.Beads.Current.State);
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_still_mark_is_a_12_point_plain_drawing_with_no_moving_visual() => headless.OnUiThread(() =>
    {
        var mark = NthWorkingMarks.Still();
        var host = new StackPanel { Children = { mark } };
        var window = Host(host);
        try
        {
            Assert.Equal(new Size(12, 12), mark.Bounds.Size);
            Assert.Empty(host.GetVisualDescendants().OfType<NthBeadsControl>());
            Assert.Equal("Working", Avalonia.Automation.AutomationProperties.GetName(mark));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_still_pose_is_the_hubs_three_beads_at_the_small_size_radius_inside_the_12_point_box()
    {
        var (centres, radius) = NthBeadsStill.Pose(NthBeadsHub.Pedes, 12);
        var u = NthBeadsPainter.Unit(12);
        Assert.Equal(NthBeadsGeometry.BeadRadius * NthBeadsPainter.RadiusFactor(12) * u, radius, 6);
        Assert.Equal(.8 * NthBeadsGeometry.BeadRadius * u, radius, 6);
        // three beads in a row on the feet, left to right, clear of each other by 0.84 of a bead (1.84 pitch to diameter, section 9), all inside the box
        Assert.Equal(3, centres.Length);
        Assert.True(centres[0].X < centres[1].X && centres[1].X < centres[2].X);
        Assert.All(centres, c => Assert.Equal(centres[0].Y, c.Y, 6));
        for (var i = 0; i < 2; i++) Assert.True(centres[i + 1].X - centres[i].X - 2 * radius >= .838 * 2 * radius, "a clear gap between the beads (pitch to diameter 1.84, section 9)");
        Assert.All(centres, c => { Assert.InRange(c.X - radius, 0, 12); Assert.InRange(c.X + radius, 0, 12); Assert.InRange(c.Y - radius, 0, 12); Assert.InRange(c.Y + radius, 0, 12); });
        // the pose is the hub's own: Tripus lifts the middle bead to the crown
        var tripus = NthBeadsStill.Pose(NthBeadsHub.Tripus, 12).Centres;
        Assert.True(tripus[1].Y < tripus[0].Y);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_helper_working_box_carries_the_14_point_thinking_beads_at_the_full_rate() => headless.OnUiThread(() =>
    {
        var box = new SubagentWorkingBox("Helper", true);
        var window = Host(box);
        try
        {
            var slot = Assert.Single(box.GetVisualDescendants().OfType<NthBeadsSlot>());
            Assert.Equal(new Size(14, 14), slot.Bounds.Size);
            Assert.Equal(NthBeadsPlace.HelperCapsule, slot.Place);
            var beads = slot.Beads;
            Assert.Equal(14, beads.GlyphSize); Assert.False(beads.Quiet); Assert.False(beads.Ambient); Assert.True(beads.FullRate);
            Assert.Equal(1, beads.Current.Every); // 60 Hz at 14 DIP: the section 9 table (section 11's 30 Hz is for the ambient sites)
            Assert.Equal(NthBeadsState.Thinking, beads.Current.State);
            Assert.True(slot.IsRunning);
        }
        finally { window.Close(); }
    });
}
