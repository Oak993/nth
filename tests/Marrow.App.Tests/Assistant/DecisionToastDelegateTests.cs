using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Toast buttons", 4.2 "Keyboard" (Review always shows the full card); the Native gate "Review opens the card";
// ED-NEW-dextra-as9-5): a press on a decision toast reaches the running app through the same activation path as a chat toast. A button that answers its
// card does so quietly: no window, no focus. Every other press brings nth forward in the order a chat toast does (main window, then the state, then
// activate) and asks the home to show the card; a stale or forged press only opens it.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-5")]
public sealed class DecisionToastDelegateTests
{
    private static (DecisionItem Card, DecisionNotice Notice) Shown(DecisionToastAppRig rig, DecisionItem? card = null)
    {
        var raised = rig.Raise(card ?? DecisionToastRig.Approval());
        rig.Pass(DecisionToasts.Together);
        return (raised, Assert.Single(rig.Channel.Sent));
    }

    [Fact]
    public void Allow_once_answers_the_card_without_bringing_nth_forward()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        rig.Press(DecisionToastAppRig.Info(card.Id, NoticeAction.AllowOnce, notice.Nonce));
        var (answered, answer) = Assert.Single(rig.Answers);
        Assert.Equal(card.Id, answered.Id);
        Assert.Equal(DecisionAnswer.AllowOnce, answer);
        Assert.Empty(rig.Windows.Events);
        Assert.False(rig.H.Main.OnMain(() => rig.App.AssistantHomeShown));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
        Assert.Equal([card.Id], rig.Channel.Withdrawn);
    }

    [Fact]
    public void Deny_answers_the_card_deny_quietly()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        rig.Press(DecisionToastAppRig.Info(card.Id, NoticeAction.Deny, notice.Nonce));
        Assert.Equal(DecisionAnswerKind.Deny, Assert.Single(rig.Answers).Answer.Kind);
        Assert.Empty(rig.Windows.Events);
    }

    [Theory]
    [InlineData(NoticeAction.Open)]
    [InlineData(NoticeAction.Review)]
    public void Open_and_review_bring_nth_forward_open_the_home_and_name_the_card_and_answer_nothing(NoticeAction action)
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        var order = new List<string>();
        rig.Windows.Events.Clear();
        using var scope = ToastActivationWindows.Current.Override(new RecordingToastWindows(e => order.Add(e + ":" + rig.App.AssistantHomeShown)));
        rig.Press(DecisionToastAppRig.Info(card.Id, action, notice.Nonce));
        Assert.Equal(["main:False", "activate:True"], order);                  // the window first, then the state, then activate (as a chat toast does)
        Assert.Equal(card.Id, rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionState.Open, rig.Controller.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public void A_wrong_nonce_on_a_button_only_opens_the_card()
    {
        using var rig = new DecisionToastAppRig();
        var (card, _) = Shown(rig);
        rig.Press(DecisionToastAppRig.Info(card.Id, NoticeAction.AllowOnce, "0123456789abcdef0123456789abcdef"));
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Empty(rig.Answers);
        Assert.Equal(card.Id, rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
    }

    [Fact]
    public void An_irreversible_cards_toast_never_answers_from_a_forged_allow_once()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig, DecisionToastRig.Approval("Send the update?", actionClass: ActionClass.Outward));
        Assert.Equal([NoticeAction.Review], notice.Actions);
        rig.Press(DecisionToastAppRig.Info(card.Id, NoticeAction.AllowOnce, notice.Nonce));
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionState.Open, rig.Controller.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public void A_toast_from_before_dextra_was_turned_off_opens_the_home_and_answers_nothing()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        rig.H.Main.OnMain(rig.App.UnbindDextra);
        rig.Press(DecisionToastAppRig.Info(card.Id, NoticeAction.AllowOnce, notice.Nonce));
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Empty(rig.Answers);
        Assert.True(rig.H.Main.OnMain(() => rig.App.AssistantHomeShown));
    }

    [Fact]
    public void A_payload_with_a_decision_that_is_not_an_id_is_an_ordinary_press_that_names_no_card()
    {
        using var rig = new DecisionToastAppRig();
        Shown(rig);
        rig.Press(new Dictionary<string, object> { ["assistantDecision"] = "not-an-id", ["action"] = "allowOnce", ["nonce"] = "x" });
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Empty(rig.Answers);
        Assert.Null(rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
        Assert.False(rig.H.Main.OnMain(() => rig.App.AssistantHomeShown));
    }

    [Fact]
    public void An_action_nth_does_not_know_is_a_review()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        rig.Press(new Dictionary<string, object> { ["assistantDecision"] = card.Id.ToString("D"), ["action"] = "send", ["nonce"] = notice.Nonce });
        rig.Press(new Dictionary<string, object> { ["assistantDecision"] = card.Id.ToString("D") });
        Assert.Equal(["main", "activate", "main", "activate"], rig.Windows.Events);
        Assert.Empty(rig.Answers);
    }

    [Fact]
    public void A_press_that_arrives_through_the_second_launch_pipe_acts_the_same_way()
    {
        using var rig = new DecisionToastAppRig();
        var (card, notice) = Shown(rig);
        var identity = new ToastBuildIdentity("nth.App", "nth-release");
        var transport = new RecordingToastTransport();
        Task<bool>? routed = null;
        using var listener = transport.Listen("isolated-instance", payload => routed = ToastSecondLaunch.Route(rig.Delegate, identity, payload));
        var uri = ToastActivation.Encode(identity, DecisionToastAppRig.Info(card.Id, NoticeAction.AllowOnce, notice.Nonce));
        rig.H.Main.OnMain(() => Assert.True(ToastSecondLaunch.Forward(transport, "isolated-instance", identity, [uri])));
        Assert.NotNull(routed);
        Assert.True(rig.H.Main.Loop.Run(() => routed, AppModelHarness.Bound));
        Assert.Equal(DecisionAnswerKind.AllowOnce, Assert.Single(rig.Answers).Answer.Kind);
        Assert.Empty(rig.Windows.Events);
    }

    [Fact]
    public void A_chat_toast_press_is_untouched()
    {
        using var rig = new DecisionToastAppRig();
        rig.Press(new Dictionary<string, object> { ["threadID"] = SwiftUUID.UuidString(rig.Thread.Id) });
        Assert.Equal(["main", "activate"], rig.Windows.Events);
        Assert.Equal(rig.Thread.Id, rig.H.Main.OnMain(() => rig.App.SelectedThreadID));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.PendingAssistantReview));
    }
}
