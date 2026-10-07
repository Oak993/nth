using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;

namespace Marrow.App.Tests.Views.MultiAccount.Cards;

// PORT-ONLY: MA-3b-14, checklist 7 / locked page :4224-4226.
[Collection("Headless")]
public sealed class MidReplyCardTests(HeadlessSession headless)
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData("finish")] [InlineData("stop")]
    public void Exact_mid_reply_words_and_keyboard_answer_do_not_repeat_while_pending(string action)
    {
        var f = FirstTimeCardTests.Make(false, DecisionKind.MidReply); var hold = new TaskCompletionSource(); f.Cards.AnswerHold = hold.Task; MultiAccountCardRows? host = null;
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); host = MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12, new MultiAccountCardContext(null, null, TimeZoneInfo.Utc)); return body; }, h =>
        {
            Assert.Contains("Claude Max 1 reached its 5-hour limit while writing this reply · resets 3:40 PM", h.Words());
            var explanation = h.All().OfType<TextBlock>().Single(t => t.Text == "Either way, the next account reads the whole conversation once before going on, which uses a little of its limit. Finish now picks up from the last finished step (a half-done step may run again). Stop waits for your next message.");
            Assert.Equal(12, explanation.FontSize); Assert.Equal(17, explanation.LineHeight);
            Assert.Contains("Finish now on Claude Max 2", h.Words()); Assert.Contains("Stop, use it from my next message", h.Words());
            var button = h.Find<Button>("limit-action-" + action); button.Focus();
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); h.Settle();
            Assert.Empty(f.Cards.Answers); Assert.All(host!.Card!.Actions, b => Assert.False(b.IsEnabled));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); hold.SetResult(); h.Until(() => f.Cards.Answers.Count == 1);
            Assert.Equal(action, Assert.Single(Assert.Single(f.Cards.Answers).Choices).Value);
        });
    }
}
