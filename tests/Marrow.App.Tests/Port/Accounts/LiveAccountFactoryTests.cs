using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Testing;
using System.Text;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountFactoryTests
{
    [Fact]
    public void Live_turn_launches_use_the_shared_account_factory_with_the_bound_account_and_current_composer_values()
    {
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Offline answer.\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream") };
        using var transport = URLSession.Transport.Override(http);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configure: thread => thread with { Model = "live-model", Effort = "high", FastMode = true });
        Guid accountId = default;
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            accountId = account.Id;
            Assert.True(APIKeychain.SetKey("offline-account-key", account));
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = account.Id });
        });
        var candidate = new ThreadRuntimeScriptedSession();
        var calls = 0;
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            calls++;
            Assert.Equal(accountId, id);
            Assert.False(fresh);
            Assert.Equal("live-model", configuration.Model);
            Assert.Equal("high", configuration.Effort);
            Assert.True(configuration.FastMode);
            var prepared = registry.PrepareSession(configuration, id, useAccountDefaults: false);
            Assert.Equal("offline-account-key", prepared.ApiKey);
            return new(prepared, candidate);
        });
        rig.Send("Start this actual runtime turn.");
        Assert.Equal(1, calls);
        Assert.Single(candidate.Inputs);
        Assert.Equal(1, candidate.StartCalls);
        Assert.Empty(http.Received);
        Assert.Equal(accountId, rig.Thread.ProviderAccountID);
    }

    [Fact]
    public void An_unreadable_account_document_keeps_legacy_live_turns_available_without_replacing_the_saved_document()
    {
        var original = Encoding.UTF8.GetBytes("{broken");
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configureWithHarness: (h, thread) => { h.Defaults.Set(AccountStore.SettingsKey, original); return thread; },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-legacy-key");
        var candidate = new ThreadRuntimeScriptedSession();
        var calls = 0;
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            calls++;
            var prepared = registry.PrepareSession(configuration, id, useAccountDefaults: false);
            Assert.Equal("offline-legacy-key", prepared.ApiKey);
            return new(prepared, candidate);
        });
        rig.Send("Continue in the original legacy account.");
        Assert.Equal(1, calls);
        Assert.Single(candidate.Inputs);
        Assert.True(rig.App.Settings.Accounts.SaveBlocked);
        Assert.Null(rig.Thread.ProviderAccountID);
        Assert.Equal(original, Assert.IsType<byte[]>(rig.H.Defaults.Object(AccountStore.SettingsKey)));
        Assert.Empty(http.Received);
    }
}
