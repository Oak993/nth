// PORT-ONLY: OD-0114; ED-403. No await separates final validation from the account/session commit.
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private Guid? limitedReplyTurnID;
    public long HandshakeRevision => saveRevision;
    public bool HandshakeReplyInterrupted => limitedReplyTurnID is not null;

    internal async Task<bool> TryResumeAfterAccountWaitAsync(Guid account, long epoch, Guid interrupted, string? originalSessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsAccountSwitchSafe || !TryHandshakeAccountId(out var active) || active != account
            || limitedReplyTurnID != interrupted || Turns.LastOrDefault()?.Id != interrupted
            || session is { IsRunning: true } && sessionEpoch != epoch
            || session is not { IsRunning: true } && Thread?.ProviderSessionID != originalSessionId)
            return false;
        var source = Thread!;
        var row = App!.Settings.Accounts.Get(account);
        if (!row.Enabled || !App.IsAccountConnected(row)) return false;
        var configuration = CreateHandshakeConfiguration(new(new(account.ToString("D"), row.Service.Id, row.CustomName ?? App.Settings.Accounts.DefaultName(account))))
            with { Model = source.Model, Effort = source.Effort, FastMode = source.FastMode };
        bool StillOwnsRestart() => IsAccountWorkSettled && (!isStartingSession
            || guardedStartingSession is not null && ReferenceEquals(session, guardedStartingSession)) && limitedReplyTurnID == interrupted
            && Turns.LastOrDefault()?.Id == interrupted && Thread is { } current
            && current.ProviderSessionID == originalSessionId && current.ProviderAccountID == source.ProviderAccountID
            && current.Provider == source.Provider && current.Model == source.Model && current.Effort == source.Effort
            && current.FastMode == source.FastMode && current.RuntimeMode == source.RuntimeMode
            && current.InteractionMode == source.InteractionMode && current.WorktreePath == source.WorktreePath
            && current.ProjectID == source.ProjectID && TryHandshakeAccountId(out var currentAccount) && currentAccount == account;
        ProviderSession candidate;
        try
        {
            candidate = session is { IsRunning: true } existing ? existing
                : await EnsureSessionOwned(configuration.WorkingDirectory.Path, allowNewSession: false, ct, StillOwnsRestart);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            if (!ct.IsCancellationRequested && StillOwnsRestart()) AppendNotice(Notice.Level.Error, error.Message);
            return false;
        }
        ct.ThrowIfCancellationRequested();
        if (!IsAccountSwitchSafe || limitedReplyTurnID != interrupted || Turns.LastOrDefault()?.Id != interrupted
            || !TryHandshakeAccountId(out active) || active != account || !ReferenceEquals(session, candidate)) return false;
        ResumeHandshakeReply(candidate, configuration, Turns.First(turn => turn.Id == interrupted));
        return true;
    }

    public void AdoptHandshakeSession(ProviderSession candidate, string sessionId, SessionConfiguration configuration,
        PillThreadState? relay = null, bool resumeReply = false, bool pinAccount = false, string? deferredContext = null)
    {
        var app = App ?? throw new InvalidOperationException("The conversation is unavailable.");
        var source = Thread ?? throw new InvalidOperationException("The conversation is unavailable.");
        if (ReferenceEquals(candidate, session) || !IsAccountSwitchSafe || configuration.AccountId is not { } accountId
            || app.Settings.Accounts.Choose(configuration.Provider, accountId) is null || !candidate.IsRunning
            || string.IsNullOrWhiteSpace(sessionId) || configuration.RuntimeMode != source.RuntimeMode
            || configuration.InteractionMode != source.InteractionMode)
            throw new InvalidOperationException("The conversation or destination is not ready for an account switch.");
        var directory = source.WorktreePath ?? app.Project(source.ProjectID)?.Path
            ?? throw new InvalidOperationException("The conversation folder is unavailable.");
        if (!string.Equals(System.IO.Path.GetFullPath(configuration.WorkingDirectory.Path), System.IO.Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The standby belongs to another conversation folder.");
        var local = configuration.Provider == ProviderKind.Local ? app.Providers.Local.Resolve(configuration.Model) : null;
        if (configuration.Provider == ProviderKind.Local && local is null)
            throw new InvalidOperationException("The local server or model is unavailable.");
        var pausedReply = OwnsAccountStepPause ? Turns.FirstOrDefault(turn => turn.Id == currentTurnID && turn.Status == TurnStatus.Running) : null;
        if (pausedReply is not null && !resumeReply) throw new InvalidOperationException("A paused reply must retain its continuation ownership.");
        var limited = resumeReply ? pausedReply ?? Turns.FirstOrDefault(turn => turn.Id == limitedReplyTurnID && turn.Status != TurnStatus.Running) : null;
        if (resumeReply && limited is null) throw new InvalidOperationException("There is no settled interrupted reply to continue.");
        if (!IsAccountSwitchSafe || Thread != source || app.Settings.Accounts.Choose(configuration.Provider, accountId) is null)
            throw new InvalidOperationException("The prepared switch became stale.");

        app.AutoContinue.TakeLimitOwnership(ThreadID);
        ReleaseSession(stop: true);
        app.ApplyHandshakeAccount(ThreadID, configuration, sessionId, relay, pinAccount);
        // ED-439: a warm-started session is idle; the recorded context rides in front of the first real turn on it.
        HoldDeferredHandoff(deferredContext);
        var destination = Thread!;
        session = candidate;
        var hydra = KeepsHydraPolicyInSystemPrompt(destination.Provider) ? app.HydraLaunch(destination) : null;
        sessionSignature = MakeSessionSignature(destination, directory, app.Settings.AntiSlopEnabled, hydra)
            with { LocalServerId = local?.Server.Id, LocalCatalogRevision = local is null ? null : app.Providers.Local.Revision };
        Observe(candidate);
        BindLiveApiKeys(candidate, accountId);
        if (limited is not null) ResumeHandshakeReply(candidate, configuration, limited);
        else
        {
            limitedReplyTurnID = null;
            InstallHandshakeBridge(candidate, new(Guid.Empty, "", "", "handoff-" + sessionEpoch + "-"));
        }
        SaveNow();
    }

    private void ResumeHandshakeReply(ProviderSession candidate, SessionConfiguration configuration, TurnRecord turn)
    {
        FlushDeltas();
        foreach (var end in Entries.Where(entry => entry.TurnID == turn.Id && entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd).ToArray()) Remove(end.Id);
        var partial = Entries.LastOrDefault(entry => entry.TurnID == turn.Id && entry.Item.TimelineItemContent is TimelineItem.Content.Assistant);
        var assistantId = partial?.Id ?? "handoff-reply-" + turn.Id.ToString("N");
        var restored = HandoffJournal.Restore(SnapshotForPersistence(), ReadHandoffJournal());
        var prefix = restored.Items.FirstOrDefault(item => item.Id == assistantId)?.TimelineItemContent is TimelineItem.Content.Assistant assistant ? assistant.Value.Text : "";
        if (partial is not null && partial.Item.TimelineItemContent is TimelineItem.Content.Assistant existing)
            partial.Item = partial.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(existing.Value with { IsStreaming = true }) };
        currentTurnID = turn.Id;
        currentProviderDiff = null;
        currentProviderAnchor = null;
        UpdateTurn(turn.Id, value => value with { Status = TurnStatus.Running, CompletedAt = null, ProviderTurnID = null });
        Phase = RuntimePhase.Running;
        TurnStartedAt = turn.StartedAt;
        var bridge = new HandshakeReplyBridge(turn.Id, assistantId, prefix, "handoff-" + sessionEpoch + "-");
        var epoch = sessionEpoch;
        InstallHandshakeBridge(candidate, bridge);
        _ = MainActor.Spawn(async () =>
        {
            try
            {
                if (epoch != sessionEpoch || currentTurnID != turn.Id || !ReferenceEquals(session, candidate)) return;
                await candidate.Send(new TurnInput
                {
                    Text = "[Nth] Continue the interrupted reply from the last completed step. Preserve completed work; do not repeat it.",
                    Images = ValueList<Attachment>.Empty, Model = configuration.Model, Effort = configuration.Effort, FastMode = configuration.FastMode,
                    ServiceTier = Thread is { } thread ? ServiceTier(thread) : null,
                    RuntimeMode = configuration.RuntimeMode, InteractionMode = configuration.InteractionMode,
                });
            }
            catch (Exception error)
            {
                if (epoch == sessionEpoch && currentTurnID == turn.Id)
                {
                    AppendNotice(Notice.Level.Error, error.Message);
                    await FinishTurn(TurnStatus.Failed, turn.Id);
                }
            }
        });
    }

    private void InstallHandshakeBridge(ProviderSession candidate, HandshakeReplyBridge bridge)
    {
        var observe = observedSessionEvent ?? throw new InvalidOperationException("The live account observer is unavailable.");
        var epoch = sessionEpoch;
        candidate.OnEvent = value =>
        {
            if (epoch != sessionEpoch) return;
            BufferHandoff(HandoffJournal.EncodeTrace(value), preserve: true);
            observe(bridge.Map(value, currentTurnID == bridge.TurnId));
        };
    }
}
