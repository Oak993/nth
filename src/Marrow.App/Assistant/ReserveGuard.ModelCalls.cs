// PORT-ONLY: DESIGN.md 4.3, 11.5, 13.2; ED-NEW-dextra-open-2.
// Side calls have no chat turn. Book their p90 on the answering account before sending.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    public AssistantModelCall? BeginModelCall(ReserveAccountRef account, string assistantId, string? model, MissionRecord? mission = null)
    {
        if (!TryBookModelCall(account, assistantId, model, mission, out var booked)) return null;
        var flight = Guid.NewGuid();
        string stamp;
        lock (gate) { var state = StateOf(account); state.TurnStarted(flight, model, booked); stamp = ReadingStamp(state); }
        return new AssistantModelCall(this, account, assistantId, model, mission, flight, booked, Costs?.ModelCallDollars(account, model, booked) ?? 0m, stamp);
    }

    // A quota poll can precede the provider's usage report. Keep the admitted amount after that poll too.
    internal string RetainModelCallBooking(ReserveAccountRef account, string? model, long tokens, string since)
    {
        lock (gate)
        {
            var state = StateOf(account);
            var stamp = ReadingStamp(state);
            if (stamp != since) state.Spend(clock.GetUtcNow(), tokens, model, afterReading: true);
            return stamp;
        }
    }

    private static string ReadingStamp(ReserveAccount state) => string.Join("|", state.Windows.OrderBy(w => w.Id, StringComparer.Ordinal).Select(w => w.Id + ":" + w.ObservedAt.UtcTicks));

    internal void EndModelCall(ReserveAccountRef account, Guid flight)
    {
        lock (gate) StateOf(account).TurnEnded(flight);
        Changed?.Invoke();
    }

    internal void AddModelCallSpend(ReserveAccountRef account, string assistant, string? model, MissionRecord? mission, long tokens, decimal dollars)
    {
        string? replan = null;
        lock (gate)
        {
            var state = StateOf(account);
            state.Spend(clock.GetUtcNow(), tokens, model, afterReading: true);
            o.Calibrator.Spend(account.Key, model, tokens);
            if (mission is not null && missions.TryGetValue(mission.Id, out var m))
            { Attribute(m, account, state, model, tokens); replan = CheckOverEstimate(m, account, state, clock.GetUtcNow()); }
        }
        if (replan is not null && mission is not null) Replanned?.Invoke(mission.Id, replan);
        Costs?.AddModelCallSpend(account, assistant, model, mission, tokens, dollars);
        Reevaluate(account.Key);
        Changed?.Invoke();
    }

    public bool TryBookModelCall(ReserveAccountRef account, string assistantId, string? model, MissionRecord? mission, out long tokens)
    {
        tokens = 0;
        lock (gate)
        {
            if (disposed) return false;
            var standing = Standing(account, assistantId, mission, null, model, 0);
            // A configured floor with no reading cannot authorize an unmeasured call.
            if (standing is null && o.Settings.Rules.Any(r => r.Scope.Covers(new ReserveSubject(account.Key, account.Name, account.ProviderId, assistantId)))) return false;
            if (standing?.Windows.Any(w => w.Projected >= w.Floor) == true) return false;
            if (Costs is { } costs && !costs.TryBookModelCall(account, assistantId, model, mission, out _)) return false;
            tokens = Math.Max(1, (long)Math.Ceiling(o.Turns.P90(model)));
            var state = StateOf(account);
            state.Spend(clock.GetUtcNow(), tokens, model, afterReading: true);
            o.Calibrator.Spend(account.Key, model, tokens);
            if (mission is not null && missions.TryGetValue(mission.Id, out var m)) Attribute(m, account, state, model, tokens);
        }
        Changed?.Invoke();
        return true;
    }
}
