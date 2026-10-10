using Marrow.App.Port.Updates;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
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
        var marrow = new FakeUpdateItem("marrow", "nth", UpdateItemKind.Marrow, "1.7.0", "1.7.1");
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
    public void A_malformed_winget_table_keeps_the_provider_and_Marrow_rows_and_degrades_only_the_dev_tools()
    {
        using var runtime = new TestRuntime();
        using var store = new AppModelHarness(withRuntime: false);
        using var self = SelfUpdateEngine.OverrideShared(new FakeSelfUpdateEngine { Installed = false });
        var model = runtime.OnMain(() => new AppModel());
        var logged = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => logged.Add(message));
        // Fake inventory seams: Git's recorded table comes back truncated, every other tool has an empty table; only git's own binary probe answers (its recorded version output).
        Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (command.Executable == "winget")
                return Task.FromResult(command.Arguments.Contains("Git.Git", StringComparer.Ordinal)
                    ? new DevToolCommandResult(0, "truncated", "")
                    : new DevToolCommandResult(0, "Name Id Version Available Source\r\n---------------------------------------------\r\n", ""));
            if (command is { Executable: "git", Arguments: ["--version"] }) return Task.FromResult(new DevToolCommandResult(0, "git version 2.54.0.windows.1\n", ""));
            return Task.FromResult(new DevToolCommandResult(null, "", "not found"));
        }
        var (items, _) = runtime.Loop.Run(() => UpdateEverythingProduction.Real(model, Run, _ => Task.FromResult<OfficeInstallation?>(null)));
        Assert.Contains(logged, m => m.Contains("Developer-tool inventory is incomplete for Git: InvalidDataException", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Id == "marrow");
        Assert.Contains(items, i => i.Id.StartsWith("cli:", StringComparison.Ordinal));
        var dev = Assert.Single(items, i => i.Id.StartsWith("dev:", StringComparison.Ordinal));
        Assert.Equal("dev:unmatched:git", dev.Id);
        Assert.Equal("2.54.0.windows.1", runtime.Loop.Run(() => dev.InstalledVersion(default)));
    }

    [Fact]
    public void A_malformed_inventory_faulting_the_source_is_logged_and_never_reaches_the_service()
    {
        using var runtime = new TestRuntime();
        using var store = new AppModelHarness(withRuntime: false);
        using var self = SelfUpdateEngine.OverrideShared(new FakeSelfUpdateEngine { Installed = false });
        var model = runtime.OnMain(() => new AppModel());
        var cli = new FakeUpdateItem("cli:kept", "Kept provider", UpdateItemKind.ProviderCli, "1.0", "2.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([cli], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var logged = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => logged.Add(message));
        using var source = UpdateEverythingProduction.OverrideSource(_ => throw new InvalidDataException("The installed winget inventory is not a complete table."));
        runtime.OnMain(() => UpdateEverythingProduction.Configure(model));
        Assert.Contains(logged, m => m.Contains("Update inventory could not be configured: InvalidDataException", StringComparison.Ordinal));
        Assert.Equal(cli.Id, Assert.Single(service.Rows).Id);
    }

    [Fact]
    public void Configure_installs_provider_and_Marrow_rows_after_a_malformed_real_inventory()
    {
        using var runtime = new TestRuntime();
        using var store = new AppModelHarness(withRuntime: false);
        using var self = SelfUpdateEngine.OverrideShared(new FakeSelfUpdateEngine { Installed = false });
        var model = runtime.OnMain(() => new AppModel());
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var logged = new List<string>(); using var sink = Logger.Sink.Override((message, _) => logged.Add(message));
        Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Assert.DoesNotContain("upgrade", command.Arguments);
            if (command.Executable == "winget") return Task.FromResult(command.Arguments.Contains("Git.Git", StringComparer.Ordinal)
                ? new DevToolCommandResult(0, "Name Id Version Source\r\n-----\r\nGit Git.Git 2.54.0 winget\r\nGit Git.Git 2.54.0 winget\r\n", "")
                : new DevToolCommandResult(0, "Name Id Version Source\r\n-----\r\n", ""));
            return Task.FromResult(new DevToolCommandResult(null, "", "not found"));
        }
        // Every fake task completes inline, so this source cannot block on a process, network or actor hop.
        using var source = UpdateEverythingProduction.OverrideSource(m => UpdateEverythingProduction.Real(m, Run, _ => Task.FromResult<OfficeInstallation?>(null)).GetAwaiter().GetResult());
        runtime.OnMain(() => UpdateEverythingProduction.Configure(model));
        Assert.Contains(logged, m => m.Contains("Developer-tool inventory is incomplete for Git: InvalidDataException", StringComparison.Ordinal));
        Assert.Contains(service.Rows, r => r.Id.StartsWith("cli:", StringComparison.Ordinal));
        Assert.Equal("marrow", service.Rows.Last().Id); Assert.DoesNotContain(service.Rows, r => r.Kind == UpdateItemKind.DevTool);
    }

    [Fact]
    public void A_failing_malformed_inventory_diagnostic_still_preserves_the_base_real_items()
    {
        using var runtime = new TestRuntime(); using var store = new AppModelHarness(withRuntime: false);
        using var self = SelfUpdateEngine.OverrideShared(new FakeSelfUpdateEngine { Installed = false }); var model = runtime.OnMain(() => new AppModel());
        var logged = new List<string>();
        using var sink = Logger.Sink.Override((message, _) =>
        {
            logged.Add(message);
            if (message.Contains("Developer-tool inventory is incomplete", StringComparison.Ordinal)) throw new InvalidDataException("diagnostic failure");
        });
        var (items, _) = runtime.Loop.Run(() => UpdateEverythingProduction.Real(model,
            (_, _) => Task.FromResult(new DevToolCommandResult(0, "truncated", "")), _ => Task.FromResult<OfficeInstallation?>(null)));
        Assert.Contains(logged, m => m.Contains("Developer-tool inventory is unavailable: InvalidDataException", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Id.StartsWith("cli:", StringComparison.Ordinal)); Assert.Contains(items, i => i.Id == "marrow");
        Assert.DoesNotContain(items, i => i.Kind == UpdateItemKind.DevTool);
    }

    [Fact]
    public void Real_does_not_turn_cancelled_developer_detection_into_a_partial_inventory()
    {
        using var runtime = new TestRuntime(); using var store = new AppModelHarness(withRuntime: false);
        using var self = SelfUpdateEngine.OverrideShared(new FakeSelfUpdateEngine { Installed = false }); var model = runtime.OnMain(() => new AppModel());
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var logged = new List<string>(); using var sink = Logger.Sink.Override((message, _) => logged.Add(message));
        Assert.ThrowsAny<OperationCanceledException>(() => runtime.Loop.Run(() => UpdateEverythingProduction.Real(model,
            (_, _) => Task.FromCanceled<DevToolCommandResult>(cancel.Token), _ => Task.FromResult<OfficeInstallation?>(null))));
        Assert.Empty(logged);
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
