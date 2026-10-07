using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Tests.Port.MultiAccount;

[Collection("Headless")]
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationTransferPromptTests(HeadlessSession headless)
{
    [Fact]
    public void A_real_modal_prompt_defaults_to_no_and_a_closed_window_refuses_the_transfer() => headless.OnUiThread(() =>
    {
        var owner = new Avalonia.Controls.Window(); owner.Show();
        try
        {
            var prompt = new ConversationTransferPrompt(() => owner);
            var task = prompt.ConfirmAsync(new(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local]), CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            var window = Assert.Single(owner.OwnedWindows);
            var dialog = Assert.IsType<ConversationTransferDialog>(window.Content);
            Assert.False(dialog.RememberBox.IsChecked);
            Assert.False(task.IsCompleted);
            window.Close();
            HeadlessSession.RunDispatcherUntil(task, TimeSpan.FromSeconds(10));
            Assert.Equal(new(false, false), task.GetAwaiter().GetResult());
        }
        finally { owner.Close(); }
    });

    [Fact]
    public void Cancelling_a_visible_prompt_closes_it_and_a_late_send_cannot_accept() => headless.OnUiThread(() =>
    {
        var owner = new Avalonia.Controls.Window(); owner.Show();
        using var ct = new CancellationTokenSource();
        try
        {
            var prompt = new ConversationTransferPrompt(() => owner);
            var task = prompt.ConfirmAsync(new(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Deepseek]), ct.Token);
            Dispatcher.UIThread.RunJobs();
            var window = Assert.Single(owner.OwnedWindows);
            var dialog = Assert.IsType<ConversationTransferDialog>(window.Content);
            ct.Cancel(); Dispatcher.UIThread.RunJobs();
            dialog.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessSession.RunDispatcherUntil(task, TimeSpan.FromSeconds(10));
            Assert.True(task.IsCanceled);
            Assert.Empty(owner.OwnedWindows);
        }
        finally { owner.Close(); }
    });
}
