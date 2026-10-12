using Marrow.App.Assistant;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint"; ED-NEW-dextra-as7-7, ED-NEW-dextra-as7-9): the seams in the app itself.
// A Dextra chat's provider events reach the controller through ThreadRuntime.Handle, so a web page its session opens
// taints it before the next approval is decided; and the owner's own message, sent from the composer, clears the home
// chat's taint. Sessions are scripted; no provider runs.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-9")]
public sealed class DextraTaintSeamTests
{
    private static AssistantController Controller(AppModel app, FakeWorkspaces workspaces) => new(new AssistantControllerOptions
    {
        Agents = new AppMissionAgents(app),
        Thread = app.Thread,
        Projects = () => [.. app.Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = app.MissionJobs,
        Workspaces = workspaces,
        Audit = _ => { },
        Memory = new FakeMemory(FakeMemory.Plenty),
        AutoAnswer = AutoAnswerSwitch.AfterCorpus(() => DextraTaintRig.Pass),
        Allowlist = () => ["dotnet test"],
    });

    private static ToolCall Web(string url) => new() { ToolCallKind = ToolCall.Kind.Web, Title = url };

    [Fact]
    public void A_mission_chats_web_read_taints_it_before_its_next_approval_is_decided()
    {
        var mission = Guid.NewGuid();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { MissionID = mission, RuntimeMode = SealedProviderConfig.CappedMode });
        var lead = rig.InitialThread.Id;
        var controller = rig.H.Main.OnMain(() =>
        {
            var made = Controller(rig.App, new FakeWorkspaces(rig.H.NewFolder("worktrees")));
            made.Runner.Load([new MissionRecord(new Mission(mission, "Prices", "dextra", MissionOrigin.Owner, MissionState.Working) { ThreadIds = new([lead]) })
            {
                ProjectId = rig.Project.Id,
                ProjectPath = rig.Project.Path,
                LeadThread = lead,
                Created = DateTimeOffset.UtcNow,
            }]);
            rig.App.AttachDextra(made);
            return made;
        });
        try
        {
            rig.Finish(rig.StartTurn("Find the price."));
            rig.H.Main.OnMain(() =>
            {
                rig.Session.Emit(new ProviderEvent.ToolStarted("w1", Web("https://shop.example/filters")));
                rig.Session.Emit(new ProviderEvent.Approval(MissionRig.Approval("r1", "dotnet test")));
            });
            rig.H.Main.Loop.RunUntil(() => controller.Queue.Of(mission).Count == 1, AppModelHarness.Bound);
            Assert.Equal("Read untrusted content: asks", controller.Queue.Of(mission).Single().Rule);
            Assert.Equal(ContentOrigin.Web, rig.H.Main.OnMain(() => controller.Runner.TaintOf(mission, lead)).Taint.Sources.Single().Origin);
            Assert.Empty(rig.Session.ApprovalAnswers);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.App.AttachDextra(null));
        }
    }

    [Fact]
    public void The_home_chat_is_tainted_by_what_it_reads_until_the_owner_sends_his_own_message()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { AssistantID = Guid.NewGuid() });
        var home = rig.InitialThread.Id;
        var controller = rig.H.Main.OnMain(() =>
        {
            var made = Controller(rig.App, new FakeWorkspaces(rig.H.NewFolder("worktrees")));
            rig.App.AttachDextra(made);
            return made;
        });
        try
        {
            rig.Send("What is the weather?");
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.ToolStarted("w1", Web("https://weather.example"))));
            Assert.True(rig.H.Main.OnMain(() => controller.HomeTaint(home)).IsTainted);
            rig.Complete();

            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.Draft = new ComposerDraft { Text = "Thanks. Book nothing." };
                rig.Runtime.Send();
            });
            Assert.False(rig.H.Main.OnMain(() => controller.HomeTaint(home)).IsTainted);
            rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.App.AttachDextra(null));
        }
    }
}
