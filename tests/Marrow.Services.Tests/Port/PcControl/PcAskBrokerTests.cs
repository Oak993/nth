using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.7). The ask broker: one queue in arrival
// order across chats; the first answer wins (compare-and-set on id and revision) and a late click does nothing; an
// answer to content read again is refused as changed; answers an ask does not take from that view are refused; the
// lifetimes (step token, end of turn, 30 minutes, the user writing first, a stop, dismiss); and the race of the pill, the
// chat and a withdrawal on one ask.
[Trait("Decision", "ED-NEW-pcctl-16")]
public sealed class PcAskBrokerTests
{
    private const string Outlook = "exe:c:\\office\\outlook.exe";
    private const string Excel = "exe:c:\\office\\excel.exe";

    private static PcAskDraft Question(string thread, params string[] choices) =>
        new(PcAskKind.Question, thread, new PcAskRequest("Which file?", choices, AllowText: true, Window: null)) { AppName = "Excel", App = Excel };

    private static PcAskDraft Approval(string thread, string app = Outlook, bool allowAlways = true) =>
        new(PcAskKind.Approval, thread, new PcApprovalAsk(PcCapability.Act, Risky: true, allowAlways)) { App = app, AppName = "Outlook" };

    private static PcAskDraft Confirm(string thread) =>
        new(PcAskKind.Confirm, thread, new PcConfirmContent(PcConfirmCategory.Send, "send", "Send", "Outlook", [])) { App = Outlook, AppName = "Outlook" };

    [Fact]
    public void Asks_queue_in_arrival_order_across_chats()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var added = new List<string>();
        broker.Added += ask => added.Add(ask.Id);

