// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.1 "Sidebar" (the Astur rows), 9.4 "Org chart (Team tab)": each node shows its mark, the name, the current work line, a status chip
// and a small usage meter, with mission chips under it; ED-NEW-dextra-as17-4). No Swift twin.
// What the org chart and the sidebar's Astur rows both show, worked out once from the sources the home already reads: the team's profiles, the tasks (with the
// assistant each belongs to), the decisions that wait, and today's use against each slice. Immutable and structural (records and value lists), so a view can tell a
// change that needs its parts redrawn from one that is the same again, and a tick that changes nothing draws nothing. Pure: no Avalonia, no clock of its own.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>How a mission chip is drawn: a dot for work, two bars for a pause, a ring for what needs the owner, a hollow dot for the rest. Never colour alone.</summary>
public enum TeamChipKind { Working, Paused, Attention, Other }

/// <summary>One live task of an assistant, as its chip under the node shows it.</summary>
public sealed record TeamMission(Guid Id, string Title, MissionState State, bool Hydra, Guid? LeadThread)
{
    public string Label => OrgChartWords.ChipLabel(Title);

    public TeamChipKind Kind => State switch
    {
        MissionState.Planning or MissionState.Working or MissionState.Reviewing or MissionState.Pausing => TeamChipKind.Working,
        MissionState.Paused or MissionState.Stopping => TeamChipKind.Paused,
        MissionState.NeedsYou or MissionState.Blocked or MissionState.Failed => TeamChipKind.Attention,
        _ => TeamChipKind.Other,
    };

    /// <summary>What a screen reader hears for the chip.</summary>
    public string Reading => (Hydra ? "Hydra team: " : "") + Title + ", " + MissionStates.Label(State);
}

/// <summary>What the detail panel counts beyond the tasks: the assistant's standing duties, its memory notes and the grants it holds, and its reserve in words.</summary>
public sealed record TeamFacts(int Duties = 0, int Notes = 0, int Grants = 0, string Reserve = "")
{
    public static TeamFacts None { get; } = new();
}

/// <summary>One assistant on the team: Dextra or an Astur.</summary>
public sealed record TeamMember(AssistantProfile Profile, AsturPresence Presence, string Work)
{
    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public bool IsDextra => Profile.IsRoot;

    public AsturShape? Shape => Profile.Shape;

    /// <summary>What it does at this moment, for the mark's hover ("Generating the voiceover"); null when it does nothing now.</summary>
    public string? Activity { get; init; }

    /// <summary>How many decisions of its own wait on the owner (the sidebar row's badge).</summary>
    public int Waiting { get; init; }

    /// <summary>Its live tasks (not finished, not stopped), newest first: one chip each, three at most are drawn.</summary>
    public ValueList<TeamMission> Missions { get; init; } = ValueList<TeamMission>.Empty;

    /// <summary>Whether Pause has a task to act on, and whether Resume has (the detail's one button shows the one that does).</summary>
    public bool CanPause { get; init; }

    public bool CanResume { get; init; }

    /// <summary>Today's use against its slice, 0 to 1 (a full bar once it is at the slice); null when there is nothing to measure it against.</summary>
    public double? Meter { get; init; }

    public string MeterWords { get; init; } = "";

    public TeamFacts Facts { get; init; } = TeamFacts.None;

    public AsturMarkState MarkState => AsturMarkStates.For(Presence);

    /// <summary>What a screen reader hears for the node or the row.</summary>
    public string Reading => OrgChartWords.NodeName(Name, Presence, Work, Waiting);
}

/// <summary>The whole team at one moment.</summary>
public sealed record TeamSnapshot(ValueList<TeamMember> Members, ArmillaState DextraMark)
{
    public static TeamSnapshot Empty { get; } = new(ValueList<TeamMember>.Empty, ArmillaState.Idle);

    /// <summary>Dextra, first. Null for an empty snapshot (no profile read yet).</summary>
    public TeamMember? Dextra => Members.Count > 0 && Members[0].IsDextra ? Members[0] : null;

    public IEnumerable<TeamMember> Asturs => Members.Where(m => !m.IsDextra);

    public TeamMember? Find(string? id) => id is null ? null : Members.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// The team now. <paramref name="team"/> is the profiles (Dextra first, the Asturs that are not retired after it); <paramref name="jobs"/> every task with the
    /// id of the assistant it belongs to; <paramref name="decisions"/> the cards that wait; <paramref name="usage"/> today's figures (null without cost controls);
    /// <paramref name="stopped"/> that Stop all holds everything. Dextra's own state is the whole team's (what waits on the owner first, then what thinks, then what
    /// works), as its header and its sidebar row say it; its work line is what its own tasks do, else the same status line they show.
    /// </summary>
    public static TeamSnapshot Build(IReadOnlyList<AssistantProfile> team, IReadOnlyList<(string AssistantId, MissionJob Job)> jobs, IReadOnlyList<DecisionItem> decisions,
        TeamUsage? usage = null, bool stopped = false, Func<string, TeamFacts>? facts = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(decisions);
        var members = new List<TeamMember>();
        var root = team.FirstOrDefault(p => p.IsRoot);
        var all = jobs.Select(j => j.Job).ToList();
        foreach (var profile in team.Where(p => !p.Retired))
        {
            var mine = jobs.Where(j => j.AssistantId == profile.Id).Select(j => j.Job).ToList();
            var asked = decisions.Where(d => d.AssistantId == profile.Id).ToList();
            members.Add(profile.IsRoot ? BuildDextra(profile, mine, all, asked, decisions, usage, stopped, facts) : BuildAstur(profile, mine, asked, usage, facts));
        }
        return new TeamSnapshot(new ValueList<TeamMember>(members), HomeWords.Mark(all, stopped));
    }

