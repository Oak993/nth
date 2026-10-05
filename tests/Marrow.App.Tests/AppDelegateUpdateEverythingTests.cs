using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Xunit;

namespace Marrow.App.Tests;

// UE-5 (OD-0100): the launch fills the shared Update everything service with the items the app has, and a launch test can never reach the real service, the real self-update engine, a
// CLI or a listing: LaunchHarness gives the launch a fresh service, a fake engine and an items source of its own.
[Collection("Headless")]
public sealed class AppDelegateUpdateEverythingTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_launch_gives_the_service_the_items_of_the_source_and_never_the_real_ones_in_a_test() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var item = new FakeUpdateItem("c", "Claude Code", UpdateItemKind.ProviderCli, "1.0.0", "1.1.0");
        var restarts = 0;
        using var source = UpdateEverythingProduction.OverrideSource(_ => ([item], () => restarts++));
        var before = UpdateEverythingService.Shared;
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(before, UpdateEverythingService.Shared); // the harness's service, not the process's
        Assert.Equal(["c"], launch.Harness.Main.OnMain(() => UpdateEverythingService.Shared.Rows.Select(r => r.Id)));
        launch.Harness.Main.OnMain(() => UpdateEverythingService.Shared.Perform(UpdatePillAction.RestartMarrow));
        Assert.Equal(1, restarts);
        Assert.Equal(0, item.Updates); // nothing ran
    });
}
