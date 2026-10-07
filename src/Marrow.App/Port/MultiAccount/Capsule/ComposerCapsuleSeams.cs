// PORT-ONLY: multi-account step 3a. Where the composer gets its capsule slot: unbound (no slot) until Program.Main binds the running app, so a test of another view never builds the app's accounts (the pattern of MultiAccountSettingsSeams).
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal static class ComposerCapsuleSeams
{
    /// <summary>Makes the slot for a thread's composer; null: the composer has none.</summary>
    public static Ambient<Func<ThreadRuntime, ComposerCapsuleSlot?>> Offer { get; } = new(_ => null);

    public static void Bind() => Offer.SetProcessDefault(For);

    internal static ComposerCapsuleSlot? For(ThreadRuntime runtime)
    {
        if (runtime.App is not { } app) return null;
        var settings = app.Settings;
        var actions = new AppAccountActions(app);
        var model = new ComposerCapsuleModel(settings.Accounts, settings.Pills, app.IsAccountConnected, id => actions.Usage(id));
        CapsuleSubject? Subject()
        {
            try
            {
                if (runtime.Thread is not { } thread) return null;
                return new CapsuleSubject(thread.Provider, runtime.HandshakeAccountId, thread.PillRelay, false, null);
            }
            catch (InvalidOperationException) { return null; } // no account is available for this conversation
        }
        var slot = new ComposerCapsuleSlot(model, settings.CapsuleStyle, Subject, settings.Pills);
        // the slot reads the thread again when the account it runs on, its relay, its state or its turns change (usage is reported around turns)
        slot.Track(() => (runtime.Thread?.ProviderAccountID, runtime.Thread?.PillRelay, runtime.Phase, runtime.Turns.Count), _ => slot.Refresh());
        var ports = ThreadPortSeams.Offer.Current;
        Observe(slot, runtime.ThreadID, ports.Cards, runtime.Thread is { } current ? ports.Account(runtime.ThreadID, current.Provider) : null);
        return slot;
    }

    internal static void Observe(ComposerCapsuleSlot slot, Guid thread, ICardPort? cards, IThreadAccountPort? account)
    {
        ContinuousInstant? readyAt = null;
        Action<TimeSpan>? frame = null;
        IFrameSource? frames = null;
        void CancelFrame()
        {
            if (frame is not null) frames?.CancelFrame(frame);
            frame = null; frames = null;
        }
        void FinishTick(TimeSpan ignored)
        {
            frame = null;
            if (readyAt is not { } at || SwiftClock.Now - at >= TimeSpan.FromMilliseconds(800))
            { readyAt = null; slot.SetInSync(false); frames = null; return; }
            frame = FinishTick; frames!.RequestFrame(frame);
        }
        void Refresh() => slot.Refresh();
        void Apply(HandshakeEvent? state)
        {
            var running = state?.To is HandshakePhase.Standby or HandshakePhase.Linking or HandshakePhase.InSync;
            slot.SetHandshake(running, Guid.TryParse(state?.IncomingAccountId, out var incoming) ? incoming : null);
            // Injected Codex/Pi readiness and primed Claude readiness use the very same engine event.
            if (state?.Kind == HandshakeEventKind.InSyncReady)
            { CancelFrame(); readyAt = SwiftClock.Now; slot.SetInSync(true); }
            else if (readyAt is not null && (state?.To == HandshakePhase.Switch
                || state is { From: HandshakePhase.Switch, To: HandshakePhase.Idle }))
            {
                // A verified history can switch within one actor turn. Keep its tick drawable for
                // the page's 800 ms sync span, with the same frame/clock seams as the capsule.
                if (state.To == HandshakePhase.Idle && frame is null && FrameClock.IsBound)
                { frames = FrameClock.Source.Current; frame = FinishTick; frames.RequestFrame(frame); }
            }
            else if (!running || state?.To == HandshakePhase.Standby)
            { CancelFrame(); readyAt = null; slot.SetInSync(false); }
        }
        void Changed(object? sender, HandshakeEvent state)
        {
            if (state.ThreadId == thread) Apply(state);
        }
        slot.AttachedToVisualTree += (_, _) =>
        {
            if (account is not null) account.Changed += Refresh;
            if (cards is not null) { cards.Changed += Changed; Apply(cards.State(thread)); }
            slot.Refresh();
        };
        slot.DetachedFromVisualTree += (_, _) =>
        {
            CancelFrame(); readyAt = null;
            if (account is not null) account.Changed -= Refresh;
            if (cards is not null) cards.Changed -= Changed;
        };
    }
}
