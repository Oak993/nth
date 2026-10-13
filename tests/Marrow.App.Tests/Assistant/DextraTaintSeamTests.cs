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
                rig.Runtime.Send(byOwner: true);
            });
            Assert.False(rig.H.Main.OnMain(() => controller.HomeTaint(home)).IsTainted);
            rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.App.AttachDextra(null));
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as7-12")]
    public void Only_the_owners_own_sends_clear_the_home_chats_taint_never_nths_own_or_its_continue_after_a_limit()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { AssistantID = Guid.NewGuid() });
        var home = rig.InitialThread.Id;
        var controller = rig.H.Main.OnMain(() =>
        {
            var made = Controller(rig.App, new FakeWorkspaces(rig.H.NewFolder("worktrees")));
            rig.App.AttachDextra(made);
            return made;
        });
        bool Tainted() => rig.H.Main.OnMain(() => controller.HomeTaint(home)).IsTainted;
        try
        {
            rig.Send("Read the page.");
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.ToolStarted("w1", Web("https://hostile.example/page"))));
            rig.Complete();
            Assert.True(Tainted());

            // nth's own send through the composer's path (a head's retry, a helper's prompt) is no owner's message.
            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.Draft = new ComposerDraft { Text = "Retry the brief." };
                rig.Runtime.Send();
            });
            Assert.True(Tainted());
            rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 2, AppModelHarness.Bound);
            rig.Complete();

            // AutoContinue after a usage limit queues its own "continue" and sends it at once: no owner's message either.
            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.EnqueueFollowUp(AutoContinue.ContinueMessage, []);
                rig.Runtime.SendFollowUpNow(rig.Runtime.FollowUps[^1].Id);
            });
            Assert.True(Tainted());
            rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 3, AppModelHarness.Bound);
            rig.Complete();

            // The owner pressing send on a message nth queued does not make its words his.
            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.EnqueueFollowUp("A report delivered into the chat.", []);
                rig.Runtime.SendFollowUpNow(rig.Runtime.FollowUps[^1].Id, byOwner: true);
            });
            Assert.True(Tainted());
            rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count == 4, AppModelHarness.Bound);

            // A message the owner wrote and queued while the turn runs, sent now by him, is his own message.
            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.Draft = new ComposerDraft { Text = "Stop. Book nothing." };
                rig.Runtime.QueueDraftAsFollowUp(byOwner: true);
                rig.Runtime.SendFollowUpNow(rig.Runtime.FollowUps[^1].Id, byOwner: true);
            });
            Assert.False(Tainted());
            Assert.Equal(1, rig.Session.InterruptCalls);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.App.AttachDextra(null));
        }
    }
}
