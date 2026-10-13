// PORT-ONLY: OD-0120. No owner, dismissal or cancellation can grant consent.
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

public sealed class ConversationTransferPrompt(Func<Avalonia.Controls.Window?>? owner = null) : IConversationTransferPrompt
{
    public async Task<ConversationTransferAnswer> ConfirmAsync(ConversationTransferQuestion question, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var answer = new TaskCompletionSource<ConversationTransferAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        Avalonia.Controls.Window? window = null;
        using var cancellation = ct.Register(() => MainActor.Post(() =>
        {
            answer.TrySetCanceled(ct);
            window?.Close();
        }));
        MainActor.Post(() =>
        {
            if (ct.IsCancellationRequested) { answer.TrySetCanceled(ct); return; }
            try
            {
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var parent = owner?.Invoke() ?? desktop?.Windows.FirstOrDefault(candidate => candidate.IsActive) ?? desktop?.MainWindow
                    ?? throw new InvalidOperationException("Conversation transfer confirmation requires an owner window.");
                window = new Avalonia.Controls.Window
                {
                    Title = "Send this conversation?", SizeToContent = SizeToContent.WidthAndHeight,
                    CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                window.Content = new ConversationTransferDialog(question, result =>
                {
                    if (ct.IsCancellationRequested) answer.TrySetCanceled(ct);
                    else answer.TrySetResult(result);
                    window.Close();
                });
                // ED-NEW-textsize-1: drawn at the app zoom of the window it is asked over.
                if (window.Content is Control content)
                {
                    window.Content = Marrow.UI.Common.AppZoom.Zoomed(content, Marrow.UI.Common.AppZoom.ScaleOfWindow(parent));
                    // ED-NEW-perf-motion-12: a zoom of the window it is asked over that changes while it shows reaches it on every
                    // frame of the glide, the window sized to its content about its centre.
                    _ = Marrow.UI.Common.WindowZoom.Follow(window, content, parent, Marrow.UI.Common.WindowZoom.KeepingCentre(window));
                }
                window.Closed += (_, _) => answer.TrySetResult(new(false));
                _ = window.ShowDialog(parent);
            }
            catch (Exception error) { answer.TrySetException(error); }
        });
        return await answer.Task.WaitAsync(ct);
    }
}
