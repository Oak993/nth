using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Port.MultiAccount;

// Only data and offline ports: no app owner, model, HTTP or CLI is started.
internal sealed class ProductionHeadSupplyRig
{
    internal Guid Thread { get; } = Guid.NewGuid();
    internal Dictionary<ProviderKind, ProductionHeadAccountPort> Raw { get; } = [];
    internal FakeLinkPort Links { get; } = new();
    internal FakeOpinionsPort Opinions { get; } = new();
    internal OtherServiceConsentStore Consent { get; } = new(new AccountsRig().Settings);
    internal ThreadPorts Ports { get; }
    internal IReadOnlyList<ThreadAccount> Rows => Raw.Values.SelectMany(port => port.Accounts).ToArray();
    internal ProductionHeadSupplyRig()
    {
        foreach (var service in Enum.GetValues<ProviderKind>())
            Raw[service] = new(service, Enumerable.Range(1, 2).Select(n => new ThreadAccount(
                Guid.NewGuid(), service, n, "Offline account", false, "O" + n, null, true, null, new())).ToArray());
        Ports = new((_, service) => Raw.GetValueOrDefault(service), null, Links, Opinions);
    }
}

internal sealed class ProductionHeadAccountPort(ProviderKind service, ThreadAccount[] rows) : IThreadAccountPort
{
    public event Action? Changed;
    public ProviderKind Service => service;
    public IReadOnlyList<ThreadAccount> Accounts { get; private set; } = rows;
    public Guid ActiveAccountId { get; } = rows[0].Id;
    public Guid? PinnedAccountId => null;
    public IReadOnlyList<Choice> ThreadChoices => [];
    internal void Replace(ThreadAccount[] next) { Accounts = next; Changed?.Invoke(); }
    public Task<PinResult> PinAsync(Guid id, Bring? bring = null, CancellationToken ct = default) => throw new InvalidOperationException("Head selection cannot pin the primary.");
    public void BackToOrder() => throw new InvalidOperationException();
    public void UseSettings() => throw new InvalidOperationException();
}
