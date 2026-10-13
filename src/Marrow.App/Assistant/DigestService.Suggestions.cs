// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2 "Accept is one tap and starts a mission inside the usual rules; a suggested duty still raises its creation card",
// 7.1, 12.4, 15.5; unit AS-11; ED-NEW-dextra-as11-5, -10). No Swift twin.
// What the owner does with the Suggestions lane. Accept starts a mission in the usual way (the router picks its account, the reserve and the plan card bind it
// as for any mission) or raises the card that makes or changes a duty: nothing that acts later is made without that card. Dismiss answers it for good and teaches
// the spotter (three dismissals of a kind turn the kind off). The brain's own `suggest` tool lands here too, rate-limited by the same three places a day.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>How an Accept came out: whether the suggestion is answered, and the line that takes its place in the lane.</summary>
public sealed record SuggestionAccept(bool Ok, string Message);

public sealed partial class DigestService
{
    public const string CreateDutyAnswer = "Create duty";

    public const string ChangeDutyAnswer = "Change duty";

    public const string DontAnswer = "Don't";

    // ---- Accept ----

    /// <summary>
    /// The owner taps Accept (main thread). A suggestion that could not be acted on (no project, the duty is gone) stays open with the reason, so he can
    /// try again or dismiss it; one that was acted on is answered and not offered again.
    /// </summary>
    public SuggestionAccept Accept(string key)
    {
        var found = Book.Ledger.Open.FirstOrDefault(s => s.Key == key);
        if (found is null) return new SuggestionAccept(false, "That suggestion is no longer open.");
        SuggestionAccept result;
        try { result = Act(found); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return new SuggestionAccept(false, "nth could not act on it: " + error.Message);
        }
        if (!result.Ok) return result;
        var now = o.Time.GetUtcNow();
        Book.Update(l => l.Accept(key, now));
        return result;
    }

    private SuggestionAccept Act(Suggestion suggestion) => suggestion.Plan.Do switch
    {
        SuggestionDo.StartMission => StartMission(suggestion.Plan),
        SuggestionDo.NewDuty => RaiseNewDutyCard(suggestion.Plan),
        SuggestionDo.ChangeDuty => RaiseChangeDutyCard(suggestion.Plan),
        _ => new SuggestionAccept(false, "nth does not know what accepting that does."),
    };

    private SuggestionAccept StartMission(SuggestionPlan plan)
    {
        var projects = o.Projects();
        var project = plan.ProjectId is { } id ? projects.FirstOrDefault(p => p.Id == id) : LastUsedProject(projects);
        if (project is null) return new SuggestionAccept(false, plan.ProjectId is null ? "Open a project first, then accept it." : "The project it was for is gone.");
        if (MainAssistantId() is not { } assistant) return new SuggestionAccept(false, "There is no assistant to run it yet.");
        // The same start as the start_mission tool: the router picks the lead's account and model where the reserve leaves room.
        var routed = o.Controller.Router?.PickLead(assistant);
        var started = o.Controller.Runner.Start(new MissionRequest(plan.Title, plan.Brief, assistant, project.Id, project.Path, routed?.Provider ?? setup.Provider)
        {
            Model = routed?.Model ?? setup.MissionModel,
            AccountId = routed?.AccountId,
            Origin = MissionOrigin.Owner,
        });
        var record = o.Controller.Runner.Find(started.Record.Id) ?? started.Record;
        if (record.Mission.State == MissionState.Blocked) return new SuggestionAccept(false, "Not started: " + (record.Reason ?? "the mission could not start."));
        return new SuggestionAccept(true, record.Mission.State == MissionState.Paused ? "Accepted. It starts when you resume." : "Started. It's on the board.");
    }

    // The project the owner used last; the only one when there is only one.
    private MissionProject? LastUsedProject(IReadOnlyList<MissionProject> projects)
    {
        if (projects.Count == 1) return projects[0];
        var last = setup.Projects?.Invoke().OrderByDescending(p => p.LastUsed).Select(p => p.Id).FirstOrDefault();
        return projects.FirstOrDefault(p => p.Id == last);
    }

    private SuggestionAccept RaiseNewDutyCard(SuggestionPlan plan)
    {
        if (MainAssistantId() is not { } assistant) return new SuggestionAccept(false, "There is no assistant to own the duty yet.");
        if (plan.Day is not { } day || plan.At is not { } at || string.IsNullOrWhiteSpace(plan.Instructions)) return new SuggestionAccept(false, "That suggestion is damaged, so no duty was made from it.");
        var name = plan.Title.Length > Duty.NameLimit ? plan.Title[..Duty.NameLimit] : plan.Title;
        var duty = new Duty(Duty.NewId(), name, assistant, new TimeTrigger(TimeSchedule.Weekly(WeekDaySets.Of(day), at), Zone.Id), plan.Instructions)
        {
            ProjectId = plan.ProjectId,
            Report = ReportStyle.SilentUnlessChanged,
            CreatedBy = assistant,
        };
        if (duty.Problem() is { } problem) return new SuggestionAccept(false, problem);
        RaiseDutyCard(duty, null);
        return new SuggestionAccept(true, "The duty card is in Decisions.");
    }

