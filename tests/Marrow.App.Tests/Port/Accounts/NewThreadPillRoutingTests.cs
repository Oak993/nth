using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class NewThreadPillRoutingTests
{
    [Fact]
    public void Launch_reuse_applies_the_current_default_pill_and_replaces_stale_account_and_step_metadata()
    {
        using var rig = new ThreadRuntimeTurnRig(configure: thread => thread with { Title = ChatThread.Untitled });
        rig.H.Main.OnMain(() =>
        {
            var first = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            var second = rig.App.Settings.Accounts.ForService(ProviderKind.Codex)[0];
            rig.App.Settings.Accounts.MarkSignedIn(first.Id);
            rig.App.Settings.Accounts.MarkSignedIn(second.Id);
            var old = rig.App.Settings.Pills.Create("Old", [new(first.Id.ToString("D"))]);
            rig.App.UpdateThread(rig.InitialThread.Id, thread => (thread with { ProviderAccountID = first.Id, ProviderSessionID = "old-native" }).WithPillRelay(new(old.Id, 0, first.Id.ToString("D"))));
            var next = rig.App.Settings.Pills.Create("Next", [new(second.Id.ToString("D"), "launch-model", "high", true)]);
            rig.App.Settings.Pills.SetUseForNewThreads(next.Id);
            rig.App.OpenThreadOnLaunch();
            var reused = rig.App.Thread(rig.InitialThread.Id)!;
            Assert.Equal(rig.InitialThread.Id, rig.App.SelectedThreadID);
            Assert.Single(rig.App.Threads);
            Assert.Equal(second.Id, reused.ProviderAccountID);
            Assert.Equal(next.Id, reused.PillID);
            Assert.Equal(second.Id.ToString("D"), reused.PillRelay!.AccountId);
            Assert.Equal("launch-model", reused.Model);
            Assert.Equal("high", reused.Effort);
            Assert.True(reused.FastMode);
            Assert.Null(reused.ProviderSessionID);
        });
    }

    [Fact]
    public void Use_for_new_threads_starts_on_the_first_connected_enabled_step_with_its_complete_overrides()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var first = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            var second = rig.App.Settings.Accounts.ForService(ProviderKind.Codex)[0];
            rig.App.Settings.Accounts.MarkSignedIn(first.Id);
            rig.App.Settings.Accounts.MarkSignedIn(second.Id);
            rig.App.Settings.Accounts.SetEnabled(first.Id, false);
            rig.App.Settings.Accounts.SetDefaults(second.Id, new() { Model = "account-model", Effort = "low", FastMode = true });
            var pill = rig.App.Settings.Pills.Create("New work", [new(first.Id.ToString("D")), new(second.Id.ToString("D"), "step-model", "high", false)]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
            var created = rig.App.NewThread(rig.Project)!;
            Assert.Equal(ProviderKind.Codex, created.Provider);
            Assert.Equal(second.Id, created.ProviderAccountID);
            Assert.Equal(pill.Id, created.PillID);
            Assert.Equal(1, created.PillStepIndex);
            Assert.Equal(second.Id.ToString("D"), created.PillRelay!.AccountId);
            Assert.Equal("step-model", created.Model);
            Assert.Equal("high", created.Effort);
            Assert.False(created.FastMode);
            Assert.Null(created.ProviderSessionID);
        });
    }
}
