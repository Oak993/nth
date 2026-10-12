using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.9). pc_ask in the engine: listed with the
// tools; its question queued in the broker with the model's name and the app; answered from the pill; waiting_for_user
// "question" after 25 s and resumed by pc_wait; declined_by_user when the user writes first, when it expires or is
// dismissed; stopped_by_user on a stop, with the question kept; a secret question only in the chat; refused when Tactus
// is off or the arguments are wrong; kept past the end of the turn.
[Trait("Decision", "ED-NEW-pcctl-16")]
public sealed class PcEngineAskTests
{
    private const string Which = "{\"question\":\"Which file should I use?\",\"choices\":[\"March\",\"April\"],\"window\":\"w:1a\"}";

    private static PcControlEngine Engine(PcEngineHarness harness) =>
        new(harness.Parts, new PcCaller("chat-1", () => true, true) { Model = "GPT-6.1 Sol" });

    private static Task<PcResult> Call(PcControlEngine engine, string tool, string json) => PcEngineHarness.Call(engine, tool, json);

    private static async Task<PcPendingAsk> Queued(PcEngineHarness harness)
    {
        await PcTestWait.Until(() => harness.Asks.Count > 0);
        return harness.Asks.Pending[0];
    }

    [Fact]
    public void Pc_ask_is_offered_with_the_tools()
    {
        Assert.Contains(PcToolSchemas.Listed(PcSettings.Default with { Enabled = true }), t => t.Name == "pc_ask");
        Assert.Same(PcToolSchemas.AskSpec, PcToolSchemas.Find("pc_ask"));
    }

    [Fact]
    public async Task The_question_waits_in_the_broker_and_its_answer_from_the_pill_returns()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        using var engine = Engine(harness);

        var call = Call(engine, "pc_ask", Which);
        var ask = await Queued(harness);

        Assert.Equal(PcAskKind.Question, ask.Kind);
        Assert.Equal("GPT-6.1 Sol", ask.Model);
        Assert.Equal("notepad", ask.AppName);
        Assert.Equal("Waiting for your answer. notepad", ask.Title);
        Assert.False(ask.ChatOnly);
        Assert.Equal(PcResolve.Done, harness.Asks.Resolve(ask.Id, 0, PcAnswer.Chose("March"), PcAnswerSource.Pill));

        var result = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result.Error);
        Assert.Contains("answer: {\"choice\":\"March\"}", result.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_25_s_it_answers_waiting_for_user_question_and_pc_wait_returns_the_answer()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);
        var call = Call(engine, "pc_ask", "{\"question\":\"Go on?\"}");
        var ask = await Queued(harness);
        await PcTestWait.Until(() => harness.WaitClock.Pending > 0);

        harness.WaitClock.Advance(PcPendingSteps.BudgetMs);
        var waiting = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcStatus.WaitingForUser, waiting.Status);
        Assert.Equal(PcWaitReason.Question, waiting.Reason);

        var resumed = Call(engine, "pc_wait", "{\"until\":\"step\",\"token\":\"" + waiting.Token + "\"}");
        harness.Asks.Resolve(ask.Id, 0, PcAnswer.Typed("yes, the March one"), PcAnswerSource.Chat);
        var result = await resumed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("answer: {\"text\":\"yes, the March one\"}", result.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writing_first_expiry_and_dismiss_decline_with_their_reason()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);

        var movedOn = Call(engine, "pc_ask", "{\"question\":\"First?\"}");
        await Queued(harness);
        harness.Asks.UserWrote("chat-1");
        Assert.StartsWith("error: declined_by_user: moved_on.", (await movedOn.WaitAsync(TimeSpan.FromSeconds(5))).Render(), StringComparison.Ordinal);

        var expired = Call(engine, "pc_ask", "{\"question\":\"Second?\"}");
        await Queued(harness);
        await PcTestWait.Until(() => harness.AskClock.Pending > 0);
        harness.AskClock.Advance(AskLifetimes.QuestionMs);
        Assert.StartsWith("error: declined_by_user: expired.", (await expired.WaitAsync(TimeSpan.FromSeconds(5))).Render(), StringComparison.Ordinal);

        var dismissed = Call(engine, "pc_ask", "{\"question\":\"Third?\"}");
        var ask = await Queued(harness);
        harness.Asks.Dismiss(ask.Id);
        Assert.Equal(PcErrorCode.DeclinedByUser, (await dismissed.WaitAsync(TimeSpan.FromSeconds(5))).Error);
    }

    [Fact]
    public async Task A_stop_answers_stopped_by_user_and_keeps_the_question()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);
        var call = Call(engine, "pc_ask", "{\"question\":\"Which one?\"}");
        var ask = await Queued(harness);

        harness.Control.Stop(PcScope.Thread("chat-1"), PcStopSource.Chat);

        Assert.Equal(PcErrorCode.StoppedByUser, (await call.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Same(ask, harness.Asks.Find(ask.Id));
    }

    [Fact]
    public async Task A_question_that_asks_for_a_secret_is_answered_only_in_the_chat()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);
        var call = Call(engine, "pc_ask", "{\"question\":\"What is your password for the portal?\"}");
        var ask = await Queued(harness);

        Assert.True(ask.ChatOnly);
        Assert.Equal(PcResolve.Refused, harness.Asks.Resolve(ask.Id, 0, PcAnswer.Typed("hunter2"), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, harness.Asks.Resolve(ask.Id, 0, PcAnswer.Typed("I will sign in myself"), PcAnswerSource.Chat));
        Assert.Null((await call.WaitAsync(TimeSpan.FromSeconds(5))).Error);
    }

    [Fact]
    public async Task Off_or_wrong_arguments_are_refused_and_nothing_is_queued()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);

        Assert.Equal(PcErrorCode.BadArgs, (await Call(engine, "pc_ask", "{\"question\":\"\"}")).Error);
        Assert.Equal(PcErrorCode.BadArgs, (await Call(engine, "pc_ask", "{\"question\":\"Go?\",\"link\":\"x\"}")).Error);
        harness.Settings = harness.Settings with { Enabled = false };
        Assert.Equal(PcErrorCode.PcOff, (await Call(engine, "pc_ask", "{\"question\":\"Go?\"}")).Error);
        Assert.Equal(0, harness.Asks.Count);
    }

    [Fact]
    public async Task The_end_of_the_turn_keeps_the_question_for_the_chat()
    {
        var harness = new PcEngineHarness();
        using var engine = Engine(harness);
        var call = Call(engine, "pc_ask", "{\"question\":\"Which one?\"}");
        var ask = await Queued(harness);
        await PcTestWait.Until(() => harness.WaitClock.Pending > 0);
        harness.WaitClock.Advance(PcPendingSteps.BudgetMs);
        var waiting = await call.WaitAsync(TimeSpan.FromSeconds(5));

        engine.EndTurn();

        var resumed = await Call(engine, "pc_wait", "{\"until\":\"step\",\"token\":\"" + waiting.Token + "\"}");
        Assert.Equal(PcErrorCode.BadArgs, resumed.Error);
        Assert.NotNull(harness.Asks.Find(ask.Id));
    }
}
