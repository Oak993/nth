using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.App.Runtime;

namespace Marrow.App;

public sealed partial class AppModel
{
    private MultiAccountRouting? accountRouting;
    private AppApiKeyRouting? apiKeys;
    internal AppApiKeyRouting ApiKeys => apiKeys ??= new(this);
    public MultiAccountRouting AccountRouting => accountRouting ??= new(this);
    internal void DetachAccountRouting(Guid thread) => accountRouting?.Detach(thread);
    internal void TickAccountRouting(Guid thread) => accountRouting?.AtSafePoint(thread);
    private long accountReportsRevision;
    public long AccountReportsRevision { get => Read(accountReportsRevision); private set => Set(ref accountReportsRevision, value); }
    internal void NotifyAccountReport() => AccountReportsRevision++;
    internal Marrow.Services.Port.Usage.AccountPlanLimitsSnapshot? AccountSnapshot(Guid id)
    {
        _ = AccountReportsRevision;
        return accountRouting?.Snapshot(id);
    }

    private bool accountReportsReady;
    private void InitializeAccountReports()
    {
        Settings.Accounts.Changed += RefreshAccountReportsIfReady;
        Settings.Pills.Changed += (_, _) => RefreshAccountReportsIfReady();
        Settings.PropertyChanged += (_, value) =>
        {
            if (value.PropertyName is nameof(AppSettings.DeepseekAPIKeyInput) or nameof(AppSettings.MetaAPIKeyInput)
                or nameof(AppSettings.ZaiAPIKeyInput) or nameof(AppSettings.OpenRouterAPIKeyInput)) RefreshAccountReportsIfReady();
        };
        Providers.PropertyChanged += (_, value) =>
        {
            if (value.PropertyName == nameof(ProviderRegistry.Statuses)) RefreshAccountReportsIfReady();
        };
    }
    private void StartAccountReports()
    {
        accountReportsReady = true;
        RefreshAccountReportsIfReady();
    }
    private void RefreshAccountReportsIfReady()
    {
        if (accountReportsReady && !Settings.Accounts.SaveBlocked
            && (Settings.Accounts.Accounts.Count(IsAccountConnected) >= 2 || Settings.Pills.Pills.Count > 0)) AccountRouting.RefreshReports();
    }
    internal bool CanUseAccount(Guid id) => accountRouting?.CanUseAccount(id) != false && ApiKeys.CanUseAccount(id);
    public bool IsAccountConnected(Account account) => account.Service == ProviderKind.Local
        ? Providers.Local.Resolve(account.Defaults.Model) is not null
        : account.Service.IsAPIKeyBased
            ? !string.IsNullOrWhiteSpace(account.UsesLegacyCredentials ? Settings.ApiKey(account.Service) : APIKeychain.Key(account, ""))
            : account.HasSignedIn || account.UsesLegacyCredentials && Providers.Status(account.Service).ProviderStatusAuth is ProviderStatus.Auth.SignedIn;

    private ChatThread ApplyNewThreadPill(ChatThread thread)
    {
        if (Settings.Pills.SaveBlocked) return thread;
        var metadata = new Marrow.Core.Port.MultiAccount.HandshakeAccountStore(Settings.Accounts, IsAccountConnected);
        var relay = Settings.Pills.NewThreadRelay(step => PillStepAccountResolver.Resolve(metadata, step) is { } selected && Guid.TryParse(selected.Id, out var id)
            && Settings.Accounts.Accounts.FirstOrDefault(account => account.Id == id) is { Enabled: true } row && IsAccountConnected(row)
            && CanUseAccount(id)
            && (row.Service != ProviderKind.Local || Providers.Local.Resolve(step.Model ?? row.Defaults.Model) is not null));
        if (relay is null || Settings.Pills.Find(relay.PillId) is not { } pill) return thread;
        var account = Settings.Accounts.Get(Guid.Parse(relay.AccountId));
        var step = pill.Effective(relay.StepIndex);
        return (thread with
        {
            Provider = account.Service, ProviderAccountID = account.Id, ProviderSessionID = null, ProviderResumeAt = null,
            Model = step.Model ?? account.Defaults.Model ?? Providers.DefaultModel(account.Service)?.Id,
            Effort = step.Effort ?? account.Defaults.Effort, FastMode = step.Fast ?? account.Defaults.FastMode,
            HydraPairID = null, HydraEnabled = false,
        }).WithPillRelay(relay);
    }
}
