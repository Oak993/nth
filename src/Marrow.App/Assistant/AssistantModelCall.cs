// PORT-ONLY: DESIGN.md 11.5, 13.2; ED-NEW-dextra-open-2. One receipt per admitted side call.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class AssistantModelCall : IDisposable
{
    private readonly ReserveGuard guard;
    private readonly ReserveAccountRef account;
    private readonly string assistant;
    private readonly string? model;
    private readonly MissionRecord? mission;
    private readonly Guid flight;
    private long charged;
    private TokenSpend? actual;
    private decimal chargedDollars;
    private bool disposed;
    private string readingStamp;

    internal AssistantModelCall(ReserveGuard guard, ReserveAccountRef account, string assistant, string? model, MissionRecord? mission, Guid flight, long booked, decimal dollars, string readingStamp)
    {
        this.guard = guard; this.account = account; this.assistant = assistant; this.model = model; this.mission = mission;
        this.flight = flight; charged = booked; chargedDollars = dollars;
        this.readingStamp = readingStamp;
    }

    public void Observe(TokenSpend spend)
    {
        if (disposed || spend.AgentID is not null || spend.TotalTokens < 0) return;
        readingStamp = guard.RetainModelCallBooking(account, model, charged, readingStamp);
        actual = actual is null ? spend : actual.Merge(spend);
        var total = Math.Max(charged, actual.TotalTokens);
        var money = Math.Max(chargedDollars, guard.Costs?.ModelCallDollars(account, model, actual) ?? 0m);
        guard.AddModelCallSpend(account, assistant, model, mission, total - charged, money - chargedDollars);
        charged = total; chargedDollars = money;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        guard.RetainModelCallBooking(account, model, charged, readingStamp);
        guard.EndModelCall(account, flight);
    }
}
