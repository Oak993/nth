using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>An account store, the decision store and the Models page's model over them, with nothing connected until a test connects it (MA-8 fixtures, shared by the Core and App tests).</summary>
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
        Pills = new PillStore();
        Model = new AccountsSettingsModel(Accounts, Decisions, Handshake, a => connected.Contains(a.Id), () => PillsExist || Pills.Pills.Count > 0, Actions, id => Usage.GetValueOrDefault(id), Clock, TimeZoneInfo.Utc, id => Credits.TryGetValue(id, out var left) ? left : null);
        PillsModel = new PillsSettingsModel(Pills, Accounts, a => connected.Contains(a.Id));
    }

    public InMemorySettingsStore Settings { get; }
    public AccountStore Accounts { get; }
    public AccountDecisionStore Decisions { get; }
    public HandshakeSwitch Handshake { get; }
    public FakeAccountActions Actions { get; }
    public ManualTimeProvider Clock { get; }
    public bool PillsExist { get; set; }
    public Dictionary<Guid, AccountUsageLine> Usage { get; } = [];
    public Dictionary<Guid, double> Credits { get; } = [];
    public AccountsSettingsModel Model { get; }
    public PillStore Pills { get; }
    public PillsSettingsModel PillsModel { get; }

    public bool IsConnected(Account account) => connected.Contains(account.Id);

    public PillEditorModel Editor(Guid pill) => new(Pills, Accounts, a => connected.Contains(a.Id), (service, model) => model is null ? "" : service.Id + ":" + model, pill);

    public Pill MakePill(string name, params Account[] steps) => Pills.Create(name, steps.Select(a => new PillStep(a.Id.ToString("D"))));

    /// <summary>Connects the service's first account (the migrated one) and returns it.</summary>
    public Account Connect(ProviderKind service, string? plan = null)
    {
        var account = Accounts.ForService(service)[0];
        if (plan is not null) Accounts.SetPlan(account.Id, plan);
        connected.Add(account.Id);
        return Accounts.Get(account.Id);
    }

    /// <summary>Connects an account that is already in the store.</summary>
    public Account Connect(Account account, string? plan = null)
    {
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
