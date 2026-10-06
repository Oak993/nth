using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Core.Port;
using Marrow.Core.Models;
using System.Security.Cryptography;

namespace Marrow.App.Runtime;

public sealed class ThreadAccountHost(AppModel app, ThreadRuntime runtime, PillStore pills, IHandoffSummary summaries,
    Func<SessionConfiguration, Guid, AccountSessionLaunch>? launch = null, Func<bool>? manualSelection = null) : IThreadAccountHost, IAccountSwitchCommitGuard, IIncomingBriefingHost
{
    private readonly Func<SessionConfiguration, Guid, AccountSessionLaunch> create = launch ?? ((configuration, id) => AccountSessionFactory.Create(app.Providers, configuration, id, freshContext: true));
    private HandshakeStandbySession? prepared;
    private string? preparedFrom;
    private long preparedEpoch;
    private long preparationGeneration;
    private SwitchTarget? preparedTarget;
    public string ActiveAccountId => runtime.HandshakeAccountId.ToString("D");
    private PillResume? Relay => app.Thread(runtime.ThreadID)?.PillRelay is { } saved ? PillRelayResolver.Resolve(pills.Pills, saved) : null;
    public Pill? Pill => Relay?.Pill;
    public int? StepIndex => Relay?.StepIndex;
    public bool Pinned => app.Thread(runtime.ThreadID)?.ProviderAccountPinID is not null && manualSelection?.Invoke() != true;
    public Func<bool>? CanCommit { get; set; }
    public bool AtSafePoint => runtime.IsAccountSwitchSafe;
    public bool ReplyInFlight => runtime.HandshakeReplyInFlight;
    public async Task<IStandbySession> StartStandbyAsync(SwitchTarget target, CancellationToken ct)
    {
        var from = ActiveAccountId;
        var epoch = runtime.HandshakeStamp.Epoch;
        var generation = ++preparationGeneration;
        var original = runtime.CreateHandshakeConfiguration(target);
        var id = Guid.Parse(target.Account.Id);
        bool CanTransfer() => SourceStillCurrent(from, epoch) && generation == preparationGeneration
            && AccountAvailable(original.Provider, id) && SameTransferSettings(original, runtime.CreateHandshakeConfiguration(target));
        var candidate = create(original, id);
        app.ApiKeys.BindPrivate(candidate.Session, Guid.Parse(target.Account.Id));
        var standby = await HandshakeStandbySession.StartAsync(candidate.Session, candidate.Configuration, ct, CanTransfer,
            observeUsage: () => MainActor.Run(() => app.AccountRouting.ObserveHandshakeCost(candidate.Configuration.AccountId!.Value)));
        if (ActiveAccountId != from || runtime.HandshakeStamp.Epoch != epoch || generation != preparationGeneration)
        {
            await standby.DisposeAsync();
            throw new InvalidOperationException("The outgoing conversation changed while the standby was starting.");
        }
        prepared = standby;
        preparedFrom = from;
        preparedEpoch = epoch;
        preparedTarget = target;
        return standby;
    }
    public async Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        var account = app.Settings.Accounts.Get(Guid.Parse(ActiveAccountId));
        var from = ActiveAccountId;
        var epoch = runtime.HandshakeStamp.Epoch;
        var target = new SwitchTarget(new(account.Id.ToString("D"), account.Service.Id, account.CustomName ?? app.Settings.Accounts.DefaultName(account.Id)));
        var configuration = runtime.BuildHandshakeConfiguration(target, useComposerSettings: true);
        bool CanTransfer() => SourceStillCurrent(from, epoch) && AccountAvailable(configuration.Provider, account.Id)
            && SameTransferSettings(configuration, runtime.BuildHandshakeConfiguration(target, useComposerSettings: true));
        var candidate = create(configuration, account.Id);
        app.ApiKeys.BindPrivate(candidate.Session, account.Id);
        await using var privateSession = await HandshakeStandbySession.StartAsync(candidate.Session, candidate.Configuration, ct, CanTransfer,
            observeUsage: () => MainActor.Run(() => app.AccountRouting.ObserveHandshakeCost(candidate.Configuration.AccountId!.Value)));
        return await privateSession.TextTurnAsync("[Marrow Handshake] Write a short briefing for your successor: the goal, why decisions were made, what is next, and pitfalls. Take no actions. Use only this recorded context:\n" + summary.Text, ct);
    }

    public async Task<string?> RequestIncomingBriefingAsync(SwitchTarget target, HandoffSummary summary, CancellationToken ct)
    {
        var from = ActiveAccountId;
        var epoch = runtime.HandshakeStamp.Epoch;
        var original = runtime.CreateHandshakeConfiguration(target);
        if (original.Provider != ProviderKind.Claude)
            throw new NotSupportedException("This service has no verified light-model briefing capability.");
        var id = Guid.Parse(target.Account.Id);
        bool CanTransfer() => SourceStillCurrent(from, epoch) && AccountAvailable(original.Provider, id)
            && SameTransferSettings(original, runtime.CreateHandshakeConfiguration(target));
        if (!CanTransfer()) throw new InvalidOperationException("The incoming AI briefing is no longer authorized.");
        var candidate = create(original with { FastMode = false, Effort = null }, id);
        app.ApiKeys.BindPrivate(candidate.Session, id);
        await using var light = await HandshakeStandbySession.StartAsync(candidate.Session, candidate.Configuration, ct, CanTransfer,
            () => MainActor.Run(() => app.AccountRouting.ObserveHandshakeCost(id)));
        var answer = await light.WriteIncomingLightBriefingAsync(summary, ct);
        if (!CanTransfer()) throw new InvalidOperationException("The incoming AI briefing became stale.");
        return answer;
    }

    public async Task SwitchAsync(IStandbySession standby, SwitchTarget target, bool resumeReply, CancellationToken ct)
    {
        if (standby is not HandshakeStandbySession incoming || !ReferenceEquals(prepared, incoming) || incoming.ReceivedPackage is not { } original)
            throw new InvalidOperationException("The standby does not belong to this conversation or has not acknowledged its context.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            CheckCandidate(incoming, target);
            CheckSource(target);
            var final = await summaries.BuildAsync(runtime.ThreadID, ct);
            var stamp = runtime.HandshakeStamp;
            var fingerprint = await FingerprintAsync(final, ct);
            var readBack = await incoming.ReceiveAsync(original with { Summary = final }, true, ct);
            var gaps = summaries.FindGaps(final, readBack ?? "");
            if (gaps.Count > 0) await incoming.FillGapsAsync(gaps, ct);
            var proof = await summaries.BuildAsync(runtime.ThreadID, ct);
            var proofFingerprint = await FingerprintAsync(proof, ct);
            if (stamp != runtime.HandshakeStamp || fingerprint != proofFingerprint) continue;
            ct.ThrowIfCancellationRequested();
            await incoming.PrepareForAdoptionAsync(ct);
            await MainActor.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                CheckCandidate(incoming, target);
                CheckSource(target);
                if (stamp != runtime.HandshakeStamp) throw new InvalidOperationException("The prepared context became stale.");
                PillThreadState? relay = null;
                if (target.StepIndex is { } index)
                {
                    var pill = Pill ?? throw new InvalidOperationException("The pill was removed while the Handshake was preparing.");
                    if (index < 0 || index >= pill.Steps.Count || pill.Effective(index) != target.Step)
                        throw new InvalidOperationException("The pill step changed while the Handshake was preparing.");
                    relay = new(pill.Id, index, target.Account.Id);
                }
                var candidate = incoming.TakeSession();
                try { runtime.AdoptHandshakeSession(candidate, incoming.SessionId, incoming.Configuration, relay, resumeReply, pinAccount: manualSelection is not null); }
                catch { candidate.OnEvent = null; candidate.Stop(); throw; }
                prepared = null;
                preparedTarget = null;
            });
            return;
        }
        throw new InvalidOperationException("The outgoing conversation kept changing before its final context was acknowledged.");
    }

    private void CheckCandidate(HandshakeStandbySession incoming, SwitchTarget target)
    {
        if (!ReferenceEquals(prepared, incoming) || preparedTarget is not { } original
            || !string.Equals(original.Account.Id, target.Account.Id, StringComparison.OrdinalIgnoreCase)
            || original.Account.Service != target.Account.Service || original.StepIndex != target.StepIndex || original.Step != target.Step
            || incoming.Configuration.AccountId != Guid.Parse(target.Account.Id) || incoming.Configuration.Provider.Id != target.Account.Service)
            throw new InvalidOperationException("The prepared session no longer belongs to this destination.");
        var current = runtime.CreateHandshakeConfiguration(target);
        var preparedConfiguration = incoming.Configuration;
        if (current.Model != preparedConfiguration.Model || current.Effort != preparedConfiguration.Effort
            || current.FastMode != preparedConfiguration.FastMode || current.RuntimeMode != preparedConfiguration.RuntimeMode
            || current.InteractionMode != preparedConfiguration.InteractionMode || current.AntiSlopEnabled != preparedConfiguration.AntiSlopEnabled
            || current.Hydra != preparedConfiguration.Hydra || current.WorkingDirectory != preparedConfiguration.WorkingDirectory)
            throw new InvalidOperationException("The destination settings changed while the Handshake was preparing.");
    }

    private void CheckSource(SwitchTarget target)
    {
        if (!AtSafePoint || Pinned || CanCommit?.Invoke() == false || ActiveAccountId != preparedFrom || runtime.HandshakeStamp.Epoch != preparedEpoch
            || !AccountAvailable(incomingProvider(target), Guid.Parse(target.Account.Id)))
            throw new InvalidOperationException("The prepared switch is no longer safe or available.");
    }

    private bool SourceStillCurrent(string from, long epoch) => CanCommit?.Invoke() != false && !Pinned
        && !app.Settings.Accounts.SaveBlocked && runtime.TryHandshakeAccountId(out var current)
        && current.ToString("D") == from && runtime.HandshakeStamp.Epoch == epoch;

    private bool AccountAvailable(ProviderKind provider, Guid id) => app.Settings.Accounts.Choose(provider, id) is { } row
        && app.IsAccountConnected(row) && app.CanUseAccount(id);

    private static bool SameTransferSettings(SessionConfiguration original, SessionConfiguration current) =>
        current.Model == original.Model && current.Effort == original.Effort && current.FastMode == original.FastMode
        && current.RuntimeMode == original.RuntimeMode && current.InteractionMode == original.InteractionMode
        && current.Hydra == original.Hydra && current.AntiSlopEnabled == original.AntiSlopEnabled
        && current.WorkingDirectory == original.WorkingDirectory;

    private static Marrow.Core.Models.ProviderKind incomingProvider(SwitchTarget target) =>
        SwiftEnum<Marrow.Core.Models.ProviderKind>.FromRawValue(target.Account.Service)
            ?? throw new InvalidOperationException("The destination service is unavailable.");

    private static async Task<string> FingerprintAsync(HandoffSummary summary, CancellationToken ct) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(summary.FilePath
            ?? throw new InvalidOperationException("The complete conversation archive is unavailable."), ct)));
}
