using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>One thread's accounts over the rig's real account store, usage and decision store: honours pin, back to order, use Settings and the store's order; the app's wiring is later (MA-10 fixtures, shared by the Core and App tests).</summary>
public sealed class FakeThreadAccountPort(AccountsRig rig, ProviderKind service, Guid? thread = null) : IThreadAccountPort
{
    private IReadOnlyList<ThreadAccount> rows = Build(rig, service);

    public event Action? Changed;

    public Guid ThreadId { get; } = thread ?? Guid.NewGuid();
    public ProviderKind Service => service;
    public IReadOnlyList<ThreadAccount> Accounts => rows;
    public Guid? PinnedAccountId { get; private set; }

    /// <summary>Every pin that was accepted, with the Bring it asked for.</summary>
    public List<(Guid Account, Bring? Bring)> Pins { get; } = [];

    public Guid ActiveAccountId =>
        PinnedAccountId ?? (rows.FirstOrDefault(a => a.Enabled && !a.IsOut) ?? rows.FirstOrDefault(a => a.Enabled) ?? rows.FirstOrDefault())?.Id ?? Guid.Empty;

    public IReadOnlyList<Choice> ThreadChoices => [.. Enum.GetValues<DecisionKind>().Select(kind => (kind, value: rig.Decisions.GetThread(ThreadId, kind))).Where(x => x.value is not null).Select(x => new Choice(x.kind, x.value!))];

    /// <summary>Reads the stores again (a test changed the order, the usage or what the thread remembers) and says so.</summary>
    public void Refresh()
    {
        rows = Build(rig, service);
        Changed?.Invoke();
    }

    public Task<PinResult> PinAsync(Guid accountId, Bring? bring = null, CancellationToken ct = default)
    {
        rows = Build(rig, service);
        var row = rows.FirstOrDefault(a => a.Id == accountId);
        if (row is null) return Task.FromResult(PinResult.UnknownAccount);
        if (!row.Enabled) return Task.FromResult(PinResult.Disabled);
        if (row.IsOut) return Task.FromResult(PinResult.OutOfService);
        PinnedAccountId = accountId;
        Pins.Add((accountId, bring));
        Changed?.Invoke();
        return Task.FromResult(PinResult.Pinned);
    }

    public void BackToOrder()
    {
        if (PinnedAccountId is null) return;
        PinnedAccountId = null;
        Changed?.Invoke();
    }

    public void UseSettings()
    {
        if (ThreadChoices.Count == 0) return;
        rig.Decisions.ClearThread(ThreadId);
        Changed?.Invoke();
    }

    private static IReadOnlyList<ThreadAccount> Build(AccountsRig rig, ProviderKind service) =>
        ThreadAccountRows.Build(rig.Accounts, service, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id));
}
