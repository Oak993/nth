using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>Accounts and pills over in-memory stores: Claude Max twice, Codex once, Local once, all connected (MA-8 fixtures, shared by the Core and App tests).</summary>
public sealed class PillsRig
{
    private readonly HashSet<Guid> connected = [];

    public PillsRig(bool connectAll = true)
    {
        Accounts = new AccountStore(new InMemorySettingsStore());
        Pills = new PillStore();
        if (connectAll)
        {
            Claude1 = Connect(Accounts.ForService(ProviderKind.Claude)[0], "Max");
            Claude2 = Connect(Accounts.Add(ProviderKind.Claude, "Max"), "Max");
            Codex1 = Connect(Accounts.ForService(ProviderKind.Codex)[0], "Plus");
        }
        else
        {
            Claude1 = Accounts.ForService(ProviderKind.Claude)[0];
            Claude2 = Claude1;
            Codex1 = Accounts.ForService(ProviderKind.Codex)[0];
        }
        Model = new PillsSettingsModel(Pills, Accounts, a => connected.Contains(a.Id));
    }

    public AccountStore Accounts { get; }
    public PillStore Pills { get; }
    public Account Claude1 { get; }
    public Account Claude2 { get; }
    public Account Codex1 { get; }
    public PillsSettingsModel Model { get; }

    public Account Connect(Account account, string? plan = null)
    {
        if (plan is not null) Accounts.SetPlan(account.Id, plan);
        connected.Add(account.Id);
        return Accounts.Get(account.Id);
    }

    public PillEditorModel Editor(Guid pill) => new(Pills, Accounts, a => connected.Contains(a.Id), (service, model) => model is null ? "" : service.Id + ":" + model, pill);

    public static string Id(Account account) => account.Id.ToString("D");

    public Pill Make(string name, params Account[] steps) => Pills.Create(name, steps.Select(a => new PillStep(Id(a))));
}
