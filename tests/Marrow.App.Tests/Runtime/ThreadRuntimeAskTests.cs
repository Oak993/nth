using System.Text.Json.Nodes;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user: "the turn shows Waiting for your answer, Stop cancels it, the card survives restart
// and answers after a restart resume the turn"). Turns run through the real StartTurn over an offline scripted session; the card is put in
// the thread the way the tool does, through the chat's host.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class ThreadRuntimeAskTests
{
    private static AskCard Pick(string id = "ask-1") => AskUserSchema.Parse(JsonNode.Parse(
        """{"questions":[{"question":"Which database?","type":"single","options":["PostgreSQL","SQLite"]},{"question":"How many GB?","type":"number","min":1,"max":64,"integer":true}]}"""), id).Card!;

    private static void Hold(ThreadRuntimeTurnRig rig)
    {
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Session.InterruptScript = () =>
        {
            rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            return Task.CompletedTask;
        };
    }

    private static void RunningTurn(ThreadRuntimeTurnRig rig)
    {
        Hold(rig);
        _ = rig.StartTurn("Set up the database");
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= 1, AppModelHarness.Bound);
    }

    private static Task<AskOutcome> Ask(ThreadRuntimeTurnRig rig, AskCard card, CancellationToken cancellation = default)
    {
        Task<AskOutcome> wait = null!;
        rig.H.Main.OnMain(() => wait = rig.Runtime.AskHost.Ask(card, cancellation));
        rig.H.Main.RunUntilIdle();
        return wait;
    }

    private static AskCard? CardIn(ThreadRuntimeTurnRig rig, string id) => rig.H.Main.OnMain(() => rig.Runtime.Entries
        .Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Ask>().Select(ask => ask.Value).FirstOrDefault(card => card.Id == id));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Given(string database, string size) =>
        new Dictionary<string, IReadOnlyList<string>> { ["q1"] = [database], ["q2"] = [size] };

    private static AskCheck? Answer(ThreadRuntimeTurnRig rig, string id, IReadOnlyDictionary<string, IReadOnlyList<string>> given) =>
        rig.H.Main.OnMain(() => rig.Runtime.AnswerAsk(id, given, System.Globalization.CultureInfo.InvariantCulture));

    private static void Settle(ThreadRuntimeTurnRig rig)
    {
        for (var round = 0; round < 20; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void A_question_lands_in_the_thread_as_a_waiting_card_and_the_turn_waits_for_the_answer()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var turn = Assert.Single(rig.Runtime.Turns).Id;
        var wait = Ask(rig, Pick());

        Assert.False(wait.IsCompleted);
        var card = CardIn(rig, "ask-1")!;
        Assert.Equal(AskState.Waiting, card.State);
        var entry = rig.H.Main.OnMain(() => rig.Runtime.Entries.Single(e => e.TimelineEntryKind == TimelineEntry.Kind.Ask));
        Assert.Equal(turn, entry.TurnID);
        Assert.True(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer));
        Assert.Equal(1, rig.H.Main.OnMain(() => rig.Runtime.LiveAskCount));
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.HoldsOnlyPersistedState));
    }

    [Fact]
    public void The_card_is_written_to_disk_the_moment_it_is_asked()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        _ = Ask(rig, Pick());
        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = Storage.ReadDocument(rig.InitialThread.Id)!.Value.Document;
        var card = saved.Items.Select(item => item.TimelineItemContent).OfType<TimelineItem.Content.Ask>().Single().Value;
        Assert.Equal(("ask-1", AskState.Waiting, 2), (card.Id, card.State, card.Questions.Count));
    }

    [Fact]
    public async Task The_answer_goes_back_to_the_waiting_call_and_closes_the_card()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var wait = Ask(rig, Pick());

        var check = Answer(rig, "ask-1", Given("SQLite", "8"));
        Assert.True(check!.IsValid);
        Settle(rig);

        var outcome = (await wait);
        Assert.Equal(AskOutcomeKind.Answered, outcome.Kind);
        Assert.Equal(["SQLite"], outcome.Card.Answers["q1"]);
        Assert.Equal(["8"], outcome.Card.Answers["q2"]);
        var card = CardIn(rig, "ask-1")!;
        Assert.Equal(AskState.Answered, card.State);
        Assert.NotNull(card.ClosedAt);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer));
        Assert.Single(rig.Session.Inputs); // the turn goes on in the same call: nothing new was sent
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.StartsWith("The user answered your questions.", AskUserWords.ToolResult(outcome.Card), StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_that_cannot_be_taken_leaves_the_card_waiting_and_names_the_problem()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var wait = Ask(rig, Pick());

        var check = Answer(rig, "ask-1", Given("SQLite", "900"));
        Assert.False(check!.IsValid);
        Assert.Equal("At most 64.", check.Problems["q2"]);
        Settle(rig);
        Assert.False(wait.IsCompleted);
        Assert.Equal(AskState.Waiting, CardIn(rig, "ask-1")!.State);
        Assert.Null(Answer(rig, "missing", Given("SQLite", "8"))); // no such card
    }

    [Fact]
    public async Task Skipping_tells_the_waiting_call_and_closes_the_card_as_skipped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var wait = Ask(rig, Pick());

        rig.H.Main.OnMain(() => rig.Runtime.SkipAsk("ask-1"));
        Settle(rig);

        Assert.Equal(AskOutcomeKind.Skipped, (await wait).Kind);
        Assert.Equal(AskState.Skipped, CardIn(rig, "ask-1")!.State);
        Assert.Single(rig.Session.Inputs);
        Assert.Null(Answer(rig, "ask-1", Given("SQLite", "8"))); // a closed card takes no answer
    }

    [Fact]
    public async Task Stop_cancels_the_question_and_ends_the_turn()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var wait = Ask(rig, Pick());

        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);

        Assert.Equal(AskOutcomeKind.Stopped, (await wait).Kind);
        Assert.Equal(AskState.Stopped, CardIn(rig, "ask-1")!.State);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer));
        Assert.Equal(TurnStatus.Interrupted, rig.Runtime.Turns.Last().Status);
        Assert.Equal(1, rig.Session.InterruptCalls);
    }

    [Fact]
    public async Task A_call_that_ends_leaves_the_card_waiting_and_a_later_answer_starts_a_new_turn_with_the_answer()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        using var cancel = new CancellationTokenSource();
        var wait = Ask(rig, Pick(), cancel.Token);

        cancel.Cancel();
        Settle(rig);
        Assert.Equal(AskOutcomeKind.Detached, (await wait).Kind);
        Assert.Equal(AskState.Waiting, CardIn(rig, "ask-1")!.State);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer)); // nobody waits any more
        rig.Complete();

        var check = Answer(rig, "ask-1", Given("PostgreSQL", "16"));
        Assert.True(check!.IsValid);
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= 2, AppModelHarness.Bound);

        var answered = CardIn(rig, "ask-1")!;
        Assert.Equal(AskState.Answered, answered.State);
        Assert.Contains(AskUserWords.ResumeMessage(answered), rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.Equal(2, rig.Runtime.Turns.Count);
        var resumed = rig.H.Main.OnMain(() => rig.Runtime.Entries.Select(e => e.Item.TimelineItemContent).OfType<TimelineItem.Content.User>().Last().Value.Text);
        Assert.StartsWith("My answers to your questions:", resumed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_asks_again_after_its_first_call_ended_gets_one_card_not_two()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        using var cancel = new CancellationTokenSource();
        var first = Ask(rig, Pick("first"), cancel.Token);
        cancel.Cancel();
        Settle(rig);
        Assert.Equal(AskOutcomeKind.Detached, (await first).Kind); // a provider time limit ended the call; the card stays

        var second = Ask(rig, Pick("second"));
        var cards = rig.H.Main.OnMain(() => rig.Runtime.Entries.Select(e => e.Item.TimelineItemContent).OfType<TimelineItem.Content.Ask>().Select(a => a.Value.Id).ToList());
        Assert.Equal(["second"], cards); // the same questions: the new card took the stale one's place
        Assert.False(second.IsCompleted);

        var other = Ask(rig, AskUserSchema.Parse(JsonNode.Parse("""{"questions":[{"question":"Something else entirely?"}]}"""), "third").Card!);
        Assert.Equal(["second", "third"], rig.H.Main.OnMain(() => rig.Runtime.Entries.Select(e => e.Item.TimelineItemContent).OfType<TimelineItem.Content.Ask>().Select(a => a.Value.Id).ToList()));
        Assert.False(other.IsCompleted);
    }

    [Fact]
    public async Task A_turn_that_finishes_while_a_call_still_waits_detaches_it_and_keeps_the_card()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var wait = Ask(rig, Pick());

        rig.Complete(); // the provider gave up on the call and finished
        Settle(rig);

        Assert.Equal(AskOutcomeKind.Detached, (await wait).Kind);
        Assert.Equal(AskState.Waiting, CardIn(rig, "ask-1")!.State);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer));
        Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.WaitingAskEntries));
    }

    [Fact]
    public async Task An_exiting_provider_detaches_every_waiting_call()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var first = Ask(rig, Pick("a"));
        var second = Ask(rig, Pick("b"));
        Assert.Equal(2, rig.H.Main.OnMain(() => rig.Runtime.LiveAskCount));

        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Exited(null)));
        rig.H.Main.Loop.RunUntil(() => first.IsCompleted && second.IsCompleted, AppModelHarness.Bound);

        Assert.Equal(AskOutcomeKind.Detached, (await first).Kind);
        Assert.Equal(AskOutcomeKind.Detached, (await second).Kind);
        Assert.Equal(0, rig.H.Main.OnMain(() => rig.Runtime.LiveAskCount));
    }

    [Fact]
    public async Task Two_questions_wait_side_by_side_and_each_closes_on_its_own()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var first = Ask(rig, Pick("a"));
        var second = Ask(rig, Pick("b"));

        rig.H.Main.OnMain(() => rig.Runtime.SkipAsk("a"));
        Settle(rig);
        Assert.Equal(AskOutcomeKind.Skipped, (await first).Kind);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, rig.H.Main.OnMain(() => rig.Runtime.LiveAskCount));

        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Assert.Equal(AskOutcomeKind.Stopped, (await second).Kind);
        Assert.Equal(AskState.Skipped, CardIn(rig, "a")!.State);
    }

    [Fact]
    public async Task The_same_card_asked_twice_waits_once()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        _ = Ask(rig, Pick());
        var again = Ask(rig, Pick());
        Assert.Equal(AskOutcomeKind.Detached, (await again).Kind);
        Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.Entries.Where(e => e.TimelineEntryKind == TimelineEntry.Kind.Ask).ToList()));
    }

    [Fact]
    public async Task A_cancelled_call_never_puts_a_card_in_the_thread()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var wait = Ask(rig, Pick(), cancel.Token);
        Assert.Equal(AskOutcomeKind.Detached, (await wait).Kind);
        Assert.Null(CardIn(rig, "ask-1"));
    }

    [Fact]
    public void The_providers_own_row_for_the_tool_is_left_out_because_the_card_is_its_place()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RunningTurn(rig);
        var row = new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "nth-ui · ask_user", Detail = "{}" };
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("t-ask", row));
            rig.Session.Emit(new ProviderEvent.ToolOutput("t-ask", "waiting"));
            rig.Session.Emit(new ProviderEvent.ToolUpdated("t-ask", new ToolUpdate { Output = "done", Status = ToolCall.Status.Completed }));
            rig.Session.Emit(new ProviderEvent.ToolStarted("t-other", new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "nth-ui · artifact_list" }));
        });
        rig.H.Main.RunUntilIdle();
        var tools = rig.H.Main.OnMain(() => rig.Runtime.Entries.Where(e => e.TimelineEntryKind == TimelineEntry.Kind.Tool).Select(e => e.Id).ToList());
        Assert.Equal(["t-other"], tools);
    }

    [Fact]
    public void A_providers_request_to_allow_the_ask_tool_is_answered_at_once_and_other_requests_still_wait_for_the_person()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        RunningTurn(rig);
        var options = new ValueList<ApprovalRequest.Option>([new("allow", "Approve", ApprovalRequest.Option.Role.Approve), new("deny", "Decline", ApprovalRequest.Option.Role.Decline)]);
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.Approval(new ApprovalRequest("r-ask", ApprovalRequest.Kind.Tool, "nth-ui · ask_user", options)));
            rig.Session.Emit(new ProviderEvent.Approval(new ApprovalRequest("r-other", ApprovalRequest.Kind.Tool, "github · create_issue", options)));
            rig.Session.Emit(new ProviderEvent.Approval(new ApprovalRequest("r-command", ApprovalRequest.Kind.Command, "nth-ui ask_user", options)));
        });
        rig.H.Main.RunUntilIdle();

        Assert.Equal([("r-ask", "allow", (string?)null)], rig.Session.ApprovalAnswers);
        Assert.Equal(["r-other", "r-command"], rig.H.Main.OnMain(() => rig.Runtime.Approvals.Select(a => a.Id).ToList()));
    }

    [Fact]
    public void A_card_that_waited_when_the_app_quit_is_still_waiting_when_the_thread_reopens()
    {
        var turn = Guid.NewGuid();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configureWithHarness: (_, thread) =>
        {
            var document = new ThreadDocument(thread.Id)
            {
                Items = new ValueList<TimelineItem>(
                [
                    new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Set up the database" })),
                    new TimelineItem(turn, new TimelineItem.Content.Ask(Pick())),
                ]),
                Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turn, Index = 0, Status = TurnStatus.Running }]),
            };
            DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(thread.Id));
            return thread;
        });

        var card = CardIn(rig, "ask-1")!;
        Assert.Equal(AskState.Waiting, card.State);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(rig.Runtime.Turns).Status); // the quit turn is over, its card is not
        Assert.Contains(rig.H.Main.OnMain(() => rig.Runtime.Entries.Select(e => e.TimelineEntryKind).ToList()), kind => kind == TimelineEntry.Kind.TurnEnd);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsWaitingForAnswer)); // no call waits; the person still can answer
        Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.WaitingAskEntries));

        var check = Answer(rig, "ask-1", Given("SQLite", "4"));
        Assert.True(check!.IsValid);
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= 1, AppModelHarness.Bound);

        Assert.Contains("My answers to your questions:\n- Which database? SQLite\n- How many GB? 4", rig.Session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Equal(AskState.Answered, CardIn(rig, "ask-1")!.State);
        Assert.Equal(2, rig.Runtime.Turns.Count);
    }

    [Fact]
    public void A_waiting_card_skipped_after_a_restart_starts_no_turn()
    {
        var turn = Guid.NewGuid();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configureWithHarness: (_, thread) =>
        {
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(thread.Id)
            {
                Items = new ValueList<TimelineItem>([new TimelineItem(turn, new TimelineItem.Content.Ask(Pick()))]),
                Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turn, Index = 0, Status = TurnStatus.Interrupted }]),
            }, Storage.ThreadURL(thread.Id));
            return thread;
        });

        rig.H.Main.OnMain(() => rig.Runtime.SkipAsk("ask-1"));
        Settle(rig);
        Assert.Equal(AskState.Skipped, CardIn(rig, "ask-1")!.State);
        Assert.Empty(rig.Session.Inputs);
        Assert.Single(rig.Runtime.Turns);
    }
}
