// PORT-ONLY: DESIGN.md 4.3, 13.2; ED-NEW-dextra-open-2.
using Marrow.Core.Port.Assistant;
using Marrow.Core.Models;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class CostGuard
{
    internal decimal ModelCallDollars(ReserveAccountRef account, string? model, long tokens) =>
        IsApiKey(account) ? tokens * PerMillion(account, model) / 1_000_000m : 0m;

    internal decimal ModelCallDollars(ReserveAccountRef account, string? model, TokenSpend spend) => !IsApiKey(account) ? 0m
        : o.Prices?.Invoke(account, model) is { } prices
            ? CostPricing.Dollars(prices, spend.TotalTokens, spend.InputTokens, spend.OutputTokens, spend.CachedInputTokens)
            : ModelCallDollars(account, model, spend.TotalTokens);

    internal void AddModelCallSpend(ReserveAccountRef account, string assistant, string? model, MissionRecord? mission, long tokens, decimal dollars)
    {
        var api = IsApiKey(account);
        var points = !api ? Kinds(account).ToDictionary(k => k, k => o.Calibrator.RateFor(account.Key, account.ProviderId, model, k).Percent(tokens)) : [];
        lock (gate)
        {
            if (mission is not null)
            {
                var m = MissionOf(mission);
                foreach (var (kind, amount) in points) m.Use.Add(account.Key, kind, amount, account.Name);
                m.Use.AddDollars(dollars);
            }
        }
        foreach (var (kind, amount) in points) o.Ledger.AddPoints(assistant, account.Key, account.Name, api, kind, amount);
        if (dollars > 0) o.Ledger.AddDollars(assistant, account.Key, account.Name, dollars);
        Reevaluate();
        Changed?.Invoke();
    }

    public bool TryBookModelCall(ReserveAccountRef account, string assistantId, string? model, MissionRecord? mission, out long tokens)
    {
        tokens = 0;
        MissionCost m;
        CapTurn turn;
        lock (gate)
        {
            if (disposed) return false;
            m = mission is null ? new MissionCost(Guid.Empty, "", assistantId) : MissionOf(mission);
            if (m.Hold is not null || Evaluate(m, account, model, withTurn: true) is not null) return false;
            tokens = Math.Max(1, (long)Math.Ceiling(o.Turns.P90(model)));
            turn = TurnOf(account, model, IsApiKey(account));
            foreach (var (kind, points) in turn.Points) m.Use.Add(account.Key, kind, points, account.Name);
            m.Use.AddDollars(turn.Dollars);
        }
        // App callers serialize admission on MainActor; ledger notifications run outside the guard lock.
        foreach (var (kind, points) in turn.Points) o.Ledger.AddPoints(m.AssistantId, account.Key, account.Name, false, kind, points);
        if (turn.Dollars > 0) o.Ledger.AddDollars(m.AssistantId, account.Key, account.Name, turn.Dollars);
        Changed?.Invoke();
        return true;
    }
}
