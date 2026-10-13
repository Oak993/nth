using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.DelegateTimeline;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.2 "Expiry", 12.4, 12.6 Stop, 12.6b; ED-NEW-dextra-as22-5, -10, -12): while the model thinks the world moves. Every check is
// made again when its answer reaches the main thread: the owner answering in the chat, turning the delegate off, a chat that read something untrusted, a
// task that was stopped or paused, a model that failed or took too long. A model call is bounded in time and in number, ends with a stop, and never
// answers a request that something else settled.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-12")]
public sealed class DelegateThinkingTests
{
    private static readonly string[] Db = ["Postgres", "SQLite"];

    private static DelegateRig On(bool manualClock = false)
    {
        var rig = new DelegateRig(manualClock: manualClock);
        rig.Everywhere();
        rig.Model.Reply = _ => FakeDelegateModel.Json("Postgres");
        return rig;
    }

    private static bool Cancelled(DelegateRig rig, int count = 1) => SpinWait.SpinUntil(() => rig.Model.Cancelled >= count, TimeSpan.FromSeconds(10));

    [Fact]
    public void The_owner_answering_in_the_chat_while_it_thinks_ends_the_work_and_nothing_more_happens()
    {
        using var rig = On();
        var chat = rig.Owner();
        var thinking = new TaskCompletionSource();
        rig.Model.Thinking = thinking.Task;
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        Assert.Equal(1, rig.Responder.Pending);
        rig.Controller.OnRequestResolved(chat.Id, "r1");
        Assert.Equal(0, rig.Responder.Pending);
        Assert.True(Cancelled(rig));
        Assert.Empty(rig.Agents.Answers);
        Assert.Empty(rig.Queue.Items);
        Assert.Empty(rig.Agents.Notes);
    }

