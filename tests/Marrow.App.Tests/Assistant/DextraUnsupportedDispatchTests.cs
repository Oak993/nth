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

[Trait("Decision", "ED-NEW-dextra-open-14")]
public sealed class DextraUnsupportedDispatchTests
{
    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Devin)]
    public void Every_unsupported_provider_is_refused_by_the_production_dispatch_factory(ProviderKind provider)
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
            Assert.True(model.Providers.CountsAsInstalled(provider));
            Assert.Null(model.PrepareAssistantText(new DelegateWho(provider, "offline-model"), account.Id));
        });
    }
}
