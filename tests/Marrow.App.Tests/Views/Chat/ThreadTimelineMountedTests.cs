using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// Mounted proofs of the timeline's scroll column and its states (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792):
// every clause is read off a real ThreadTimeline in a shown window on a real runtime (ThreadTimelineRig).
[Collection("Headless")]
public sealed class ThreadTimelineMountedTests(HeadlessSession headless)
{
    private static double RowLeft(TimelineMount mount) => mount.Rows[0].TranslatePoint(default, mount.Scroll)!.Value.X;

    private static void BeginSidebarSlide(TimelineMount mount, double delta) =>
        typeof(Marrow.UI.Chrome.SidebarLayout).GetMethod("BeginSlide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.Model.Sidebar, [delta]);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-001 (layout width quantized to 12, capped at 860, during a reshape); ThreadTimeline.swift:58-67,418,446-457
    public void Reshaping_steps_the_column_width_by_twelve_caps_it_at_860_and_the_exact_width_lands_when_it_ends() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(2, sentences: 14));
        mount.Settle();
        var resize = Marrow.App.Port.AppEnvironment.GetWindowLiveResize(mount.View);
        Assert.Equal(820, mount.Rows[0].Bounds.Width, 3);
        Assert.Equal(70, RowLeft(mount), 3);
        // :60 `guard liveResize.isReshaping`: a pane drag is a reshape. :66 min(820 + 40, floor(paneWidth / 12) * 12); the 860
        // frame sits centred in the pane (:422 alignment .top), its content inset 20 (:417).
        resize.IsPaneResizing = true; mount.Window.Width = 755; mount.Advance(200);
        Assert.Equal(744, mount.Column.Width, 3);
        Assert.Equal(704, mount.Rows[0].Bounds.Width, 3);
        Assert.Equal(5.5 + 20, RowLeft(mount), 0.5); // layout rounding puts the half point on a whole pixel
        // The cap: 1100 steps to 1092, held at 860.
        mount.Window.Width = 1100; mount.Advance(200);
        Assert.Equal(860, mount.Column.Width, 3);
        Assert.Equal(820, mount.Rows[0].Bounds.Width, 3);
        Assert.Equal(120 + 20, RowLeft(mount), 3);
        mount.Window.Width = 700; mount.Advance(200);
        Assert.Equal(696, mount.Column.Width, 3);
        Assert.Equal(656, mount.Rows[0].Bounds.Width, 3);
        // A new row mid-reshape draws again: the width keeps following the pane, it does not freeze at the drawn value.
        mount.SetEntries(Turns(2, "n", sentences: 14)); mount.Advance(100);
        mount.Window.Width = 755; mount.Advance(200);
        Assert.Equal(744, mount.Column.Width, 3);
        // :454-457 the exact width lands when the drag ends.
        resize.IsPaneResizing = false; mount.Advance(200);
        Assert.True(double.IsNaN(mount.Column.Width));
        Assert.Equal(715, mount.Rows[0].Bounds.Width, 3);
        Assert.Equal(20, RowLeft(mount), 3);
        // :63-65 the sidebar's slide: the rows wrap once, at the width the pane will end at (the width when the slide began plus the slide's delta).
        mount.Window.Width = 960; mount.Advance(100);
        resize.IsPaneResizing = true; mount.Advance(100);
        BeginSidebarSlide(mount, -300); mount.Advance(60);
        mount.Window.Width = 900; mount.Advance(100);
        Assert.Equal(660, mount.Column.Width, 3);
        Assert.Equal(620, mount.Rows[0].Bounds.Width, 3);
        // A second slide while the first runs moves the target: the delta alone changed, the width follows it.
        BeginSidebarSlide(mount, -200); mount.Advance(60);
        Assert.Equal(760, mount.Column.Width, 3);
        mount.Advance(500);
        Assert.False(mount.Model.Sidebar.IsSliding);
        Assert.Equal(860, mount.Column.Width, 3);
        resize.IsPaneResizing = false; mount.Advance(100);
        Assert.True(double.IsNaN(mount.Column.Width));
    });
}
