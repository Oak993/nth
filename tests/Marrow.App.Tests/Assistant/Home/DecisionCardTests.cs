using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 4.2 "Keyboard" and its answers table, 12.2 the structured grant, 16 "Enter never sends", "focus never
// lands on an irreversible button"; ED-NEW-dextra-as8-6): an irreversible card takes no key and acts only on a click or a held Space;
// a reversible card's answers have number keys; Always opens a grant filled from the card; focus after a card closes goes to a card.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-6")]
public sealed class DecisionCardTests(HeadlessSession headless)
{
    [Fact]
    public void Enter_and_a_tap_of_Space_never_answer_an_irreversible_card_and_a_held_Space_does()
    {
        headless.OnUiThread(() =>
        {
            using var hints = HoldToActButton.HintPresenter.Override(static (_, _) => { });
            var decisions = new FakeDecisions();
            var item = Irreversible("Send the reply to Sam");
            decisions.Add(item);
            var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
            using var stage = new HomeStage(list, 600);
            var card = Assert.Single(list.Cards);
            Assert.Equal(["Send", "Don't send"], card.AnswerButtons.Select(b => AutomationProperties.GetName(b)));
            var send = Assert.IsType<HoldToActButton>(card.AnswerButtons[0]);

            card.Target.Focus(NavigationMethod.Tab);
            stage.Press(Key.D1);
            stage.Press(Key.Enter);
            Assert.Empty(decisions.Answers);

            send.Focus(NavigationMethod.Tab);
            stage.Press(Key.Enter);
            stage.Down(Key.Space);
            stage.Advance(TimeSpan.FromMilliseconds(300));
            stage.Up(Key.Space);
            Assert.Empty(decisions.Answers);

            stage.Down(Key.Space);
            stage.Advance(HoldToActButton.HoldFor + TimeSpan.FromMilliseconds(10));
            var answer = Assert.Single(decisions.Answers);
            Assert.Equal((item.Id, DecisionAnswerKind.DoIt), (answer.Id, answer.Answer.Kind));
        });
    }

    [Fact]
    public void Number_keys_answer_a_reversible_card_in_its_order()
    {
        headless.OnUiThread(() =>
        {
            using var hints = HoldToActButton.HintPresenter.Override(static (_, _) => { });
            var decisions = new FakeDecisions();
            var item = Reversible("Sign in to the shop's dashboard");
            decisions.Add(item);
            var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
            using var stage = new HomeStage(list, 600);
            var card = Assert.Single(list.Cards);
            Assert.Equal(["Allow once", "Always…", "Deny"], card.AnswerButtons.Select(b => AutomationProperties.GetName(b)));
            Assert.DoesNotContain(card.AnswerButtons, b => b is HoldToActButton);

            card.Target.Focus(NavigationMethod.Tab);
            stage.Press(Key.D3);
            var answer = Assert.Single(decisions.Answers);
            Assert.Equal(DecisionAnswerKind.Deny, answer.Answer.Kind);
        });
    }

    [Fact]
    public void When_a_card_closes_focus_goes_to_the_next_card_never_to_an_irreversible_button()
    {
        headless.OnUiThread(() =>
        {
            using var hints = HoldToActButton.HintPresenter.Override(static (_, _) => { });
            var decisions = new FakeDecisions();
            var first = Irreversible("Send the reply to Sam");
            var second = Irreversible("Pay the invoice");
            decisions.Add(first, second);
            var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
            using var stage = new HomeStage(list, 600);
            Assert.Equal(2, list.Cards.Count);
            Assert.Equal("2 decisions", list.BatchText);

            list.Cards[0].Target.Focus(NavigationMethod.Tab);
            decisions.Answer(first.Id, DecisionAnswer.Dont());
            stage.Layout();
            var card = Assert.Single(list.Cards);
            Assert.Same(card.Target, stage.Focused);
            Assert.IsNotType<HoldToActButton>(stage.Focused);

            decisions.Answer(second.Id, DecisionAnswer.Dont());
            stage.Layout();
            Assert.Empty(list.Cards);
            Assert.Same(list.Empty, stage.Focused);
            Assert.Equal("Nothing waiting on you.", list.Empty.Text);
        });
    }

    [Fact]
    public void Always_opens_a_grant_filled_from_the_card_and_saving_answers_with_it()
    {
        headless.OnUiThread(() =>
        {
            using var hints = HoldToActButton.HintPresenter.Override(static (_, _) => { });
            var decisions = new FakeDecisions();
            var mission = Guid.NewGuid();
            var item = Reversible("Sign in to the shop's dashboard", mission);
            decisions.Add(item);
            using var stage = new HomeStage(new Border(), 600);
            var list = new DecisionList(decisions, new DecisionContext(decisions.Answer, MissionTitle: _ => "Espresso machines under $800", Clock: stage.Clock));
            stage.Window.Content = list;
            stage.Layout();
            var card = Assert.Single(list.Cards);
            Assert.Equal(["Allow once", "For this mission", "Always…", "Deny"], card.AnswerButtons.Select(b => AutomationProperties.GetName(b)));

            Click(card.AnswerButtons[2]);
            stage.Layout();
            Assert.True(card.ShowsGrant);
            var texts = Texts(card);
            Assert.Contains("Dextra", texts);
            Assert.Contains("Sign in", texts);
            Assert.Contains("https://shop.example", texts);
            Assert.Contains("Sending, paying or deleting: no grant can cover them", texts);
            Assert.Equal("Save grant", AutomationProperties.GetName(stage.Focused!));

            card.Target.Focus(NavigationMethod.Tab);
            stage.Press(Key.D1); // number keys sleep while the grant is open
            Assert.Empty(decisions.Answers);

            Click((Button)Nodes<Button>(card).First(b => AutomationProperties.GetName(b) == "Save grant"));
            var answer = Assert.Single(decisions.Answers);
            Assert.Equal(DecisionAnswerKind.Always, answer.Answer.Kind);
            var grant = Assert.IsType<Grant>(answer.Answer.Grant);
            Assert.Equal(ActionClass.Login, grant.Class);
            Assert.Equal("dextra", grant.AssistantId);
            Assert.Equal(item.Id, grant.FromDecision);
            Assert.Equal(GrantScope.Standing, grant.Scope);
            Assert.Equal(Now, grant.Created);
        });
    }
}
