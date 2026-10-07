using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Cards;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Cards;

// PORT-ONLY: MA-3b-11, locked card layout A :2925-2942; OD-0116 three equal choices.
[Collection("Headless")]
public sealed class FirstTimeCardTests(HeadlessSession headless)
{
    internal static (AccountsRig Rig, FakeThreadAccountPort Accounts, FakeCardPort Cards, HandshakeEvent State) Make(bool first = true, params DecisionKind[] kinds)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var accounts = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort();
        var request = new DecisionRequest(accounts.ThreadId, kinds.Length == 0 ? [DecisionKind.OnLimit] : kinds, first, a.Id.ToString("D"), b.Id.ToString("D"), new DateTimeOffset(2026, 10, 7, 15, 40, 0, TimeSpan.Zero));
        var state = new HandshakeEvent(accounts.ThreadId, HandshakeEventKind.DecisionRequested, HandshakePhase.Idle, HandshakePhase.Asking, request.OutgoingAccountId, request.IncomingAccountId, HandshakeReason.HardLimit, DateTimeOffset.UnixEpoch, Request: request);
        cards.Raise(state); return (rig, accounts, cards, state);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Three_equal_choices_no_selection_and_keyboard_remember_send_one_answer()
    {
        var f = Make(); var model = new LimitCardModel(f.State, f.Accounts.Accounts, f.Cards, TimeZoneInfo.Utc); LimitCardView? view = null;
        MultiAccountHarness.Run(headless, f.Rig, () => view = new LimitCardView(model), h =>
        {
            Assert.Contains("Claude Max 1 ran out. How should Marrow handle this?", h.Words());
            Assert.Equal(new[] { "Keep me going", "Ask me each time", "Just wait" }, view!.Actions.Select(b => ((TextBlock)b.Content!).Text));
            Assert.All(view.Actions, b => Assert.DoesNotContain("primary", b.Classes));
            Assert.Equal(3, view.Actions.Count); Assert.Equal(RememberScope.JustThisTime, model.Scope);
            view.Remember.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); h.Settle();
            Assert.Equal(RememberScope.ThisThread, model.Scope);
            Assert.True(h.Press("limit-action-ask")); Assert.False(h.Press("limit-action-ask"));
            var answer = Assert.Single(f.Cards.Answers); Assert.Equal(RememberScope.ThisThread, answer.Scope);
            Assert.Equal("ask", Assert.Single(answer.Choices).Value); Assert.All(view.Actions, b => Assert.False(b.IsEnabled));
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Bring_has_no_first_time_pick_and_move_waits_for_explicit_choices()
    {
        var f = Make(true, DecisionKind.OnLimit, DecisionKind.Bring, DecisionKind.OtherService); var model = new LimitCardModel(f.State, f.Accounts.Accounts, f.Cards); LimitCardView? view = null;
        MultiAccountHarness.Run(headless, f.Rig, () => view = new LimitCardView(model), h =>
        {
            Assert.All(view!.Options.Values, group => Assert.Empty(group.SelectedId));
            Assert.False(h.Press("limit-action-switch")); Assert.Empty(f.Cards.Answers);
            view.Options[DecisionKind.Bring].Buttons.Single(b => b.Id == "full").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            view.Options[DecisionKind.OtherService].Buttons.Single(b => b.Id == "switch").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); h.Settle();
            Assert.True(h.Press("limit-action-switch")); Assert.Equal(3, Assert.Single(f.Cards.Answers).Choices.Count);
        });
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Timeline_slot_is_after_rows_before_sentinel_and_empty_without_a_card(bool reduced)
    {
        var f = Make(); f.Cards = new FakeCardPort(); MultiAccountCardRows? host = null; StackPanel body = null!; Border rows = null!; Border sentinel = null!;
        MultiAccountHarness.Run(headless, f.Rig, () => { body = new StackPanel(); rows = new Border(); sentinel = new Border { Height = 1 }; body.Children.Add(rows); host = MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12); body.Children.Add(sentinel); return body; }, h =>
        {
            Assert.Equal(new Control[] { rows, host!, sentinel }, body.Children); Assert.Equal(0, host!.Bounds.Height);
            f.Cards.Raise(f.State); h.Settle(); Assert.NotNull(host.Card); Assert.True(host.Bounds.Height > 0);
            Assert.True(h.Press("limit-action-ask")); h.Settle(); Assert.Equal(0, host.Bounds.Height);
            Assert.Null(host.RenderTransform);
        }, reduceMotion: reduced);
    }
}
