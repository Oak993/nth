using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-16 (DL-19, OD-0100): the skills pipeline's installed items are the Updates page's "Skills and tools" rows. The shared Update everything service gets them through the one hook UE-4 left
// (UpdateEverythingProduction.ExtraItems) and is given the set again when the installed set changes. A launch test never reaches a real pipeline, a CLI or a listing (the items source is the harness's).
[Collection("Headless")]
public sealed class SkillsUpdateRowsTests(HeadlessSession headless)
{
    private static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk16 rows " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Attach_gives_the_hook_the_pipelines_items_and_dispose_puts_the_hook_back_empty()
    {
        var root = FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            var item = new FakeUpdateItem("S1", "Skill set", UpdateItemKind.SkillsAndTools, "1", "2");
            var attach = SkillsUpdateRows.Attach(host, () => [item]);
            Assert.Same(item, Assert.Single(UpdateEverythingProduction.ExtraItems()));
            attach.Dispose();
            Assert.Empty(UpdateEverythingProduction.ExtraItems());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void With_no_items_given_the_hook_reads_the_pipelines_update_items_which_are_none_while_nothing_is_installed()
    {
        var root = FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            using var attach = SkillsUpdateRows.Attach(host);
            Assert.Empty(UpdateEverythingProduction.ExtraItems());
            Assert.Empty(host.Pipeline.UpdateItems());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_change_of_the_installed_set_gives_the_service_its_items_again_so_the_new_rows_show_without_a_restart() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var first = new FakeUpdateItem("S1", "First set", UpdateItemKind.SkillsAndTools, "1", "1");
        var second = new FakeUpdateItem("S2", "Second set", UpdateItemKind.SkillsAndTools, "1", "1");
        var service = new UpdateEverythingService([first], () => { });
        using var shared = UpdateEverythingService.OverrideShared(service);
        using var source = UpdateEverythingProduction.OverrideSource(_ => ([second], () => { }));
        UpdateEverythingProduction.Remember(null!);
        var root = FreshRoot();
        try
        {
            Assert.Equal(["S1"], service.Rows.Select(r => r.Id));
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            using var attach = SkillsUpdateRows.Attach(host);
            host.ResetAsync().GetAwaiter().GetResult(); // the pipeline says its installed set may have changed
            runtime.RunUntilIdle();
            Assert.Equal(["S2"], service.Rows.Select(r => r.Id));
        }
        finally
        {
            UpdateEverythingProduction.Forget();
            Directory.Delete(root, recursive: true);
        }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_service_that_is_running_an_update_is_not_given_new_items_mid_run() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        var busy = new FakeUpdateItem("S1", "First set", UpdateItemKind.SkillsAndTools, "1", "2") { Gate = gate.Task };
        var later = new FakeUpdateItem("S2", "Second set", UpdateItemKind.SkillsAndTools, "1", "1");
        var service = new UpdateEverythingService([busy], () => { });
        using var shared = UpdateEverythingService.OverrideShared(service);
        using var source = UpdateEverythingProduction.OverrideSource(_ => ([later], () => { }));
        UpdateEverythingProduction.Remember(null!);
        var root = FreshRoot();
        try
        {
            runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            runtime.RunUntilIdle();
            Assert.True(service.IsBusy);
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            using var attach = SkillsUpdateRows.Attach(host);
            host.ResetAsync().GetAwaiter().GetResult();
            runtime.RunUntilIdle();
            Assert.Equal(["S1"], service.Rows.Select(r => r.Id));
        }
        finally
        {
            gate.TrySetResult();
            runtime.RunUntilIdle();
            UpdateEverythingProduction.Forget();
            Directory.Delete(root, recursive: true);
        }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Before_the_launch_has_configured_the_service_a_change_gives_it_nothing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var first = new FakeUpdateItem("S1", "First set", UpdateItemKind.SkillsAndTools, "1", "1");
        var service = new UpdateEverythingService([first], () => { });
        using var shared = UpdateEverythingService.OverrideShared(service);
        UpdateEverythingProduction.Forget();
        var root = FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            using var attach = SkillsUpdateRows.Attach(host);
            host.ResetAsync().GetAwaiter().GetResult();
            runtime.RunUntilIdle();
            Assert.Equal(["S1"], service.Rows.Select(r => r.Id));
        }
        finally { Directory.Delete(root, recursive: true); }
    });
}
