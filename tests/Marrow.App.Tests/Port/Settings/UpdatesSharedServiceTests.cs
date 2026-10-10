using Avalonia;
using HostWindow = Avalonia.Controls.Window;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Settings;

// Unit UE-4 with UE-5 (the coordinator's note: the pill and the page must read the same state). The Updates page built without a runner reads the app's one UpdateEverythingService, the service the
// sidebar's pill reads: a run started from either shows on both, and the restart is the service's.
[Collection("Headless")]
public sealed class UpdatesSharedServiceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-308")] // one orchestrator for the pill and the page
    public void The_page_and_the_pill_read_one_service_so_a_run_from_either_shows_on_both() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        List<string> restarts = [];
        var claude = new ScriptedUpdateItem("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "2.1.283", "2.1.290");
        var marrow = new ScriptedUpdateItem("marrow", "Nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0") { OnUpdate = _ => Task.FromResult(new UpdateOutcome(true, "1.1.0", true, true, null)) };
        var service = new UpdateEverythingService([claude, marrow], () => restarts.Add("restart"));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var page = new UpdatesSettingsPage();
        var window = new HostWindow { Width = 600, Height = 800, Content = new Border { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = page } };
        window.Show();
        runtime.RunUntilIdle();
        try
        {
            Assert.Equal("2 updates available", page.TitleText.Text);
            Assert.Equal(UpdatePillPhase.Available, service.State.Phase);
            runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything)); // the pill's button
            runtime.RunUntilIdle();
            Assert.Equal(("2 updated", "Restart Nth"), (page.TitleText.Text, page.MainButton.Label));
            Assert.Equal(UpdatePillPhase.Restart, service.State.Phase);
            page.MainButton.Activate(); // the page's Restart Marrow is the service's restart
            runtime.RunUntilIdle();
            Assert.Equal(["restart"], restarts);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")] // a check made on the page reaches the pill's rows
    public void A_check_from_the_page_updates_the_pills_state() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var claude = new ScriptedUpdateItem("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "2.1.290", "2.1.290");
        var service = new UpdateEverythingService([claude], () => { });
        using var shared = UpdateEverythingService.OverrideShared(service);
        var page = new UpdatesSettingsPage();
        var window = new HostWindow { Width = 600, Height = 800, Content = new Border { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = page } };
        window.Show();
        runtime.RunUntilIdle();
        try
        {
            Assert.Equal(UpdatePillPhase.Hidden, service.State.Phase);
            claude.Latest = "2.2.0";
            page.MainButton.Activate(); // Check now
            runtime.RunUntilIdle();
            Assert.Equal(("1 update available", UpdatePillPhase.Available), (page.TitleText.Text, service.State.Phase));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")] // the hook the units that own the skills and the developer tools use: nothing by default
    public void The_extra_items_hook_is_empty_until_a_unit_sets_it() =>
        Assert.Empty(UpdateEverythingProduction.ExtraItems());
}
