using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Accounts.Pills;
using Marrow.App.Runtime;
using Marrow.Services.Providers;
using System.Text.Json;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class NewThreadPillKeyRoutingTests
{
    [Fact]
    public void A_new_thread_skips_its_pill_keys_known_exhaustion_and_starts_on_the_next_available_step()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        Guid next = default;
        rig.H.Main.OnMain(() =>
        {
            var anchor = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var outKey = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            var ready = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-out", outKey));
            Assert.True(APIKeychain.SetKey("offline-ready", ready));
            next = ready.Id;
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = outKey.Id });
            var pill = rig.App.Settings.Pills.Create("Availability", [new(anchor.Id.ToString("D"), KeyId: outKey.Id.ToString("D")), new(ready.Id.ToString("D"))]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
        });
        rig.Install(rig.Session);
        rig.Send("Record this account's reported exhaustion.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            var created = rig.App.NewThread(rig.Project)!;
            Assert.Equal(next, created.ProviderAccountID);
            Assert.Equal(1, created.PillStepIndex);
        });
    }

    [Fact]
    public void A_new_thread_does_not_bind_the_anchor_when_a_saved_pill_key_identity_is_malformed()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-anchor";
            var anchor = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var id = Guid.NewGuid();
            var raw = "{\"version\":1,\"useForNewThreads\":" + JsonSerializer.Serialize(id) + ",\"pills\":[{\"id\":"
                + JsonSerializer.Serialize(id) + ",\"name\":\"Preserve\",\"steps\":[{\"account\":"
                + JsonSerializer.Serialize(anchor.Id.ToString("D")) + ",\"key\":123}]}]}";
            rig.H.Defaults.Set(PillStorage.DefaultKey, raw);
            var created = rig.App.NewThread(rig.Project)!;
            Assert.NotEqual(anchor.Id, created.ProviderAccountID);
            Assert.Null(created.PillRelay);
            Assert.Equal(raw, rig.H.Defaults.Object(PillStorage.DefaultKey));
        });
    }

    [Fact]
    public void A_new_thread_uses_the_pill_keys_actual_account_defaults_and_persistent_identity()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-anchor";
            var anchor = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var selected = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-selected", selected));
            rig.App.Settings.Accounts.SetDefaults(anchor.Id, new() { Model = "anchor-model", Effort = "low" });
            rig.App.Settings.Accounts.SetDefaults(selected.Id, new() { Model = "selected-model", Effort = "high", FastMode = true });
            var pill = rig.App.Settings.Pills.Create("Key relay", [new(anchor.Id.ToString("D"), KeyId: selected.Id.ToString("D"))]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
            var created = rig.App.NewThread(rig.Project)!;
            Assert.Equal(selected.Id, created.ProviderAccountID);
            Assert.Equal(selected.Id.ToString("D"), created.PillRelay!.AccountId);
            Assert.Equal("selected-model", created.Model);
            Assert.Equal("high", created.Effort);
            Assert.True(created.FastMode);
            Assert.Null(created.ProviderSessionID);
        });
    }

    [Theory]
    [InlineData("off")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public void A_new_thread_skips_a_pill_step_whose_actual_key_is_unusable_instead_of_using_the_anchor(string problem)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-anchor";
            var anchor = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var selected = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-selected", selected));
            var fallback = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            rig.App.Settings.Accounts.MarkSignedIn(fallback.Id);
            var key = problem == "missing" ? Guid.NewGuid() : problem == "foreign" ? fallback.Id : selected.Id;
            if (problem == "off") rig.App.Settings.Accounts.SetEnabled(selected.Id, false);
            var pill = rig.App.Settings.Pills.Create("Skip key", [new(anchor.Id.ToString("D"), KeyId: key.ToString("D")), new(fallback.Id.ToString("D"))]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
            var created = rig.App.NewThread(rig.Project)!;
            Assert.Equal(ProviderKind.Claude, created.Provider);
            Assert.Equal(fallback.Id, created.ProviderAccountID);
            Assert.Equal(1, created.PillStepIndex);
            Assert.Equal(fallback.Id.ToString("D"), created.PillRelay!.AccountId);
        });
    }
}
