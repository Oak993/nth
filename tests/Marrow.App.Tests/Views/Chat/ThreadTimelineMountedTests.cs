using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// Mounted proofs of the timeline's scroll column and its states (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792):
// every clause is read off a real ThreadTimeline in a shown window on a real runtime (ThreadTimelineRig).
[Collection("Headless")]
public sealed class ThreadTimelineMountedTests(HeadlessSession headless)
{
    private static double RowLeft(TimelineMount mount) => mount.Rows[0].TranslatePoint(default, mount.Scroll)!.Value.X;

    private static double RowTop(TimelineMount mount, int index) => mount.Rows[index].TranslatePoint(default, mount.Column)!.Value.Y;

    private static void BeginSidebarSlide(TimelineMount mount, double delta) =>
        typeof(SidebarLayout).GetMethod("BeginSlide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.Model.Sidebar, [delta]);

    private static string[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "").ToArray();

    // 1 point for the end-of-conversation sentinel (:402-404), then the 18 of bottom padding (:420).
    private const double BelowLastRow = 1 + 18;

    // :416-420, :388. Two finished turns, each taller than half the pane.
    private static void AssertColumnGeometry(TimelineMount mount)
    {
        mount.SetEntries(Turns(2, sentences: 14));
        mount.Settle();
        // Chrome.swift:40 contentTopInset = chromeTopPadding + capsuleHeight + contentBreathBelowChrome, computed from its three parts (12 + 32 + 28).
        Assert.Equal(12 + 32 + 28, Chrome.ChromeTopPadding + Chrome.CapsuleHeight + Chrome.ContentBreathBelowChrome);
        Assert.Equal(Chrome.ChromeTopPadding + Chrome.CapsuleHeight + Chrome.ContentBreathBelowChrome, Chrome.ContentTopInset);
        // The pane the cousin's video shows (x 268 to 2558, 2290 wide) and four others: wider than the 860 cap, exactly the cap, narrower, narrower still.
        foreach (var width in new[] { 2290.0, 960.0, 860.0, 600.0, 420.0 })
        {
            mount.Window.Width = width; mount.Advance(150);
            var cap = Math.Min(820, width - 40);
            foreach (var row in mount.Rows)
            {
                Assert.Equal(cap, row.Bounds.Width, 3); // :416 maxWidth 820, the pane's width less the 20 either side while that is smaller
                Assert.Equal((width - cap) / 2, row.TranslatePoint(default, mount.Scroll)!.Value.X, 0.5); // :417 padding 20, :422 the capped column centred in the pane
            }
        }
        mount.Window.Width = 960; mount.Advance(150);
        // :419 the first row 72 below the top of the content; :420 the content ends 18 below the last row (and the 1 point end sentinel).
        mount.Scroll.Offset = default; mount.Advance(100);
        Assert.Equal(72, RowTop(mount, 0), 3);
        var last = mount.Rows[^1]; var extent = mount.Scroll.Extent.Height;
        Assert.Equal(extent, RowTop(mount, 1) + last.Bounds.Height + BelowLastRow, 3);
        Assert.Equal(extent, mount.Column.Bounds.Height, 3);
        // :388 rows 20 apart.
        Assert.Equal(20, RowTop(mount, 1) - (RowTop(mount, 0) + mount.Rows[0].Bounds.Height), 3);
    }

    // :421-422, :361. One short message.
    private static void AssertShortConversationRestsAtTheBottom(TimelineMount mount)
    {
        mount.SetEntries(new[] { User("only", Guid.NewGuid(), "one short message") });
        mount.Settle();
        foreach (var height in new[] { 480.0, 720.0, 500.0, 360.0 })
        {
            mount.Window.Height = height; mount.Advance(200);
            var viewport = mount.Scroll.Viewport.Height;
            // :446-449 the measured height, floored to a multiple of 12, is the content's least height (:422).
            Assert.Equal(Math.Floor(viewport / 12) * 12, mount.Column.MinHeight, 3);
            // The content fills the pane: the viewport is at least the content, so there is nothing to scroll.
            Assert.Equal(viewport, mount.Column.Bounds.Height, 3);
            Assert.Equal(0, mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height, 3);
            // :361 the leading spacer takes the rest: the one row rests on the bottom padding, empty space above it.
            var row = mount.Rows.Single(); var top = RowTop(mount, 0);
            Assert.Equal(viewport, top + row.Bounds.Height + BelowLastRow, 3);
            Assert.True(top > 72 + 20, $"the row sits at {top} in a pane {viewport} high");
        }
    }

    // :58-67, :418, :446-457, :458-462. Two finished turns.
    private static void AssertReshapeSteppingAndTheLandingWidth(TimelineMount mount)
    {
        mount.SetEntries(Turns(2, sentences: 14));
        mount.Settle();
        var resize = AppEnvironment.GetWindowLiveResize(mount.View);
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
    }

    [Fact]
    [Trait("Req", "PR-CHAT-021")] // ThreadTimeline.swift:416-420; Chrome.swift:32-40 (contentTopInset); map/CATALOGUE.md static.timeline-rows (the 820 px column at x 1004-1824 of the pane that starts at 268 and ends at 2558)
    public void The_column_is_820_wide_centred_padded_20_at_the_sides_72_from_the_top_and_18_from_the_bottom() =>
        ThreadTimelineRig.Run(headless, AssertColumnGeometry);

    [Fact]
    [Trait("Req", "PR-CHAT-022")] // ThreadTimeline.swift:421-422,361 (min height = the measured viewport height, a zero-minimum spacer above the rows); map/CATALOGUE.md static.thread-timeline ("bottom-anchored above the composer")
    public void A_short_conversation_fills_the_pane_and_rests_at_its_bottom() =>
        ThreadTimelineRig.Run(headless, AssertShortConversationRestsAtTheBottom);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-001 (layout width quantized to 12, capped at 860, during a reshape); ThreadTimeline.swift:58-67,418,446-457
    public void Reshaping_steps_the_column_width_by_twelve_caps_it_at_860_and_the_exact_width_lands_when_it_ends() =>
        ThreadTimelineRig.Run(headless, AssertReshapeSteppingAndTheLandingWidth);

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // ThreadTimeline.swift:136-141; porting/port_impacts.md 136 (both side effects are required)
    public void An_empty_idle_thread_shows_the_new_thread_prompt_and_clears_the_veil_and_the_jump_button() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(6, sentences: 10));
        mount.Settle();
        // A reader who scrolled up: the veil's progress is up and the jump button is showing.
        mount.Scroll.Offset = new Vector(0, 200); mount.Advance(300);
        Assert.True(mount.Chrome.Progress > 0, "the chrome's scroll travel drives the veil");
        mount.State.ShowsJumpButton = true;
        // The conversation reverts to nothing, with no turn running: no history is loading, nothing is running.
        Assert.False(mount.Runtime.IsLoadingHistory); Assert.False(mount.Runtime.IsRunning);
        mount.SetEntries([]);
        // :137 the prompt for a new thread, with the project's name (:137 projectName), and not the timeline.
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.Equal("What should we build in Project?", string.Concat(Texts(prompt)));
        Assert.Null(mount.ScrollOrNull);
        // :139-140 on appear, with no animation and not a frame later: the chrome's travel is 0 (its progress with it, which clears the top veil) and the button is hidden.
        Assert.Equal(0, mount.Chrome.Progress);
        Assert.False(mount.State.ShowsJumpButton);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-027")] // ThreadTimeline.swift:134-144 (the empty branch needs both: no history loading and no turn running, in that order)
    public void A_running_turn_on_an_empty_thread_shows_the_timeline_with_a_working_line_not_the_prompt() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        // Running, nothing in the thread yet: the timeline branch, whose block list gets the working block (:1600-1602).
        mount.SetPhase(RuntimePhase.Running); mount.Advance(100);
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View));
        Assert.Contains(WorkingWords.Word(WorkingWords.Seed(Marrow.Core.Port.SwiftUUID.UuidString(mount.Runtime.ThreadID)), 0) + "…", Texts(line));
        Assert.NotNull(mount.ScrollOrNull);
        // The turn over with nothing said: back to the prompt.
        mount.SetPhase(RuntimePhase.Idle); mount.Advance(100);
        Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View));
        // The two conditions in order: a history still loading shows neither, running or not (:134 comes first).
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(mount.Runtime, true);
        mount.SetPhase(RuntimePhase.Running); mount.Advance(100);
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View));
        Assert.Null(mount.ScrollOrNull);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // ThreadTimeline.swift:359-440 (column, states, interactions); OD-0016/ED-097 for the loading state (the animated mark where upstream draws a clear view); OD-0013/OD-0017 for the live-resize edge
    public void The_scroll_column_has_its_geometry_its_four_states_no_background_and_no_scroll_indicators() => ThreadTimelineRig.Run(headless, mount =>
    {
        // State: loading history. Upstream draws `Color.clear` (:134-135); the port draws the loading mark (OD-0016), and neither the prompt nor a timeline.
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(mount.Runtime, true);
        mount.Advance(100);
        Assert.Single(LogicalQuery.SelfAndDescendants<LoadingMark>(mount.View));
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)); Assert.Null(mount.ScrollOrNull);
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(mount.Runtime, false);
        // State: empty and idle: the prompt, the chrome's travel zeroed and the jump button hidden.
        mount.State.ShowsJumpButton = true; mount.Advance(100);
        Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)); Assert.Null(mount.ScrollOrNull);
        Assert.Equal(0, mount.Chrome.Progress); Assert.False(mount.State.ShowsJumpButton);
        // State: empty and running: the timeline with a working block.
        mount.SetPhase(RuntimePhase.Running); mount.Advance(100);
        Assert.NotNull(mount.ScrollOrNull); Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View));
        mount.SetPhase(RuntimePhase.Idle); mount.Advance(100);
        // State: a short thread: its rows rest at the bottom of the pane (and the geometry of the column, its reshape stepping).
        mount.Window.Width = 960; mount.Window.Height = 480;
        AssertShortConversationRestsAtTheBottom(mount);
        mount.Window.Width = 960; mount.Window.Height = 480; mount.Advance(100);
        AssertColumnGeometry(mount);
        mount.Window.Width = 960; mount.Window.Height = 480; mount.Advance(100);
        AssertReshapeSteppingAndTheLandingWidth(mount);
        // The column has no background of its own, so the window's backdrop shows through; the scroll view shows no indicators (:436).
        Assert.True(Transparent(mount.Column) && Transparent(mount.Scroll), "the scroll content and the scroll view draw nothing of their own");
        Assert.All(LogicalQuery.SelfAndDescendants<Border>((Control)mount.Column).Where(border => border.MaxWidth == 820), border => Assert.Null(border.Background));
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, mount.Scroll.VerticalScrollBarVisibility);
        Assert.Empty(mount.Scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>());
        // Interaction: the wheel scrolls it.
        mount.SetEntries(Turns(6, "w", sentences: 10)); mount.Settle();
        mount.Scroll.Offset = default; mount.Advance(400);
        var point = mount.Rows[0].TranslatePoint(new Point(40, 20), mount.Window)!.Value;
        var before = mount.Scroll.Offset.Y;
        mount.Window.MouseMove(point);
        mount.Window.MouseWheel(point, new Vector(0, -1)); mount.Advance(600);
        Assert.True(mount.Scroll.Offset.Y > before + 1, $"a wheel notch moved the offset from {before} to {mount.Scroll.Offset.Y}");
    });

    private static bool Transparent(Control control) => (control as Avalonia.Controls.Primitives.TemplatedControl)?.Background is null or Avalonia.Media.ISolidColorBrush { Color.A: 0 }
        && (control as Panel)?.Background is null or Avalonia.Media.ISolidColorBrush { Color.A: 0 };
}
