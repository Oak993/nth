// PORT-ONLY: multi-account step 3a. Where the composer gets its capsule slot: unbound (no slot) until Program.Main binds the running app, so a test of another view never builds the app's accounts (the pattern of MultiAccountSettingsSeams).
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Models;
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
        return slot;
    }
}
