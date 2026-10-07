using System.ComponentModel;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

// T25 W: one app owner; no test doubles or fabricated reports are installed in production.
internal sealed partial class LiveThreadPorts : IDisposable
{
    private readonly AppModel app;
    private readonly AppAccountActions actions;
    private readonly AccountConversationHeadHost host;
    private readonly Dictionary<(Guid Thread, ProviderKind Service), LiveThreadAccountPort> accounts = [];
    private readonly Dictionary<Guid, (ThreadRuntime Runtime, HandshakeSourceStamp Stamp, long Revision)> revisions = [];
    private readonly HashSet<Guid> retired = [];
    private long nextRevision;
    private bool disposed;
    internal LiveThreadPorts(AppModel app, IConversationTransferPrompt? prompt = null,
        Func<IConversationHeadHost, IConversationHeadHost>? decorateHeads = null)
    {
        MainActor.AssumeIsolated(); this.app = app; actions = new(app);
        host = new(app.Providers, Active, Revision, HeadAccounts, ContextAsync,
            (thread, account) => app.Runtime(thread).BuildHandshakeConfiguration(
                new(new(account.ToString("D"), app.Settings.Accounts.Get(account).Service.Id, app.Settings.Accounts.DefaultName(account))),
                useComposerSettings: Active(thread) == account),
            app.Settings.ConversationConsent(prompt ?? new ConversationTransferPrompt()), PostCombinedAsync, app.ApiKeys.BindPrivate);
        var heads = decorateHeads?.Invoke(host) ?? host;
        Links = new(heads); Opinions = new(heads);
        Ports = new(Account, new LiveCardPort(app.AccountRouting), Links, Opinions);
        app.PropertyChanged += AppChanged;
        app.Settings.PropertyChanged += ConfigurationChanged;
        app.Providers.PropertyChanged += ConfigurationChanged;
        app.Settings.Accounts.Changed += ConfigurationChanged;
        app.Settings.AccountDecisions.Changed += CapacityChanged;
        app.Settings.AccountKeys.Changed += CapacityChanged;
        app.Settings.Pills.Changed += PillsChanged;
    }
    internal ThreadPorts Ports { get; }
    internal LinkEngine Links { get; }
    internal OpinionsEngine Opinions { get; }
    internal IReadOnlyList<ThreadAccount> Rows(ProviderKind service) => ThreadAccountRows.Build(app.Settings.Accounts, service, app.IsAccountConnected, actions.Usage);
    internal Guid Active(Guid thread) => !retired.Contains(thread) && app.Thread(thread) is not null
        && app.Runtime(thread).TryHandshakeAccountId(out var id) ? id : Guid.Empty;
    private IThreadAccountPort? Account(Guid thread, ProviderKind service)
    {
        MainActor.AssumeIsolated(); ObjectDisposedException.ThrowIf(disposed, this);
        if (app.Thread(thread) is null) return null;
        retired.Remove(thread);
        ObserveRuntime(thread);
        var key = (thread, service);
        if (!accounts.TryGetValue(key, out var port)) accounts[key] = port = new(this, app, thread, service);
        return port;
    }
    private void ObserveRuntime(Guid thread)
    {
        var runtime = app.Runtime(thread);
        if (revisions.TryGetValue(thread, out var old) && ReferenceEquals(old.Runtime, runtime)) return;
        if (old.Runtime is not null) old.Runtime.PropertyChanged -= RuntimeChanged;
        revisions[thread] = (runtime, runtime.HandshakeStamp, checked(++nextRevision));
        runtime.PropertyChanged += RuntimeChanged;
    }
    internal long Revision(Guid thread)
    {
        if (disposed || retired.Contains(thread) || app.Thread(thread) is null) return -1;
        ObserveRuntime(thread);
        var current = revisions[thread]; var stamp = current.Runtime.HandshakeStamp;
        if (stamp != current.Stamp) revisions[thread] = current = (current.Runtime, stamp, checked(++nextRevision));
        return current.Revision;
    }
    internal IReadOnlyList<ConversationHeadAccount> HeadAccounts(Guid thread) => disposed || retired.Contains(thread) || app.Thread(thread) is null ? []
        : app.Settings.Accounts.Accounts.Where(app.IsAccountConnected).Select(account => new ConversationHeadAccount(account.Id, account.Service,
            account.CustomName ?? app.Settings.Accounts.DefaultName(account.Id), account.Enabled, app.CanUseAccount(account.Id))
        { MonthlyCapUsd = account.Service.IsAPIKeyBased ? app.Settings.AccountKeys.Keys(account.Service.Id).FirstOrDefault(key => key.Id == account.Id.ToString("D"))?.MonthlyCapUsd : null }).ToArray();
    private async Task<string> ContextAsync(Guid thread, CancellationToken ct)
    {
        var runtime = app.Runtime(thread); await runtime.EnsureLoaded(); ct.ThrowIfCancellationRequested();
        var source = await runtime.BuildHandshakeSourceAsync(ct); ct.ThrowIfCancellationRequested();
        return Marrow.Core.Port.MultiAccount.HandoffSummary.Build(source.Context, Path.Combine(source.ThreadFolder, "opinion-context.json")).FullConversation;
    }
    private Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct)
    {
        MainActor.AssumeIsolated(); ct.ThrowIfCancellationRequested();
        if (disposed || retired.Contains(thread) || app.Thread(thread) is null) throw new OperationCanceledException();
        var entry = app.Runtime(thread).AppendCombinedOpinion(round, text, byline, ct);
        if (opinionEntries.TryGetValue((thread, round), out var association))
            opinionEntries[(thread, round)] = association with { Combined = entry, Byline = byline };
        return Task.CompletedTask;
    }
    internal UsagePanelModel? Usage(ProviderKind provider)
    {
        if (app.SelectedThread is not { } thread || thread.Provider != provider) return null;
        var ports = SwiftEnum<ProviderKind>.AllCases.Select(service => Account(thread.Id, service)).OfType<IThreadAccountPort>().ToArray();
        return new(ports.First(port => port.Service == provider), ports, id => app.ApiKeys.CreditsLeft(id.ToString("D")));
    }
    internal void Completed(ThreadRuntime runtime, Guid turn, Guid author, string message, string answer)
    {
        if (disposed || retired.Contains(runtime.ThreadID) || Links.Linked(runtime.ThreadID).Count == 0) return;
        _ = MainActor.Spawn(async () =>
        {
            try { await Links.CheckCompletedAnswerAsync(runtime.ThreadID, turn, author, message, answer); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { } // ownership/availability invalidated; no late checker may publish
        });
    }
    internal void Detach(Guid thread)
    {
        DisposeViews(thread);
        retired.Add(thread);
        if (revisions.Remove(thread, out var value)) value.Runtime.PropertyChanged -= RuntimeChanged;
        host.NotifyAccountsChanged(thread);
    }
    private void AppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.AccountReportsRevision)) CapacityChanged();
        else if (e.PropertyName == nameof(AppModel.Threads)) CapacityChanged();
    }
    private void RuntimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ThreadRuntime runtime) Refresh(runtime.ThreadID);
    }
    private void ConfigurationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == app.Providers && e.PropertyName == nameof(Marrow.Services.Providers.ProviderRegistry.Statuses)) CapacityChanged();
        else ConfigurationChanged();
    }
    private void PillsChanged(object? sender, Marrow.Core.Accounts.Pills.PillStoreChange e) => ConfigurationChanged();
    private void ConfigurationChanged()
    {
        foreach (var pair in revisions.ToArray()) revisions[pair.Key] = (pair.Value.Runtime, pair.Value.Stamp, checked(++nextRevision));
        CapacityChanged();
    }
    private void CapacityChanged()
    {
        foreach (var thread in accounts.Keys.Select(key => key.Thread).Concat(revisions.Keys).Distinct().ToArray()) Refresh(thread);
    }
    private void Refresh(Guid thread)
    {
        if (disposed) return;
        foreach (var port in accounts.Where(pair => pair.Key.Thread == thread).Select(pair => pair.Value).ToArray()) port.Refresh();
        host.NotifyAccountsChanged(thread);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var thread in views.Keys.ToArray()) DisposeViews(thread);
        Links.Dispose(); Opinions.Dispose();
        foreach (var port in accounts.Values) port.Dispose();
        foreach (var value in revisions.Values) value.Runtime.PropertyChanged -= RuntimeChanged;
        app.PropertyChanged -= AppChanged; app.Settings.PropertyChanged -= ConfigurationChanged; app.Providers.PropertyChanged -= ConfigurationChanged;
        app.Settings.Accounts.Changed -= ConfigurationChanged; app.Settings.AccountDecisions.Changed -= CapacityChanged;
        app.Settings.AccountKeys.Changed -= CapacityChanged; app.Settings.Pills.Changed -= PillsChanged;
    }
}
