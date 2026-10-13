using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-11): what a digest test sets up around the duty host rig: the setup the host is given and missions in known states. Helper code only (R-TEST-3).</summary>
internal static class DigestRigs
{
    /// <summary>The setup of a digest in a test: UTC, no model, nothing from Asturs or the delegate.</summary>
    public static DigestSetup Setup(Func<DateOnly, string?>? dayFinal = null, ISuggestionModel? rewriter = null, Func<IReadOnlyList<ChatHabit>>? habits = null,
        Func<IReadOnlyList<SpotProject>>? projects = null, Func<IReadOnlyList<string>>? logins = null, Action? fold = null,
        Func<IReadOnlyList<DigestAnswered>>? answered = null, Func<IReadOnlyList<DigestAstur>>? asturs = null, TimeSpan? modelTimeout = null) =>
        new()
        {
            Zone = () => TimeZoneInfo.Utc,
            DayFinal = dayFinal,
            Rewriter = rewriter,
            Habits = habits,
            Projects = projects,
            ExpiredLogins = logins,
            FoldNotes = fold,
            Answered = answered,
            Asturs = asturs,
            ModelTimeout = modelTimeout ?? TimeSpan.FromSeconds(30),
            AssistantName = id => id == "dextra" ? "Dextra" : id,
        };

    /// <summary>A rig with the digest on and the Morning digest duty switched off, so the scheduler does not compose one while a test sets up its state.</summary>
    public static DutyHostRig Quiet(DigestSetup? setup = null, Func<string, bool>? folderGranted = null)
    {
        var rig = new DutyHostRig(folderGranted: folderGranted, digest: setup ?? Setup());
        rig.Host.Start();
        Assert.Null(rig.Host.Scheduler.SetEnabled(DigestDuty.Id, false));
        return rig;
    }

    /// <summary>An owner mission, started and not finished.</summary>
    public static MissionRecord Started(DutyHostRig rig, string title) =>
        rig.Controller.Runner.Start(new MissionRequest(title, "Do it.", "dextra", rig.Project.Id, rig.Project.Path, ProviderKind.Claude)).Record;

    /// <summary>An owner mission that ended Done after the reviewer passed it.</summary>
    public static MissionRecord Finished(DutyHostRig rig, string title, string report = "Done.")
    {
        var record = Started(rig, title);
        rig.Complete(record, report);
        return rig.Now(record);
    }

    /// <summary>A mission whose lead's turn failed: it is Blocked and waits on the owner.</summary>
    public static MissionRecord Blocked(DutyHostRig rig, string title)
    {
        var record = Started(rig, title);
        var lead = rig.LeadOf(record);
        rig.Controller.OnTurnFinished(lead, TurnStatus.Failed, false);
        MissionRig.Until(() => rig.Now(record).Mission.State == MissionState.Blocked, "the mission to be blocked");
        return rig.Now(record);
    }

    public static DecisionItem Card(DutyHostRig rig, string title) =>
        rig.Controller.Queue.Raise(new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, title)
        {
            AssistantId = "dextra",
            Options = new ValueList<string>(["Yes", "No"]),
        }, (_, _) => { });

    /// <summary>Waits for the digest the scheduler or a call is composing to be kept.</summary>
    public static Digest Latest(DutyHostRig rig)
    {
        MissionRig.Until(() => rig.Host.Digests!.Latest is not null, "a digest to be composed");
        return rig.Host.Digests!.Latest!;
    }

    public static Suggestion Offer(DutyHostRig rig, SuggestionCandidate candidate)
    {
        rig.Host.Digests!.Book.Update(l => l.Admit([candidate], rig.Clock.GetUtcNow(), DateOnly.FromDateTime(rig.Clock.GetUtcNow().UtcDateTime)).Ledger);
        return rig.Host.Digests.Book.Ledger.Today.Single(s => s.Key == candidate.Key);
    }

    public static SuggestionCandidate Candidate(string key, SuggestionPlan plan, SuggestionKind kind = SuggestionKind.FromBrain, double urgency = 1) =>
        new(kind, key, "A line for " + key + ".", "A cost.", plan) { Urgency = urgency };
}
