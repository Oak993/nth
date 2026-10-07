// PORT-ONLY: MA-3b-11. The timeline owns one stable slot; no card means no extent, including its above spacing.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Cards;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Cards;

internal sealed class MultiAccountCardRows : Decorator
{
    private readonly Guid thread;
    private readonly ICardPort port;
    private readonly Func<IReadOnlyList<ThreadAccount>> accounts;
    private readonly double above;
    private readonly CrossfadeSlot slot = new();
    private DecisionRequest? request;
    internal LimitCardView? Card { get; private set; }
    private MultiAccountCardRows(Guid thread, ICardPort port, Func<IReadOnlyList<ThreadAccount>> accounts, double above)
    {
        this.thread = thread; this.port = port; this.accounts = accounts; this.above = above; Child = slot;
        AttachedToVisualTree += (_, _) => { port.Changed += Changed; Apply(port.State(thread)); };
        DetachedFromVisualTree += (_, _) => port.Changed -= Changed;
    }
    internal static MultiAccountCardRows Append(Panel body, Guid thread, ICardPort port, Func<IReadOnlyList<ThreadAccount>> accounts, double above)
    {
        var host = new MultiAccountCardRows(thread, port, accounts, above); body.Children.Add(host); return host;
    }
    internal static bool Append(Panel body, ThreadRuntime runtime, double above)
    {
        var seams = ThreadPortSeams.Offer.Current; if (seams.Cards is not { } cards) return false;
        Append(body, runtime.ThreadID, cards, () => SwiftEnum<ProviderKind>.AllCases.SelectMany(service => seams.Account(runtime.ThreadID, service)?.Accounts ?? []).ToArray(), above); return true;
    }
    private void Changed(object? sender, HandshakeEvent state)
    {
        if (state.ThreadId != thread) return;
        if (Dispatcher.UIThread.CheckAccess()) Apply(state); else Dispatcher.UIThread.Post(() => Apply(state));
    }
    private void Apply(HandshakeEvent? state)
    {
        if (state?.Request is not { } next) return;
        if (request == next) return;
        request = next; var model = new LimitCardModel(state, accounts(), port); Card = new LimitCardView(model); Card.Answered += () => Show(null);
        Show(Card);
    }
    private void Show(Control? control)
    {
        slot.Show(control ?? new Border(), FrameClock.IsBound ? Animation.EaseOut(duration: CapsuleMotion.Reduce(this) ? .01 : .22) : null);
        Margin = control is null ? default : new Thickness(0, above, 0, 0);
    }
}
