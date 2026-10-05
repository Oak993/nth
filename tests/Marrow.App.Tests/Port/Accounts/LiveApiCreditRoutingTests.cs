using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiCreditRoutingTests
{
    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.OpenRouter)]
    public void A_connected_settings_account_has_one_shared_snapshot_before_any_runtime_attaches(ProviderKind service)
    {
        using var http = new InProcessHttpHandler { Answer = request => InProcessHttpHandler.Reply(200,
            request.Url == OpenRouterAPI.KeyURL ? "{\"data\":{\"limit_remaining\":6}}" : "{\"is_available\":true,\"balance_infos\":[{\"total_balance\":\"6\",\"currency\":\"USD\"}]}") };
        using var transport = URLSession.Transport.Override(http);
        using var h = new AppModelHarness();
        var app = h.MakeModel();
        h.PrepareBootstrap(app);
        Guid id = default;
        h.Main.OnMain(() =>
        {
            var routing = app.AccountRouting;
            app.Settings.SetAPIKeyInput("offline-selected", service);
            id = app.Settings.Accounts.ForService(service)[0].Id;
            app.Settings.Accounts.SetEnabled(id, false);
            app.Settings.Accounts.SetEnabled(id, true);
        });
        h.Main.Loop.Run(() => app.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var snapshot = app.AccountRouting.Snapshot(id);
            Assert.NotNull(snapshot);
            Assert.NotNull(app.AutoContinue.UsageLimits);
            Assert.Same(snapshot, app.AutoContinue.UsageLimits.Snapshot(UsageAccountKey.Default(service)));
            Assert.Equal(6, snapshot.Limits!.AccountCredits!.Total);
        });
        Assert.Equal("Bearer offline-selected", Assert.Single(http.Received).Header("Authorization"));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("CNY")]
    public void Live_most_credits_order_uses_actual_USD_reports_and_never_treats_other_currency_as_USD(string sourceCurrency)
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = ValueList<PlanLimits.Window>.Empty,
            AccountCredits = new() { Total = key.IsDefault ? sourceCurrency == "USD" ? 1 : 1000 : 9, Currency = key.IsDefault ? sourceCurrency : "USD", IsUsable = true },
        }));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-target", next));
            LiveApiKeyLaunch.Keys(rig).SetOrder(ProviderKind.Deepseek.Id, KeyOrderMode.MostCreditsFirst);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(target.ToString("D"), api.KeyRotation!.Current()!.Id);
            Assert.Equal(target, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        });
        Assert.Empty(http.Received);
    }

    [Theory]
    [InlineData(0, "USD", true)]
    [InlineData(1000, "CNY", false)]
    public void A_reported_empty_or_unusable_live_key_cannot_send_even_when_pinned(double total, string currency, bool usable)
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = ValueList<PlanLimits.Window>.Empty,
            AccountCredits = new() { Total = total, Currency = currency, IsUsable = usable },
        }));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = source, ProviderAccountPinID = source });
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => Assert.Null(api.KeyRotation!.Current()));
        Assert.Empty(http.Received);
    }

    [Fact]
    public void A_new_thread_skips_a_pill_key_at_its_local_month_cap_without_an_existing_account_router()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var next = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            rig.App.Settings.Accounts.MarkSignedIn(next.Id);
            LiveApiKeyLaunch.Keys(rig).SetCap(source.Id, 0);
            var pill = rig.App.Settings.Pills.Create("Available", [new(source.Id.ToString("D")), new(next.Id.ToString("D"))]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
            var created = rig.App.NewThread(rig.Project)!;
            Assert.Equal(next.Id, created.ProviderAccountID);
            Assert.Equal(1, created.PillStepIndex);
        });
    }
}
