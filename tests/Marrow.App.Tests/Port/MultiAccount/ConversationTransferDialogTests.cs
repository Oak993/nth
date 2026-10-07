using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Tests.Port.MultiAccount;

[Collection("Headless")]
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationTransferDialogTests(HeadlessSession headless)
{
    [Fact]
    public void Dont_ask_again_is_unchecked_in_each_new_dialog_and_only_an_explicit_send_accepts() => headless.OnUiThread(() =>
    {
        var answers = new List<ConversationTransferAnswer>();
        var question = new ConversationTransferQuestion(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex]);
        var dialog = new ConversationTransferDialog(question, answers.Add);
        Assert.False(dialog.RememberBox.IsChecked);
        Assert.Equal("Don't ask again", dialog.RememberBox.Content);
        Assert.Empty(answers);
        dialog.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new ConversationTransferAnswer(true, false), Assert.Single(answers));
        var second = new ConversationTransferDialog(question, answers.Add);
        Assert.False(second.RememberBox.IsChecked);
        second.RememberBox.IsChecked = true;
        second.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new ConversationTransferAnswer(true, true), answers[1]);
        Assert.False(new ConversationTransferDialog(question, answers.Add).RememberBox.IsChecked);
    });

    [Fact]
    public void Cancel_never_accepts_or_remembers_a_checked_box_and_double_actions_have_no_second_effect() => headless.OnUiThread(() =>
    {
        var answers = new List<ConversationTransferAnswer>();
        var question = new ConversationTransferQuestion(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local]);
        var dialog = new ConversationTransferDialog(question, answers.Add);
        dialog.RememberBox.IsChecked = true;
        dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new ConversationTransferAnswer(false, false), Assert.Single(answers));
        Assert.True(dialog.CancelButton.IsCancel);
        Assert.False(dialog.SendButton.IsDefault);
    });
}
