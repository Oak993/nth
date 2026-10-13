using System.Reflection;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-13")]
public sealed class DextraTriageEligibilityTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, false)]
    [InlineData(ProviderKind.Commandcode, true)]
    public void Connected_installed_models_are_usable_only_when_Dextra_can_disable_every_tool(ProviderKind provider, bool usable)
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.PrepareBootstrap(model);
        harness.Main.OnMain(() =>
        {
            var account = model.Settings.Accounts.Choose(provider) ?? model.Settings.Accounts.Add(provider);
            model.Settings.Accounts.MarkSignedIn(account.Id);
            typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers,
                model.Providers.Statuses.Mutating(map => map[provider] = new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch") }));
            typeof(ProviderRegistry).GetProperty("Catalogs")!.SetValue(model.Providers,
                model.Providers.Catalogs.Mutating(map => map[provider] = new ValueList<ModelOption>([new ModelOption("offline-model", "Offline")])));
            Assert.True(model.IsAccountConnected(model.Settings.Accounts.Get(account.Id)));
            Assert.True(model.CanUseAccount(account.Id));
            var offered = model.TriageModels().Where(m => m.ProviderId == provider.Id).ToArray();
            Assert.NotEmpty(offered);
            Assert.All(offered, m => Assert.Equal(usable, m.Usable));
            if (!usable) Assert.Null(model.PrepareAssistantText(new DelegateWho(provider, "offline-model"), account.Id));
        });
    }
}
