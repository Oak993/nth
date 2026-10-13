// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 13.1 "The NotificationChannel abstraction ships now with one implementation, Windows"; ED-NEW-dextra-as9-4,
// -6). No Swift twin.
// The one channel this release has: a decision notice becomes a Windows toast through nth's own toast center (the one chat toasts use, with its
// registration, its delegate's presentation gate and its protocol activation). The toast center is read at the moment of sending, so a test
// that binds a recording center sees every request.
using Marrow.App.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed class WindowsNotificationChannel(Func<string?>? logo = null, Action? requestPermission = null, Func<IToastCenter>? center = null) : NotificationChannel
{
    public override string Name => "Windows";

    public override async Task<bool> Send(DecisionNotice notice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        cancellationToken.ThrowIfCancellationRequested();
        requestPermission?.Invoke();
        var request = DecisionToastRequests.Build(notice, logo?.Invoke());
        await (center?.Invoke() ?? ToastNotifications.Center.Current).Add(request).ConfigureAwait(false);
        return true;
    }

    public override Task Withdraw(Guid target) => (center?.Invoke() ?? ToastNotifications.Center.Current).Remove(DecisionToastRequests.Identifier(target));
}
