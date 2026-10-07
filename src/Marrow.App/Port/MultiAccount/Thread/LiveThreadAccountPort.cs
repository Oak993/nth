using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed class LiveThreadAccountPort(LiveThreadPorts owner, AppModel app, Guid thread, ProviderKind service) : IThreadAccountPort
{
    private Action? changed;
    private IDisposable? reports;
    public event Action? Changed
    {
        add { if (changed is null) reports = app.AccountRouting.ObserveReports(); changed += value; }
        remove { changed -= value; if (changed is null) { reports?.Dispose(); reports = null; } }
    }
    internal void Refresh() => changed?.Invoke();
    internal void Dispose() { changed = null; reports?.Dispose(); reports = null; }
    public ProviderKind Service => service;
    public IReadOnlyList<ThreadAccount> Accounts => owner.Rows(service);
    public Guid ActiveAccountId => owner.Active(thread);
    public Guid? PinnedAccountId => app.Thread(thread)?.ProviderAccountPinID;
    public IReadOnlyList<Choice> ThreadChoices => Enum.GetValues<DecisionKind>()
        .Select(kind => (kind, value: app.Settings.AccountDecisions.GetThread(thread, kind)))
        .Where(pair => pair.value is not null).Select(pair => new Choice(pair.kind, pair.value!)).ToArray();
    public async Task<PinResult> PinAsync(Guid accountId, Bring? bring = null, CancellationToken ct = default)
    {
        MainActor.AssumeIsolated(); ct.ThrowIfCancellationRequested();
        var row = Accounts.FirstOrDefault(account => account.Id == accountId);
        if (row is null || app.Thread(thread)?.Provider != service) return PinResult.UnknownAccount;
        if (!row.Enabled) return PinResult.Disabled;
        if (row.IsOut || !app.CanUseAccount(accountId)) return PinResult.OutOfService;
        var policy = new DecisionPolicy(app.Settings.AccountDecisions).Resolve(thread, DecisionKind.Bring).Value;
        var selected = bring ?? DecisionValues.ToBring(policy ?? DecisionValues.MarrowSummary);
        return await app.TryPinThreadAccountAsync(thread, accountId, selected, ct) ? PinResult.Pinned : PinResult.OutOfService;
    }
    public void BackToOrder() { MainActor.AssumeIsolated(); app.TryPinThreadAccount(thread, null); }
    public void UseSettings() { MainActor.AssumeIsolated(); app.Settings.AccountDecisions.ClearThread(thread); }
}
