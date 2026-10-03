using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
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
        Assert.Contains(WorkingWords.Word(WorkingWords.Seed(SwiftUUID.UuidString(mount.Runtime.ThreadID)), 0) + "…", Texts(line));
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

    private static HydraHeadInfo FinishedHead(long index, DateTimeOffset started, DateTimeOffset finished) =>
        new(index, "task " + index, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, StartedAt = started, FinishedAt = finished };

    // A head's record changes in the app model, as when it starts or finishes working.
    private static void SetHeadStatus(TimelineMount mount, long index, HydraHeadInfo.Status status, DateTimeOffset? finished)
    {
        var head = mount.Model.Children(mount.Runtime.ThreadID).First(thread => thread.Hydra!.Index == index);
        mount.Model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = status, FinishedAt = finished } });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
    }

    private static string TurnID(Guid turn, string first) => "turn-" + SwiftUUID.UuidString(turn) + "-" + first;

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // ThreadTimeline.swift:104-110,1608-1614 (the pills split, the insertion and its three guards); TimelineRows.swift:420-433 (finishedHeads)
    [Trait("Req", "PR-CHAT-035")] // ThreadTimeline.swift:105-110,1615-1617,1505 (the later pills, the two identities); the interaction across a turn boundary
    public void Finished_heads_sit_above_the_running_turn_or_after_it_in_finish_order_and_the_pill_row_keeps_its_identity_into_the_next_turn()
    {
        var s = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        // Four heads that set out together at +0 (roster indices 1 to 4); 2 and 4 finish at +100 and +200, before the second turn begins at +300; 3 finishes at +400, during it.
        var heads = new[] { FinishedHead(1, s, s.AddSeconds(1000)), FinishedHead(2, s, s.AddSeconds(100)), FinishedHead(3, s, s.AddSeconds(400)), FinishedHead(4, s, s.AddSeconds(200)) };
        ThreadTimelineRig.RunWithTeam(headless, mount =>
        {
            var turn1 = Guid.NewGuid(); var turn2 = Guid.NewGuid(); var turn3 = Guid.NewGuid();
            // Heads 1 and 3 are still at work: a finished head is shown only while a head of its team still is (TimelineRows.swift:426).
            SetHeadStatus(mount, 1, HydraHeadInfo.Status.Running, null); SetHeadStatus(mount, 3, HydraHeadInfo.Status.Running, null);
            var first = new[] { User("p1", turn1), Reply("a1", turn1), End("e1", turn1) };
            mount.SetEntries(first); mount.Settle();
            // No turn runs: every finished head ends the timeline under the untrailed id (:1615-1617, :1505), in finish order (:106).
            var blocks = mount.Blocks();
            Assert.Equal(new[] { TurnID(turn1, "p1"), "hydra-heads-working" }, blocks.Select(block => block.Id));
            var idle = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[1].Block); Assert.Equal(new[] { 2, 4 }, idle.Heads); Assert.False(idle.Trailing);
            Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsWorkingRow>(blocks[1].Row));
            // The second turn begins at +300: the two that finished before it (in finish order) sit above it under the plain id, as one row.
            var second = first.Concat(new[] { User("p2", turn2), Reply("a2", turn2) }).ToArray();
            mount.SetEntries(second); mount.SetTurnStartedAt(s.AddSeconds(300)); mount.SetPhase(RuntimePhase.Running); mount.Advance(200);
            blocks = mount.Blocks();
            Assert.Equal(new[] { TurnID(turn1, "p1"), "hydra-heads-working", TurnID(turn2, "p2") }, blocks.Select(block => block.Id));
            var above = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[1].Block); Assert.Equal(new[] { 2, 4 }, above.Heads); Assert.False(above.Trailing);
            // Head 3 finishes at +400, nothing else changed in the thread: it joins at the end under the tail id (:1615-1617).
            SetHeadStatus(mount, 3, HydraHeadInfo.Status.Completed, s.AddSeconds(400)); mount.Advance(200);
            blocks = mount.Blocks();
            Assert.Equal(new[] { TurnID(turn1, "p1"), "hydra-heads-working", TurnID(turn2, "p2"), "hydra-heads-working-tail" }, blocks.Select(block => block.Id));
            var after = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[3].Block); Assert.Equal(new[] { 3 }, after.Heads); Assert.True(after.Trailing);
            Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsWorkingRow>(blocks[3].Row));
            Assert.True(blocks[1].Row.TranslatePoint(default, mount.Column)!.Value.Y < blocks[2].Row.TranslatePoint(default, mount.Column)!.Value.Y);
            // Head 2's finish time moves past the turn's start with nothing else changed (its status is the same): it leaves the pills above for the one after, in finish order (+400, +450).
            SetHeadStatus(mount, 2, HydraHeadInfo.Status.Completed, s.AddSeconds(450)); mount.Advance(200);
            blocks = mount.Blocks();
            Assert.Equal(new[] { 4 }, Assert.IsType<DisplayBlock.HeadsWorking>(blocks[1].Block).Heads);
            Assert.Equal(new[] { 3, 2 }, Assert.IsType<DisplayBlock.HeadsWorking>(blocks[3].Block).Heads);
            SetHeadStatus(mount, 2, HydraHeadInfo.Status.Completed, s.AddSeconds(100)); mount.Advance(200);
            Assert.Equal(new[] { 2, 4 }, Assert.IsType<DisplayBlock.HeadsWorking>(mount.Blocks()[1].Block).Heads);
            // :1612 the guard: a turn whose own rows hold a head sent out shows no pill above it (the same heads would appear twice); the later one stays.
            var withHead = second.Append(ToolEntry("sent", turn2, head: true)).ToArray();
            mount.SetEntries(withHead); mount.Advance(100);
            Assert.Equal(new[] { TurnID(turn1, "p1"), TurnID(turn2, "p2"), "hydra-heads-working-tail" }, mount.Blocks().Select(block => block.Id));
            // The turn ends: nothing runs, so all three end the timeline under the untrailed id, in finish order (+100, +200, +400).
            mount.SetEntries(second.Append(End("e2", turn2)).ToArray()); mount.SetPhase(RuntimePhase.Idle); mount.Advance(200);
            blocks = mount.Blocks();
            Assert.Equal(new[] { TurnID(turn1, "p1"), TurnID(turn2, "p2"), "hydra-heads-working" }, blocks.Select(block => block.Id));
            var all = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[2].Block); Assert.Equal(new[] { 2, 4, 3 }, all.Heads); Assert.False(all.Trailing);
            var pillRow = blocks[2].Row;
            // The third turn begins after all three finished: they are "earlier", above it, under the same id with the same list, so the same row stays and the new turn simply appears below it.
            mount.SetEntries(second.Append(End("e2", turn2)).Concat(new[] { User("p3", turn3), Reply("a3", turn3) }).ToArray());
            mount.SetTurnStartedAt(s.AddSeconds(500)); mount.SetPhase(RuntimePhase.Running); mount.Advance(200);
            blocks = mount.Blocks();
            Assert.Equal(new[] { TurnID(turn1, "p1"), TurnID(turn2, "p2"), "hydra-heads-working", TurnID(turn3, "p3") }, blocks.Select(block => block.Id));
            Assert.Same(pillRow, blocks[2].Row);
            Assert.True(blocks[2].Row.TranslatePoint(default, mount.Column)!.Value.Y < blocks[3].Row.TranslatePoint(default, mount.Column)!.Value.Y);
        }, heads);
    }

    private static string? PillText(TimelineMount mount) => mount.Scroll.GetVisualDescendants().OfType<Button>().SelectMany(button => Texts(button)).FirstOrDefault(text => text.StartsWith("Show ", StringComparison.Ordinal));

    private static Button EarlierPill(TimelineMount mount) => mount.Scroll.GetVisualDescendants().OfType<Button>().Single(button => Texts(button).Any(text => text.StartsWith("Show ", StringComparison.Ordinal)));

    // The identity of the turn block of entry group `index` (a turn's block is named by the turn and its first row, ThreadTimeline.swift:1593).
    private static string BlockID(IReadOnlyList<TimelineEntry> entries, int index) => TurnID(entries[index * 3].TurnID!.Value, entries[index * 3].Id);

    private static void ToTheTop(TimelineMount mount) { mount.Scroll.Offset = default; mount.Advance(400); }

    private static void ToTheEnd(TimelineMount mount) { mount.Scroll.Offset = new Vector(0, Math.Max(0, mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height)); mount.Advance(400); }

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // ThreadTimeline.swift:1804-1811,81,257-268,384-391,595-608,703-707; PR-CHAT-191 (appended blocks add to the window)
    public void The_window_opens_at_three_blocks_anchors_on_the_newest_twenty_four_a_beat_later_pages_back_by_twenty_four_and_drops_back_past_ninety_six() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var entries = Turns(60, sentences: 14, unique: true).ToList(); // tall blocks: the newest three fill the pane, so the earlier pill is out of view at the open
        var finished = entries.Where(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Assistant).Select(entry => ((TimelineItem.Content.Assistant)entry.Item.TimelineItemContent).Value.Text).ToList();
        Assert.Equal(24, Marrow.UI.Markdown.MarkdownView.WarmTexts(finished).Count); // none parsed yet (at most 24 are asked for at a time, MarkdownView.swift:221-235)
        mount.SetEntries(entries);
        // :1805 firstPaint: the thread opens at once with its newest three blocks (:277), the rows in a plain stack (:388, eager, not virtualized).
        Assert.Equal(new[] { BlockID(entries, 57), BlockID(entries, 58), BlockID(entries, 59) }, mount.Blocks().Select(block => block.Id));
        Assert.Equal("Show 57 earlier messages", PillText(mount));
        // :599 the 250 ms beat: not a moment before.
        mount.Advance(249); Assert.Equal(3, mount.Blocks().Length);
        mount.Advance(1); mount.Advance(100);
        // :603 the window's start is the block 24 from the end of the list; :81 anchored by identity; every block in it is built (:384-391).
        Assert.Equal(BlockID(entries, 36), mount.Field<string?>("windowStartID"));
        Assert.Equal(24, mount.Blocks().Length);
        Assert.Equal("Show 36 earlier messages", PillText(mount));
        // The 24 landed above the viewport, the reader is still looking at the end.
        mount.Advance(2000);
        Assert.True(mount.Scroll.Extent.Height - (mount.Scroll.Offset.Y + mount.Scroll.Viewport.Height) < 2, "the reader stays on the end");
        // :289-297 every finished reply's markdown is parsed off the main thread (a real task: wait for it).
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Marrow.UI.Markdown.MarkdownView.WarmTexts(finished.Take(24)).Count != 0 && waited.ElapsedMilliseconds < 20000) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        // The pass takes the replies oldest first and no more than 24 of them (:289-297, MarkdownView.swift:221-235).
        Assert.Empty(Marrow.UI.Markdown.MarkdownView.WarmTexts(finished.Take(24)));
        Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.WarmTexts(finished.Skip(24)).Count); // replies 24 to 35: neither warmed (the cap) nor in the window (36 to 59, parsed by their own rows)
        // :263 each later page moves the start 24 blocks further back, floored at the first block.
        ToTheTop(mount); Assert.Equal(BlockID(entries, 12), mount.Field<string?>("windowStartID")); Assert.Equal(48, mount.Blocks().Length);
        ToTheTop(mount); Assert.Equal(BlockID(entries, 0), mount.Field<string?>("windowStartID")); Assert.Equal(60, mount.Blocks().Length);
        Assert.Null(PillText(mount));
        // :77-80 blocks appended at the end add to the window rather than pushing older rows out.
        entries.AddRange(Turns(5, "late-", sentences: 14)); mount.SetEntries(entries); mount.Advance(300);
        Assert.Equal(65, mount.Blocks().Length); Assert.Equal(BlockID(entries, 0), mount.Field<string?>("windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // ThreadTimeline.swift:703-707 (the followed run past eagerLimit drops back to the newest 24), :1808-1810
    public void A_followed_run_growing_past_ninety_six_blocks_drops_the_window_back_to_the_newest_twenty_four() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var entries = Turns(100, sentences: 10).ToList();
        mount.SetEntries(entries); mount.Settle();
        Assert.Equal(24, mount.Blocks().Length);
        // The reader pages back to the start of the thread: 76 blocks are hidden, so the window grows 24, 48, 72, 96, 100 blocks.
        for (var page = 0; page < 5 && mount.Blocks().Length < 100; page++) ToTheTop(mount);
        Assert.Equal(100, mount.Blocks().Length);
        // The reader comes back to the end and follows a new block landing there: the window holds more than 96, so it drops back to the newest 24 (the blocks above come back as history).
        ToTheEnd(mount);
        entries.AddRange(Turns(1, "new-", sentences: 10)); mount.SetEntries(entries); mount.Advance(400);
        Assert.Equal(24, mount.Blocks().Length);
        Assert.Equal(BlockID(entries, 101 - 24), mount.Field<string?>("windowStartID"));
        Assert.Equal("Show 77 earlier messages", PillText(mount));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // ThreadTimeline.swift:600 (a task cancelled within its 250 ms does nothing further)
    public void A_thread_closed_within_two_hundred_fifty_milliseconds_never_loads_its_window() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        mount.SetEntries(Turns(60, sentences: 14).ToList());
        mount.Advance(200);
        mount.Host.Child = null; mount.Advance(500);
        Assert.Null(mount.Field<string?>("windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-046")] // ThreadTimeline.swift:161-162,366-382 (the pill, its text, its tap and its 10 percent threshold), :258 (a load is refused while one is pending)
    public void The_earlier_pill_reads_Show_N_earlier_messages_and_loads_a_page_when_tapped_or_once_a_tenth_of_its_frame_is_on_screen() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var entries = Turns(60, sentences: 14).ToList();
        mount.SetEntries(entries); mount.Advance(2500);
        // :162 N is the count of blocks above the window's first block, no separator; the up chevron leads it; centred above the rows with 20 points over it.
        var pill = EarlierPill(mount);
        Assert.Equal("Show 36 earlier messages", PillText(mount));
        Assert.Equal("chevron.up", LogicalQuery.SelfAndDescendants<SwiftImage>(pill).Single().SymbolName);
        var top = pill.TranslatePoint(default, mount.Column)!.Value;
        Assert.Equal(Chrome.ContentTopInset + 20, top.Y, 3);
        Assert.Equal(mount.Column.Bounds.Width / 2, top.X + pill.Bounds.Width / 2, 0.5);
        Assert.Equal(top.Y + pill.Bounds.Height, mount.Rows[0].TranslatePoint(default, mount.Column)!.Value.Y, 3); // the rows follow it at once (the stack's spacing is 0, :360)
        // The count has no thousands separator.
        var big = (Button)typeof(ThreadTimeline).GetMethod("EarlierPill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, [1234, (Action)(() => { })])!;
        Assert.Equal("Show 1234 earlier messages", Texts(big).Single());
        // :366-367 the tap loads a page of 24; :258 a second tap before that page has been measured is refused.
        pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        mount.Advance(400);
        Assert.Equal(BlockID(entries, 12), mount.Field<string?>("windowStartID")); Assert.Equal("Show 12 earlier messages", PillText(mount));
        // :380-382 the reader comes back to it: nine hundredths of its frame (the pill and the 20 points over it, :379-380) on screen load nothing (that is more than a tenth of the pill itself), eleven hundredths do.
        pill = EarlierPill(mount); top = pill.TranslatePoint(default, mount.Column)!.Value;
        var frame = pill.Bounds.Height + pill.Margin.Top;
        mount.Scroll.Offset = new Vector(0, top.Y + pill.Bounds.Height - 0.09 * frame); mount.Advance(400);
        Assert.Equal("Show 12 earlier messages", PillText(mount));
        mount.Scroll.Offset = new Vector(0, top.Y + pill.Bounds.Height - 0.11 * frame); mount.Advance(400);
        Assert.Equal(BlockID(entries, 0), mount.Field<string?>("windowStartID")); Assert.Null(PillText(mount));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-046: a pill that is on screen when it appears loads its page at once (ThreadTimeline.swift:380-382), without any scroll
    public void A_short_conversation_growing_past_its_first_window_loads_the_earlier_blocks_by_itself() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        // A thread that opens with seven short messages: its newest three fit above the composer with the pill in view, which loads the rest at once.
        mount.SetEntries(Enumerable.Range(0, 7).Select(i => User("u" + i, null, "message " + i)).ToList()); mount.Advance(300);
        Assert.Equal(7, mount.Blocks().Length); Assert.Null(PillText(mount));
        // And one that grows a message at a time.
        for (var count = 8; count <= 12; count++)
        {
            mount.SetEntries(Enumerable.Range(0, count).Select(i => User("u" + i, null, "message " + i)).ToList()); mount.Advance(300);
            Assert.Equal(count, mount.Blocks().Length);
        }
        Assert.Null(PillText(mount));
    });

    private static TimelineMinimapColumn? Rail(TimelineMount mount) => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(mount.View).SingleOrDefault();

    private static void UpdateInputs(TimelineMount mount, bool showsMinimap, double columnHeight) =>
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, ["Project", null, false, columnHeight, showsMinimap]);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 (the rail's frame, its overlay and when it shows); ThreadTimeline.swift:176-208
    public void The_rail_is_a_thirty_point_overlay_below_the_titlebar_that_takes_no_layout_and_shows_with_two_sent_messages_and_the_flag() => ThreadTimelineRig.Run(headless, mount =>
    {
        // Fewer than two sent messages: no rail (:180).
        mount.Settle();
        mount.SetEntries(new[] { User("only", Guid.NewGuid(), "one message") }); mount.Settle();
        Assert.Null(Rail(mount));
        var turns = Turns(2, sentences: 14).ToList();
        mount.SetEntries(turns); mount.Settle();
        var rail = Rail(mount)!; Assert.NotNull(rail);
        // :191-193 30 wide, full height below the title bar (WindowChrome.titlebarHeight), at the leading edge of the pane.
        Assert.Equal(30, rail.Bounds.Width); Assert.Equal(WindowChrome.TitlebarHeight, rail.Bounds.Top, 3); Assert.Equal(mount.Scroll.Bounds.Height, rail.Bounds.Bottom, 3);
        Assert.Equal(0, rail.TranslatePoint(default, mount.Window)!.Value.X, 0.5);
        // :176 it floats: the rows are where they are with and without it.
        var rowX = mount.Rows[0].TranslatePoint(default, mount.Scroll)!.Value.X; var rowW = mount.Rows[0].Bounds.Width;
        // :187 the ticks centre in the full column height less the title bar, whatever the pane's own height is.
        var ticks = LogicalQuery.SelfAndDescendants<Button>(rail).ToList();
        Assert.Equal(2, ticks.Count);
        UpdateInputs(mount, true, 700); mount.Advance(100);
        rail = Rail(mount)!; ticks = LogicalQuery.SelfAndDescendants<Button>(rail).ToList();
        var middle = (ticks[0].TranslatePoint(default, rail)!.Value.Y + ticks[1].TranslatePoint(new Point(0, ticks[1].Bounds.Height), rail)!.Value.Y) / 2;
        Assert.Equal((700 - WindowChrome.TitlebarHeight) / 2, middle, 1);
        // :180 the flag: a panel docked on the leading side takes the rail's edge.
        UpdateInputs(mount, false, 700); mount.Advance(1000);
        Assert.Null(Rail(mount));
        Assert.Equal(rowX, mount.Rows[0].TranslatePoint(default, mount.Scroll)!.Value.X, 3); Assert.Equal(rowW, mount.Rows[0].Bounds.Width, 3);
        UpdateInputs(mount, true, 700); mount.Advance(1000);
        Assert.NotNull(Rail(mount));
        Assert.Equal(rowX, mount.Rows[0].TranslatePoint(default, mount.Scroll)!.Value.X, 3);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 and PR-CHAT-158 (the rail cancels the slide's padding and its offset); ThreadTimeline.swift:201-207
    public void The_rail_stays_on_the_columns_leading_edge_through_every_frame_of_a_panel_slide() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(2, sentences: 14).ToList()); mount.Settle();
        var rail = Rail(mount)!;
        // ReserveSlide (ChatView.swift:747-768) pads the timeline by the reserve at its final width at once and carries it over with a transform that glides to 0.
        foreach (var (leading, offset) in new[] { (0.0, 0.0), (160.0, -80.0), (160.0, -33.5), (160.0, 0.0), (0.0, 0.0), (240.0, 120.0) })
        {
            ChatView.SetReserveSlideLeading(mount.View, leading); ChatView.SetReserveSlideOffset(mount.View, offset);
            mount.View.Margin = new Thickness(leading, 0, 0, 0); mount.View.RenderTransform = new Avalonia.Media.TranslateTransform(offset, 0);
            mount.Advance(50);
            Assert.Equal(0, rail.TranslatePoint(default, mount.Window)!.Value.X, 0.5);
            // The conversation itself moved with the slide: its column is centred in what the padding leaves (the scroll view's own width is that).
            Assert.Equal(leading + offset + Math.Max(0, (mount.Scroll.Bounds.Width - 860) / 2) + 20, mount.Rows[0].TranslatePoint(default, mount.Window)!.Value.X, 0.6);
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003; ThreadTimeline.swift:179-200 (the rail fades with the panel slide; the same rail goes on across a redraw)
    public void The_rail_fades_in_and_out_on_the_panel_slide_and_survives_a_redraw_with_its_state() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(2, sentences: 14).ToList()); mount.Settle();
        var rail = Rail(mount)!;
        Assert.Equal(1, rail.Opacity);
        // A redraw that changes no tick: the same rail (its hover and its scrub go on), still fully drawn.
        mount.Model.Settings.ShowReasoning = !mount.Model.Settings.ShowReasoning; mount.Advance(100);
        Assert.Same(rail, Rail(mount));
        // :200 Chrome.panelSlide: the flag turning off fades the rail out over the slide, then it is gone.
        UpdateInputs(mount, false, 480);
        Assert.Same(rail, Rail(mount)); Assert.Equal(1, rail.Opacity);
        mount.Advance((int)(Chrome.PanelSlide.Duration * 1000 / 2)); Assert.InRange(rail.Opacity, 0.01, 0.99);
        mount.Advance((int)(Chrome.PanelSlide.Duration * 1000)); Assert.Null(Rail(mount));
        // And turning on again fades it in from nothing.
        UpdateInputs(mount, true, 480);
        var fresh = Rail(mount)!; Assert.Equal(0, fresh.Opacity);
        mount.Advance((int)(Chrome.PanelSlide.Duration * 1000 / 2)); Assert.InRange(fresh.Opacity, 0.01, 0.99);
        mount.Advance((int)(Chrome.PanelSlide.Duration * 1000)); Assert.Equal(1, fresh.Opacity);
        // :198-199 held while the window is dragged: no fade then.
        var resize = AppEnvironment.GetWindowLiveResize(mount.View); resize.IsPaneResizing = true;
        UpdateInputs(mount, false, 480); Assert.Null(Rail(mount));
        UpdateInputs(mount, true, 480); Assert.Equal(1, Rail(mount)!.Opacity);
        resize.IsPaneResizing = false;
        // A new message changes the ticks: the rail is the same place with new content, not a new rail fading in.
        var before = Rail(mount); mount.SetEntries(Turns(3, sentences: 14).ToList()); mount.Advance(100);
        Assert.NotNull(Rail(mount)); Assert.Equal(1, Rail(mount)!.Opacity); Assert.Equal(3, LogicalQuery.SelfAndDescendants<Button>(Rail(mount)!).Count());
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 (a tick's release is one 0.35 s smooth glide to the landing block, a scrub is unanimated); ThreadTimeline.swift:188,224-252
    public void A_jump_to_a_block_glides_over_a_third_of_a_second_and_a_scrub_lands_at_once() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(8, sentences: 14).ToList()); mount.Settle();
        var blocks = mount.Blocks(); var target = blocks[5].Id;
        var jump = typeof(ThreadTimeline).GetMethod("Jump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var all = (IReadOnlyList<DisplayBlock>)blocks.Select(block => block.Block).ToList();
        double Where() => mount.Scroll.Offset.Y;
        ToTheEnd(mount); var start = Where();
        jump.Invoke(mount.View, [target, all, false]); mount.Advance(0);
        var landed = Where();
        Assert.True(landed < start - 50, $"the scrub moved the view from {start} to {landed}");
        // Back to the end, then the release's glide.
        ToTheEnd(mount); start = Where(); Assert.Equal(mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height, start, 1);
        jump.Invoke(mount.View, [target, all, true]);
        var samples = new List<double> { Where() };
        for (var step = 0; step < 8; step++) { mount.Advance(50); samples.Add(Where()); }
        Assert.Equal(start, samples[0], 2);
        var travel = start - landed;
        Assert.True(samples.Zip(samples.Skip(1), (a, b) => a - b).All(delta => delta >= -0.01), "the glide goes one way");
        Assert.True(samples[1] > samples[2] && samples[2] > samples[4], $"it is under way at 100 and 200 ms: {string.Join(", ", samples)}");
        Assert.True(samples[3] < start - 0.2 * travel && samples[3] > landed + 0.1 * travel, $"mid-glide at 150 ms: {string.Join(", ", samples)}");
        // .smooth(duration: 0.35): a third of a second, not a snap and not a drift: most of the way left at 100 ms, the last tenth not yet gone at 250 ms.
        Assert.True(samples[2] - landed > 0.65 * travel, $"at 100 ms: {string.Join(", ", samples)}");
        Assert.InRange(samples[5] - landed, 0.05 * travel, 0.3 * travel);
        // It arrives by its duration, and settles into the block a moment after it.
        Assert.True(samples[7] - landed < 0.05 * travel, $"at 350 ms it is within a twentieth of the block: {string.Join(", ", samples)}");
        mount.Advance((int)(Animation.Smooth(0.35).Duration * 1000)); Assert.Equal(landed, Where(), 1);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-060")] // ThreadTimeline.swift:393-397,345,665-672,689-712; Chrome.swift:938-960 (softAppear); PR-CHAT-167/168 windows_path for the viewport refresh
    public void A_question_badge_follows_every_block_under_its_id_arrives_softly_brings_the_reader_to_the_end_and_leaves_with_a_quick_fade_when_answered() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(6, sentences: 10).ToList()); mount.Settle();
        // A reader who scrolled up to read: the jump button says something is below.
        mount.Scroll.Offset = new Vector(0, 300); mount.Advance(300); mount.State.ShowsJumpButton = true;
        Assert.True(mount.Scroll.Extent.Height - mount.Scroll.Offset.Y - mount.Scroll.Viewport.Height > 200);
        var first = new QuestionRequest("one", new([new QuestionRequest.Question("choice", "Header", "Choose", new([]), false, false, false)]));
        var second = new QuestionRequest("two", new([new QuestionRequest.Question("other", "Header", "Another", new([]), false, false, false)]));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(mount.Runtime, new ValueList<QuestionRequest>(new[] { first }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        // :345,:393-397 under the id question- plus the request id, after every block (and the badges after one another, in the runtime's order).
        var badge = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View).Single();
        Assert.Equal("question-one", badge.Name);
        var stack = Assert.IsType<StackPanel>(badge.Parent); Assert.Same(badge, stack.Children[^1]); Assert.Equal(6, stack.Children.Count - 1);
        // :396 it arrives with the soft appear: opacity 0 and 4 points down, settled over 0.32 s.
        Assert.Equal(0, badge.Opacity); Assert.Equal(4, Assert.IsType<Avalonia.Media.TranslateTransform>(badge.RenderTransform).Y);
        // :689-712 a question brings the reader to the end even from up there, and the jump button goes.
        mount.Advance((int)(Animation.SoftAppear.Duration * 1000) + 50);
        Assert.Equal(1, badge.Opacity); Assert.False(mount.State.ShowsJumpButton);
        Assert.True(mount.Scroll.Extent.Height - mount.Scroll.Offset.Y - mount.Scroll.Viewport.Height < 2, "the question is on screen at the end");
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(mount.Runtime, new ValueList<QuestionRequest>(new[] { first, second }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Advance(400);
        var badges = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View).ToList();
        Assert.Equal(new[] { "question-one", "question-two" }, badges.Select(row => row.Name)); var nowStack = Assert.IsType<StackPanel>(badges[0].Parent); Assert.Same(badges[0], nowStack.Children[^2]); Assert.Same(badges[1], nowStack.Children[^1]);
        // Answering one removes its badge, with the soft appear's removal: a quick 0.12 s fade (Chrome.swift:949-955), where it stood.
        mount.Runtime.Answer(first, new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>>()));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.Contains(badges[0], LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View));
        Assert.Equal(1, badges[0].Opacity);
        mount.Advance(100); Assert.InRange(badges[0].Opacity, 0.001, 0.99);
        mount.Advance(100);
        Assert.DoesNotContain(badges[0], LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View));
        Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View));
        // The last one answered: the badge goes, the reader is still at the end with rows on screen (the lazy stack's explicit viewport refresh has no work to do in an eager stack, PR-CHAT-167/168).
        mount.Runtime.Answer(second, new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>>()));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Advance(400);
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(mount.View));
        Assert.True(mount.Scroll.Extent.Height - mount.Scroll.Offset.Y - mount.Scroll.Viewport.Height < 2);
        Assert.Contains(mount.Blocks(), block => block.Row.TranslatePoint(default, mount.Scroll) is { } at && at.Y < mount.Scroll.Viewport.Height && at.Y + block.Row.Bounds.Height > 0);
    });
}
