using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Core.Port.Accounts;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Accounts;

[Collection("AppSettings")]
[Trait("Feature", "MA-1")]
[Trait("Rule", "R-SET-1")]
public sealed class AppSettingsAccountsTests
{
    [Fact]
    public void Changing_service_clears_the_previous_services_account_binding()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Codex);
            rig.App.UpdateThread(rig.InitialThread.Id, t => t with { ProviderAccountID = account.Id });
            rig.App.UpdateThread(rig.InitialThread.Id, t => t with { Provider = ProviderKind.Claude, ProviderSessionID = null });
        });
        Assert.Null(rig.Thread.ProviderAccountID);
        Assert.Equal(ProviderKind.Claude, rig.Thread.Provider);
    }

    [Fact]
    public void Reordered_new_account_cannot_reuse_a_live_session_started_for_the_legacy_account()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        rig.H.Main.OnMain(() =>
        {
            var store = rig.App.Settings.Accounts;
            var legacy = store.ForService(ProviderKind.Codex)[0];
            var next = store.Add(ProviderKind.Codex);
            store.Reorder(ProviderKind.Codex, [next.Id, legacy.Id]);
        });
        Task task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
        Assert.True(task.IsFaulted); // no CLI exists in the rig; a new account must reach a new launch.
        Assert.Equal(1, rig.Session.StopCalls);
    }

    [Fact]
    public void Disabling_every_account_refuses_a_turn_even_with_a_live_session()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        rig.H.Main.OnMain(() =>
        {
            foreach (var account in rig.App.Settings.Accounts.ForService(ProviderKind.Codex))
                rig.App.Settings.Accounts.SetEnabled(account.Id, false);
        });
        Task task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
        Assert.True(task.IsFaulted);
        Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);
    }

    [Fact]
    public void Production_settings_migrate_current_model_effort_and_fast_preference_and_reopen_stable_ids()
    {
        using var scope = AppSettingsScope.Create();
        Account? original = null;
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            settings.Remember("opus", "high", ProviderKind.Claude);
            settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true }, ProviderKind.Claude, "opus");
            original = settings.Accounts.ForService(ProviderKind.Claude)[0];
            Assert.Equal("opus", original.Defaults.Model);
            Assert.Equal("high", original.Defaults.Effort);
            Assert.True(original.Defaults.FastMode);
            Assert.True(original.UsesLegacyCredentials);
            settings.Accounts.Rename(original.Id, "Work");
        });
        scope.Relaunch();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal(original!.Id, settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            Assert.Equal("Work", settings.Accounts.Get(original.Id).CustomName);
            Assert.Equal("opus", settings.LastModel(ProviderKind.Claude));
            Assert.Equal("high", settings.LastEffort(ProviderKind.Claude));
            Assert.True(settings.Preference(ProviderKind.Claude, "opus").FastMode);
        });
    }
}
