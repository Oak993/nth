using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Nth;

// Calculi WU5 (DESIGN.md sections 5, 5.1, 5.2, 6, 9 and 14; ED-NEW-calculi-wu5-1): the working line's beads. The state mapping and the entrance rule,
// then the line in a real timeline: the 18 DIP beads in the running line's title, a step for each new tool description, the turn's end (the composer
// shows Send and the label says how it ended on the same frame, while the exit plays in the finished header), a new turn while the last is leaving
// (Enter on the same beads, never a reset) and the glyph's layout moves reported as reflows (a scroll is not one).
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
    [Trait("Design", "Calculi WU5")] // section 6: what the line says maps to one state
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
    [Trait("Design", "Calculi WU5")] // section 5: Scriptio after more than 2 minutes idle or on the first turn, Ortus otherwise
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
    [Trait("Design", "Calculi WU5")] // section 9: an 18 DIP n box in its 18.9 x 22.1 DIP envelope; the row makes room; a new tool description is a step
    public void The_running_line_shows_the_18_DIP_beads_and_each_new_tool_description_is_a_step() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(2, "old-", sentences: 3).ToList();
        entries.Add(User("prompt", Turn, "go"));
        Start(mount, entries);
        var title = Title(mount); var beads = Assert.IsType<NthBeadsControl>(title.Glyph);
        Assert.Same(NthWorkingBeads.For(mount.Runtime).Beads, beads);
        Assert.Equal(18, beads.GlyphSize);
        Assert.Equal(18.9, beads.Bounds.Width, 1); Assert.Equal(22.1, beads.Bounds.Height, 1);
        Assert.True(title.Bounds.Height >= beads.Bounds.Height - 1e-9, "the row makes room for the whole envelope");
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
    [Trait("Design", "Calculi WU5")] // section 5: the first turn of a conversation, and one after more than 2 minutes idle, opens with Scriptio
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
    [Trait("Design", "Calculi WU5")] // section 5.1: the UI follows the turn state, never the animation
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

    [Fact]
    [Trait("Design", "Calculi WU5")] // section 5.2: a new turn while the last is leaving calls Enter (Rejoin), never a reset
    public void A_new_turn_while_the_last_is_leaving_enters_on_the_same_beads_and_takes_them_to_the_new_line() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", Turn, "go"), ToolEntry("Running a command", Turn) };
        Start(mount, entries);
        var beads = Title(mount).Glyph!;
        entries.Add(EndEntry("end", Turn, TurnStatus.Completed));
        End(mount, entries); mount.Advance(50);
        Assert.Equal(NthBeadsExit.Finish, beads.Current.Exit);
        // a queued follow-up starts the next turn 50 ms into the exit
        entries.Add(User("prompt-2", Next, "and then"));
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        Assert.Equal(2, beads.Current.Turn); Assert.Equal(NthBeadsExit.None, beads.Current.Exit); Assert.Equal(NthBeadsEntrance.Ortus, beads.Current.Entrance);
        // the new line arrives with its fade; the beads come to it once it is whole (they never fade), the same control
        mount.Advance(1000);
        var title = Title(mount);
        Assert.Same(beads, title.Glyph);
        Assert.Equal(2, beads.Current.Turn);
    });

    [Fact]
    [Trait("Design", "Calculi WU5")] // section 9: layout moves of the glyph are reflows; a scroll is not
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
        var before = beads.TranslatePoint(default, mount.Column)!.Value;
        entries.Add(EndEntry("end", Turn, TurnStatus.Completed));
        End(mount, entries); mount.Advance(50);
        var after = beads.TranslatePoint(default, mount.Column)!.Value;
        Assert.True(Math.Abs(after.Y - before.Y) > .01, "the header holds the beads at another height than the pill did");
        Assert.Equal(reflows + 1, beads.Current.Reflows);
        var u = NthBeadsPainter.Unit(NthWorkingBeads.GlyphSize);
        Assert.Equal((after.X - before.X) / u, beads.Current.ReflowX, 6);
        Assert.Equal((after.Y - before.Y) / u, beads.Current.ReflowY, 6);
        Assert.InRange(Math.Abs(after.X - before.X), 0, .5); // the slot keeps the line's inset
    });
}
