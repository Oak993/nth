// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4, 16.2 "AS-17: no external dependency"; ED-NEW-dextra-as17-6). No Swift twin.
// Where the org chart and the sidebar's Astur rows meet the running app: the team source over the home's own sources (its tasks, its queue, its controls), the controller's task owners, today's use
// against each slice, and the facts the detail counts (duties, notes, grants, the owner's standing reserve rules). One place, so the home and the sidebar read the same team and a test can say what
// each figure is read from.
using Marrow.App.Assistant.Views;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

internal static class TeamBinding
{
    /// <summary>The team over a running controller: the profiles the host holds, the home's tasks, cards and controls, and the controller's figures.</summary>
    /// <param name="dutyCount">How many standing duties an assistant has (the duty host's scheduler; 0 while the duties are not running).</param>
    public static TeamSource For(Func<IReadOnlyList<AssistantProfile>> profiles, DextraLive live, AssistantController controller, Func<string, int>? dutyCount = null) => new(
        profiles,
        live.Jobs,
        id => controller.Runner.Find(id)?.Mission.AssistantId,
        live.Decisions,
        live.Controls,
        usage: () => controller.Costs is null ? null : controller.UsageToday(),
        facts: id => Facts(profiles, controller, id, dutyCount),
        watchTeam: handler => Watch(controller, handler));

    /// <summary>What the detail counts for one assistant. Dextra reads every note; an Astur its own.</summary>
    public static TeamFacts Facts(Func<IReadOnlyList<AssistantProfile>> profiles, AssistantController controller, string id, Func<string, int>? dutyCount = null)
    {
        var root = profiles().FirstOrDefault(p => p.IsRoot)?.Id;
        var notes = controller.Notes?.Store.Notes(id).Count(n => id == root || n.Scope == id) ?? 0;
        var grants = controller.Grants.Count(g => g.AssistantId == id);
        return new TeamFacts(dutyCount?.Invoke(id) ?? 0, notes, grants, StandingRules(controller, id));
    }

    /// <summary>The owner's standing reserve rules that reach this assistant, in words ("Your standing rule: Always keep 30% on Claude 20x."), or empty.</summary>
    public static string StandingRules(AssistantController controller, string id)
    {
        var titles = controller.Reserve?.Settings.Rules
            .Where(r => r.Scope.Kind != ReserveScopeKind.Assistant || r.Scope.AssistantId == id)
            .Select(ReserveWords.Title).ToList() ?? [];
        if (titles.Count == 0) return "";
        return (titles.Count == 1 ? "Your standing rule: " : "Your standing rules: ") + string.Join("; ", titles) + ".";
    }

    /// <summary>The sentences for profile files nth could not use (the Team tab shows them under the chart).</summary>
    public static IReadOnlyList<string> Problems(AssistantController controller) => controller.Team?.Problems ?? [];

    // the team's own change event, as a subscription the source can end
    private static IDisposable Watch(AssistantController controller, Action handler)
    {
        if (controller.Team is not { } team) return Quiet.Instance;
        team.Changed += handler;
        return new Unwatch(() => team.Changed -= handler);
    }

    private sealed class Unwatch(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    private sealed class Quiet : IDisposable
    {
        public static readonly Quiet Instance = new();

        public void Dispose()
        {
        }
    }
}
