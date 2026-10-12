using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Port;
using Marrow.Services.Port.Skills.Generations;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Collection("MCPHub processes")]
[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiKeyPoolTests
{
    [Fact]
    public void A_live_API_launch_uses_its_recorded_key_and_persists_reported_spend_on_that_actual_identity()
    {
        using var shell = new C7OfflineShell();
        using var skills = ProviderSkills.Prepare.Override((_, _, _, _) => Task.FromResult<Generation?>(null));
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid key = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-first";
            var second = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-recorded", second));
            key = second.Id;
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = key });
        });
        var session = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.OnMain(() =>
        {
            var rotation = Assert.IsAssignableFrom<IAsyncApiKeyRotation>(session.KeyRotation);
            Assert.Equal(key.ToString("D"), rotation.Current()!.Id);
            rotation.RecordSpend(0.75);
            Assert.Equal(0.75, LiveApiKeyLaunch.Keys(rig).SpendSince(key.ToString("D"), DateTimeOffset.MinValue));
            Assert.Equal(key, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        });
        Assert.Empty(http.Received);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_live_key_at_its_local_month_cap_cannot_send_or_escape_the_cap_through_a_thread_pin(bool pin)
    {
        using var shell = new C7OfflineShell();
        using var skills = ProviderSkills.Prepare.Override((_, _, _, _) => Task.FromResult<Generation?>(null));
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid key = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            key = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = key, ProviderAccountPinID = pin ? key : null });
            LiveApiKeyLaunch.Keys(rig).SetCap(key, 0);
        });
        var session = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.OnMain(() =>
        {
            Assert.NotNull(session.KeyRotation);
            Assert.Null(session.KeyRotation.Current());
        });
        Assert.Empty(http.Received);
    }

    [Fact]
    public void A_private_handshake_uses_only_its_fixed_destination_key_even_when_live_automatic_key_moves_are_allowed()
    {
        using var shell = new C7OfflineShell();
        using var skills = ProviderSkills.Prepare.Override((_, _, _, _) => Task.FromResult<Generation?>(null));
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        rig.H.Main.Loop.Run(async () =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-priority";
            var destination = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-destination", destination));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries);
            await using var standby = await host.StartStandbyAsync(new(new(destination.Id.ToString("D"), destination.Service.Id, "Destination")), CancellationToken.None);
            // ED-NEW-inst-c7-5: transparent adapter retains fixed-key ownership and zero HTTP.
            var adapter = Assert.IsType<InstallerStandbySession>(standby);
            var actual = Assert.IsType<HandshakeStandbySession>(adapter.Inner);
            var provider = (ProviderSession)typeof(HandshakeStandbySession).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual)!;
            var api = Assert.IsAssignableFrom<OpenAICompatibleSession>(provider);
            Assert.NotNull(api.KeyRotation);
            Assert.Equal(destination.Id.ToString("D"), api.KeyRotation.Current()!.Id);
            Assert.Null(rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        }, AppModelHarness.Bound);
        Assert.Empty(http.Received);
    }
}
