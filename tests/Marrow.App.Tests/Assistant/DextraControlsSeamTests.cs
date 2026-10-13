using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tray;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6, 12.6b, 7.4; ED-NEW-dextra-as6-4, -10, -12): the controls in the app itself. A Hydra head the
// owner paused ends its turn at the boundary and is NOT finished by Hydra (it does not tell the lead) until Resume gives the
// finish back; StartDextra binds the one stop chord to Stop all and puts the Assistant's items in the one tray icon, and
// UnbindDextra takes them away again.
[Trait("Decision", "ED-NEW-dextra-as6-12")]
[Trait("Feature", "AS-6")]
public sealed class DextraControlsSeamTests
{
    private static MissionRecord Record(Guid mission, Guid lead, Guid project, string path) =>
        new(new Mission(mission, "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working) { ThreadIds = new([lead]) })
        {
            Brief = "Make it 30 s.",
            ProjectId = project,
            ProjectPath = path,
            LeadThread = lead,
            Created = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void A_head_the_owner_paused_is_not_finished_by_Hydra_until_Resume_gives_its_finish_back()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = SealedProviderConfig.CappedMode, MissionID = mission };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var audit = new List<AuditEntry>();
        var controller = h.Main.OnMain(() =>
        {
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            var made = new AssistantController(new AssistantControllerOptions
            {
                Agents = new AppMissionAgents(model),
                Thread = model.Thread,
                Projects = () => [.. model.Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
                Profiles = () => [new AssistantProfile("dextra", "Dextra")],
                Jobs = model.MissionJobs,
                Workspaces = new FakeWorkspaces(h.NewFolder("worktrees")),
                Audit = e =>
                {
                    lock (audit) audit.Add(e);
                },
                Memory = new FakeMemory(FakeMemory.Plenty),
            });
            made.Runner.Load([Record(mission, lead.Id, project.Id, project.Path)]);
            model.AttachDextra(made);
            return made;
        });
        try
        {
            var head = h.Main.OnMain(() => model.SpawnDroppyHead(lead.Id, "Write the tests", HydraHeadInfo.Origin.Delegated, [], null, null, null, null, static (_, _) => "brief"))!;
            var id = head.Id.ToString("D");
            h.Main.Loop.RunUntil(() => controller.Runner.Find(mission)!.Mission.Heads.Any(x => x.Id == id && x.State == HeadState.Working), AppModelHarness.Bound);

            // The owner pauses the head while its turn runs: the head is Pausing, and nothing was cut.
            h.Main.Loop.RunUntil(() => model.ExistingRuntime(head.Id)?.IsRunning == true, AppModelHarness.Bound);
            Assert.Null(h.Main.OnMain(() => controller.Controls.PauseHead(mission, head.Id)));
            Assert.Equal(HeadState.Pausing, controller.Runner.Find(mission)!.Mission.Heads.Single(x => x.Id == id).State);

            // Its turn ends. Hydra is told to hold the finish: the head is Paused and not finished, and the lead hears nothing.
            h.Main.OnMain(() => model.HydraHeadTurnFinished(model.Thread(head.Id)!, TurnStatus.Completed));
            h.Main.Loop.RunUntil(() => controller.Runner.Find(mission)!.Mission.Heads.Single(x => x.Id == id).State == HeadState.Paused, AppModelHarness.Bound);
            Assert.False(model.Thread(head.Id)!.Hydra!.IsFinished);

            // Resume gives the finish back: Hydra finishes the head now and the mission counts it done.
            Assert.Null(h.Main.OnMain(() => controller.Controls.ResumeHead(mission, head.Id)));
            h.Main.Loop.RunUntil(() => model.Thread(head.Id)!.Hydra!.IsFinished, AppModelHarness.Bound);
            Assert.Equal(HeadState.Done, controller.Runner.Find(mission)!.Mission.Heads.Single(x => x.Id == id).State);
        }
        finally
        {
            h.Main.OnMain(() => model.AttachDextra(null));
        }
    }

    [Fact]
    public void Start_dextra_binds_the_stop_chord_to_Stop_all_and_puts_the_Assistants_items_in_the_one_tray_icon()
    {
        using var h = new AppModelHarness();
        using var temp = new AssistantTempRoot();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var chord = new FakeStopChordHost();
        using var key = new Marrow.App.Safety.GlobalStop(chord, () => null);
        var trayHost = new FakeTrayHost();
        var windowHidden = true; // nth is closed to the tray
        using var tray = new AppTray(trayHost, () => windowHidden);
        using var store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });

        var controller = h.Main.OnMain(() => model.StartDextra(store, _ => { }, () => [new AssistantProfile("dextra", "Dextra")], stopKey: key, tray: tray));
        try
        {
            Assert.True(tray.Visible);
            Assert.Equal("Stop Assistant|Pause all", trayHost.Menu());
            Assert.Equal(TrayState.Ready, trayHost.Last!.State);

            // The one chord is Stop all's: a press reaches the Assistant (its report shows the chord it came from).
            Assert.True(key.TryAcquire("test:work", out var lease));
            chord.Press();
            Assert.Equal(1, controller.Stopper.Presses);
            Assert.Contains("(Ctrl+Alt+Esc)", controller.Stopper.Report!.Strip, StringComparison.Ordinal);
            lease!.Dispose();
            Assert.True(controller.Hold.StopHeld);
            Assert.Equal("Stop Assistant|Resume all", trayHost.Menu());

            // The tray's Stop works with no window: it is the Assistant's own item.
            trayHost.Click("assistant.stop");
            Assert.Equal(2, controller.Stopper.Presses);
        }
        finally
        {
            h.Main.OnMain(model.UnbindDextra);
            controller.Dispose();
        }
        // The Assistant's items go when it is unbound; the icon, hidden window or not, has nothing of its own to show.
        Assert.Empty(trayHost.Last!.Groups);
    }
}