    private static TeamMember BuildAstur(AssistantProfile profile, List<MissionJob> mine, List<DecisionItem> asked, TeamUsage? usage, Func<string, TeamFacts>? facts)
    {
        var presence = AsturPresences.Of(mine.Select(j => j.State), asked.Count > 0);
        var member = Member(profile, presence, mine, asked, facts);
        if (usage is null) return member;
        // an Astur with a slice and no line yet has used nothing of it today
        var fraction = usage.Lines.FirstOrDefault(l => l.AssistantId == profile.Id)?.Fraction ?? (profile.Budget is null ? null : 0);
        return member with
        {
            Meter = fraction is { } f ? Math.Clamp(f, 0, 1) : null,
            MeterWords = OrgChartWords.SliceUse(fraction),
        };
    }

    private static TeamMember BuildDextra(AssistantProfile profile, List<MissionJob> mine, List<MissionJob> all, List<DecisionItem> asked, IReadOnlyList<DecisionItem> every,
        TeamUsage? usage, bool stopped, Func<string, TeamFacts>? facts)
    {
        // its own tasks give its work line; its chip and its mark are the whole team's
        var presence = Overall(HomeWords.Mark(all, stopped), stopped);
        var own = AsturPresences.Of(mine.Select(j => j.State), asked.Count > 0);
        var member = Member(profile, own, mine, asked, facts);
        var work = mine.Any(j => j.State is not (MissionState.Done or MissionState.Stopped))
            ? member.Work
            : HomeWords.Status(all, stopped);
        var worst = usage?.TotalWorstWindowPercent ?? 0;
        return member with
        {
            Presence = presence,
            Work = work,
            Waiting = every.Count,                                  // the one queue is Dextra's: its badge counts every card that waits
            Meter = usage is null ? null : Math.Clamp(worst / 100, 0, 1),
            MeterWords = OrgChartWords.TotalUse(worst),
        };
    }

    private static AsturPresence Overall(ArmillaState mark, bool stopped) => mark switch
    {
        ArmillaState.NeedsYou => AsturPresence.Waiting,
        ArmillaState.Thinking => AsturPresence.Thinking,
        ArmillaState.Working => AsturPresence.Working,
        ArmillaState.Paused => stopped ? AsturPresence.Stopped : AsturPresence.Paused,
        ArmillaState.Offline => AsturPresence.Blocked,
        _ => AsturPresence.Idle,
    };

    // The parts every member shares: the tasks it owns, the line they give, the chips, and what Pause and Resume can reach.
    private static TeamMember Member(AssistantProfile profile, AsturPresence presence, List<MissionJob> mine, List<DecisionItem> asked, Func<string, TeamFacts>? facts)
    {
        var live = mine.Where(j => j.State is not (MissionState.Done or MissionState.Stopped)).OrderByDescending(j => j.Created).ThenBy(j => j.Id).ToList();
        var focus = Focus(presence, mine);
        var texts = focus.Select(j => presence is AsturPresence.Working or AsturPresence.Thinking ? j.Activity ?? j.Title : j.Title).ToList();
        var work = OrgChartWords.WorkLine(presence, texts, asked.FirstOrDefault()?.Title);
        return new TeamMember(profile, presence, work)
        {
            Activity = focus.Count > 0 && presence is AsturPresence.Working or AsturPresence.Thinking ? focus[0].Activity : null,
            Waiting = asked.Count,
            Missions = new ValueList<TeamMission>(live.Select(j => new TeamMission(j.Id, j.Title, j.State, j.Heads.Count > 0, j.LeadThread))),
            CanPause = mine.Any(j => j.Slots is { A: SlotA.Pause, AEnabled: true }),
            CanResume = mine.Any(j => j.Slots is { A: SlotA.Resume, AEnabled: true }),
            Facts = facts?.Invoke(profile.Id) ?? TeamFacts.None,
        };
    }

    // The tasks the work line speaks of: the ones the presence is about, newest first.
    private static List<MissionJob> Focus(AsturPresence presence, List<MissionJob> mine)
    {
        Func<MissionJob, bool> about = presence switch
        {
            AsturPresence.Working => j => j.State is MissionState.Working or MissionState.Pausing,
            AsturPresence.Thinking => j => j.State is MissionState.Planning or MissionState.Reviewing,
            AsturPresence.Waiting => j => j.State == MissionState.NeedsYou,
            AsturPresence.Blocked => j => j.State is MissionState.Blocked or MissionState.Failed,
            AsturPresence.Paused => j => j.State == MissionState.Paused,
            AsturPresence.Stopped => j => j.State is MissionState.Stopping or MissionState.Stopped,
            _ => _ => false,
        };
        return [.. mine.Where(about).OrderByDescending(j => j.Created).ThenBy(j => j.Id)];
    }
}
