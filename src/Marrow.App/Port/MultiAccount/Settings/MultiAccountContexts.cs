// PORT-ONLY: T11 step 2. The default context: the models over the running AppModel's stores. The only things done here that no foundation does are the sign-in and the sign-out of an account and the keeping of an API key (IAccountActions), the usage lines
// (read from the per-account limits scheduler when the app has one) and the credits a key has left (the API key routing, OD-0149).
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.App.Views.Settings;
using Marrow.Services.Port.Accounts;

namespace Marrow.App.Port.MultiAccount.Settings;

internal static class MultiAccountContexts
{
    internal static MultiAccountContext For(AppModel model)
    {
        var settings = model.Settings;
        var actions = new AppAccountActions(model);
        var accounts = new AccountsSettingsModel(settings.Accounts, settings.AccountDecisions, new HandshakeSwitch(UserDefaults.Standard.Current), model.IsAccountConnected,
            () => settings.Pills.Pills.Count > 0, actions, id => actions.Usage(id), credits: id => model.ApiKeys.CreditsLeft(id.ToString("D")));
        var pills = new PillsSettingsModel(settings.Pills, settings.Accounts, model.IsAccountConnected);
        string Name(ProviderKind service, string? id) => id is null ? "" : model.Providers.Model(id, service)?.ChipName ?? id;
        return new MultiAccountContext(accounts, pills,
            id => new PillEditorModel(settings.Pills, settings.Accounts, model.IsAccountConnected, Name, id),
            service => model.Providers.Models(service),
            () => SettingsNavigation.Shared.RequestedPage = SettingsPage.Providers);
    }
}
