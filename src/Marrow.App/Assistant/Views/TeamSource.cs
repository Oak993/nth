// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.1, 9.4, 12.6b; ED-NEW-dextra-as17-4, -5). No Swift twin.
// Where the org chart and the sidebar's Astur rows read the team from: one snapshot of the profiles, the tasks, the cards that wait and today's use, rebuilt whenever
// any of them changes, and the two controls the detail panel has for an assistant's tasks (Pause and Resume, which act on the tasks the board already shows, through
// the same calls, so a refusal reads as it does there). Everything runs on the UI thread: the sources raise their changes there.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

public interface ITeamSource
{
    /// <summary>The team now.</summary>
    TeamSnapshot Now();

    /// <summary>Something the snapshot reads changed (raised on the UI thread).</summary>
    event Action? Changed;

    /// <summary>
    /// The id of the Astur a task belongs to; null when the task is Dextra's own (or belongs to nobody, or to an Astur that is retired). Dextra's Missions group in the sidebar lists the tasks
    /// this answers null for; each Astur's lists its own.
    /// </summary>
    string? AsturOf(Guid mission);

    /// <summary>Pauses the tasks of one assistant that can pause; the line says what it did.</summary>
    string PauseTasks(string assistantId);

    /// <summary>Resumes the tasks of one assistant that can resume; the line says what it did.</summary>
    string ResumeTasks(string assistantId);
}

public sealed class TeamSource : ITeamSource, IDisposable
{
    private readonly Func<IReadOnlyList<AssistantProfile>> team;
    private readonly IMissionJobs jobs;
    private readonly Func<Guid, string?> ownerOf;
    private readonly IDecisionSource decisions;
    private readonly IGlobalControls controls;
    private readonly Func<TeamUsage?>? usage;
    private readonly Func<string, TeamFacts>? facts;
    private readonly IDisposable? watching;
    private bool disposed;

    /// <param name="team">The profiles: Dextra's, then the Asturs' that are not retired.</param>
    /// <param name="ownerOf">The id of the assistant a task belongs to (null: Dextra).</param>
    /// <param name="usage">Today's use against each slice (null, or a null answer: no cost controls).</param>
    /// <param name="facts">The duties, notes and grants an assistant has, and its reserve in words, for the detail.</param>
    /// <param name="watchTeam">Subscribes to a change of the team (an Astur made, edited or retired); the handle ends the subscription.</param>
    public TeamSource(Func<IReadOnlyList<AssistantProfile>> team, IMissionJobs jobs, Func<Guid, string?> ownerOf, IDecisionSource decisions, IGlobalControls controls,
        Func<TeamUsage?>? usage = null, Func<string, TeamFacts>? facts = null, Func<Action, IDisposable>? watchTeam = null)
    {
        this.team = team ?? throw new ArgumentNullException(nameof(team));
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        this.ownerOf = ownerOf ?? throw new ArgumentNullException(nameof(ownerOf));
        this.decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
        this.usage = usage;
        this.facts = facts;
        jobs.Changed += Raise;
        decisions.Changed += Raise;
        controls.Changed += Raise;
        watching = watchTeam?.Invoke(Raise);
    }

    public event Action? Changed;

    /// <summary>Raises <see cref="Changed"/>: the views call it when only a slow figure (today's use) moved.</summary>
    public void Raise()
    {
        if (!disposed) Changed?.Invoke();
    }

    public TeamSnapshot Now()
    {
        var profiles = team();
        var root = profiles.FirstOrDefault(p => p.IsRoot)?.Id ?? "";
        var now = SwiftClock.Provider.Current.GetUtcNow();
        var all = jobs.Snapshot(now).Select(j => (ownerOf(j.Id) ?? root, j)).ToList();
        return TeamSnapshot.Build(profiles, all, decisions.Waiting, usage?.Invoke(), controls.Stopped is not null, facts, now);
    }

    public string? AsturOf(Guid mission) =>
        ownerOf(mission) is { } owner && team().FirstOrDefault(p => p.Id == owner) is { IsRoot: false, Retired: false } ? owner : null;

    public string PauseTasks(string assistantId)
    {
        var name = NameOf(assistantId);
        var done = 0;
        string? refusal = null;
        foreach (var job in Mine(assistantId).Where(j => j.Slots is { A: SlotA.Pause, AEnabled: true }))
        {
            var said = jobs.Pause(job.Id);
            if (said is null) done++;
            else refusal ??= said;
        }
        return OrgChartWords.PauseLine(name, done, refusal);
    }

    public string ResumeTasks(string assistantId)
    {
        var name = NameOf(assistantId);
        var done = 0;
        string? refusal = null;
        foreach (var job in Mine(assistantId).Where(j => j.Slots is { A: SlotA.Resume, AEnabled: true }))
        {
            var said = jobs.Resume(job.Id);
            if (said is null) done++;
            else refusal ??= said;
        }
        return OrgChartWords.ResumeLine(name, done, refusal);
    }

    private List<MissionJob> Mine(string assistantId)
    {
        var root = team().FirstOrDefault(p => p.IsRoot)?.Id ?? "";
        return [.. jobs.Snapshot(SwiftClock.Provider.Current.GetUtcNow()).Where(j => (ownerOf(j.Id) ?? root) == assistantId)];
    }

    private string NameOf(string assistantId) => team().FirstOrDefault(p => p.Id == assistantId)?.Name ?? AssistantNames.Assistant;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        jobs.Changed -= Raise;
        decisions.Changed -= Raise;
        controls.Changed -= Raise;
        watching?.Dispose();
    }
}