    [Fact]
    public void A_stop_lets_every_question_with_the_model_go_and_nothing_is_answered_after_it()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Model.Thinking = new TaskCompletionSource().Task;
        rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "r1");
        rig.Ask(chat.Id, "Which test framework does it use?", ["xUnit", "NUnit"], id: "r2");
        Assert.Equal(2, rig.Responder.Pending);
        rig.Controller.Controls.StopAll(StopSource.Program);
        Assert.Equal(0, rig.Responder.Pending);
        Assert.True(Cancelled(rig, 2));
        Assert.Empty(rig.Agents.Answers);
        Assert.Empty(rig.Queue.Items);
    }

    [Fact]
    public void Letting_the_controller_go_cancels_the_work_in_hand()
    {
        var rig = On();
        var chat = rig.Owner();
        rig.Model.Thinking = new TaskCompletionSource().Task;
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        rig.Dispose();
        Assert.True(Cancelled(rig));
        Assert.Empty(rig.Agents.Answers);
    }

    [Fact]
    public void Turned_off_while_it_thinks_the_question_stays_the_chats_own_with_no_card_and_no_answer()
    {
        using var rig = On();
        var chat = rig.Owner();
        var thinking = new TaskCompletionSource();
        rig.Model.Thinking = thinking.Task;
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        rig.Off();
        thinking.SetResult();
        rig.Think();
        Assert.Empty(rig.Agents.Answers);
        Assert.Empty(rig.Queue.Items);
        Assert.Empty(rig.Agents.Notes);
        Assert.Equal(0, rig.Book.Count);
    }

    [Fact]
    public void A_chat_that_reads_something_untrusted_while_the_model_thinks_gets_no_answer_and_the_owner_gets_the_card()
    {
        using var rig = On();
        var chat = rig.Owner();
        var thinking = new TaskCompletionSource();
        rig.Model.Thinking = thinking.Task;
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        rig.Timelines[chat.Id] = [User("go"), Tool(ToolCall.Kind.Web, "https://news.example/a")];
        thinking.SetResult();
        rig.Think();
        Assert.Empty(rig.Agents.Answers);
        Assert.Equal(DelegateWords.Reason(DelegateReason.Tainted), Assert.Single(rig.Queue.Waiting).Why);
    }

    [Fact]
    public void A_chat_that_already_read_something_untrusted_is_not_even_asked_of_the_model()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Timelines[chat.Id] = [User("go"), Tool(ToolCall.Kind.Mcp, "gmail · read_message")];
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        Assert.Empty(rig.Model.Prompts);
        Assert.Empty(rig.Agents.Answers);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Equal(DelegateWords.RuleLine(DelegateReason.Tainted), card.Rule);

        // the owner writes in the chat again: a fresh turn, and the delegate answers
        rig.Timelines[chat.Id] = [User("go"), Tool(ToolCall.Kind.Mcp, "gmail · read_message"), User("the owner wrote again")];
        rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "r2");
        rig.Think();
        Assert.True(rig.Agents.Answers.Count == 1, rig.Why());
        Assert.Equal("r2", Assert.Single(rig.Agents.Answers).Request);
    }

    [Fact]
    public void A_model_that_fails_or_takes_too_long_leaves_the_question_to_the_owner()
    {
        using var failing = On();
        var chat = failing.Owner();
        failing.Model.Fails = new IOException("The CLI is gone.");
        var faults = new List<string>();
        failing.Controller.Runner.Faulted += faults.Add;
        failing.Ask(chat.Id, "Which database does the repo use?", Db);
        failing.Think();
        Assert.Empty(failing.Agents.Answers);
        Assert.Equal(DelegateWords.Reason(DelegateReason.NoModel), Assert.Single(failing.Queue.Waiting).Why);
        Assert.Contains(faults, f => f.StartsWith("Dextra's delegate could not be asked", StringComparison.Ordinal));

        using var slow = On(manualClock: true);
        var other = slow.Owner();
        slow.Model.Thinking = new TaskCompletionSource().Task;
        slow.Ask(other.Id, "Which database does the repo use?", Db);
        Assert.Equal(1, slow.Responder.Pending);
        slow.Clock!.Advance(TimeSpan.FromSeconds(46));
        Assert.True(Cancelled(slow));
        slow.Think();
        Assert.Empty(slow.Agents.Answers);
        Assert.Equal(DelegateWords.Reason(DelegateReason.NoModel), Assert.Single(slow.Queue.Waiting).Why);
        Assert.Equal(0, slow.Responder.Pending);
    }

    [Fact]
    public void Only_a_few_questions_are_with_the_model_at_once_and_the_next_goes_to_the_owner()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Model.Thinking = new TaskCompletionSource().Task;
        for (var i = 1; i <= DelegateResponder.MaxPending; i++) rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "r" + i);
        Assert.Equal(DelegateResponder.MaxPending, rig.Responder.Pending);
        Assert.Empty(rig.Queue.Items);
        rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "over");
        Assert.Equal(DelegateResponder.MaxPending, rig.Responder.Pending);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Equal("over", card.RequestId);
        Assert.Equal(DelegateWords.Reason(DelegateReason.TooMuch), card.Why);
    }

    [Fact]
    public void The_same_request_again_is_already_with_the_model_and_is_not_asked_twice()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Model.Thinking = new TaskCompletionSource().Task;
        Assert.True(rig.Ask(chat.Id, "Which database does the repo use?", Db));
        Assert.True(SpinWait.SpinUntil(() => rig.Model.Asked >= 1, TimeSpan.FromSeconds(10)));
        Assert.True(rig.Ask(chat.Id, "Which database does the repo use?", Db));
        Assert.Equal(1, rig.Responder.Pending);
        Assert.Equal(1, rig.Model.Asked);
        Assert.Empty(rig.Queue.Items);
    }

    // ---- a mission's chat ----

    [Fact]
    public void A_mission_agents_question_is_answered_by_the_delegate_when_it_is_on_and_the_runner_raises_no_card()
    {
        using var rig = On();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        Assert.True(rig.Ask(lead, "Which database does the repo use?", Db));
        rig.Think();
        var answer = Assert.Single(rig.Agents.Answers);
        Assert.Equal(lead, answer.Thread);
        Assert.Equal(["Postgres"], answer.Answers["q1"]);
        Assert.Empty(rig.Queue.Of(record.Id));
        var line = Assert.Single(rig.Delegated());
        Assert.Equal(record.Id.ToString("D"), line.Mission);
        Assert.Equal("Answered by Dextra for you: Postgres", Assert.Single(rig.Agents.Notes).Text);
        Assert.Equal(MissionState.Working, rig.Core.Now(record).Mission.State);
    }

    [Fact]
    public void A_mission_agents_question_the_delegate_leaves_goes_to_the_runners_card()
    {
        using var rig = On();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        rig.Ask(lead, "Should I pay for the Pro plan?", ["Yes", "No"]);
        var card = Assert.Single(rig.Queue.Of(record.Id));
        Assert.Equal(DecisionKind.Question, card.Kind);
        Assert.Equal("Questions are yours to answer", card.Rule);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Equal(MissionState.NeedsYou, rig.Core.Now(record).Mission.State);
        Assert.Equal("Dextra left this to you: " + DelegateWords.Reason(DelegateReason.Money), Assert.Single(rig.Agents.Notes).Text);
        Assert.Empty(rig.Model.Prompts);
    }

    [Fact]
    public void A_mission_that_is_stopped_while_the_model_thinks_takes_no_answer_and_raises_no_card()
    {
        using var rig = On();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        var thinking = new TaskCompletionSource();
        rig.Model.Thinking = thinking.Task;
        rig.Ask(lead, "Which database does the repo use?", Db);
        Assert.Null(rig.Controller.Controls.Stop(record.Id));
        thinking.SetResult();
        rig.Think();
        Assert.Empty(rig.Agents.Answers);
        Assert.DoesNotContain(rig.Queue.Of(record.Id), c => c.Kind == DecisionKind.Question);
    }

    [Fact]
    public void A_mission_that_is_paused_while_the_model_thinks_gets_the_runners_card_instead()
    {
        using var rig = On();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        var thinking = new TaskCompletionSource();
        rig.Model.Thinking = thinking.Task;
        rig.Ask(lead, "Which database does the repo use?", Db);
        Assert.Null(rig.Controller.Controls.Pause(record.Id));
        thinking.SetResult();
        rig.Think();
        Assert.Empty(rig.Agents.Answers);
        Assert.Single(rig.Queue.Of(record.Id), c => c.Kind == DecisionKind.Question);
    }

    [Fact]
    public void With_the_delegate_off_a_mission_question_is_the_runners_card_as_it_always_was()
    {
        using var rig = new DelegateRig();
        var record = rig.Mission();
        var lead = record.LeadThread!.Value;
        rig.Ask(lead, "Which database does the repo use?", Db);
        var card = Assert.Single(rig.Queue.Of(record.Id));
        Assert.Equal("Questions are yours to answer", card.Rule);
        Assert.Empty(rig.Model.Prompts);
        Assert.Empty(rig.Agents.Notes);
        Assert.Empty(rig.Delegated());
    }
}