    private SuggestionAccept RaiseChangeDutyCard(SuggestionPlan plan)
    {
        if (plan.DutyId is not { } id || o.Scheduler.Find(id) is not { } existing) return new SuggestionAccept(false, "That duty is gone.");
        if (plan.EveryMinutes is not { } every) return new SuggestionAccept(false, "That suggestion is damaged, so no duty was changed by it.");
        Duty? changed = existing.Trigger switch
        {
            TimeTrigger { Schedule.Kind: ScheduleKind.Every } time => existing with { Trigger = time with { Schedule = TimeSchedule.Every(every) } },
            PageWatchTrigger page => existing with { Trigger = page with { EveryMinutes = every } },
            _ => null,
        };
        if (changed is null) return new SuggestionAccept(false, "That duty no longer looks on a fixed interval, so there is nothing to slow down.");
        if (changed.Trigger.Equals(existing.Trigger)) return new SuggestionAccept(false, "It already looks that rarely.");
        if (changed.Problem() is { } problem) return new SuggestionAccept(false, problem);
        RaiseDutyCard(changed, existing);
        return new SuggestionAccept(true, "The duty card is in Decisions.");
    }

    // 7.1: a duty is permanent configuration that acts later without the owner, so making or changing one always asks, with what it will be (and, for a
    // change, what changed). His yes on this card is the confirmation; nothing is made before it.
    private void RaiseDutyCard(Duty duty, Duty? existing)
    {
        var name = setup.AssistantName ?? (id => id);
        var yes = existing is null ? CreateDutyAnswer : ChangeDutyAnswer;
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question,
            existing is null ? $"Create the duty “{duty.Name}”?" : $"Change the duty “{duty.Name}”?")
        {
            Inputs = new ValueList<DecisionInput>(DutyCardWords.CardInputs(duty, name(duty.AssistantId), existing, DutyForecast.Of(duty, o.Time.GetUtcNow()), Zone)),
            Why = existing is null ? "Always shown: a new standing duty" : "Always shown: a change to a standing duty",
            Rule = "A duty acts later without you, so " + (existing is null ? "creating" : "changing") + " one always asks.",
            AssistantId = duty.AssistantId,
            Options = new ValueList<string>([yes, DontAnswer]),
        };
        o.Controller.Queue.Raise(card, (_, answer) =>
        {
            if (answer.Kind != DecisionAnswerKind.Pick || answer.Text != yes) return;
            var (saved, problem) = existing is null ? o.Scheduler.Add(duty) : o.Scheduler.Update(duty with { Id = existing.Id });
            if (saved is null) Fault($"The duty “{duty.Name}” was not {(existing is null ? "made" : "changed")}: {problem}");
        });
    }

    // ---- Dismiss ----

    /// <summary>
    /// The owner taps Dismiss: answered for good, counted against its kind, and the line that takes its place says so; the third dismissal of a kind says the
    /// kind is off. Null when there is no such open suggestion.
    /// </summary>
    public string? Dismiss(string key)
    {
        var kind = Book.Ledger.Open.FirstOrDefault(s => s.Key == key)?.Kind;
        if (kind is not { } known) return null;
        var now = o.Time.GetUtcNow();
        var after = Book.Update(l => l.Dismiss(key, now));
        return SuggestionWords.Dismissed(after.IsOff(known));
    }

    /// <summary>The owner turns a kind of suggestion back on (Settings > Assistant): its dismissals start again.</summary>
    public void TurnOn(SuggestionKind kind) => Book.Update(l => l.TurnOn(kind));

    /// <summary>Whether the owner turned this kind off with three dismissals.</summary>
    public bool IsOff(SuggestionKind kind) => Book.Ledger.IsOff(kind);

    // ---- the brain's own line ----

    /// <summary>
    /// The brain's <c>suggest</c> tool (15.5): one line for the lane. It takes one of the day's three places like any suggestion, and a context that read
    /// untrusted content cannot use the tool at all (12.4, the policy engine's Suggestion guard). Accept starts a mission whose brief is the tool's action, or its line.
    /// </summary>
    public AssistantToolResult Suggest(string assistantId, string line, string? action)
    {
        var text = FeedShield.Excerpt(line, 300);
        if (text.Length == 0) return AssistantToolResult.Failed("Not added: say the suggestion in one line.");
        var brief = FeedShield.Excerpt(action, 300) is { Length: > 0 } given ? given : text;
        var key = BrainKey(text);
        var now = o.Time.GetUtcNow();
        var today = DayOf(now, Zone);
        var candidate = new SuggestionCandidate(SuggestionKind.FromBrain, key, text, "One mission. The plan card shows what it will use before it starts.",
            new SuggestionPlan(SuggestionDo.StartMission, FeedShield.Excerpt(text, 80)) { Brief = brief })
        {
            Urgency = 10,
        };
        var before = Book.Ledger.OnDay(today);
        var after = Book.Update(l => l.Admit([candidate], now, today).Ledger);
        if (after.Today.Any(s => s.Key == key))
            return before.Today.Any(s => s.Key == key)
                ? AssistantToolResult.Done("It is in today's suggestions already.")
                : AssistantToolResult.Done($"Added to today's suggestions ({after.OfferedToday.ToString(System.Globalization.CultureInfo.InvariantCulture)} of at most {SuggestionLedger.MaxPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture)}). The owner sees it in the digest.");
        if (after.IsOff(SuggestionKind.FromBrain)) return AssistantToolResult.Failed("Not added: the owner turned this kind of suggestion off.");
        if (after.Settled.Contains(key)) return AssistantToolResult.Failed("Not added: the owner answered this one already.");
        return AssistantToolResult.Failed($"Not added: today's {SuggestionLedger.MaxPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture)} suggestions are used.");
    }
}
