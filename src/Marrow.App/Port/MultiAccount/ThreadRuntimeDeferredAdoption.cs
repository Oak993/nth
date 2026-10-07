// PORT-ONLY: OD-0116, lead decision 2026-10-06 (ED-436). Adoption of an account whose session is not started: the thread is bound to it and its first real turn carries the context.
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    // Held until the destination's first real turn is sent. ED-439: it is also kept in the thread's own folder (the account it belongs to on the first line),
    // so a restart before that first turn does not lose it.
    private string? deferredHandoff;
    private bool deferredHandoffChecked;
    internal bool HasDeferredHandoff => deferredHandoff is not null;

    private string DeferredHandoffFile => System.IO.Path.Combine(HandoffFolder, "deferred-handoff.txt");

    private void HoldDeferredHandoff(string? context)
    {
        deferredHandoff = context;
        deferredHandoffChecked = true;
        try
        {
            if (context is null)
            {
                if (File.Exists(DeferredHandoffFile)) File.Delete(DeferredHandoffFile);
            }
            else if (Thread?.ProviderAccountID is { } account)
            {
                Directory.CreateDirectory(HandoffFolder);
                var temporary = DeferredHandoffFile + ".tmp";
                File.WriteAllText(temporary, account.ToString("D") + "\n" + context, new System.Text.UTF8Encoding(false));
                File.Move(temporary, DeferredHandoffFile, overwrite: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The in-memory copy still holds it until this run ends.
        }
    }

    private string? PendingDeferredHandoff()
    {
        if (deferredHandoff is null && !deferredHandoffChecked)
        {
            deferredHandoffChecked = true;
            if (Thread is { ProviderAccountID: { } account })
            {
                try
                {
                    if (File.Exists(DeferredHandoffFile) && File.ReadAllText(DeferredHandoffFile) is var text && text.IndexOf('\n', StringComparison.Ordinal) is var line and > 0
                        && text[..line] == account.ToString("D"))
                        deferredHandoff = text[(line + 1)..];
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Unreadable or no storage: nothing was held.
                }
            }
        }

        return deferredHandoff;
    }

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
        HoldDeferredHandoff(context);
        SaveNow();
    }
}
