using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W6 (DESIGN.md 3.9 and 13.2 "Side chat seeding: snapshot content and limits; never writes to the parent"). The
// snapshot is the last 6 turns verbatim with tool output trimmed to 2k characters each, an earlier-turns digest made once and kept, the
// current plan and todos, the changed files and what the main run is doing now. It is a pure function of the main thread's items.
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class SideChatSeedTests
{
    private static TurnRecord Turn(long index, TurnStatus status = TurnStatus.Completed, params string[] touched) =>
        new() { Index = index, Status = status, TouchedPaths = touched.Length == 0 ? null : new ValueList<string>(touched) };

    private static TimelineItem User(TurnRecord turn, string text) => new(turn.Id, new TimelineItem.Content.User(new UserMessage { Text = text }));

    private static TimelineItem Reply(TurnRecord turn, string text, bool streaming = false) =>
        new(turn.Id, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }));

    private static TimelineItem Tool(TurnRecord turn, ToolCall.Kind kind, string title, string output = "", ToolCall.Status status = ToolCall.Status.Completed, params FileEdit[] edits) =>
        new(turn.Id, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = kind, Title = title, Output = output, ToolCallStatus = status, Edits = new ValueList<FileEdit>(edits),
        }));

    // A thread of `count` turns, each a question, a command with output and a reply.
    private static (List<TurnRecord> Turns, List<TimelineItem> Items) Thread(int count, int outputLength = 50)
    {
        var turns = new List<TurnRecord>();
        var items = new List<TimelineItem>();
        for (var i = 0; i < count; i++)
        {
            var turn = Turn(i);
            turns.Add(turn);
            items.Add(User(turn, $"Question {i + 1}: what about part {i + 1}?"));
            items.Add(Tool(turn, ToolCall.Kind.Command, $"run step {i + 1}", new string('o', outputLength)));
            items.Add(Reply(turn, $"Answer {i + 1}: part {i + 1} is done."));
        }
        return (turns, items);
    }

    private static SideChatMain MainState(List<TurnRecord> turns, List<TimelineItem> items, SideChatMainPhase phase = SideChatMainPhase.Idle,
        int approvals = 0, bool waiting = false, string? root = null) =>
        new("Fix the hero", phase, items, turns, approvals, waiting, "Landing", "feature/hero", root);

    private static SideChatSnapshot Build(SideChatMain main, SideChatDigest? digest = null, SideChatLimits? limits = null) =>
        SideChatSeed.Build(main, digest ?? new SideChatDigest(), limits);

    [Fact]
    public void The_snapshot_names_the_thread_its_project_and_branch_and_says_what_the_run_is_doing()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(1);
        var text = Build(MainState(turns, items)).Text;
        Assert.StartsWith("Main thread: \"Fix the hero\", project \"Landing\", branch feature/hero\n", text, StringComparison.Ordinal);
        Assert.Contains("Main run: idle, waiting for the person's next message.", text, StringComparison.Ordinal);
        Assert.Contains("Question 1: what about part 1?", text, StringComparison.Ordinal);
        Assert.Contains("Answer 1: part 1 is done.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_six_turns_are_given_in_full_and_the_earlier_ones_as_digest_lines()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(10);
        var snapshot = Build(MainState(turns, items));
        Assert.Equal(6, snapshot.VerbatimTurns);
        Assert.Equal(4, snapshot.SummarisedTurns);
        Assert.False(snapshot.WasShortened);
        for (var i = 5; i <= 10; i++) Assert.Contains($"Question {i}: what about part {i}?", snapshot.Text, StringComparison.Ordinal);
        for (var i = 1; i <= 4; i++)
        {
            Assert.DoesNotContain($"Question {i}: what about part {i}?\n", snapshot.Text, StringComparison.Ordinal);
            Assert.Contains($"Turn {i} (completed): asked \"Question {i}: what about part {i}?\"; answered \"Answer {i}: part {i} is done.\"; 1 tool call", snapshot.Text, StringComparison.Ordinal);
        }
        Assert.Contains("Earlier turns, summarised (4):", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("The last 6 turns, in full:", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_output_is_trimmed_to_two_thousand_characters_keeping_its_head_and_its_longer_tail()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0);
        var output = "HEAD-" + new string('x', 8_000) + "-TAIL";
        var snapshot = Build(MainState([turn], [User(turn, "run it"), Tool(turn, ToolCall.Kind.Command, "npm test", output), Reply(turn, "done")]));
        Assert.Contains("HEAD-", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("-TAIL", snapshot.Text, StringComparison.Ordinal);
        Assert.Matches(@"\[… \d+ characters omitted …\]", snapshot.Text);
        var shown = snapshot.Text.Split('\n').Where(line => line.TrimStart().StartsWith("HEAD-", StringComparison.Ordinal) || line.Trim().All(c => c == 'x') && line.Trim().Length != 0 || line.Contains("-TAIL", StringComparison.Ordinal))
            .Sum(line => line.Trim().Length);
        Assert.InRange(shown, 1_500, 2_100);
    }

    [Fact]
    public void The_earlier_turns_summary_is_made_once_and_read_back_after_that()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(8);
        var digest = new SideChatDigest();
        var first = Build(MainState(turns, items), digest);
        Assert.Equal(2, digest.Made);

        // Turn 1's words change (they cannot in a real thread; the cache is what is under test): the line is the one made before.
        var edited = items.Select(item => item.TurnID == turns[0].Id && item.TimelineItemContent is TimelineItem.Content.User
            ? item with { TimelineItemContent = new TimelineItem.Content.User(new UserMessage { Text = "CHANGED" }) }
            : item).ToList();
        var second = Build(MainState(turns, edited), digest);
        Assert.Equal(2, digest.Made);
        Assert.DoesNotContain("CHANGED", second.Text, StringComparison.Ordinal);

        // A turn that leaves the verbatim window is made once, the first time it needs a line.
        var (more, moreItems) = Thread(9);
        var third = Build(MainState(more, moreItems), new SideChatDigest());
        Assert.Contains("Earlier turns, summarised (3):", third.Text, StringComparison.Ordinal);
        Assert.Equal(2, first.SummarisedTurns);
    }

    [Fact]
    public void A_turn_the_thread_no_longer_has_is_forgotten_by_the_digest()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(9);
        var digest = new SideChatDigest();
        Build(MainState(turns, items), digest);
        Assert.Equal(3, digest.Count);
        var (fewer, fewerItems) = Thread(7);
        Build(MainState(fewer, fewerItems), digest);
        Assert.Equal(1, digest.Count);
    }

    [Fact]
    public void The_current_plan_and_todos_are_in_the_snapshot_and_a_dismissed_plan_is_not()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0);
        var items = new List<TimelineItem>
        {
            User(turn, "plan it"),
            new(turn.Id, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "1. old", ProposedPlanState = ProposedPlan.State.Accepted })),
            new(turn.Id, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "1. dropped", ProposedPlanState = ProposedPlan.State.Dismissed })),
            new(turn.Id, new TimelineItem.Content.Todos(new ValueList<TodoStep>([new TodoStep { Text = "first", TodoStepStatus = TodoStep.Status.Done }]))),
            new(turn.Id, new TimelineItem.Content.Todos(new ValueList<TodoStep>([
                new TodoStep { Text = "first", TodoStepStatus = TodoStep.Status.Done },
                new TodoStep { Text = "second", TodoStepStatus = TodoStep.Status.Active },
                new TodoStep { Text = "third", TodoStepStatus = TodoStep.Status.Pending },
            ]))),
        };
        var text = Build(MainState([turn], items)).Text;
        Assert.Contains("Plan (approved):\n1. old", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.Contains("Todos:\n- [done] first\n- [doing] second\n- [to do] third\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_changed_files_are_the_touched_paths_and_the_edits_once_each_relative_to_the_folder()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0, TurnStatus.Completed, "src/Hero.css", "README.md");
        var other = Turn(1, TurnStatus.Completed, "src/Hero.css");
        var items = new List<TimelineItem>
        {
            User(turn, "edit"),
            Tool(turn, ToolCall.Kind.Edit, "Edit Hero.css", "", ToolCall.Status.Completed, new FileEdit(@"C:\work\site\src\Hero.css", "diff", 3, 1), new FileEdit(@"C:\work\site\src\Nav.css", "diff", 1, 0)),
            User(other, "again"),
        };
        var snapshot = Build(MainState([turn, other], items, root: @"C:\work\site"));
        Assert.Equal(["src/Hero.css", "README.md", "src/Nav.css"], snapshot.ChangedFiles);
        Assert.Contains("Changed files (3):\n- src/Hero.css\n- README.md\n- src/Nav.css\n", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("edited src/Hero.css (+3 -1)", snapshot.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_changed_files_list_is_cut_and_counted()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0, TurnStatus.Completed, Enumerable.Range(0, 130).Select(i => $"src/file{i:000}.cs").ToArray());
        var text = Build(MainState([turn], [User(turn, "many")])).Text;
        Assert.Contains("Changed files (130):", text, StringComparison.Ordinal);
        Assert.Contains("- src/file119.cs\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/file120.cs", text, StringComparison.Ordinal);
        Assert.Contains("- and 10 more", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_run_is_doing_is_stated_for_every_state_it_can_be_in()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0, TurnStatus.Running);
        string Activity(List<TimelineItem> items, SideChatMainPhase phase, int approvals = 0, bool waiting = false) =>
            Build(MainState([turn], items, phase, approvals, waiting)).Text.Split('\n').First(line => line.StartsWith("Main run:", StringComparison.Ordinal));

        Assert.Equal("Main run: working, running a command: npm test.",
            Activity([User(turn, "go"), Tool(turn, ToolCall.Kind.Command, "npm test", "", ToolCall.Status.Running)], SideChatMainPhase.Running));
        Assert.Equal("Main run: working, reading: src/Hero.css.",
            Activity([User(turn, "go"), Tool(turn, ToolCall.Kind.Read, "src/Hero.css", "", ToolCall.Status.Running)], SideChatMainPhase.Running));
        Assert.Equal("Main run: working, writing its reply.", Activity([User(turn, "go"), Reply(turn, "So far", streaming: true)], SideChatMainPhase.Running));
        Assert.Equal("Main run: working, thinking.",
            Activity([User(turn, "go"), new(turn.Id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "hm", IsStreaming = true }))], SideChatMainPhase.Running));
        Assert.Equal("Main run: working.", Activity([User(turn, "go")], SideChatMainPhase.Running));
        Assert.Equal("Main run: starting a turn.", Activity([User(turn, "go")], SideChatMainPhase.Starting));
        Assert.Equal("Main run: waiting for the person to answer a request.", Activity([User(turn, "go")], SideChatMainPhase.Running, approvals: 1));
        Assert.Equal("Main run: waiting for the person to answer 3 requests.", Activity([User(turn, "go")], SideChatMainPhase.Running, approvals: 3));
        Assert.Equal("Main run: waiting for the person to answer its question.", Activity([User(turn, "go")], SideChatMainPhase.Running, waiting: true));
        Assert.Equal("Main run: working.", Activity([User(turn, "go"), Tool(turn, ToolCall.Kind.Command, "done already", "", ToolCall.Status.Completed)], SideChatMainPhase.Running));
        Assert.Equal("Main run: idle; no turn has run yet.", Build(MainState([], [])).Text.Split('\n')[1]);
    }

    [Fact]
    public void Reasoning_is_not_part_of_the_snapshot_and_a_helpers_report_is_named()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0);
        var items = new List<TimelineItem>
        {
            User(turn, "hello"),
            new(turn.Id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "PRIVATE-THOUGHT" })),
            new(turn.Id, new TimelineItem.Content.User(new UserMessage { Text = "head says done", HydraHeads = new ValueList<long>([1L]) })),
            Reply(turn, "ok"),
        };
        var text = Build(MainState([turn], items)).Text;
        Assert.DoesNotContain("PRIVATE-THOUGHT", text, StringComparison.Ordinal);
        Assert.Contains("A helper's report:\nhead says done", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_over_its_bound_gives_up_tool_output_first_and_stays_inside_it()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(12, outputLength: 9_000);
        var limits = SideChatLimits.Default with { SnapshotCharacters = 14_000 };
        var snapshot = Build(MainState(turns, items), limits: limits);
        Assert.True(snapshot.WasShortened);
        Assert.InRange(snapshot.Text.Length, 1, 14_000);
        // The tool output gave way and the turns did not: six in full, each output cut to 600 characters or fewer.
        Assert.Equal(6, snapshot.VerbatimTurns);
        Assert.Equal(6, snapshot.SummarisedTurns);
        var longestRun = snapshot.Text.Split('\n').Max(line => line.Trim().All(c => c == 'o') ? line.Trim().Length : 0);
        Assert.InRange(longestRun, 1, 600);
        Assert.Contains("Question 12: what about part 12?", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("Turn 1 (completed): asked", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_still_over_its_bound_keeps_fewer_turns_in_full_and_the_newest_always()
    {
        using var runtime = new TestRuntime();
        var turns = new List<TurnRecord>();
        var items = new List<TimelineItem>();
        for (var i = 0; i < 10; i++)
        {
            var turn = Turn(i);
            turns.Add(turn);
            items.Add(User(turn, $"Question {i + 1} " + new string('q', 5_000)));
            items.Add(Reply(turn, $"Answer {i + 1} " + new string('a', 5_000)));
        }
        var limits = SideChatLimits.Default with { SnapshotCharacters = 12_000 };
        var snapshot = Build(MainState(turns, items), limits: limits);
        Assert.True(snapshot.WasShortened);
        Assert.InRange(snapshot.Text.Length, 1, 12_000);
        Assert.InRange(snapshot.VerbatimTurns, 1, 5);
        Assert.Equal(10, snapshot.VerbatimTurns + snapshot.SummarisedTurns);
        Assert.Contains("Question 10 ", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("Turn 1 (completed): asked", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_nothing_can_meet_cuts_the_text_with_a_note()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0);
        var snapshot = Build(MainState([turn], [User(turn, new string('q', 40_000)), Reply(turn, new string('a', 40_000))]), limits: SideChatLimits.Default with { SnapshotCharacters = 2_000 });
        Assert.True(snapshot.WasShortened);
        Assert.InRange(snapshot.Text.Length, 1, 2_100);
        Assert.Contains("characters omitted", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_is_cut_at_the_message_bound_and_a_surrogate_pair_is_never_split()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0);
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 9_000));
        var snapshot = Build(MainState([turn], [User(turn, emoji)]));
        Assert.DoesNotContain("\uFFFD", snapshot.Text, StringComparison.Ordinal);
        for (var i = 0; i < snapshot.Text.Length; i++)
        {
            if (char.IsHighSurrogate(snapshot.Text[i])) Assert.True(i + 1 < snapshot.Text.Length && char.IsLowSurrogate(snapshot.Text[++i]));
            else Assert.False(char.IsLowSurrogate(snapshot.Text[i]));
        }
        Assert.Contains("characters omitted", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_thread_gives_the_same_snapshot_and_the_thread_is_left_as_it_was()
    {
        using var runtime = new TestRuntime();
        var (turns, items) = Thread(9, outputLength: 3_000);
        var turnsBefore = turns.ToArray();
        var itemsBefore = items.ToArray();
        var first = Build(MainState(turns, items));
        var second = Build(MainState(turns, items));
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(turnsBefore, turns);
        Assert.Equal(itemsBefore, items);
    }

    [Fact]
    public void A_user_turn_with_no_text_and_a_thread_with_odd_items_never_break_the_snapshot()
    {
        using var runtime = new TestRuntime();
        var turn = Turn(0, TurnStatus.Failed);
        var stray = new TimelineItem(null, new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "network is slow" }));
        var unknown = new TimelineItem(Guid.NewGuid(), new TimelineItem.Content.Assistant(new AssistantMessage { Text = "from nowhere" }));
        var items = new List<TimelineItem> { stray, User(turn, ""), Reply(turn, ""), stray, unknown };
        var text = Build(MainState([turn], items)).Text;
        Assert.Contains("--- Turn 1 (failed) ---", text, StringComparison.Ordinal);
        Assert.Contains("Notice (warning): network is slow", text, StringComparison.Ordinal);
        Assert.DoesNotContain("from nowhere", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Random_threads_always_fit_their_bound_and_never_fail()
    {
        using var runtime = new TestRuntime();
        var random = new Random(0x51DE);
        for (var round = 0; round < 300; round++)
        {
            var count = random.Next(0, 16);
            var turns = new List<TurnRecord>();
            var items = new List<TimelineItem>();
            for (var i = 0; i < count; i++)
            {
                var turn = Turn(i, (TurnStatus)random.Next(0, 4), Enumerable.Range(0, random.Next(0, 4)).Select(f => $"f{f}.cs").ToArray());
                turns.Add(turn);
                items.Add(User(turn, new string('u', random.Next(0, 30_000))));
                for (var tool = random.Next(0, 4); tool > 0; tool--)
                    items.Add(Tool(turn, (ToolCall.Kind)random.Next(0, 8), "tool " + tool, new string('t', random.Next(0, 20_000)), (ToolCall.Status)random.Next(0, 4)));
                items.Add(Reply(turn, new string('a', random.Next(0, 30_000)), random.Next(0, 5) == 0));
            }
            var limits = SideChatLimits.Default with { SnapshotCharacters = random.Next(1_500, 80_000) };
            var snapshot = Build(MainState(turns, items, (SideChatMainPhase)random.Next(0, 3), random.Next(0, 3)), limits: limits);
            Assert.True(snapshot.Text.Length <= limits.SnapshotCharacters + 80, $"round {round}: {snapshot.Text.Length} over {limits.SnapshotCharacters}");
            Assert.Equal(count, snapshot.VerbatimTurns + snapshot.SummarisedTurns);
        }
    }
}
