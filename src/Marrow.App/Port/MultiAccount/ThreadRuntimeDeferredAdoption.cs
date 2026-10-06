// PORT-ONLY: OD-0116, lead decision 2026-10-06 (ED-NEW-ma-engine-4). Adoption of an account whose session is not started: the thread is bound to it and its first real turn carries the context.
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    // Held in memory until the destination's first real turn is sent; a restart before that loses it, like a conversation the provider no longer has.
    private string? deferredHandoff;
    internal bool HasDeferredHandoff => deferredHandoff is not null;

    internal void AdoptDeferredAccount(SessionConfiguration configuration, string context, PillThreadState? relay = null, bool pinAccount = false)
    {
        var app = App ?? throw new InvalidOperationException("The conversation is unavailable.");
        var source = Thread ?? throw new InvalidOperationException("The conversation is unavailable.");
        if (!IsAccountSwitchSafe || OwnsAccountStepPause || configuration.AccountId is not { } accountId
            || app.Settings.Accounts.Choose(configuration.Provider, accountId) is null
            || string.IsNullOrWhiteSpace(context) || configuration.RuntimeMode != source.RuntimeMode
            || configuration.InteractionMode != source.InteractionMode)
            throw new InvalidOperationException("The conversation or destination is not ready for an account switch.");
        var directory = source.WorktreePath ?? app.Project(source.ProjectID)?.Path
            ?? throw new InvalidOperationException("The conversation folder is unavailable.");
        if (!string.Equals(System.IO.Path.GetFullPath(configuration.WorkingDirectory.Path), System.IO.Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The destination belongs to another conversation folder.");
        app.AutoContinue.TakeLimitOwnership(ThreadID);
        ReleaseSession(stop: true);
        app.ApplyHandshakeAccount(ThreadID, configuration, null, relay, pinAccount, deferred: true);
        limitedReplyTurnID = null;
        deferredHandoff = context;
        SaveNow();
    }
}