        var first = broker.Add(Approval("chat-1"));
        var second = broker.Add(Question("chat-2", "March"));
        var third = broker.Add(Confirm("chat-1"));

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, broker.Pending.Select(a => a.Id));
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, added);
        Assert.Equal(3, broker.Count);
        Assert.Equal(new[] { first.Id, third.Id }, broker.Waiting("chat-1").Select(a => a.Id));
        Assert.False(first.Outcome.IsCompleted);
    }

    [Fact]
    public async Task The_first_answer_wins_and_a_later_one_is_too_late()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var resolved = new List<(string Id, object Answer, PcAnswerSource Source)>();
        broker.Resolved += (ask, answer, source) => resolved.Add((ask.Id, answer, source));
        var ask = broker.Add(Approval("chat-1"));

        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, PcApproval.Chat, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.TooLate, broker.Resolve(ask.Id, 0, PcApproval.Denied, PcAnswerSource.Chat));

        var outcome = await ask.Outcome;
        Assert.Equal(PcAskStatus.Answered, outcome.Status);
        Assert.Equal(PcApproval.Chat, outcome.Answer);
        Assert.Equal(PcAnswerSource.Pill, outcome.Source);
        Assert.Single(resolved);
        Assert.Null(broker.Find(ask.Id));
        Assert.Equal(0, broker.Count);
    }

    [Fact]
    public async Task An_answer_to_content_read_again_is_changed_and_the_new_revision_takes_it()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var ask = broker.Add(Confirm("chat-1"));
        var revisions = new List<int>();
        broker.Revised += revised => revisions.Add(revised.Revision);
        var now = new PcConfirmContent(PcConfirmCategory.Send, "send", "Send", "Outlook", [new PcConfirmRow("To", "ana@example.com")]);

        var revised = broker.Revise(ask.Id, now);

        Assert.NotNull(revised);
        Assert.Equal(1, revised.Revision);
        Assert.Same(now, broker.Find(ask.Id)?.Payload);
        Assert.Equal(PcResolve.Changed, broker.Resolve(ask.Id, 0, true, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 1, true, PcAnswerSource.Pill));
        Assert.Equal(new[] { 1 }, revisions);
        Assert.True((await ask.Outcome).Answer is true);
        Assert.Null(broker.Revise(ask.Id, now));
    }

    [Fact]
    public void Answers_an_ask_does_not_take_from_that_view_are_refused()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var head = broker.Add(Approval("chat-1", allowAlways: false));
        var provider = broker.Add(new PcAskDraft(PcAskKind.ProviderApproval, "chat-1", new PcProviderAsk("Run a command", "npm test")));
        var secret = broker.Add(new PcAskDraft(PcAskKind.Question, "chat-1", new PcAskRequest("What is the PIN for the card?", [], true, null)));
        var question = broker.Add(Question("chat-2", "March", "April"));
        var choicesOnly = broker.Add(new PcAskDraft(PcAskKind.Question, "chat-2", new PcAskRequest("Which?", ["A", "B"], AllowText: false, null)));

        Assert.Equal(PcResolve.Refused, broker.Resolve(head.Id, 0, PcApproval.Always, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(head.Id, 0, PcApproval.TimedOut, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(provider.Id, 0, "approve", PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(secret.Id, 0, PcAnswer.Typed("1234"), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(question.Id, 0, PcAnswer.Chose("May"), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(question.Id, 0, PcAnswer.Typed("  "), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Refused, broker.Resolve(choicesOnly.Id, 0, PcAnswer.Typed("C"), PcAnswerSource.Pill));
        Assert.Equal(5, broker.Count);

        Assert.Equal(PcResolve.Done, broker.Resolve(head.Id, 0, PcApproval.Chat, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(provider.Id, 0, "approve", PcAnswerSource.Chat));
        Assert.Equal(PcResolve.Done, broker.Resolve(secret.Id, 0, PcAnswer.Typed("I will type it"), PcAnswerSource.Chat));
        Assert.Equal(PcResolve.Done, broker.Resolve(question.Id, 0, PcAnswer.Chose("March"), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(choicesOnly.Id, 0, PcAnswer.Chose("B"), PcAnswerSource.Chat));
    }

    [Fact]
    public async Task A_confirm_goes_with_its_step_and_a_question_outlives_it()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var step = new CancellationTokenSource();
        var confirm = broker.Add(Confirm("chat-1"), step.Token);
        var question = broker.Add(Question("chat-1", "March"), step.Token);

        await step.CancelAsync();

        var outcome = await confirm.Outcome.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new PcAskOutcome(PcAskStatus.Withdrawn, Reason: PcWithdrawal.StepEnded), outcome);
        Assert.Equal(PcResolve.TooLate, broker.Resolve(confirm.Id, 0, true, PcAnswerSource.Pill));
        Assert.Equal(new[] { question.Id }, broker.Pending.Select(a => a.Id));
    }

    [Fact]
    public async Task A_step_already_ended_withdraws_its_ask_at_once()
    {
        using var broker = new PcAskBroker(new PcManualClock());

        var ask = broker.Add(Confirm("chat-1"), new CancellationToken(canceled: true));

        Assert.Equal(PcWithdrawal.StepEnded, (await ask.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
        Assert.Equal(0, broker.Count);
    }

    [Fact]
    public async Task A_question_ends_after_thirty_minutes_and_not_before()
    {
        var clock = new PcManualClock();
        using var broker = new PcAskBroker(clock);
        var withdrawn = new List<PcWithdrawal>();
        broker.Withdrawn += (_, reason) => withdrawn.Add(reason);
        var ask = broker.Add(Question("chat-1", "March"));
        await PcTestWait.Until(() => clock.Pending > 0);

        clock.Advance(AskLifetimes.QuestionMs - 1);
        Assert.True(await PcTestWait.StillWaiting(ask.Outcome));
        clock.Advance(1);

        Assert.Equal(PcWithdrawal.Expired, (await ask.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
        Assert.Equal(new[] { PcWithdrawal.Expired }, withdrawn);
    }

    [Fact]
    public async Task An_answered_question_stops_its_clock()
    {
        var clock = new PcManualClock();
        using var broker = new PcAskBroker(clock);
        var ask = broker.Add(Question("chat-1", "March"));
        await PcTestWait.Until(() => clock.Pending > 0);

        broker.Resolve(ask.Id, 0, PcAnswer.Chose("March"), PcAnswerSource.Pill);

        Assert.Equal(0, clock.Pending);
        clock.Advance(AskLifetimes.QuestionMs);
        Assert.Equal(PcAskStatus.Answered, (await ask.Outcome).Status);
    }

    [Fact]
    public async Task Writing_in_the_chat_moves_its_questions_on_and_nothing_else()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var mine = broker.Add(Question("chat-1", "March"));
        var other = broker.Add(Question("chat-2", "March"));
        var approval = broker.Add(Approval("chat-1"));

        broker.UserWrote("chat-1");

        Assert.Equal(PcWithdrawal.MovedOn, (await mine.Outcome).Reason);
        Assert.Equal(new[] { other.Id, approval.Id }, broker.Pending.Select(a => a.Id));
    }

    [Fact]
    public async Task The_end_of_a_turn_withdraws_its_approvals_and_confirms_and_keeps_its_questions()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var approval = broker.Add(Approval("chat-1"));
        var confirm = broker.Add(Confirm("chat-1"));
        var question = broker.Add(Question("chat-1", "March"));
        var otherChat = broker.Add(Approval("chat-2"));

        broker.EndTurn("chat-1");

        Assert.Equal(PcWithdrawal.TurnEnded, (await approval.Outcome).Reason);
        Assert.Equal(PcWithdrawal.TurnEnded, (await confirm.Outcome).Reason);
        Assert.Equal(new[] { question.Id, otherChat.Id }, broker.Pending.Select(a => a.Id));
    }

    [Fact]
    public async Task A_stop_withdraws_what_it_covers_but_not_questions_and_sleep_withdraws_nothing()
    {
        var control = new PcRunControl();
        using var broker = new PcAskBroker(new PcManualClock(), control);
        var outlook = broker.Add(Approval("chat-1"));
        var excel = broker.Add(Approval("chat-1", Excel));
        var question = broker.Add(Question("chat-1", "March"));

        control.Sleeping();
        Assert.Equal(3, broker.Count);
        control.Stop(PcScope.App(Outlook), PcStopSource.Pill);
        Assert.Equal(PcWithdrawal.Stopped, (await outlook.Outcome).Reason);
        Assert.Equal(new[] { excel.Id, question.Id }, broker.Pending.Select(a => a.Id));

        control.Stop(PcScope.All, PcStopSource.StopKey);
        Assert.Equal(PcWithdrawal.Stopped, (await excel.Outcome).Reason);
        Assert.Equal(new[] { question.Id }, broker.Pending.Select(a => a.Id));
    }

    [Fact]
    public async Task Dismiss_drops_only_a_question()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var question = broker.Add(Question("chat-1", "March"));
        var approval = broker.Add(Approval("chat-1"));

        Assert.False(broker.Dismiss(approval.Id));
        Assert.True(broker.Dismiss(question.Id));
        Assert.False(broker.Dismiss(question.Id));

        Assert.Equal(PcWithdrawal.Dismissed, (await question.Outcome).Reason);
        Assert.Equal(new[] { approval.Id }, broker.Pending.Select(a => a.Id));
    }

    [Fact]
    public async Task The_pill_the_chat_and_a_withdrawal_racing_on_one_ask_end_it_exactly_once()
    {
        for (var round = 0; round < 200; round++)
        {
            using var broker = new PcAskBroker(new PcManualClock());
            var ask = broker.Add(Approval("chat-1"));
            var ends = 0;
            broker.Resolved += (_, _, _) => Interlocked.Increment(ref ends);
            broker.Withdrawn += (_, _) => Interlocked.Increment(ref ends);
            using var start = new Barrier(3);
            var results = new PcResolve[2];
            var withdrew = false;
            var racers = new[]
            {
                Task.Run(() => { start.SignalAndWait(); results[0] = broker.Resolve(ask.Id, 0, PcApproval.Chat, PcAnswerSource.Pill); }),
                Task.Run(() => { start.SignalAndWait(); results[1] = broker.Resolve(ask.Id, 0, PcApproval.Denied, PcAnswerSource.Chat); }),
                Task.Run(() => { start.SignalAndWait(); withdrew = broker.Withdraw(ask.Id, PcWithdrawal.StepEnded); }),
            };
            await Task.WhenAll(racers).WaitAsync(TimeSpan.FromSeconds(10));

            var winners = results.Count(r => r == PcResolve.Done) + (withdrew ? 1 : 0);
            Assert.Equal(1, winners);
            Assert.Equal(1, Volatile.Read(ref ends));
            var outcome = await ask.Outcome;
            if (withdrew) Assert.Equal(PcAskStatus.Withdrawn, outcome.Status);
            else Assert.Equal(results[0] == PcResolve.Done ? PcApproval.Chat : PcApproval.Denied, outcome.Answer);
            Assert.All(results.Where(r => r != PcResolve.Done), r => Assert.Equal(PcResolve.TooLate, r));
        }
    }
}
