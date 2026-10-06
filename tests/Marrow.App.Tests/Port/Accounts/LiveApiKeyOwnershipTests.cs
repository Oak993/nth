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
public sealed class LiveApiKeyOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_already_canceled_API_selection_cannot_persist_a_new_key_or_spend_on_it(bool failure)
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.OnMain(() => Assert.Equal(source.ToString("D"), api.KeyRotation!.Current()!.Id));
        if (!failure) rig.H.Main.OnMain(() => LiveApiKeyLaunch.Keys(rig).SetCap(source, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var selected = rig.H.Main.OnMain(() => failure
            ? ((IAsyncApiKeyRotation)api.KeyRotation!).OnFailureAsync(429, TimeSpan.FromHours(1), cancellation.Token)
            : ((IAsyncApiKeyRotation)api.KeyRotation!).CurrentAsync(cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => rig.H.Main.Loop.Run(() => selected, AppModelHarness.Bound));
        Assert.Equal(source, rig.Thread.ProviderAccountID);
        Assert.Equal(source, api.Configuration.AccountId);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Fact]
    public async Task Replacing_the_session_from_a_thread_notification_cannot_be_overwritten_by_the_retired_keys_attribution()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid b = default, c = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-a";
            var second = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            var third = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            b = second.Id; c = third.Id;
            Assert.True(APIKeychain.SetKey("offline-b", second));
            Assert.True(APIKeychain.SetKey("offline-c", third));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        var old = LiveApiKeyLaunch.Ensure(rig);
        Task<ProviderSession>? replacement = null;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, value) =>
        {
            if (value.PropertyName != nameof(AppModel.Threads) || rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID != b) return;
            rig.Runtime.StopSession();
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = c });
            replacement = ThreadRuntimeReflection.Call<Task<ProviderSession>>(rig.Runtime, "EnsureSession", rig.Directory, true)!;
            Assert.True(replacement.IsCompletedSuccessfully);
        };
        rig.H.Main.OnMain(() =>
        {
            Assert.NotNull(old.KeyRotation!.Current());
            rig.App.PropertyChanged += changed;
            try { old.KeyRotation.OnFailure(429, TimeSpan.FromHours(1)); }
            catch (OperationCanceledException) { }
            finally { rig.App.PropertyChanged -= changed; }
        });
        Assert.NotNull(replacement);
        var current = Assert.IsAssignableFrom<OpenAICompatibleSession>(await replacement);
        Assert.NotSame(old, current);
        Assert.Equal(c, rig.Thread.ProviderAccountID);
        Assert.Equal(new UsageAccountKey(ProviderKind.Deepseek, c.ToString("D")), rig.Runtime.UsageAccount);
        var signature = ThreadRuntimeReflection.Get<object>(rig.Runtime, "sessionSignature")!;
        Assert.Equal(c, signature.GetType().GetProperty("AccountId")!.GetValue(signature));
        Assert.Equal(c, current.Configuration.AccountId);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
