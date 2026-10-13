using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Notifications only for decisions", "through AppModel.Notify's pipeline"; ED-NEW-dextra-as9-1, -3, -4): the toasts
// as the app holds them. StartDextra binds them to the queue (the profile's name says who asks), the home in front silences them, turning Dextra off lets
// them go, and the real Windows channel reaches the toast center as an AssistantDecision request. A mission chat's own decision does not toast twice, and
// a mission ending is a status that never pushes; the owner's own chats toast as they always did.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-1")]
public sealed class DecisionToastWiringTests
{
    private static readonly Guid Mission = Guid.Parse("60000000-0000-4000-8000-000000000006");

    [Fact]
    public void Start_dextra_binds_the_toasts_to_its_queue_and_the_profile_names_who_asks()
    {
        using var rig = new DecisionToastAppRig();
        Assert.NotNull(rig.App.AssistantToasts);
        rig.Raise(DecisionToastRig.Approval("Run npm test?", "store"));
        rig.Pass(DecisionToasts.Together);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal("Store needs you", notice.Title);
        rig.Raise(DecisionToastRig.Approval("Reply to Sam?", "someone-unknown"));
        rig.Pass(DecisionToasts.Gap);
        Assert.Equal("Dextra needs you", rig.Channel.Sent[1].Title);        // an id no profile has is Dextra's own
    }

    [Fact]
    public void The_home_in_front_silences_the_toast_and_leaving_it_brings_it_back_for_the_next_card()
    {
        using var rig = new DecisionToastAppRig(active: true);
        rig.H.Main.OnMain(rig.App.ShowAssistantHome);
        rig.Raise(DecisionToastRig.Approval("seen on the home"));
        rig.Pass(DecisionToasts.Together);
        Assert.Empty(rig.Channel.Sent);
        rig.Active = false;                                                 // nth is no longer in front
        rig.Raise(DecisionToastRig.Approval("raised while away"));
        rig.Pass(DecisionToasts.Together);
        Assert.Equal("raised while away", Assert.Single(rig.Channel.Sent).Body);
    }

    [Fact]
    public void Turning_dextra_off_lets_the_toasts_go_and_takes_back_the_ones_still_up()
    {
        using var rig = new DecisionToastAppRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(DecisionToasts.Together);
        Assert.Single(rig.Channel.Sent);
        rig.H.Main.OnMain(rig.App.UnbindDextra);
        Assert.Null(rig.App.AssistantToasts);
        Assert.Equal([card.Id], rig.Channel.Withdrawn);
        rig.Raise(DecisionToastRig.Approval("after"));
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Single(rig.Channel.Sent);
    }

    [Fact]
    public void Without_an_override_the_real_windows_channel_posts_a_decision_request_and_asks_for_the_permission_once()
    {
        using var rig = new DecisionToastAppRig(fakeChannel: false);
        rig.Raise(DecisionToastRig.Approval("Run npm test?"));
        rig.Pass(DecisionToasts.Together);
        rig.H.Main.Loop.RunUntilIdle();
        var request = Assert.Single(rig.Center.Requests);
        Assert.Equal(ToastKind.AssistantDecision, request.Kind);
        Assert.Equal(["Allow once", "Deny", "Open"], request.Buttons.Select(b => b.Content));
        Assert.Null(request.Logo);                                          // a recording center is not the shell: nothing is written to disk
        Assert.Equal(1, rig.Center.Permissions);
        rig.Raise(DecisionToastRig.Approval("Run npm build?"));
        rig.Pass(DecisionToasts.Gap);
        Assert.Equal(1, rig.Center.Permissions);
        Assert.Equal(2, rig.Center.Requests.Count);
    }

    [Fact]
    public void A_toast_the_shell_will_not_take_is_said_once_on_the_board_and_never_breaks_the_card()
    {
        using var rig = new DecisionToastAppRig();
        rig.Channel.Throws = new InvalidOperationException("the shell refused");
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(DecisionToasts.Together);
        Assert.Equal(["toast.send.failed InvalidOperationException"], faults);
        Assert.Null(rig.H.Main.OnMain(() => rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mission_chats_need_for_a_decision_is_said_by_its_card_not_by_the_chats_own_toast(bool mission)
    {
        using var rig = new DecisionToastAppRig(thread => mission ? thread with { MissionID = Mission } : thread);
        rig.H.Main.OnMain(() => rig.App.ThreadNeedsAttention(rig.Thread.Id));
        rig.H.Main.Loop.RunUntilIdle();
        if (mission) Assert.Empty(rig.Center.Requests);
        else Assert.Equal("Waiting for your decision.", Assert.Single(rig.Center.Requests).Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mission_ending_is_a_status_that_never_pushes_while_the_owners_own_chat_ending_still_does(bool mission)
    {
        using var rig = new DecisionToastAppRig(thread => mission ? thread with { MissionID = Mission } : thread);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.NotifyWhenFinished = true;
            rig.App.Settings.ChimeWhenFinished = false;
            rig.App.TurnFinished(rig.Thread.Id, TurnStatus.Completed, continues: false);
        });
        rig.H.Main.Loop.RunUntilIdle();
        if (mission) Assert.Empty(rig.Center.Requests);
        else Assert.Equal("Finished.", Assert.Single(rig.Center.Requests).Body);
    }

    [Fact]
    public void With_no_dextra_a_mission_marked_chat_keeps_its_old_toast()
    {
        using var rig = new DecisionToastAppRig(thread => thread with { MissionID = Mission });
        rig.H.Main.OnMain(rig.App.UnbindDextra);
        rig.H.Main.OnMain(() => rig.App.ThreadNeedsAttention(rig.Thread.Id));
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal("Waiting for your decision.", Assert.Single(rig.Center.Requests).Body);
    }
}
