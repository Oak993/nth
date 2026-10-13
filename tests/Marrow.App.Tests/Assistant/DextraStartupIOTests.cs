using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Assistant;
[Trait("Decision", "ED-NEW-dextra-open-6")]
public sealed class DextraStartupIOTests
{
    [Fact]
    public void An_unpublished_controller_is_disposed_if_the_worker_handoff_throws_and_start_can_retry()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        AssistantController? failed = null;
        var bad = new BackgroundStartSeams
        {
            Roots = () => temp.Roots, Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
            ControllerBuilt = controller => { failed = controller; throw new IOException("handoff failed"); },
        };
        Assert.Null(launch.Main.Loop.Run(() => launch.Model.AssistantHost.EnsureStartedAsync(bad)));
        Assert.NotNull(failed);
        Assert.Null(launch.Model.Dextra);
        Assert.False(failed.Reserve!.TryBookModelCall(ReserveRig.CodexA, "dextra", ReserveRig.Model, null, out _));
        Assert.NotNull(launch.Main.Loop.Run(() => launch.Model.AssistantHost.EnsureStartedAsync(launch.Seams)));
        launch.OnMain(launch.Model.AssistantHost.Stop);
    }

    [Fact]
    public void A_one_duty_wake_waits_for_activation_and_is_replayed_exactly_once()
    {
        var activated = false;
        using var rig = new DutyHostRig(mode: new StartupMode(true, "d1", null), tray: false, activated: () => activated);
        var duty = new Duty("d1", "Write a note", "dextra", new ManualTrigger(), "Use the fake provider.");
        rig.Store.SaveDuties("dextra", DutyJson.Write(new DutyDocument([duty])));
        Assert.Empty(rig.Host.Start());
        Assert.Empty(rig.Controller.Runner.Missions);
        Assert.Equal(0, rig.Quits);
        activated = true;
        rig.Host.Activate();
        var mission = rig.MissionOf(duty);
        Assert.Equal("d1", mission.DutyId);
        rig.Host.Activate();
        Assert.Single(rig.Controller.Runner.Missions);
        Assert.Equal(0, rig.Quits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Persisted_services_and_duties_never_access_files_on_MainActor_even_before_the_first_frame(bool hidden)
    {
        using var temp = new AssistantTempRoot();
        DextraFixStartupTests.DutyFile(temp);
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveReserves(new JsonObject { ["version"] = 1 });
            store.SaveCalibration(new JsonObject { ["version"] = 1 });
            store.SaveCosts(new JsonObject { ["version"] = 1 });
            store.SaveHomeTaint(new JsonObject { ["version"] = 99 });
            store.SaveBackground(new JsonObject { ["version"] = 1 });
            store.WriteMemoryFile("dextra", "recent.md", "# Recent\n");
        }
        using var launch = new BackgroundLaunchRig(temp);
        var access = new ConcurrentBag<(string Path, bool Main)>();
        var seams = new BackgroundStartSeams
        {
            Roots = () => { access.Add(("roots", MainActor.IsIsolated)); return temp.Roots; },
            Keys = () => { access.Add(("keys", MainActor.IsIsolated)); return temp.Keys; },
            FileIO = path => access.Add((path, MainActor.IsIsolated)),
            StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
        };
        var task = launch.OnMain(() => hidden
            ? BackgroundStart.RunAsync(launch.Model, new StartupMode(true, null, null), () => { }, seams)
            : Foreground());
        Task<DutyHost?> Foreground() => StartForeground();
        async Task<DutyHost?> StartForeground()
        {
            await launch.Model.AssistantHost.EnsureStartedAsync(seams);
            return launch.Model.Duties;
        }
        // A render-ready main-loop turn can run while startup disk work is pending. No native window is opened.
        var firstFrameTurn = launch.OnMain(() => access.Where(a => a.Main).ToArray());
        Assert.Empty(firstFrameTurn);
        Assert.NotNull(launch.Main.Loop.Run(() => task, TimeSpan.FromSeconds(20)));
        Assert.True(access.Count >= 12, "The real persisted-service reads must be observed.");
        Assert.DoesNotContain(access, a => a.Main);
        Assert.Contains(access, a => a.Path.EndsWith("reserves.json", StringComparison.Ordinal));
        Assert.Contains(access, a => a.Path.EndsWith("background.json", StringComparison.Ordinal));
        Assert.Contains(access, a => a.Path.EndsWith("delegate-answers.json", StringComparison.Ordinal));
        Assert.NotNull(launch.Model.Dextra!.Notes);
        launch.OnMain(launch.Model.AssistantHost.Stop);
    }

    [Fact]
    public void A_stop_during_worker_open_never_publishes_and_a_new_start_can_retry()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var seams = new BackgroundStartSeams
        {
            Roots = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(20))); return temp.Roots; },
            Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
        };
        var pending = launch.OnMain(() => launch.Model.AssistantHost.EnsureStartedAsync(seams));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
        launch.OnMain(launch.Model.AssistantHost.Stop);
        release.Set();
        Assert.Null(launch.Main.Loop.Run(() => pending, TimeSpan.FromSeconds(20)));
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
        Assert.NotNull(launch.Main.Loop.Run(() => launch.Model.AssistantHost.EnsureStartedAsync(launch.Seams), TimeSpan.FromSeconds(20)));
        launch.OnMain(launch.Model.AssistantHost.Stop);
    }

    [Fact]
    public void Legacy_duty_owners_share_the_canonical_roots_policy_in_the_host_and_controller()
    {
        using var temp = new AssistantTempRoot();
        DextraFixStartupTests.DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        Assert.NotNull(launch.Run(StartupMode.Normal, out _));
        var hostProfiles = launch.OnMain(() => launch.Model.AssistantHost.Profiles());
        var legacy = hostProfiles.Single(p => p.Id == "dextra");
        Assert.Equal(launch.Model.Dextra!.Team!.Root!.Id, legacy.ParentId);
        Assert.Equal(hostProfiles, launch.Model.Dextra.Profiles);
        Assert.Single(hostProfiles, p => p.IsRoot);
        Assert.NotNull(launch.OnMain(() => launch.Model.Dextra.PolicyContext(new AssistantChat(Guid.NewGuid(), legacy.Id, null, AssistantChatRole.Home, Marrow.Core.Models.ProviderKind.Codex, "fake"))));
    }
}
