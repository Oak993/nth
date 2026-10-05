using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

/// <summary>An account store, the decision store and the Models page's model over them, with nothing connected until a test connects it (MA-8 fixtures).</summary>
public sealed class AccountsRig
{
    private readonly HashSet<Guid> connected = [];
    public AccountsRig(bool pills = false)
    {
        Settings = new InMemorySettingsStore();
        Accounts = new AccountStore(Settings);
        Decisions = new AccountDecisionStore(Settings);
        Handshake = new HandshakeSwitch(Settings);
        Actions = new FakeAccountActions();
        Clock = new ManualTimeProvider();
        PillsExist = pills;
        Model = new AccountsSettingsModel(Accounts, Decisions, Handshake, a => connected.Contains(a.Id), () => PillsExist, Actions, id => Usage.GetValueOrDefault(id), Clock, TimeZoneInfo.Utc);
    }

    public InMemorySettingsStore Settings { get; }
    public AccountStore Accounts { get; }
    public AccountDecisionStore Decisions { get; }
    public HandshakeSwitch Handshake { get; }
    public FakeAccountActions Actions { get; }
    public ManualTimeProvider Clock { get; }
    public bool PillsExist { get; set; }
    public Dictionary<Guid, AccountUsageLine> Usage { get; } = [];
    public AccountsSettingsModel Model { get; }

    /// <summary>Connects the service's first account (the migrated one) and returns it.</summary>
    public Account Connect(ProviderKind service, string? plan = null)
    {
        var account = Accounts.ForService(service)[0];
        if (plan is not null) Accounts.SetPlan(account.Id, plan);
        connected.Add(account.Id);
        return Accounts.Get(account.Id);
    }

    /// <summary>Adds and connects another account of the service.</summary>
    public Account ConnectAnother(ProviderKind service, string? plan = null)
    {
        var account = Accounts.Add(service, plan);
        connected.Add(account.Id);
        return account;
    }
}
