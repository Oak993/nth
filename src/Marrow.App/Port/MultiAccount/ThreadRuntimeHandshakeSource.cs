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

    internal bool TryHandshakeAccountId(out Guid id)
    {
        id = default;
        if (Thread is not { } thread || App is not { } app) return false;
        var rows = app.Settings.Accounts.ForService(thread.Provider);
        var account = thread.ProviderAccountID is { } bound ? rows.FirstOrDefault(row => row.Id == bound)
            : UsageAccount is { } used && used.Provider == thread.Provider
                ? used.IsDefault ? rows.FirstOrDefault(row => row.UsesLegacyCredentials)
                    : Guid.TryParse(used.AccountId, out var observed) ? rows.FirstOrDefault(row => row.Id == observed) : null
            : thread.ProviderSessionID is not null ? rows.FirstOrDefault(row => row.UsesLegacyCredentials)
            : app.Settings.Accounts.Choose(thread.Provider);
        if (account is null) return false;
        id = account.Id;
        return true;
    }

    internal Guid HandshakeAccountId
    {
        get
        {
            return TryHandshakeAccountId(out var id) ? id
                : throw new InvalidOperationException("The original account for this conversation is unavailable.");
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

    internal SessionConfiguration CreateHandshakeConfiguration(SwitchTarget target) => BuildHandshakeConfiguration(target, useComposerSettings: false);

    internal SessionConfiguration BuildHandshakeConfiguration(SwitchTarget target, bool useComposerSettings)
    {
        var app = App ?? throw new InvalidOperationException("The conversation is unavailable.");
        var source = Thread ?? throw new InvalidOperationException("The conversation is unavailable.");
        var id = Guid.Parse(target.Account.Id);
        var account = app.Settings.Accounts.Get(id);
        if (!account.Enabled || account.Service.Id != target.Account.Service)
            throw new InvalidOperationException("The destination account is unavailable.");
        var directory = source.WorktreePath ?? app.Project(source.ProjectID)?.Path
            ?? throw new InvalidOperationException("The conversation folder is unavailable.");
        var destination = source with
        {
            Provider = account.Service, ProviderAccountID = id,
            Model = useComposerSettings ? source.Model : target.Settings.Model ?? account.Defaults.Model ?? app.Providers.DefaultModel(account.Service)?.Id,
            Effort = useComposerSettings ? source.Effort : target.Settings.Effort ?? account.Defaults.Effort,
            FastMode = useComposerSettings ? source.FastMode : target.Settings.Fast ?? account.Defaults.FastMode,
        };
        return new()
        {
            Provider = account.Service, AccountId = id, Executable = app.Providers.Executable(account.Service),
            WorkingDirectory = SessionDirectoryUrl(directory), Environment = ValueMap<string, string>.Empty,
            Model = destination.Model, Effort = destination.Effort, FastMode = destination.FastMode,
            RuntimeMode = source.RuntimeMode, InteractionMode = source.InteractionMode, AntiSlopEnabled = app.Settings.AntiSlopEnabled,
            Hydra = KeepsHydraPolicyInSystemPrompt(account.Service) ? app.HydraLaunch(destination) : null,
        };
    }
}
