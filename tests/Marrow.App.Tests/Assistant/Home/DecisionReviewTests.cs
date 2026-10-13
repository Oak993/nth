using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Review opens the card", 4.2 "Keyboard" (no irreversible button ever gets focus), 16.1 Native gate "Review opens
// the card"; ED-NEW-dextra-as9-5): a toast's Review opens the home on its Decisions tab and brings the card in front: the card itself takes the focus,
// never one of its buttons, even when it is an irreversible one. A request made before the home is built is taken up when it is built; a request for a
// card that is gone still shows the tab; with Dextra off there is no board and the request is let go.
[Collection("Headless")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-5")]
public sealed class DecisionReviewTests(HeadlessSession headless)
{
    private static object? Focused(HomeStage stage) => stage.Window.FocusManager?.GetFocusedElement();

    [Fact]
    public void Reveal_brings_the_card_in_front_and_focus_goes_to_the_card_itself() => headless.OnUiThread(() =>
    {
        var decisions = new FakeDecisions();
        var first = Reversible("Sign in to the shop's dashboard");
        var wanted = Reversible("Run npm publish");
        decisions.Add(first);
        decisions.Add(wanted);
        var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
        using var stage = new HomeStage(list, 600);
        Assert.True(list.Reveal(wanted.Id));
        stage.Layout();
        var card = list.Cards.Single(c => c.Item.Id == wanted.Id);
        Assert.Same(card.Target, Focused(stage));
        Assert.Empty(decisions.Answers);
    });

    [Fact]
    public void An_irreversible_card_is_revealed_with_focus_on_the_card_and_never_on_its_send_button() => headless.OnUiThread(() =>
    {
        using var hints = HoldToActButton.HintPresenter.Override(static (_, _) => { });
        var decisions = new FakeDecisions();
        var send = Irreversible("Send the reply to Sam");
        decisions.Add(send);
        var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
        using var stage = new HomeStage(list, 600);
        Assert.True(list.Reveal(send.Id));
        stage.Layout();
        var card = Assert.Single(list.Cards);
        Assert.Same(card.Target, Focused(stage));
        Assert.DoesNotContain(card.AnswerButtons, b => ReferenceEquals(b, Focused(stage)));
        stage.Press(Key.Enter);
        Assert.Empty(decisions.Answers);                                    // Enter on the revealed card sends nothing
    });

    [Fact]
    public void A_card_that_is_gone_is_not_found_and_changes_nothing() => headless.OnUiThread(() =>
    {
        var decisions = new FakeDecisions();
        var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
        using var stage = new HomeStage(list, 600);
        Assert.False(list.Reveal(Guid.NewGuid()));
        stage.Layout();
        Assert.Empty(list.Cards);
    });

    [Fact]
    public void A_card_raised_a_moment_ago_is_found_without_waiting_for_the_next_refresh() => headless.OnUiThread(() =>
    {
        var decisions = new FakeDecisions();
        var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
        using var stage = new HomeStage(list, 600);
        var late = Reversible("Raised just now");
        decisions.Add(late);
        Assert.True(list.Reveal(late.Id));
        stage.Layout();
        Assert.Same(Assert.Single(list.Cards).Target, Focused(stage));
    });

    [Fact]
    public void A_review_asked_of_a_built_home_picks_the_decisions_tab_and_the_request_is_taken() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        Assert.NotNull(host.Home);
        Assert.Equal(BoardTab.Missions, host.Home!.Board.Tabs.Selected);
        var ask = Guid.NewGuid();
        h.Main.OnMain(() => model.ReviewAssistantDecision(ask));
        stage.Layout();
        Assert.Equal(BoardTab.Decisions, host.Home.Board.Tabs.Selected);
        Assert.Null(model.PendingAssistantReview);
        Assert.True(model.AssistantHomeShown);
    });

    [Fact]
    public void A_review_asked_before_the_home_exists_is_taken_up_when_it_is_built() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var ask = Guid.NewGuid();
        h.Main.OnMain(() => model.ReviewAssistantDecision(ask));
        Assert.Equal(ask, model.PendingAssistantReview);
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        stage.Layout();
        Assert.Equal(BoardTab.Decisions, host.Home!.Board.Tabs.Selected);
        Assert.Null(model.PendingAssistantReview);
    });

    [Fact]
    public void With_dextra_off_there_is_no_board_and_the_request_is_let_go() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        h.Main.OnMain(() => model.AssistantHost.Settings.Update(p => p with { On = false }));
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        Assert.Null(host.Home);
        h.Main.OnMain(() => model.ReviewAssistantDecision(Guid.NewGuid()));
        stage.Layout();
        Assert.Null(model.PendingAssistantReview);
        Assert.Null(host.Home);
    });

    [Fact]
    public void A_host_that_left_the_window_no_longer_hears_the_request() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var host = new AssistantHomeHost(model);
        using (new HomeStage(host, 1200)) { }
        h.Main.OnMain(() => model.ReviewAssistantDecision(Guid.NewGuid()));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(model.PendingAssistantReview);                       // nobody took it up: it waits for the next home
    });
}
