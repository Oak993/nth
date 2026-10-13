using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-5")]
public sealed class DextraAsturBindingTests
{
    [Fact]
    public void A_failure_after_controller_binding_disposes_it_and_the_next_start_can_retry()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        var settings = new DextraSettings(new InMemorySettingsStore());
        settings.Update(value => value with { Id = Guid.NewGuid(), FirstRunDone = true });
        using var host = new DextraHost(launch.Model, settings, voice: (_, _, _) => null);
        launch.Main.OnMain(() => launch.Model.AssistantHost = host);
        AssistantController? failed = null;
        var bad = new BackgroundStartSeams
        {
            Roots = () => temp.Roots, Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray,
            ControllerBuilt = c => failed = c,
            Host = new DutySeams { Time = new FailingStartupClock(() => { }), RunKey = launch.Seams.Host!.RunKey, TaskHost = launch.Seams.Host.TaskHost, Processes = launch.Seams.Host.Processes, Desktop = launch.Seams.Host.Desktop },
        };
        Assert.Null(launch.Main.Loop.Run(() => host.EnsureStartedAsync(bad)));
        Assert.NotNull(failed);
        Assert.Null(launch.Model.Dextra);
        Assert.False(failed.Reserve!.TryBookModelCall(ReserveRig.CodexA, host.AssistantId, ReserveRig.Model, null, out _));
        Assert.Contains("fake duty startup", host.StartFault, StringComparison.Ordinal);
        var good = new BackgroundStartSeams { Roots = () => temp.Roots, Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host };
        Assert.NotNull(launch.Main.Loop.Run(() => host.EnsureStartedAsync(good)));
        launch.Main.OnMain(host.Stop);
    }

    [Fact]
    public void Background_start_binds_the_team_and_the_host_keeps_its_saved_root_identity()
    {
        using var temp = new AssistantTempRoot();
        var root = new AssistantProfile(Guid.NewGuid().ToString("N"), "Dextra");
        var astur = new AssistantProfile(Guid.NewGuid().ToString("N"), "Shop") { ParentId = root.Id, Shape = AsturShape.Squircle };
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveProfile(root.Id, AssistantProfileFile.ToJson(root));
            store.SaveProfile(astur.Id, AssistantProfileFile.ToJson(astur));
            store.SaveDuties(astur.Id, DutyJson.Write(new DutyDocument([new Duty("shop-note", "Shop note", astur.Id, new ManualTrigger(), "Write a note.")])));
        }
        using var launch = new BackgroundLaunchRig(temp);
        var settings = new DextraSettings(new Marrow.Testing.InMemorySettingsStore());
        settings.Update(p => p with { FirstRunDone = true });
        launch.Model.AssistantHost = new DextraHost(launch.Model, settings,
            starter: (model, _, _) => model.Dextra, voice: (_, _, _) => null);
        Assert.NotNull(launch.Run(StartupMode.Normal, out var problem));
        Assert.Null(problem);
        Assert.Equal(root.Id, launch.Model.Dextra!.Team!.Root!.Id);
        Assert.Equal(root.Id, launch.Model.AssistantHost.AssistantId);
        Assert.Equal([root.Id, astur.Id], launch.Model.AssistantHost.Profiles().Select(p => p.Id));
        Assert.Same(launch.Model.Dextra, launch.Model.AssistantHost.EnsureStarted());
    }

    [Fact]
    public void Production_style_starts_share_one_worker_initialization_and_bind_real_team_operations()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        var settings = new DextraSettings(new InMemorySettingsStore());
        settings.Update(value => value with { Id = Guid.NewGuid(), FirstRunDone = true });
        using var host = new DextraHost(launch.Model, settings, voice: (_, _, _) => null);
        launch.Main.OnMain(() => launch.Model.AssistantHost = host);
        var reads = 0;
        var seams = new BackgroundStartSeams
        {
            Roots = () => { Assert.False(MainActor.IsIsolated); Interlocked.Increment(ref reads); return temp.Roots; },
            Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
        };
        var calls = launch.Main.OnMain(() => (First: host.EnsureStartedAsync(seams), Second: host.EnsureStartedAsync(seams)));
        Assert.Same(calls.First, calls.Second);
        var controller = launch.Main.Loop.Run(() => calls.First);
        Assert.NotNull(controller);
        Assert.Equal(1, reads);
        Assert.Equal(host.AssistantId, controller.Team!.Root!.Id);
        Assert.NotNull(launch.Model.Duties!.Digests);
        var made = launch.Main.Loop.Run(() => controller.CreateByOwner(new AsturDraft { Name = "Shop", Shape = AsturShape.Squircle, Area = "Fake shop" }));
        Assert.True(made.Ok, made.Problem);
        Assert.Contains(host.Profiles(), p => p.Id == made.Profile!.Id);
        launch.Main.OnMain(host.Stop);
    }
}
