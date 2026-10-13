using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2, 6, 7.1, 12.4, 15.5; ED-NEW-dextra-as11-5, -7, -10): the Suggestions lane in the app. At most three a day however many digests;
// the cheap model may reword a line but not its numbers or its plan; Accept starts a mission inside the usual rules or raises the duty card (a duty is never made without
// it); Dismiss teaches and three dismissals of a kind turn the kind off; the brain's own suggest tool lands in the same lane, within the same three places.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-10")]
public sealed class DigestSuggestionTests
{
    private static ChatHabit Habit(string key, string label, bool local = false)
    {
        DateTimeOffset Friday(int weeksAgo) => new DateTimeOffset(2026, 10, 9, 16, 5, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo);
        return new ChatHabit("app:" + key, label, MissionRig.Project, "App", [Friday(2), Friday(1), Friday(0)]) { LocalOnly = local };
    }

    private static string AnswerFor(string prompt, string line, string cost)
    {
        var keys = new List<string>();
        for (var at = prompt.IndexOf("<<<", StringComparison.Ordinal); at >= 0; at = prompt.IndexOf("<<<", at + 3, StringComparison.Ordinal))
            keys.Add(prompt[(at + 3)..prompt.IndexOf('\n', at)]);
        return "[" + string.Join(",", keys.Select(k => $"{{\"key\":\"{k}\",\"line\":\"{line}\",\"cost\":\"{cost}\"}}")) + "]";
    }

    private static SuggestionPlan Mission(string brief = "Look into it.", Guid? project = null) => new(SuggestionDo.StartMission, "Look into it") { Brief = brief, ProjectId = project ?? MissionRig.Project };

    // ---- the day's three places ----

    [Fact]
    public async Task However_many_candidates_there_are_three_are_offered_in_a_day_across_all_the_digests()
    {
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings"), Habit("b", "File the invoices"), Habit("c", "Check the stock"), Habit("d", "Write the newsletter")]));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Equal(3, digest.Suggestions.Count);
        Assert.Equal("Suggestions · 3 of at most 3 today", digest.LaneTitle);
        var later = (await rig.Host.Digests.Compose(DigestKind.Asked))!;
        Assert.Equal(digest.Suggestions.Select(s => s.Key), later.Suggestions.Select(s => s.Key));
        Assert.Equal(3, rig.Host.Digests.Book.Ledger.OfferedToday);
    }

    [Fact]
    public async Task A_new_day_lets_yesterdays_go_and_offers_what_is_still_true()
    {
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")]));
        var first = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Single(first.Suggestions);
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var next = (await rig.Host.Digests.Compose(DigestKind.Scheduled))!;
        Assert.Single(next.Suggestions);                               // the habit is still a habit, and yesterday's offer was never answered
        Assert.Equal(1, rig.Host.Digests.Book.Ledger.OfferedToday);
    }

    [Fact]
    public async Task A_suggestion_is_never_a_decision_card_so_it_never_toasts()
    {
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")]));
        var before = rig.Controller.Queue.Waiting.Count;
        await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        Assert.Equal(before, rig.Controller.Queue.Waiting.Count);
        Assert.Single(rig.Host.Digests.Open);
    }

    // ---- the model's wording ----

    [Fact]
    public async Task A_model_that_rewords_the_lines_is_asked_once_and_its_words_are_used_where_the_check_allows()
    {
        var model = new FakeSuggestionModel(prompt => AnswerFor(prompt, "You do this every Friday. Want it as a duty?", "Nothing runs until you say so."));
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings"), Habit("b", "File the invoices")], rewriter: model));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Single(model.Prompts);
        Assert.Equal([false], model.Local);
        Assert.All(digest.Suggestions, s => Assert.Equal("You do this every Friday. Want it as a duty?", s.Line));
        Assert.All(rig.Host.Digests.Book.Ledger.Today, s => Assert.Equal(SuggestionDo.NewDuty, s.Plan.Do));    // the plan is nth's, never the model's
        // The model is asked about the new ones only: the same digest again asks it nothing.
        await rig.Host.Digests.Compose(DigestKind.Asked);
        Assert.Single(model.Prompts);
    }

