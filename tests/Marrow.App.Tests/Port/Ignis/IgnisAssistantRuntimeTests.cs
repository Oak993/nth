using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Ignis;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Ignis"; ED-NEW-dextra-as15-7): the second lock. Whatever the controller asks, a chat's own runtime refuses a
// keep-warm ping when the chat is Dextra's home thread, and says it is not between tasks while its mission is not done. What Dextra makes of a
// chat is read from the chat itself and from the mission the running controller holds. The real ThreadRuntime over the offline scripted session.
[Trait("Decision", "ED-NEW-dextra-as15-7")]
[Trait("Feature", "AS-15")]
public sealed class IgnisAssistantRuntimeTests
{
    private const string Opus = "claude-opus-5-5";

    private static ThreadRuntimeTurnRig Rig(Guid? assistant = null, Guid? mission = null) =>
        new(ProviderKind.Claude, configure: thread => thread with { Model = Opus, AssistantID = assistant, MissionID = mission }, settings: app => app.Settings.ChimeWhenFinished = false);

    private static void Turn(ThreadRuntimeTurnRig rig, string text)
    {
        _ = rig.StartTurn(text);
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("reply-1", "Done.")));
        rig.Complete();
    }

    [Fact]
    public void A_chat_of_no_assistant_sends_a_keep_warm_ping_between_tasks_and_the_home_thread_does_not()
    {
        using var plain = Rig();
        Turn(plain, "Fix the parser");
        Assert.True(plain.H.Main.OnMain(() => ((IIgnisRuntimePort)plain.Runtime).IsBetweenTasks));
        Assert.True(plain.H.Main.OnMain(() => ((IIgnisRuntimePort)plain.Runtime).SendHiddenPing()));      // as it always was
        plain.H.Main.Loop.RunUntil(() => plain.Session.Inputs.Count >= 2, AppModelHarness.Bound);
        plain.H.Main.OnMain(() => plain.Session.Emit(new ProviderEvent.MessageCompleted("reply-ping", "ok")));
        plain.Complete();

        using var home = Rig(assistant: Guid.NewGuid());
        Turn(home, "What is on my plate today?");
        Assert.True(home.H.Main.OnMain(() => ((IIgnisRuntimePort)home.Runtime).IsBetweenTasks));          // it is between tasks: it may be tidied
        Assert.False(home.H.Main.OnMain(() => ((IIgnisRuntimePort)home.Runtime).SendHiddenPing()));       // but it is not pinged, whoever asks
        Assert.False(home.H.Main.OnMain(() => ((IIgnisRuntimePort)home.Runtime).KeepWarmSilently()));
        Assert.Single(home.Session.Inputs);                                                                // nothing was sent to the model
    }

    [Fact]
    public void A_chat_that_carries_a_mission_is_never_between_tasks_while_the_mission_is_not_done()
    {
        using var plain = Rig();
        Assert.True(plain.H.Main.OnMain(() => ((IIgnisRuntimePort)plain.Runtime).IsBetweenTasks));
        using var mission = Rig(mission: Guid.NewGuid());
        Assert.False(mission.H.Main.OnMain(() => ((IIgnisRuntimePort)mission.Runtime).IsBetweenTasks));   // Dextra is not running it: unknown counts as mid-task
        Assert.False(mission.H.Main.OnMain(() => ((IIgnisRuntimePort)mission.Runtime).SendHiddenPing()));
        Assert.False(mission.H.Main.OnMain(() => ((IIgnisRuntimePort)mission.Runtime).Tidy(12)));
    }

    [Fact]
    public void What_dextra_makes_of_a_chat_is_read_from_the_chat_and_from_the_mission_the_controller_holds()
    {
        using var rig = Rig();
        var thread = rig.InitialThread;
        var mission = Guid.NewGuid();
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(IgnisAssistantUse.None, rig.App.AssistantUseOf(thread));
            Assert.Equal(IgnisAssistantUse.HomeThread, rig.App.AssistantUseOf(thread with { AssistantID = Guid.NewGuid() }));
            var missions = thread with { AssistantID = Guid.NewGuid(), MissionID = mission };
            Assert.Equal(IgnisAssistantUse.MissionMidTask, rig.App.AssistantUseOf(missions));             // a mission chat is never the home thread
        });
    }
}
