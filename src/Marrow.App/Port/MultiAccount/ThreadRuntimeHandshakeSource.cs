using Marrow.Core.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    internal HandshakeSourceStamp HandshakeStamp => new(saveRevision, sessionEpoch, Thread);

    internal Guid HandshakeAccountId
    {
        get
        {
            var thread = Thread ?? throw new InvalidOperationException("The conversation is unavailable.");
            var accounts = App?.Settings.Accounts ?? throw new InvalidOperationException("The conversation is unavailable.");
            if (thread.ProviderAccountID is { } bound) return accounts.Get(bound).Id;
            if (thread.ProviderSessionID is not null)
                return accounts.ForService(thread.Provider).FirstOrDefault(account => account.UsesLegacyCredentials)?.Id
                    ?? throw new InvalidOperationException("The original account for this conversation is unavailable.");
            return accounts.Choose(thread.Provider)?.Id
                ?? throw new InvalidOperationException("No enabled account is available for this conversation.");
        }
    }

    public async Task<HandshakeSummarySource> BuildHandshakeSourceAsync(CancellationToken ct)
    {
        var directory = await MainActor.Run(() => Thread is { } thread
            ? thread.WorktreePath ?? App?.Project(thread.ProjectID)?.Path : null);
        if (directory is null) throw new InvalidOperationException("The conversation folder is unavailable.");
        var working = await FullWorkingContext.ReadAsync(directory, ct);
        ct.ThrowIfCancellationRequested();
        return await MainActor.Run(() =>
        {
            FlushHandoffJournal(explicitCheckpoint: true);
            var journal = ReadHandoffJournal();
            var history = HandoffJournal.Restore(SnapshotForPersistence(), journal);
            return new HandshakeSummarySource(new HandoffContext(history, Thread?.Title ?? "", working.Diff)
            {
                EventJournal = journal, ChangedFiles = working.ChangedFiles, IsQuiescent = IsAccountSwitchSafe,
            }, HandoffFolder);
        });
    }

    internal SessionConfiguration CreateHandshakeConfiguration(SwitchTarget target)
    {
        var app = App ?? throw new InvalidOperationException("The conversation is unavailable.");
        var source = Thread ?? throw new InvalidOperationException("The conversation is unavailable.");
        var id = Guid.Parse(target.Account.Id);
        var account = app.Settings.Accounts.Get(id);
        if (!account.Enabled || account.Service.Id != target.Account.Service)
            throw new InvalidOperationException("The destination account is unavailable.");
        var directory = source.WorktreePath ?? app.Project(source.ProjectID)?.Path
            ?? throw new InvalidOperationException("The conversation folder is unavailable.");
        return new()
        {
            Provider = account.Service, AccountId = id, Executable = app.Providers.Executable(account.Service),
            WorkingDirectory = SessionDirectoryUrl(directory), Environment = ValueMap<string, string>.Empty,
            Model = target.Settings.Model ?? account.Defaults.Model ?? app.Providers.DefaultModel(account.Service)?.Id,
            Effort = target.Settings.Effort ?? account.Defaults.Effort, FastMode = target.Settings.Fast ?? account.Defaults.FastMode,
            RuntimeMode = source.RuntimeMode, InteractionMode = source.InteractionMode, AntiSlopEnabled = app.Settings.AntiSlopEnabled,
        };
    }
}
