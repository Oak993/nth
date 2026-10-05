using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;

namespace Marrow.App;

public sealed partial class AppModel
{
    public bool IsAccountConnected(Account account) => account.Service == ProviderKind.Local
        ? Providers.Local.Resolve(account.Defaults.Model) is not null
        : account.Service.IsAPIKeyBased
            ? !string.IsNullOrWhiteSpace(account.UsesLegacyCredentials ? Settings.ApiKey(account.Service) : APIKeychain.Key(account, ""))
            : account.HasSignedIn || account.UsesLegacyCredentials && Providers.Status(account.Service).ProviderStatusAuth is ProviderStatus.Auth.SignedIn;

    private ChatThread ApplyNewThreadPill(ChatThread thread)
    {
        if (Settings.Pills.SaveBlocked) return thread;
        var relay = Settings.Pills.NewThreadRelay(step => Guid.TryParse(step.AccountId, out var id)
            && Settings.Accounts.Accounts.FirstOrDefault(account => account.Id == id) is { Enabled: true } row && IsAccountConnected(row)
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
