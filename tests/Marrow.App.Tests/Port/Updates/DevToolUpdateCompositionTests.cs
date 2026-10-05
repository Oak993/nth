using Marrow.App.Port.Updates;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Updates;

[Trait("Decision", "ED-308")]
public sealed class DevToolUpdateCompositionTests
{
    [Fact]
    public void A_completed_inventory_configures_installed_rows_and_Marrow_last_immediately()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        var git = new FakeUpdateItem("dev:Git.Git", "Git", UpdateItemKind.DevTool, "2.54.0", "2.55.0.5");
        var marrow = new FakeUpdateItem("marrow", "Marrow", UpdateItemKind.Marrow, "1.7.0", "1.7.1");
        var pending = runtime.OnMain(() => UpdateEverythingProduction.ConfigureSource(service, () => Task.FromResult<(IReadOnlyList<IUpdateItem>, Action)>(([marrow, git], () => { }))));
        Assert.True(pending.IsCompletedSuccessfully); Assert.Equal([git.Id, marrow.Id], service.Rows.Select(r => r.Id));
        Assert.Equal(UpdateItemKind.DevTool, service.Rows[0].Kind);
    }

    [Fact]
    public void Deferred_inventory_waits_for_detection_without_reaching_any_real_process()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        var source = new DeferredDevToolSource();
        var pending = runtime.OnMain(() => UpdateEverythingProduction.ConfigureSource(service, source.Read));
        Assert.False(pending.IsCompleted); Assert.Empty(service.Rows);
        var python = new FakeUpdateItem("dev:Python.Python.3.14", "Python 3.14", UpdateItemKind.DevTool, "3.14.4", "3.14.7");
        var restarts = 0;
        source.Completion.SetResult(([python], () => restarts++));
        runtime.Loop.RunUntil(() => pending.IsCompleted);
        Assert.True(pending.IsCompletedSuccessfully); Assert.Equal(python.Id, Assert.Single(service.Rows).Id);
        runtime.OnMain(() => service.Perform(UpdatePillAction.RestartMarrow)); Assert.Equal(1, restarts);
        Assert.Equal(0, python.Updates);
    }

    [Fact]
    public async Task Failed_inventory_propagates_without_erasing_previously_configured_items()
    {
        using var runtime = new TestRuntime();
        var cli = new FakeUpdateItem("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "1.0", "2.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([cli], () => { }));
        var pending = runtime.OnMain(() => UpdateEverythingProduction.ConfigureSource(service, () => Task.FromException<(IReadOnlyList<IUpdateItem>, Action)>(new InvalidOperationException("inventory unavailable"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(cli.Id, Assert.Single(service.Rows).Id);
    }

    [Fact]
    public void An_attached_Updates_page_receives_rows_when_deferred_detection_finishes()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        var controller = runtime.OnMain(() => new UpdatesController(service));
        runtime.OnMain(() => controller.Attach().GetAwaiter().GetResult());
        var source = new DeferredDevToolSource();
        var pending = runtime.OnMain(() => UpdateEverythingProduction.ConfigureSource(service, source.Read));
        var notifications = 0; controller.ViewChanged += () => notifications++;
        source.Completion.SetResult(([new FakeUpdateItem("dev:Git.Git", "Git", UpdateItemKind.DevTool, "2.54.0", "2.55.0.5")], () => { }));
        runtime.Loop.RunUntil(() => pending.IsCompleted); runtime.RunUntilIdle();
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal("dev:Git.Git", Assert.Single(controller.View.Sections.SelectMany(s => s.Rows)).Id);
        Assert.True(notifications > 0);
        runtime.OnMain(controller.Detach);
    }
}