    [Fact]
    public async Task A_rewrite_that_adds_a_number_leaves_the_line_nth_wrote()
    {
        var model = new FakeSuggestionModel(prompt => AnswerFor(prompt, "Do it 5 times?", "Nothing."));
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")], rewriter: model));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.StartsWith("You started “Re-price listings” by hand three Fridays running.", digest.Suggestions[0].Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_that_holds_a_private_chats_habit_goes_only_to_a_model_on_this_pc()
    {
        var model = new FakeSuggestionModel(prompt => AnswerFor(prompt, "A friendlier line?", "Nothing."));
        using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings", local: true)], rewriter: model));
        await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        Assert.Equal([true], model.Local);
    }

    [Fact]
    public async Task A_model_that_fails_or_never_answers_leaves_the_lines_and_the_digest_goes_on()
    {
        using var failing = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")], rewriter: new ThrowingSuggestionModel(new HttpRequestException("down"))));
        var digest = (await failing.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Single(digest.Suggestions);

        var hanging = new HangingSuggestionModel();
        using var slow = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")], rewriter: hanging, modelTimeout: TimeSpan.FromSeconds(5)));
        var composing = slow.Host.Digests!.Compose(DigestKind.Scheduled);
        await hanging.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        slow.Clock.Advance(TimeSpan.FromSeconds(6));                    // nth's own clock runs the limit out
        var late = (await composing.WaitAsync(TimeSpan.FromSeconds(10)))!;
        Assert.Single(late.Suggestions);
        Assert.StartsWith("You started", late.Suggestions[0].Line, StringComparison.Ordinal);
    }

    // ---- Accept ----

    [Fact]
    public void Accepting_a_missions_suggestion_starts_a_mission_of_the_owners_in_its_project_with_its_brief()
    {
        using var rig = Quiet();
        var offered = Offer(rig, Candidate("m1", Mission("List the outdated packages and report.")));
        var result = rig.Host.Digests!.Accept(offered.Key);
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Started. It's on the board.", result.Message);
        var mission = Assert.Single(rig.Controller.Runner.Missions);
        Assert.Equal(MissionOrigin.Owner, mission.Mission.Origin);
        Assert.Equal("dextra", mission.Mission.AssistantId);
        Assert.Equal(rig.Project.Id, mission.ProjectId);
        Assert.Equal("List the outdated packages and report.", mission.Brief);
        Assert.Equal(SuggestionState.Accepted, rig.Host.Digests.Book.Ledger.Today.Single().State);
        Assert.Empty(rig.Host.Digests.Open);
    }

    [Fact]
    public void A_mission_started_from_a_suggestion_waits_while_Stop_all_is_on()
    {
        using var rig = Quiet();
        rig.Controller.Hold.HoldForStop();
        var result = rig.Host.Digests!.Accept(Offer(rig, Candidate("m1", Mission())).Key);
        Assert.True(result.Ok);
        Assert.Equal("Accepted. It starts when you resume.", result.Message);
    }

    [Fact]
    public void A_suggestion_whose_project_is_gone_stays_open_with_the_reason()
    {
        using var rig = Quiet();
        var offered = Offer(rig, Candidate("m1", Mission(project: Guid.NewGuid())));
        var result = rig.Host.Digests!.Accept(offered.Key);
        Assert.False(result.Ok);
        Assert.Equal("The project it was for is gone.", result.Message);
        Assert.Empty(rig.Controller.Runner.Missions);
        Assert.Equal(SuggestionState.Open, rig.Host.Digests.Book.Ledger.Today.Single().State);
    }

    [Fact]
    public void A_suggestion_with_no_project_goes_to_the_only_project_and_a_closed_one_is_refused()
    {
        using var rig = Quiet();
        var plan = new SuggestionPlan(SuggestionDo.StartMission, "Look") { Brief = "Look." };
        Assert.True(rig.Host.Digests!.Accept(Offer(rig, Candidate("m1", plan)).Key).Ok);
        Assert.Equal(rig.Project.Id, Assert.Single(rig.Controller.Runner.Missions).ProjectId);
        var again = rig.Host.Digests.Accept("m1");
        Assert.False(again.Ok);
        Assert.Equal("That suggestion is no longer open.", again.Message);
    }

    [Fact]
    public void Accepting_a_new_duty_raises_its_card_and_makes_the_duty_only_when_the_owner_says_yes()
    {
        using var rig = Quiet();
        var plan = new SuggestionPlan(SuggestionDo.NewDuty, "Re-price listings")
        {
            ProjectId = rig.Project.Id,
            Day = DayOfWeek.Friday,
            At = new TimeOnly(16, 0),
            Instructions = "Re-price the listings the way the owner does.",
        };
        var result = rig.Host.Digests!.Accept(Offer(rig, Candidate("hand:a", plan, SuggestionKind.DoneByHand)).Key);
        Assert.True(result.Ok);
        Assert.Equal("The duty card is in Decisions.", result.Message);
        Assert.DoesNotContain(rig.Host.Scheduler.Duties, v => v.Duty.Name == "Re-price listings");       // nothing is made before he says yes
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal("Create the duty “Re-price listings”?", card.Title);
        Assert.Equal("Always shown: a new standing duty", card.Why);
        Assert.Equal(["Create duty", "Don't"], card.Options);
        var inputs = card.Inputs.ToDictionary(i => i.Name, i => i.Value);
        Assert.Equal("Fridays at 16:00 (UTC)", inputs["Trigger"]);
        Assert.Equal("Dextra", inputs["Assistant"]);
        Assert.Equal("Silent unless changed", inputs["Report"]);
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.Pick("Create duty")));
        var duty = Assert.Single(rig.Host.Scheduler.Duties, v => v.Duty.Name == "Re-price listings").Duty;
        Assert.Equal(new TimeTrigger(TimeSchedule.Weekly(WeekDays.Friday, new TimeOnly(16, 0)), "UTC"), duty.Trigger);
        Assert.Equal(rig.Project.Id, duty.ProjectId);
        Assert.Equal(ReportStyle.SilentUnlessChanged, duty.Report);
    }

