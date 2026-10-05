using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private void BindLiveApiKeys(ProviderSession candidate, Guid accountId)
    {
        if (candidate is not OpenAICompatibleSession api || !api.Configuration.Provider.IsAPIKeyBased) return;
        var app = App!;
        var epoch = sessionEpoch;
        bool Owns() => epoch == sessionEpoch && ReferenceEquals(session, candidate);
        app.ApiKeys.BindLive(api, this, accountId, choice =>
        {
            if (!Owns()) throw new CancellationError();
            var id = Guid.Parse(choice.Id);
            var account = app.Settings.Accounts.Get(id);
            if (account.Service != api.Configuration.Provider || !account.Enabled || !app.IsAccountConnected(account))
                throw new InvalidOperationException("The selected API account is unavailable.");
            var switched = api.Configuration.AccountId != id;
            var source = Thread ?? throw new CancellationError();
            var destination = source with
            {
                ProviderAccountID = id,
                Model = switched ? account.Defaults.Model ?? app.Providers.DefaultModel(account.Service)?.Id : source.Model,
                Effort = switched ? account.Defaults.Effort : source.Effort,
                FastMode = switched ? account.Defaults.FastMode : source.FastMode,
            };
            var hydra = switched ? app.HydraLaunch(destination) : api.Configuration.Hydra;
            api.RebindApiAccount(id, app.Providers.Environment(id), destination.Model, destination.Effort, destination.FastMode, hydra);
            UsageAccount = account.UsesLegacyCredentials ? UsageAccountKey.Default(account.Service) : new(account.Service, id.ToString("D"));
            if (sessionSignature is { } signature) sessionSignature = signature with { AccountId = id, Hydra = switched ? hydra : signature.Hydra };
            app.UpdateThread(ThreadID, thread => thread with
            {
                ProviderAccountID = id, Model = destination.Model, Effort = destination.Effort, FastMode = destination.FastMode,
            });
            if (!Owns()) throw new CancellationError();
        }, Owns);
    }
}
