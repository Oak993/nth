using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Nth;

// Calculi WU5 (DESIGN.md sections 5, 5.1, 5.2, 6, 9 and 14; ED-NEW-calculi-wu5-1): the working line's beads. The state mapping and the entrance rule,
// then the line in a real timeline: the 18 DIP beads in the running line's title, a step for each new tool description, the turn's end (the composer
// shows Send and the label says how it ended on the same frame, while the exit plays in the finished header), a new turn while the last is leaving
// (Enter on the same beads, never a reset) and the glyph's layout moves reported as reflows (a scroll is not one). ED-NEW-calculi-wu5-2: frame by
// frame, with the render thread's engine running, the beads never leave the screen, never draw under a fade and never move further than a reflow in
// a frame in which they draw: a queued follow-up (the exit ends in the old header, then the new line's entrance), a mid-turn split (the old line
// holds them unfaded while they leave) and the first turn (it enters once its arriving line is whole).
[Collection("Headless")]
public sealed class NthSitesBeadsTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("5eed0000-abcd-1234-abcd-123456789abc");
    private static readonly Guid Next = Guid.Parse("5eed0001-abcd-1234-abcd-123456789abc");

    private static TimelineItem Item(TimelineItem.Content content, Guid? turn = null, DateTimeOffset? date = null) => new(turn ?? Turn, content, null, date);
    private static TimelineItem UserItem() => Item(new TimelineItem.Content.User(new UserMessage { Text = "go" }));
    private static TimelineItem ToolItem(string title, ToolCall.Status status = ToolCall.Status.Running) =>
        Item(new TimelineItem.Content.Tool(new ToolCall { Title = title, ToolCallKind = ToolCall.Kind.Command, ToolCallStatus = status }));
    private static TimelineItem ReplyItem(bool streaming) => Item(new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Hi", IsStreaming = streaming }));
    private static TimelineItem ThoughtItem() => Item(new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "hm", IsStreaming = true }));

    private static TimelineEntry EndEntry(string id, Guid turn, TurnStatus status, DateTimeOffset? date = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = status, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), id, date));

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 6: what the line says maps to one state
    public void The_turn_maps_to_one_state_and_a_running_tool_carries_its_description()
    {
        static (NthBeadsState, string?) Of(RuntimePhase phase, params TimelineItem[] items) => NthWorkingBeads.StateOf(phase, false, false, items);
        Assert.Equal((NthBeadsState.Connecting, null), Of(RuntimePhase.Starting));
        Assert.Equal((NthBeadsState.Connecting, null), Of(RuntimePhase.Starting, UserItem()));
        Assert.Equal((NthBeadsState.Thinking, null), Of(RuntimePhase.Running, UserItem()));
        Assert.Equal((NthBeadsState.Thinking, null), Of(RuntimePhase.Running, UserItem(), ThoughtItem()));
        Assert.Equal((NthBeadsState.Tool, "Reading ThreadTimeline.cs"), Of(RuntimePhase.Running, UserItem(), ToolItem("Reading ThreadTimeline.cs")));
        // the newest running tool is the description; a finished one says nothing
        Assert.Equal((NthBeadsState.Tool, "b"), Of(RuntimePhase.Running, UserItem(), ToolItem("a"), ToolItem("b")));
        Assert.Equal((NthBeadsState.Thinking, null), Of(RuntimePhase.Running, UserItem(), ToolItem("a", ToolCall.Status.Completed)));
        Assert.Equal((NthBeadsState.Streaming, null), Of(RuntimePhase.Running, UserItem(), ToolItem("a", ToolCall.Status.Completed), ReplyItem(true)));
        Assert.Equal((NthBeadsState.Thinking, null), Of(RuntimePhase.Running, UserItem(), ReplyItem(false)));
        // a tool still running while the reply streams: the tool
        Assert.Equal((NthBeadsState.Tool, "a"), Of(RuntimePhase.Running, UserItem(), ToolItem("a"), ReplyItem(true)));
        // waiting for an approval or an answer, and Hydra heads at work, come first
        Assert.Equal((NthBeadsState.Waiting, null), NthWorkingBeads.StateOf(RuntimePhase.Running, true, true, [UserItem(), ToolItem("a")]));
        Assert.Equal((NthBeadsState.Hydra, null), NthWorkingBeads.StateOf(RuntimePhase.Running, false, true, [UserItem(), ToolItem("a")]));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 5: Scriptio after more than 2 minutes idle or on the first turn, Ortus otherwise
    public void Scriptio_opens_the_first_turn_and_any_turn_after_more_than_two_minutes_idle()
    {
        var at = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(NthBeadsEntrance.Scriptio, NthWorkingBeads.EntranceFor(null, at));
        Assert.Equal(NthBeadsEntrance.Scriptio, NthWorkingBeads.EntranceFor(at.AddMinutes(-3), at));
        Assert.Equal(NthBeadsEntrance.Scriptio, NthWorkingBeads.EntranceFor(at.AddMinutes(-2).AddMilliseconds(-1), at));
        Assert.Equal(NthBeadsEntrance.Ortus, NthWorkingBeads.EntranceFor(at.AddMinutes(-2), at));
        Assert.Equal(NthBeadsEntrance.Ortus, NthWorkingBeads.EntranceFor(at.AddSeconds(-30), at));
    }

    private static WorkingIndicator Line(TimelineMount mount) => LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();
    private static NthWorkingTitle Title(TimelineMount mount) => LogicalQuery.SelfAndDescendants<NthWorkingTitle>(Line(mount)).Single();

    private static void Start(TimelineMount mount, IEnumerable<TimelineEntry> entries)
    {
        mount.SetEntries(entries);
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
    }

    // The runtime's own turn end (ThreadRuntime.FinishTurn): the end entry appended, the turn start cleared and the phase set to idle in one run of the
    // main thread, then the dispatcher's jobs and one layout pass.
    private static void End(TimelineMount mount, IEnumerable<TimelineEntry> entries)
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(mount.Runtime, new ValueList<TimelineEntry>(entries));
        mount.SetTurnStartedAt(null);
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(mount.Runtime, RuntimePhase.Idle);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 9: an 18 DIP n box in its 18.9 x 22.1 DIP envelope; the row makes room; a new tool description is a step
    public void The_running_line_shows_the_18_DIP_beads_and_each_new_tool_description_is_a_step() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(2, "old-", sentences: 3).ToList();
        entries.Add(User("prompt", Turn, "go"));
        Start(mount, entries);
        var title = Title(mount); var beads = Assert.IsType<NthBeadsControl>(title.Glyph);
        Assert.Same(NthWorkingBeads.For(mount.Runtime).Beads, beads);
        Assert.Equal(18, beads.GlyphSize);
        Assert.Equal(18.9, beads.Bounds.Width, 1); Assert.Equal(22.1, beads.Bounds.Height, 1);
        // the envelope is centred on the word's line, and the pill around the line holds all of it (the row makes room: nothing clips a bead)
        var letters = LogicalQuery.SelfAndDescendants<NthLetters>(title).Single();
        Assert.Equal(letters.TranslatePoint(new Point(0, letters.Bounds.Height / 2), title)!.Value.Y, beads.TranslatePoint(new Point(0, beads.Bounds.Height / 2), title)!.Value.Y, 1);
        var pill = LogicalQuery.SelfAndDescendants<Border>(Line(mount)).Single(border => border.Child is StackPanel { Spacing: 10 });
        var top = beads.TranslatePoint(default, pill)!.Value;
        Assert.InRange(top.Y, 0, pill.Bounds.Height - beads.Bounds.Height); Assert.InRange(top.X, 0, pill.Bounds.Width - beads.Bounds.Width);
        var gap = LogicalQuery.SelfAndDescendants<NthLetters>(title).Single().TranslatePoint(default, title)!.Value.X - beads.TranslatePoint(default, title)!.Value.X - beads.Bounds.Width;
        Assert.Equal(8, gap, 3);
        // the turn has started: thinking, entered once (the old turns ended moments ago: Ortus)
        Assert.Equal(1, beads.Current.Turn); Assert.Equal(NthBeadsState.Thinking, beads.Current.State); Assert.Equal(NthBeadsEntrance.Ortus, beads.Current.Entrance);
        Assert.Equal(NthBeadsExit.None, beads.Current.Exit);
        // a tool starts: the state changes, no step; a second tool (a new description) is one step; the same description again is none
        entries.Add(ToolEntry("Reading ThreadTimeline.cs", Turn)); mount.SetEntries(entries); mount.Advance(100);
        Assert.Equal(NthBeadsState.Tool, beads.Current.State); Assert.Equal(0, beads.Current.Steps);
        entries.Add(ToolEntry("Editing NthBeadsPainter.cs", Turn)); mount.SetEntries(entries); mount.Advance(100);
        Assert.Equal(1, beads.Current.Steps);
        mount.SetEntries(entries.ToList()); mount.Advance(100);
        Assert.Equal(1, beads.Current.Steps);
        Assert.Equal(1, beads.Current.Turn); // never entered again while the turn runs
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 5: the first turn of a conversation, and one after more than 2 minutes idle, opens with Scriptio
    public void A_conversations_first_turn_and_a_turn_after_a_long_idle_open_with_Scriptio() => ThreadTimelineRig.Run(headless, mount =>
    {
        Start(mount, [User("prompt", Turn, "go")]);
        Assert.Equal(NthBeadsEntrance.Scriptio, Title(mount).Glyph!.Current.Entrance);
        // the turn ends; the next one starts three minutes after it ended
        End(mount, [User("prompt", Turn, "go"), EndEntry("end", Turn, TurnStatus.Completed, SwiftDate.Now.AddMinutes(-3))]);
        mount.Advance(100);
        Start(mount, [User("prompt", Turn, "go"), EndEntry("end", Turn, TurnStatus.Completed, SwiftDate.Now.AddMinutes(-3)), User("prompt-2", Next, "again")]);
        var beads = NthWorkingBeads.For(mount.Runtime).Beads;
        Assert.Equal(2, beads.Current.Turn); Assert.Equal(NthBeadsEntrance.Scriptio, beads.Current.Entrance);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 5.1: the UI follows the turn state, never the animation
    [InlineData(TurnStatus.Completed, NthBeadsExit.Finish, "Worked for ")]
    [InlineData(TurnStatus.Failed, NthBeadsExit.Fail, "Failed after ")]
    [InlineData(TurnStatus.Interrupted, NthBeadsExit.Stop, "Stopped after ")]
    public void The_composer_shows_Send_and_the_label_says_how_the_turn_ended_on_the_frame_it_ends_while_the_exit_plays_in_the_header(TurnStatus status, NthBeadsExit exit, string label) =>
        ThreadTimelineRig.Run(headless, mount =>
        {
            var entries = new List<TimelineEntry> { User("prompt", Turn, "go"), ToolEntry("Running a command", Turn) };
            Start(mount, entries);
            var send = new SendRunState(mount.Runtime, false, false, () => { });
            var composer = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = send };
            composer.Show();
            try
            {
                var face = LogicalQuery.SelfAndDescendants<NthSendFace>(send).Single();
                Assert.True(face.IsRunning);
                var beads = Title(mount).Glyph!;
                // The turn ends. No frame passes: the dispatcher's jobs and one layout pass, as the runtime's own turn end runs them.
                entries.Add(EndEntry("end", Turn, status));
                End(mount, entries);
                composer.UpdateLayout();
                Assert.False(LogicalQuery.SelfAndDescendants<NthSendFace>(send).Single().IsRunning); // Send, at once
                var finished = LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(mount.View).Single();
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(finished), text => text.Text?.StartsWith(label, StringComparison.Ordinal) == true);
                Assert.Empty(mount.View.GetVisualDescendants().OfType<NthWorkingTitle>()); // the running line is gone at once
                // the exit is cosmetic and plays beside them, on the same beads, in the finished header's slot
                Assert.Equal(exit, beads.Current.Exit);
                Assert.Same(finished.BeadsSlot, beads.Parent);
                Assert.True(beads.IsEffectivelyVisible);
                for (Visual? at = beads; at is not null; at = at.GetVisualParent()) Assert.Equal(1, at.Opacity); // the header fades in; the beads never fade
            }
            finally { composer.Close(); }
        });

    // One 60 Hz frame: the clock, the frames and layout, then a render tick (the render thread's engine applies what was posted and advances).
    private static void Frame(TimelineMount mount) { mount.Advance(16); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    // Whether the beads draw anything this frame (the render thread's engine is on a turn).
    private static bool Draws(NthBeadsControl beads) =>
        beads.IsAttachedToVisualTree() && beads.IsEffectivelyVisible && beads.Painter?.Driver.Engine is { Mode: not NthBeadsMode.Gone };

    // Frame by frame (ED-NEW-calculi-wu5-2): the beads never leave the screen, never draw under a fade (every ancestor at opacity 1), and never move
    // further than a reflow (whose glide keeps their place on screen) in a frame in which they draw, before or after it. Returns how many frames drew.
    private static int Watch(TimelineMount mount, NthBeadsControl beads, int frames, Action<int>? each = null)
    {
        Point? last = null; var lastDrew = false; var drawn = 0;
        for (var i = 0; i < frames; i++)
        {
            each?.Invoke(i);
            Frame(mount);
            Assert.True(beads.IsAttachedToVisualTree(), $"frame {i}: the beads left the screen");
            var drew = Draws(beads); if (drew) drawn++;
            if (drew)
                for (Visual? at = beads; at is not null; at = at.GetVisualParent())
                    Assert.True(at.Opacity == 1, $"frame {i}: the beads draw under a fade ({at.GetType().Name} at {at.Opacity})");
            var place = beads.TranslatePoint(default, mount.Column)!.Value;
            if (last is { } was && (drew || lastDrew))
            {
                Assert.True(Math.Abs(place.X - was.X) <= NthWorkingBeads.ReflowLimit && Math.Abs(place.Y - was.Y) <= NthWorkingBeads.ReflowLimit,
                    $"frame {i}: the drawn beads jumped from {was} to {place}");
            }
            last = place; lastDrew = drew;
        }
        return drawn;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // sections 5, 5.2 and 9: a queued follow-up; the beads never jump and never fade (ED-NEW-calculi-wu5-2)
    public void A_queued_follow_up_lets_the_exit_end_in_the_old_header_and_enters_the_same_beads_in_the_new_line_without_a_jump() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(1, "old-", sentences: 4).ToList();
        entries.Add(User("prompt", Turn, "go")); entries.Add(ToolEntry("Running a command", Turn));
        Start(mount, entries);
        var beads = Title(mount).Glyph!;
        Watch(mount, beads, 10);
        Assert.True(Draws(beads), "the turn draws");
        entries.Add(EndEntry("end", Turn, TurnStatus.Completed));
        entries.Add(Reply("answer", Turn, "Done. " + string.Join(" ", Enumerable.Repeat("The answer runs over several lines of the finished block.", 8))));
        End(mount, entries);
        var header = LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(mount.View).Single(block => block.BeadsSlot is not null).BeadsSlot;
        Assert.Same(header, beads.Parent);
        Watch(mount, beads, 3); // 50 ms into the exit
        Assert.Equal(NthBeadsExit.Finish, beads.Current.Exit);
        // a queued follow-up starts the next turn: the UI shows it at once; the beads finish where they are, then the new line's entrance plays
        entries.Add(User("prompt-2", Next, "and then"));
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        var goneInHeader = false;
        Watch(mount, beads, 150, _ => goneInHeader |= ReferenceEquals(beads.Parent, header) && !Draws(beads) && beads.Painter?.Driver.Engine is { Mode: NthBeadsMode.Gone });
        Assert.True(goneInHeader, "the exit ended in the old header before the beads moved");
        var title = Title(mount);
        Assert.Same(beads, title.Glyph); // the same control, in the new line
        Assert.Equal(2, beads.Current.Turn); Assert.Equal(NthBeadsExit.None, beads.Current.Exit); Assert.Equal(NthBeadsEntrance.Ortus, beads.Current.Entrance);
        Assert.True(Draws(beads), "the new turn draws in the new line");
        var gap = Math.Abs(beads.TranslatePoint(default, mount.Column)!.Value.Y - header!.TranslatePoint(default, mount.Column)!.Value.Y);
        Assert.True(gap > NthWorkingBeads.ReflowLimit, $"the new line is far from the old header ({gap} DIP): this is the far case");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // sections 5 and 9: a Hydra note splits the running line; the beads never fade and never jump (ED-NEW-calculi-wu5-2)
    public void A_line_split_off_mid_turn_holds_its_beads_unfaded_while_they_leave_and_the_new_line_enters_them() => ThreadTimelineRig.Run(headless, mount =>
    {
        var t0 = SwiftDate.Now.AddSeconds(-30);
        TimelineEntry Dated(string id, TimelineItem.Content content, Guid? turn, int second) => new(new TimelineItem(turn, content, id, t0.AddSeconds(second)));
        TimelineItem.Content Tool(string title) => new TimelineItem.Content.Tool(new ToolCall { Title = title, ToolCallKind = ToolCall.Kind.Command, ToolCallStatus = ToolCall.Status.Running });
        var entries = new List<TimelineEntry> { Dated("prompt", new TimelineItem.Content.User(new UserMessage { Text = "go" }), Turn, 0), Dated("tool-1", Tool("Reading ThreadTimeline.cs"), Turn, 1) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(t0); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var beads = Title(mount).Glyph!;
        Watch(mount, beads, 10);
        Assert.True(Draws(beads), "the turn draws");
        // a note (no turn) arrives mid-turn and the turn goes on after it: the running line is split off and a new segment's line runs below the note
        entries.Add(Dated("note", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "A head reports back. " + string.Join(" ", Enumerable.Repeat("Its note runs over a few lines.", 6)) }), null, 2));
        entries.Add(Dated("tool-2", Tool("Editing NthBeadsPainter.cs"), Turn, 3));
        var exited = false;
        Watch(mount, beads, 150, i => { if (i == 0) mount.SetEntries(entries); exited |= beads.Current.Exit == NthBeadsExit.Finish; });
        Assert.True(exited, "the beads left the split-off line with their exit");
        var running = mount.View.GetVisualDescendants().OfType<NthWorkingTitle>().Single();
        Assert.Same(beads, running.Glyph); // the same control, in the new segment's line
        Assert.Equal(2, beads.Current.Turn); Assert.Equal(NthBeadsExit.None, beads.Current.Exit); Assert.Equal(NthBeadsEntrance.Ortus, beads.Current.Entrance);
        Assert.Equal(NthBeadsState.Tool, beads.Current.State);
        Assert.True(Draws(beads), "the turn draws in the new line");
        // the split-off line keeps its hold (at no height); once the beads left it, its room closed and the label glided to the place a view made
        // again gives it, the leading edge (ED-NEW-calculi-rv-1; it stood at NthBeadsHold.Indent, where the running word stood, while the beads were there)
        var hold = mount.View.GetVisualDescendants().OfType<NthBeadsHold>().Single();
        Assert.Empty(hold.Slot.Children);
        var label = ((Panel)hold.GetVisualParent()!).Children.OfType<HydraWorkingTitle>().Single();
        Assert.Equal(0, label.Bounds.X, 1);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // section 5: pair A's entrance draws at opacity 1; the first turn enters once its arriving line is whole (ED-NEW-calculi-wu5-2)
    public void The_first_turn_enters_once_its_arriving_line_is_whole_so_Scriptio_never_draws_under_a_fade() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var beads = NthWorkingBeads.For(mount.Runtime).Beads;
        var faded = false;
        mount.SetEntries([User("prompt", Turn, "go")]); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        for (var i = 0; i < 90; i++)
        {
            Frame(mount);
            if (!beads.IsAttachedToVisualTree()) continue;
            var whole = true;
            for (Visual? at = beads; at is not null; at = at.GetVisualParent()) whole &= at.Opacity == 1;
            if (!whole) { faded = true; Assert.Equal(0, beads.Current.Turn); } // the line is still arriving: not entered yet
            if (Draws(beads)) Assert.True(whole, $"frame {i}: the entrance draws under the line's arrival fade");
        }
        Assert.True(faded, "the line arrived with a fade (the case under test)");
        Assert.Equal(1, beads.Current.Turn); Assert.Equal(NthBeadsEntrance.Scriptio, beads.Current.Entrance);
        Assert.Same(beads, Title(mount).Glyph);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-rv-1: the finished header's room for the exit closes once the exit has ended; the label glides, never snaps
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Interrupted)]
    public void Once_the_exit_has_ended_the_finished_headers_label_glides_to_the_leading_edge_where_a_view_made_again_puts_it(TurnStatus status) =>
        ThreadTimelineRig.Run(headless, mount =>
        {
            var entries = new List<TimelineEntry> { User("prompt", Turn, "go"), ToolEntry("Running a command", Turn) };
            Start(mount, entries);
            var beads = Title(mount).Glyph!;
            Watch(mount, beads, 10);
            entries.Add(EndEntry("end", Turn, status));
            End(mount, entries);
            var block = LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(mount.View).Single();
            var slot = block.BeadsSlot!;
            var header = ((StackPanel)slot.GetVisualParent()!).Children[1];
            double X() => header.TranslatePoint(default, block)!.Value.X;
            var start = X();
            Assert.True(start > 30, $"the label stands after the beads' slot at the hand-off ({start:0.0} DIP)");
            var last = start; var glided = 0; var steps = 0.0;
            for (var i = 0; i < 60 * 5; i++)
            {
                Frame(mount);
                var x = X();
                if (Draws(beads)) Assert.Equal(start, x, 3); // while the exit plays the label stands still beside it
                Assert.True(x <= last + 1e-6, $"frame {i}: the label moved back ({last:0.00} to {x:0.00})");
                Assert.True(last - x <= .16 * start, $"frame {i}: the label snapped {last - x:0.00} DIP of {start:0.0} in one frame");
                if (x < last - 1e-6) glided++;
                steps = Math.Max(steps, last - x); last = x;
            }
            Assert.True(glided >= 10, $"the label glided over {glided} frames");
            Assert.Equal(0, last, 2); // the leading edge: a finished header made again has no slot, its label at 0
            Assert.Equal(0, slot.Bounds.Width, 2); Assert.Equal(0, slot.Bounds.Height, 2);
            Assert.False(Draws(beads), "the exit has ended");
        });

    // Where layout put a visual in an ancestor: its arranged places only (a render transform, such as a row's arrival offset, moves the beads rigidly
    // and is not a layout move).
    private static Point Place(Visual visual, Visual root)
    {
        double x = 0, y = 0;
        for (var at = visual; !ReferenceEquals(at, root); at = at.GetVisualParent()!) { x += at.Bounds.X; y += at.Bounds.Y; }
        return new Point(x, y);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu5-1; section 9: layout moves of the glyph are reflows; a scroll is not
    public void The_glyphs_layout_moves_are_reported_as_reflows_and_a_scroll_is_not() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(8, "old-", sentences: 10).ToList();
        entries.Add(User("prompt", Turn, "go")); entries.Add(ToolEntry("Running a command", Turn));
        Start(mount, entries);
        var beads = Title(mount).Glyph!;
        var reflows = beads.Current.Reflows;
        // a scroll moves the glyph rigidly: no reflow
        mount.Scroll.Offset = new Vector(0, Math.Max(0, mount.Scroll.Offset.Y - 120)); mount.Advance(100);
        Assert.Equal(reflows, beads.Current.Reflows);
        // the turn ends: the beads move from the line's pill to the finished header, a layout move: one reflow, straight up or down (same column)
        var before = Place(beads, mount.Column); var (reflowX, reflowY) = (beads.Current.ReflowX, beads.Current.ReflowY); // the frame keeps a running total
        entries.Add(EndEntry("end", Turn, TurnStatus.Completed));
        End(mount, entries); mount.Advance(50);
        var after = Place(beads, mount.Column);
        Assert.True(Math.Abs(after.Y - before.Y) > .01, "the header holds the beads at another height than the pill did");
        Assert.Equal(reflows + 1, beads.Current.Reflows);
        var u = NthBeadsPainter.Unit(NthWorkingBeads.GlyphSize);
        Assert.Equal((after.X - before.X) / u, beads.Current.ReflowX - reflowX, 6);
        Assert.Equal((after.Y - before.Y) / u, beads.Current.ReflowY - reflowY, 6);
        Assert.InRange(Math.Abs(after.X - before.X), 0, .5); // the slot keeps the line's inset
    });
}
