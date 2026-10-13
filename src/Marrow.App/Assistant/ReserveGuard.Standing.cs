// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.6; unit AS-14; ED-NEW-dextra-as14-5). No Swift twin.
// Where an account stands for a turn that has not been sent: the router's question. It is the projection that gates a turn (the
// estimate between polls, this turn's p90, the margin, the quantum) laid against the floor that would bind the mission, for every
// window of the account, so the router can rank accounts by the room they leave and never choose one the gate would then refuse.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    /// <summary>
    /// The account as the router sees it (11.6) for one turn of <paramref name="p90Tokens"/> (0: the model's own typical turn) on
    /// <paramref name="model"/>: each window's estimate, the floor that would bind (100 where no rule applies) and the projection.
    /// An existing <paramref name="mission"/> is held to its own floors, baselines and allowance; for one not started yet the floors
    /// are those of the standing rules and <paramref name="taskRules"/>, measured from the windows as they are now. Null when
    /// nothing has read the account yet. Nothing is registered.
    /// </summary>
    public ReserveStanding? Standing(ReserveAccountRef account, string assistantId, MissionRecord? mission, IReadOnlyList<ReserveRule>? taskRules, string? model, double p90Tokens)
    {
        ArgumentNullException.ThrowIfNull(account);
        var tokens = p90Tokens > 0 ? p90Tokens : o.Turns.P90(model);
        var now = clock.GetUtcNow();
        lock (gate)
        {
            if (disposed || !accounts.TryGetValue(account.Key, out var state) || state.Windows.Count == 0) return null;
            var m = mission is not null && missions.TryGetValue(mission.Id, out var live) ? live : Scratch(mission, assistantId, taskRules, now);
            var windows = new List<RouteWindow>();
            RouteWindow? tightest = null;
            foreach (var projection in state.Projections(model, tokens, now))
            {
                var floor = FloorFor(m, account, state, projection.Window, now)?.Percent ?? 100;
                var window = new RouteWindow(projection.Window.Id, projection.Window.Kind, projection.Estimate, floor, projection.Projected, projection.Window.ResetsAt);
                windows.Add(window);
                if (tightest is null || window.Room < tightest.Room) tightest = window;
            }
            // The rate that matters is the one of the window with the least room, as the plan marks it (11.4).
            var rough = !o.Calibrator.RateFor(account.Key, account.ProviderId, model, tightest!.Kind).Calibrated;
            return new ReserveStanding(windows, rough);
        }
    }

    // A task the guard has not met: the rules that would bind it, on a copy nothing else sees.
    private MissionReserve Scratch(MissionRecord? mission, string assistantId, IReadOnlyList<ReserveRule>? taskRules, DateTimeOffset now)
    {
        var scratch = new MissionReserve(mission?.Id ?? Guid.Empty, mission?.Mission.Title ?? "", mission?.Mission.AssistantId ?? assistantId, now);
        if (taskRules is not null) scratch.TaskRules.AddRange(taskRules.Where(r => r.Lifetime == ReserveLifetime.Task).Select(r => r.Normalized()));
        return scratch;
    }
}