    [Fact]
    public void Saying_dont_to_the_duty_card_makes_nothing()
    {
        using var rig = Quiet();
        var plan = new SuggestionPlan(SuggestionDo.NewDuty, "Re-price listings") { Day = DayOfWeek.Friday, At = new TimeOnly(16, 0), Instructions = "Do it." };
        Assert.True(rig.Host.Digests!.Accept(Offer(rig, Candidate("hand:a", plan, SuggestionKind.DoneByHand)).Key).Ok);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.Pick("Don't")));
        Assert.DoesNotContain(rig.Host.Scheduler.Duties, v => v.Duty.Name == "Re-price listings");
    }

    [Fact]
    public void A_damaged_new_duty_suggestion_makes_no_card()
    {
        using var rig = Quiet();
        var result = rig.Host.Digests!.Accept(Offer(rig, Candidate("hand:a", new SuggestionPlan(SuggestionDo.NewDuty, "Re-price"), SuggestionKind.DoneByHand)).Key);
        Assert.False(result.Ok);
        Assert.Empty(rig.Controller.Queue.Waiting);
    }

    [Fact]
    public void Accepting_a_slower_look_raises_the_change_card_with_what_changed_and_changes_the_duty_only_on_yes()
    {
        using var rig = Quiet();
        var duty = rig.Save(DutyHostRig.Every("d1", 120));
        var plan = new SuggestionPlan(SuggestionDo.ChangeDuty, duty.Name) { DutyId = "d1", EveryMinutes = 360 };
        var result = rig.Host.Digests!.Accept(Offer(rig, Candidate("idle:d1", plan, SuggestionKind.IdleDuty)).Key);
        Assert.True(result.Ok);
        Assert.Equal(120, ((TimeTrigger)rig.Host.Scheduler.Find("d1")!.Trigger).Schedule.EveryMinutes);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal("Change the duty “Duty d1”?", card.Title);
        Assert.Equal(["Change duty", "Don't"], card.Options);
        Assert.Contains("(was:", card.Inputs.Single(i => i.Name == "Trigger").Value, StringComparison.Ordinal);
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.Pick("Change duty")));
        Assert.Equal(360, ((TimeTrigger)rig.Host.Scheduler.Find("d1")!.Trigger).Schedule.EveryMinutes);
    }

    [Fact]
    public void A_slower_look_for_a_duty_that_is_gone_or_already_slow_or_not_on_an_interval_is_refused()
    {
        using var rig = Quiet();
        rig.Save(DutyHostRig.Every("slow", 360));
        rig.Save(DutyHostRig.Daily("daily", 9));
        SuggestionAccept Try(string id) => rig.Host.Digests!.Accept(Offer(rig, Candidate("idle:" + id, new SuggestionPlan(SuggestionDo.ChangeDuty, id) { DutyId = id, EveryMinutes = 360 }, SuggestionKind.IdleDuty)).Key);
        Assert.Equal("That duty is gone.", Try("nope").Message);
        Assert.Equal("It already looks that rarely.", Try("slow").Message);
        Assert.Equal("That duty no longer looks on a fixed interval, so there is nothing to slow down.", Try("daily").Message);
        Assert.Empty(rig.Controller.Queue.Waiting);
    }

    // ---- Dismiss ----

    [Fact]
    public void Dismiss_says_so_and_the_third_dismissal_of_a_kind_says_the_kind_is_off_and_offers_nothing_of_it_again()
    {
        using var rig = Quiet();
        var service = rig.Host.Digests!;
        var keys = Enumerable.Range(1, 3).Select(i => Offer(rig, Candidate("b" + i, Mission(), urgency: 10 - i)).Key).ToList();
        Assert.Equal("Dismissed. I'll offer fewer like this.", service.Dismiss(keys[0]));
        Assert.Equal("Dismissed. I'll offer fewer like this.", service.Dismiss(keys[1]));
        Assert.False(service.IsOff(SuggestionKind.FromBrain));
        Assert.Equal("Dismissed. I won't offer these any more.", service.Dismiss(keys[2]));
        Assert.True(service.IsOff(SuggestionKind.FromBrain));
        Assert.Null(service.Dismiss(keys[2]));                                      // answered already
        Assert.Empty(service.Open);
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var refused = service.Suggest("dextra", "Try something new?", null);
        Assert.True(refused.IsError);
        Assert.Equal("Not added: the owner turned this kind of suggestion off.", refused.Text);
        service.TurnOn(SuggestionKind.FromBrain);
        Assert.False(service.IsOff(SuggestionKind.FromBrain));
        Assert.False(service.Suggest("dextra", "Try something new?", null).IsError);
    }

    // ---- the brain's own line ----

    [Fact]
    public void The_suggest_tool_adds_one_line_within_the_days_three_places_and_never_the_same_one_twice()
    {
        using var rig = Quiet();
        var service = rig.Host.Digests!;
        var first = service.Suggest("dextra", "The espresso writer could run on Codex A tonight?", "Draft the article on Codex A.");
        Assert.False(first.IsError);
        Assert.Equal("Added to today's suggestions (1 of at most 3). The owner sees it in the digest.", first.Text);
        Assert.Equal("It is in today's suggestions already.", service.Suggest("dextra", "  the espresso writer could run on codex a tonight?  ", null).Text);
        Assert.False(service.Suggest("dextra", "Two", null).IsError);
        Assert.False(service.Suggest("dextra", "Three", null).IsError);
        var full = service.Suggest("dextra", "Four", null);
        Assert.True(full.IsError);
        Assert.Equal("Not added: today's 3 suggestions are used.", full.Text);
        var suggestion = service.Open[0];
        Assert.Equal(SuggestionKind.FromBrain, suggestion.Kind);
        Assert.Equal(SuggestionDo.StartMission, suggestion.Plan.Do);
        Assert.Equal("Draft the article on Codex A.", suggestion.Plan.Brief);
    }

    [Fact]
    public void The_suggest_tool_with_nothing_to_say_or_a_secret_in_it_is_handled()
    {
        using var rig = Quiet();
        var service = rig.Host.Digests!;
        Assert.True(service.Suggest("dextra", "   ", null).IsError);
        var secret = "sk-" + new string('k', 40);
        Assert.False(service.Suggest("dextra", "Use the key " + secret + " to publish?", "Publish with " + secret).IsError);
        var stored = service.Open.Single();
        Assert.DoesNotContain(secret, stored.Line + stored.Plan.Brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_the_brain_adds_after_the_digest_was_composed_shows_in_the_lane_and_as_a_chip()
    {
        using var rig = Quiet();
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Empty(digest.Suggestions);
        var changes = 0;
        rig.Host.Digests.Changed += () => changes++;
        rig.Host.Digests.Suggest("dextra", "Back up the invoices?", null);
        Assert.Equal(1, changes);
        Assert.Single(rig.Host.Digests.Open);
    }

    [Fact]
    public async Task The_tool_is_wired_through_the_controller_and_says_it_is_not_part_of_this_build_when_the_lane_is_let_go()
    {
        using var rig = Quiet();
        AssistantToolCall Call(string line) => new(new AssistantChat(Guid.NewGuid(), "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a"),
            AssistantToolCatalog.Find("suggest")!, JSONValue.Parse("{\"line\":\"" + line + "\"}")!,
            new Classification(ActionClass.Work, "adds a line to the Suggestions lane"), new PolicyDecision(Verdict.Allow, PolicyRule.Alone, "Allowed"), AssistantRoute.Mcp);
        var ran = await rig.Controller.Run(Call("Back up the invoices?"), CancellationToken.None);
        Assert.False(ran.IsError);
        Assert.Single(rig.Host.Digests!.Open);
        rig.Host.Digests.Dispose();
        var gone = await rig.Controller.Run(Call("Another?"), CancellationToken.None);
        Assert.True(gone.IsError);
        Assert.Contains("is not part of this build", gone.Text, StringComparison.Ordinal);
    }
}
