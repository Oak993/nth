using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.CostRig;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps"; ED-NEW-dextra-as15-4): the cost guard over the mission runner and fake chats. Before each turn it
// checks what the work has used plus this turn's p90 against the owner's caps (this task's, the assistant's for the day, the API keys' dollars for
// the day); a turn that would pass one pauses the mission at the turn boundary (a running turn is never cut) and puts the options in the queue;
// the answers act. The rig's subscription accounts cost 100,000 tokens a percent and a new turn is 1.3162 points; its API key costs $10 a million
// tokens, so a new turn is $1.32 (a reviewer's turn, on a model whose turns nth has not seen, is the 120,000 tokens it assumes: $1.20).
[Trait("Decision", "ED-NEW-dextra-as15-4")]
[Trait("Feature", "AS-15")]
public sealed class CostGuardTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private CostRig Rig() => new(temp.Folder);

    private static MissionRequest For(CostRig rig, string assistant, string title = "Fix the login timeout") => rig.Missions.Request(title) with { AssistantId = assistant };

    // ---- a task's cap ----

    [Fact]
    public void A_task_whose_first_turn_would_pass_its_cap_waits_Paused_before_its_lead_starts_with_the_card_and_the_toast()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(1)));

        Assert.Equal(MissionState.Paused, record.Mission.State);
        Assert.Equal(PauseCause.Reserve, record.Mission.PausedBy);
        Assert.Equal(MissionState.Planning, record.Mission.ResumeTo);
        Assert.Null(record.LeadThread);
        Assert.Empty(rig.Missions.Agents.Chats);                    // nothing was sent
        Assert.Equal(0, rig.Missions.Gate.Running);                 // its slot was given back
        Assert.Equal(BoardGroup.NeedsYou, record.Mission.Group);
        Assert.Equal("Paused at your cap of 1% for this task", record.Waiting);

        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal(DecisionKind.Cap, card.Kind);
        Assert.Equal("Fix the login timeout would pass your cap", card.Title);
        Assert.Equal("Your cap for this task: at most 1% of any account", card.Why);
        Assert.Equal("Your cap", card.Rule);
        Assert.Equal(["Cap", "Where", "Used", "Your cap", "The next step needs about", "Where it stands"], card.Inputs.Select(i => i.Name));
        Assert.Equal(["At most 1% of any account for this task", "Claude 20x's 5-hour window", "0%", "1%", "1.3%",
            "Claude 20x's 5-hour window: 0% used by this task; your cap is 1%. The next step needs about 1.3% more."], card.Inputs.Select(i => i.Value));
        Assert.Equal(["Allow 1% more once", "Stop and give me what you have"], card.Options);   // a task's cap has no tomorrow to wait for
        Assert.Equal(ReserveGuard.PausedNote, card.Note);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Equal([DecisionAnswerKind.Pick], card.Answers);      // the options and nothing to type

        var hold = Assert.Single(rig.Holds);
        Assert.Equal("Paused at your cap. Claude 20x's 5-hour window: 0% used by this task; your cap is 1%.", hold.Toast);
        Assert.Equal("Paused at your cap of 1% for this task", hold.Line);
        Assert.Equal(card.Id, hold.Card);
        Assert.Equal(card.Id, rig.Guard.HoldOf(record.Id)!.Card);
        Assert.Contains(rig.Audit, e => e.Tool == "cap_hold" && e.Mission == record.Id.ToString("D") && e.Result == "start: Claude 20x's 5-hour window, 0% of 1%");
    }

    [Fact]
    public void A_task_with_room_for_its_first_turn_starts_and_raises_no_card()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(5)));
        Assert.Equal(MissionState.Working, record.Mission.State);
        Assert.NotNull(record.LeadThread);
        Assert.Null(rig.Card(record));
        Assert.Empty(rig.Holds);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void Allowing_it_once_lets_the_task_start_the_card_is_answered_and_the_answer_is_logged()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(1)));
        var card = rig.Card(record)!;
        Assert.Null(rig.Pick(card, CapOptionKind.AllowOnce));

        var now = rig.Missions.Now(record);
        Assert.Equal(MissionState.Working, now.Mission.State);
        Assert.NotNull(now.LeadThread);
        Assert.Null(now.Waiting);
        Assert.Null(rig.Guard.HoldOf(record.Id));
        Assert.Equal(DecisionState.Answered, rig.Missions.Queue.Find(card.Id)!.State);
        Assert.Contains(rig.Audit, e => e.Tool == "cap_answer" && e.Result == "Allow 1% more once");
    }

    [Fact]
    public void Stopping_gives_the_owner_what_there_is()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(1)));
        Assert.Null(rig.Pick(rig.Card(record)!, CapOptionKind.Stop));
        Assert.Equal(MissionState.Stopped, rig.Missions.Now(record).Mission.State);
        Assert.Empty(rig.Missions.Agents.Chats);
        Assert.Null(rig.Guard.HoldOf(record.Id));
        Assert.Contains(rig.Audit, e => e.Tool == "cap_answer" && e.Result == "Stop and give me what you have");
    }

    [Fact]
    public void A_spend_that_reaches_the_cap_pauses_the_task_at_its_boundary_and_never_cuts_the_running_turn()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(5)));
        var lead = rig.LeadOf(record);

        rig.Guard.OnSpend(lead, Model, 450_000);                    // 4.5 points
        Assert.Equal(MissionState.Working, rig.Missions.Now(record).Mission.State);
        Assert.Null(rig.Card(record));
        rig.Guard.OnSpend(lead, Model, 60_000);                     // 5.1: the cap is reached

        var pausing = rig.Missions.Now(record);
        Assert.Equal(MissionState.Pausing, pausing.Mission.State);
        Assert.Equal(PauseCause.Reserve, pausing.Mission.PausedBy);
        var chat = rig.Missions.Agents.Of(lead);
        Assert.True(chat.Running);                                  // the turn goes on
        Assert.Equal(0, chat.Interrupts);                           // nothing was interrupted
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal(["Cap", "Where", "Used", "Your cap", "The next step needs about", "Where it stands"], card.Inputs.Select(i => i.Name));
        Assert.Equal("5.1%", card.Inputs.Single(i => i.Name == "Used").Value);
        Assert.Equal("1.3%", card.Inputs.Single(i => i.Name == "The next step needs about").Value);
        Assert.Contains("Allow 2% more once", card.Options);        // 5.1 used + 1.32 for the next step - 5 = 1.42, rounded up
        Assert.Single(chat.Sent);                                   // nothing new was sent to it

        // Its turn ends: now it is Paused, in Needs you, and still nothing is sent.
        rig.EndTurn(lead, "Half done.");
        var paused = rig.Missions.Now(record);
        Assert.Equal(MissionState.Paused, paused.Mission.State);
        Assert.Equal(BoardGroup.NeedsYou, paused.Mission.Group);
        Assert.Single(chat.Sent);
        Assert.Equal(0, chat.Interrupts);

        // The owner lets it go over once, by what the next step needs: the mission goes on to its review.
        Assert.Null(rig.Pick(card, CapOptionKind.AllowOnce));
        Assert.Equal(MissionState.Reviewing, rig.Missions.Now(record).Mission.State);
        Assert.NotNull(rig.Missions.Now(record).ReviewerThread);
    }

    [Fact]
    public void A_cap_the_review_would_pass_holds_the_task_after_its_lead_is_done()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(5)));
        var lead = rig.LeadOf(record);
        rig.Guard.OnSpend(lead, Model, 400_000);                    // 4 points: room for nothing more than 1
        rig.EndTurn(lead, "Done.");                                 // the reviewer's turn (model-b, not seen yet: 120,000 tokens, 1.33 points) would need 1.33 more: 5.33 > 5

        var held = rig.Missions.Now(record);
        Assert.Equal(MissionState.Paused, held.Mission.State);
        Assert.Null(held.ReviewerThread);
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal("4%", card.Inputs.Single(i => i.Name == "Used").Value);
        Assert.Contains(rig.Audit, e => e.Tool == "cap_hold" && e.Result!.StartsWith("review:", StringComparison.Ordinal));
        Assert.Null(rig.Pick(card, CapOptionKind.AllowOnce));
        Assert.Equal(MissionState.Reviewing, rig.Missions.Now(record).Mission.State);
    }

    [Fact]
    public void A_pause_the_owner_made_stands_and_there_is_no_second_card()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(5)));
        var lead = rig.LeadOf(record);
        Assert.Null(rig.Missions.Runner.Pause(record.Id));
        rig.Guard.OnSpend(lead, Model, 600_000);
        Assert.Null(rig.Card(record));
        Assert.Null(rig.Guard.HoldOf(record.Id));
        Assert.Equal(PauseCause.Owner, rig.Missions.Now(record).Mission.PausedBy);
        Assert.Empty(rig.Holds);
    }

    // ---- an assistant's day ----

    [Fact]
    public void An_assistants_daily_cap_counts_all_of_its_missions_and_holds_the_next_one_in_the_canvas_words()
    {
        using var rig = Rig();
        rig.DailyCap(3, "channel-a");
        var first = rig.Started(For(rig, "channel-a", "first"));
        rig.Guard.OnSpend(rig.LeadOf(first), Model, 150_000);       // 1.5 today
        var second = rig.Started(For(rig, "channel-a", "second"));  // 1.5 + 1.32 = 2.82: room
        Assert.Null(rig.Card(second));
        rig.Guard.OnSpend(rig.LeadOf(second), Model, 100_000);      // 2.5 today: the cap is not reached between turns
        Assert.Null(rig.Card(second));
        rig.Guard.OnSpend(rig.LeadOf(second), Model, 60_000);       // 3.1: it is, and every mission of the assistant with a turn running pauses
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(second).Mission.State);
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(first).Mission.State);

        var third = rig.Started(For(rig, "channel-a", "third"));    // 3.1 + 1.32 > 3: held before it starts
        Assert.Equal(MissionState.Paused, third.Mission.State);
        var card = Assert.IsType<DecisionItem>(rig.Card(third));
        Assert.Equal("Standing cap for Channel A: at most 3% of any account a day", card.Why);
        Assert.Equal("Channel A: at most 3% of any account a day", card.Inputs.Single(i => i.Name == "Cap").Value);
        Assert.Equal("Claude 20x's 5-hour window: 3.1% used today; your cap is 3%. The next step needs about 1.3% more.", card.Inputs.Single(i => i.Name == "Where it stands").Value);
        Assert.Equal(["Allow 2% more once", "Wait until tomorrow", "Stop and give me what you have"], card.Options);
        Assert.Equal("Paused at your cap of 3% a day", rig.Missions.Now(third).Waiting);
    }

    [Fact]
    public void A_cap_on_one_assistant_leaves_the_others_alone_and_one_that_names_none_limits_each_on_its_own()
    {
        using var rig = Rig();
        rig.DailyCap(3, "channel-a");
        rig.Guard.OnSpend(rig.LeadOf(rig.Started(For(rig, "channel-a", "spender"))), Model, 450_000);   // 4.5 today: over it
        Assert.NotNull(rig.Card(rig.Missions.Runner.Missions.Single(m => m.Mission.Title == "spender")));
        var other = rig.Started(For(rig, "dextra", "other"));      // no cap names Dextra
        Assert.Equal(MissionState.Working, other.Mission.State);
        Assert.Null(rig.Card(other));

        using var each = Rig();
        each.DailyCap(3, assistant: null);
        var a = each.Started(For(each, "dextra", "a"));
        each.Guard.OnSpend(each.LeadOf(a), Model, 200_000);          // 2 today for Dextra
        var b = each.Started(For(each, "channel-a", "b"));
        each.Guard.OnSpend(each.LeadOf(b), Model, 200_000);          // 2 today for Channel A: 4 together, but each is under 3
        Assert.Null(each.Card(a));
        Assert.Null(each.Card(b));
        var c = each.Started(For(each, "dextra", "c"));              // Dextra: 2 + 1.32 > 3
        Assert.Equal(MissionState.Paused, c.Mission.State);
        Assert.NotNull(each.Card(c));
    }

    [Fact]
    public void A_turn_that_runs_on_another_account_is_counted_against_that_account_not_the_missions()
    {
        // AS-14: a reviewer picked on another account passes the account its turn will run on (ReserveGuard.BeforeTurn's onAccount).
        using var rig = Rig();
        rig.DailyCap(2, "dextra");
        rig.Ledger.AddPoints("dextra", CodexA.Key, CodexA.Name, false, ReserveWindowKind.FiveHour, 1.0);   // 1 today on Codex A, nothing on Claude 20x
        var record = rig.Started(For(rig, "dextra"));
        Assert.Null(rig.Card(record));

        Assert.True(rig.Guard.BeforeTurn(record, null, ProviderKind.Claude, Model, ReserveTurns.Review));                  // on its own account: 1.32 fits under 2
        Assert.False(rig.Guard.BeforeTurn(record, null, ProviderKind.Codex, Model, ReserveTurns.Review, CodexA));          // on Codex A: 1 + 1.32 passes it
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Contains(card.Inputs, i => i.Value == "Codex A's 5-hour window");
    }

    [Fact]
    public void A_mission_is_not_held_by_what_its_assistant_used_of_an_account_the_mission_is_not_running_on()
    {
        // The router spreads an assistant's missions over accounts: one mission using up Codex A's share of the day is not a reason to stop another on Claude 20x.
        using var rig = Rig();
        rig.DailyCap(3, "channel-a");
        rig.DefaultAccount = CodexA;
        var onCodex = rig.Started(For(rig, "channel-a", "on codex"));
        rig.Guard.OnSpend(rig.LeadOf(onCodex), Model, 450_000);      // 4.5 of Codex A today: over the cap
        Assert.NotNull(rig.Card(onCodex));

        rig.DefaultAccount = Claude20;
        var onClaude = rig.Started(For(rig, "channel-a", "on claude"));
        Assert.Equal(MissionState.Working, onClaude.Mission.State);   // 0 + 1.32 of Claude 20x fits
        rig.Guard.OnSpend(rig.LeadOf(onClaude), Model, 100_000);      // 1 of Claude 20x today: well under
        Assert.Equal(MissionState.Working, rig.Missions.Now(onClaude).Mission.State);
        Assert.Null(rig.Card(onClaude));
        Assert.Single(rig.Holds);                                      // only the mission on Codex A was ever held
    }

    [Fact]
    public void What_an_assistants_own_thread_spends_counts_toward_its_day_and_a_chat_of_no_assistant_counts_for_nobody()
    {
        using var rig = Rig();
        rig.DailyCap(3, "dextra");
        var home = Guid.NewGuid();
        rig.HomeThreads[home] = "dextra";
        rig.Guard.OnSpend(home, Model, 250_000);                    // 2.5 today, spent in the home thread
        var stranger = Guid.NewGuid();
        rig.Guard.OnSpend(stranger, Model, 900_000);                // a chat that belongs to no assistant: not counted
        var record = rig.Started();                                  // 2.5 + 1.32 > 3
        Assert.Equal(MissionState.Paused, record.Mission.State);
        Assert.NotNull(rig.Card(record));
        Assert.Equal(2.5, rig.Ledger.UseOf(new CostCap("c", CostCapScope.AssistantDay) { Percent = 3, AssistantId = "dextra" }).Points[(Claude20.Key, ReserveWindowKind.FiveHour)], 6);
    }

    [Fact]
    public void Waiting_until_tomorrow_resumes_the_task_a_minute_after_local_midnight_when_the_day_starts_again()
    {
        using var rig = Rig();
        rig.DailyCap(3, "channel-a");
        var spender = rig.Started(For(rig, "channel-a", "spender"));
        rig.Guard.OnSpend(rig.LeadOf(spender), Model, 310_000);     // 3.1 today
        var record = rig.Started(For(rig, "channel-a", "waits"));
        Assert.Null(rig.Pick(rig.Card(record)!, CapOptionKind.WaitForTomorrow));
        Assert.Equal("Waiting for tomorrow, then your cap of 3% a day starts again", rig.Missions.Now(record).Waiting);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        Assert.Equal(1, rig.Clock.ActiveTimers);

        rig.Clock.Advance(TimeSpan.FromHours(15));                   // it is 00:00 on the 14th: AutoContinue's margin of 60 s is still to come
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        rig.Clock.Advance(TimeSpan.FromSeconds(61));
        var now = rig.Missions.Now(record);
        Assert.Equal(MissionState.Working, now.Mission.State);
        Assert.NotNull(now.LeadThread);
        Assert.Null(now.Waiting);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        Assert.Contains(rig.Audit, e => e.Tool == "cap_wait" && e.Result == "a new day, resumed");
    }

    [Fact]
    public void After_tomorrow_a_cap_that_still_holds_asks_again_and_a_stopped_task_waits_for_nothing()
    {
        using var rig = Rig();
        rig.DailyCap(3, "channel-a");
        rig.Guard.OnSpend(rig.LeadOf(rig.Started(For(rig, "channel-a", "spender"))), Model, 310_000);
        var record = rig.Started(For(rig, "channel-a", "waits"));
        Assert.Null(rig.Pick(rig.Card(record)!, CapOptionKind.WaitForTomorrow));
        rig.Clock.Advance(TimeSpan.FromHours(15) + TimeSpan.FromSeconds(30));
        rig.DailyCap(0.5, "channel-a");                              // the owner sets a cap tomorrow's first step still passes (1.32 > 0.5)
        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        var asked = rig.Missions.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Cap && c.State == DecisionState.Open);
        Assert.StartsWith("It is a new day, but a cap still holds.", asked.Note, StringComparison.Ordinal);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        Assert.Contains(rig.Audit, e => e.Tool == "cap_wait" && e.Result == "a new day, a cap still holds");

        // Waiting again, then stopped: the timer goes with the task and nothing resumes it.
        Assert.Null(rig.Pick(asked, CapOptionKind.WaitForTomorrow));
        Assert.Equal(1, rig.Clock.ActiveTimers);
        Assert.Null(rig.Missions.Runner.Stop(record.Id));
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(MissionState.Stopped, rig.Missions.Now(record).Mission.State);
    }

    [Fact]
    public void A_cap_the_owner_adds_while_a_task_runs_holds_it_at_once_if_the_day_is_already_past_it()
    {
        using var rig = Rig();
        var record = rig.Started();
        var lead = rig.LeadOf(record);
        rig.Guard.OnSpend(lead, Model, 300_000);                    // 3 today, no cap yet
        Assert.Null(rig.Card(record));
        rig.DailyCap(2, "dextra");
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(record).Mission.State);
        Assert.NotNull(rig.Card(record));
    }

    // ---- money ----

    [Fact]
    public void A_cap_on_the_api_keys_counts_dollars_with_the_models_price_and_holds_the_next_turn()
    {
        using var rig = Rig();
        rig.DefaultAccount = Key;
        rig.KeyCap(2m);
        var record = rig.Started();                                  // $0 so far + $1.32 for the first turn: room
        Assert.Null(rig.Card(record));
        var lead = rig.LeadOf(record);
        rig.Guard.OnSpend(lead, Model, 100_000);                    // 100,000 tokens at $10 a million: $1.00
        Assert.Null(rig.Card(record));
        rig.EndTurn(lead, "Done.");                                 // the reviewer's turn, on model-b whose turns nth has not seen: 120,000 tokens, $1.20; 1.00 + 1.20 > 2.00

        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal("At most $2 a day on API keys", card.Inputs.Single(i => i.Name == "Cap").Value);
        Assert.Equal("today's API spend", card.Inputs.Single(i => i.Name == "Where").Value);
        Assert.Equal("$1.00", card.Inputs.Single(i => i.Name == "Used").Value);
        Assert.Equal("$2.00", card.Inputs.Single(i => i.Name == "Your cap").Value);
        Assert.Equal("$1.20", card.Inputs.Single(i => i.Name == "The next step needs about").Value);
        Assert.Equal("Today's API spend is $1.00; your cap is $2.00. The next step needs about $1.20 more.", card.Inputs.Single(i => i.Name == "Where it stands").Value);
        Assert.Equal("Standing cap: at most $2 a day on API keys", card.Why);
        Assert.Equal(["Allow $1 more once", "Wait until tomorrow", "Stop and give me what you have"], card.Options);
        Assert.Equal("Paused at your cap of $2 a day", rig.Missions.Now(record).Waiting);
    }

    [Fact]
    public void A_models_spend_is_priced_from_its_input_output_and_cached_input_and_an_unknown_price_is_a_dear_one()
    {
        using var rig = Rig();
        rig.DefaultAccount = Key;
        var lead = Guid.NewGuid();
        rig.HomeThreads[lead] = "dextra";
        // 150,000 input of which 100,000 cached, 50,000 output at $10 / $10 / $1 cached: 50,000 x 10 + 100,000 x 1 + 50,000 x 10 = $1.10
        rig.Guard.OnSpend(lead, new TokenSpend(ProviderKind.OpenRouter, 200_000, 150_000, 50_000, 100_000, Model));
        var keys = new CostCap("k", CostCapScope.ApiDay) { Dollars = 100m };
        Assert.Equal(1.1m, rig.Ledger.UseOf(keys).Dollars);

        var unpriced = Guid.NewGuid();
        rig.HomeThreads[unpriced] = "dextra";
        rig.AccountOfThread[unpriced] = Unpriced;                    // a key whose model has no known price
        rig.Guard.OnSpend(unpriced, new TokenSpend(ProviderKind.Deepseek, 200_000, model: "mystery"));
        Assert.Equal(1.1m + 3m, rig.Ledger.UseOf(keys).Dollars);     // 200,000 tokens at the dear $15 a million

        var plain = Guid.NewGuid();
        rig.HomeThreads[plain] = "dextra";
        rig.Guard.OnSpend(plain, Model, 100_000);                    // no figure from the caller: the model's price, $1.00
        Assert.Equal(1.1m + 3m + 1m, rig.Ledger.UseOf(keys).Dollars);
    }

    [Fact]
    public void A_subscription_turn_is_never_held_by_a_cap_on_money_and_an_api_turn_is_never_held_by_a_percent_cap()
    {
        using var money = Rig();
        money.KeyCap(1m);
        money.Ledger.AddDollars("dextra", Key.Key, Key.Name, 5m);   // far past it already
        var onSubscription = money.Started();
        Assert.Equal(MissionState.Working, onSubscription.Mission.State);
        Assert.Null(money.Card(onSubscription));

        using var percent = Rig();
        percent.DefaultAccount = Key;
        percent.DailyCap(1, "dextra");
        percent.Ledger.AddPoints("dextra", Claude20.Key, Claude20.Name, false, ReserveWindowKind.FiveHour, 9);
        var onKey = percent.Started();
        Assert.Equal(MissionState.Working, onKey.Mission.State);
        Assert.Null(percent.Card(onKey));
    }

    [Fact]
    public void A_missions_own_budget_in_dollars_is_a_cap_for_that_task()
    {
        using var rig = Rig();
        rig.DefaultAccount = Key;
        var record = rig.Started(rig.Missions.Request("Budgeted") with { BudgetUsd = 2m });
        Assert.Null(rig.Card(record));
        var lead = rig.LeadOf(record);
        rig.Guard.OnSpend(lead, Model, 150_000);                    // $1.50 of $2
        rig.EndTurn(lead, "Done.");
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal("At most $2 for this task", card.Inputs.Single(i => i.Name == "Cap").Value);
        Assert.Equal("Your cap for this task: at most $2", card.Why);
        Assert.Equal("This task has spent $1.50; your cap is $2.00. The next step needs about $1.20 more.", card.Inputs.Single(i => i.Name == "Where it stands").Value);
        Assert.Equal(["Allow $1 more once", "Stop and give me what you have"], card.Options);
    }

    // ---- what it does not know, and what it never says ----

    [Fact]
    public void A_task_on_an_account_nothing_is_known_of_is_not_held()
    {
        using var rig = Rig();
        rig.DefaultAccount = null;
        rig.DailyCap(0.1, "dextra");
        var record = rig.Started(rig.WithTaskCap(TaskPercent(0.1)));
        Assert.Equal(MissionState.Working, record.Mission.State);
        Assert.Null(rig.Card(record));
    }

    [Fact]
    public void Nothing_a_missions_title_says_reaches_the_activity_log_and_a_log_that_fails_is_reported_not_thrown()
    {
        const string Sentinel = "sk-SENTINEL-0123456789abcdef";
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(1), "Use " + Sentinel + " to log in"));
        Assert.Null(rig.Pick(rig.Card(record)!, CapOptionKind.AllowOnce));
        Assert.NotEmpty(rig.Audit);
        Assert.All(rig.Audit, e => Assert.DoesNotContain(Sentinel, string.Join(" ", e.Tool, e.Target, e.Result, e.Mission, e.Assistant, string.Join(" ", e.Inputs.Select(i => i.ToString()))), StringComparison.Ordinal));
        Assert.All(rig.Audit, e => Assert.Equal(record.Id.ToString("D"), e.Mission));   // each line names its mission, by id

        using var failing = Rig();
        failing.AuditFails = true;
        var held = failing.Started(failing.WithTaskCap(TaskPercent(1)));
        Assert.NotNull(failing.Card(held));                          // the owner still sees the card
        Assert.Contains(failing.Faults, line => line.StartsWith("The activity log could not be written", StringComparison.Ordinal));
    }

    [Fact]
    public void A_task_that_ends_while_held_takes_its_card_with_it()
    {
        using var rig = Rig();
        var record = rig.Started(rig.WithTaskCap(TaskPercent(1)));
        var card = rig.Card(record)!;
        Assert.True(rig.Guard.IsHeld(record.Id));
        rig.Guard.MissionEnded(record.Id);
        Assert.False(rig.Guard.IsHeld(record.Id));
        Assert.Equal(DecisionState.Withdrawn, rig.Missions.Queue.Find(card.Id)!.State);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void A_cap_that_limits_nothing_holds_nothing()
    {
        using var rig = Rig();
        Assert.Equal("Say how much the cap is.", rig.Settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { AssistantId = "dextra" }));
        var record = rig.Started(rig.WithTaskCap(new CostCap("", CostCapScope.Task)));
        Assert.Equal(MissionState.Working, record.Mission.State);
    }
}
