using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", "Toasts are suppressed while the Assistant home ... is on screen"; ED-NEW-dextra-as9-3, -5, -9): a duty's
// toast as the app holds it. The duties the app starts tell the owner through the decision toasts; the delegate checks the home once more right before the
// Windows call (DutyToastSurfaceTests); and a press on it brings nth forward in the order a chat toast does and asks the home to show the mission. It never
// answers anything. The app rig runs off the Avalonia thread: the first LoginEnvironment.Load() of a process deadlocks on it.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyToastDelegateTests
{
    private static readonly Guid Mission = Guid.Parse("b0000000-0000-4000-8000-000000000001");

    private static Dictionary<string, object> Info(string mission = "b0000000-0000-4000-8000-000000000001") => new(StringComparer.Ordinal)
    {
        [DecisionToastKeys.Mission] = mission,
        [DecisionToastKeys.Action] = "open",
    };

    [Fact]
    public void The_app_tells_a_finding_through_the_toasts_and_a_finding_with_dextra_off_is_let_go()
    {
        using var rig = new DecisionToastAppRig();
        var finding = new DutyFinding(Mission, "Espresso price", "The price fell to $640.");
        rig.H.Main.OnMain(() => rig.App.NotifyDutyFinding(finding));
        rig.Pass(DecisionToasts.Together);
        var notice = Assert.Single(rig.Channel.SentDuty);
        Assert.Equal(("Espresso price", "The price fell to $640.", Mission), (notice.Title, notice.Body, notice.Target));

        rig.H.Main.OnMain(rig.App.UnbindDextra);
        rig.H.Main.OnMain(() => rig.App.NotifyDutyFinding(finding));                // no toasts are bound: nothing to make, nothing thrown
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Single(rig.Channel.SentDuty);
    }

    [Fact]
    public void The_duties_the_app_starts_are_bound_to_the_toasts_so_a_run_that_found_something_reaches_the_owner()
    {
        using var rig = new DecisionToastAppRig();
        var host = rig.StartDuties();
        Assert.NotNull(host.Options.Notify);
        host.Options.Notify!(new DutyFinding(Mission, "Espresso price", "The price fell to $640."));
        rig.Pass(DecisionToasts.Together);
        Assert.Equal(Mission, Assert.Single(rig.Channel.SentDuty).Target);
    }

    [Fact]
    public void A_finding_is_not_shown_while_the_home_is_in_front()
    {
        using var rig = new DecisionToastAppRig(active: true);
        rig.H.Main.OnMain(rig.App.ShowAssistantHome);
        rig.H.Main.OnMain(() => rig.App.NotifyDutyFinding(new DutyFinding(Mission, "Espresso price", "The price fell to $640.")));
        rig.Pass(DecisionToasts.Together);
        Assert.Empty(rig.Channel.SentDuty);
    }

    [Fact]
    public void Without_an_override_the_real_windows_channel_posts_a_duty_request_with_no_picture_for_a_recording_center()
    {
        using var rig = new DecisionToastAppRig(fakeChannel: false);
        rig.H.Main.OnMain(() => rig.App.NotifyDutyFinding(new DutyFinding(Mission, "Espresso price", "The price fell to $640.")));
        rig.Pass(DecisionToasts.Together);
        rig.H.Main.Loop.RunUntilIdle();
        var request = Assert.Single(rig.Center.Requests);
        Assert.Equal(ToastKind.AssistantDuty, request.Kind);
        Assert.Equal(Mission, request.Mission);
        Assert.Equal(["Open"], request.Buttons.Select(b => b.Content));
        Assert.Null(request.Logo);
        Assert.Equal(1, rig.Center.Permissions);
    }

    [Fact]
    public void A_press_brings_nth_forward_in_a_chat_toasts_order_opens_the_home_and_names_the_mission_and_answers_nothing()
    {
        using var rig = new DecisionToastAppRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        var order = new List<string>();
        using var scope = ToastActivationWindows.Current.Override(new RecordingToastWindows(e => order.Add(e + ":" + rig.App.AssistantHomeShown)));
        rig.Press(Info());
        Assert.Equal(["main:False", "activate:True"], order);
        Assert.Equal(Mission, rig.H.Main.OnMain(() => rig.App.PendingAssistantMission));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionState.Open, rig.Controller.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public void A_press_of_a_toast_from_before_dextra_was_turned_off_still_opens_the_home()
    {
        using var rig = new DecisionToastAppRig();
        rig.H.Main.OnMain(rig.App.UnbindDextra);
        rig.Press(Info());
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.True(rig.H.Main.OnMain(() => rig.App.AssistantHomeShown));
    }

    [Fact]
    public void A_payload_whose_mission_is_not_an_id_is_an_ordinary_press_that_names_no_mission()
    {
        using var rig = new DecisionToastAppRig();
        rig.Press(Info("not-an-id"));
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Null(rig.H.Main.OnMain(() => rig.App.PendingAssistantMission));
        Assert.False(rig.H.Main.OnMain(() => rig.App.AssistantHomeShown));
    }

    [Fact]
    public void A_press_that_arrives_through_the_second_launch_pipe_acts_the_same_way()
    {
        using var rig = new DecisionToastAppRig();
        var identity = new ToastBuildIdentity("nth.App", "nth-release");
        var transport = new RecordingToastTransport();
        Task<bool>? routed = null;
        using var listener = transport.Listen("isolated-instance", payload => routed = ToastSecondLaunch.Route(rig.Delegate, identity, payload));
        var uri = ToastActivation.Encode(identity, Info());
        rig.H.Main.OnMain(() => Assert.True(ToastSecondLaunch.Forward(transport, "isolated-instance", identity, [uri])));
        Assert.NotNull(routed);
        Assert.True(rig.H.Main.Loop.Run(() => routed, AppModelHarness.Bound));
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Equal(Mission, rig.H.Main.OnMain(() => rig.App.PendingAssistantMission));
    }
}
