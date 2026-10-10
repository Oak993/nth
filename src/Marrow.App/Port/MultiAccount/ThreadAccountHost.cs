using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Core.Port;
using Marrow.Core.Models;
using System.Security.Cryptography;

namespace Marrow.App.Runtime;

public sealed class ThreadAccountHost(AppModel app, ThreadRuntime runtime, PillStore pills, IHandoffSummary summaries,
    Func<SessionConfiguration, Guid, AccountSessionLaunch>? launch = null, Func<bool>? manualSelection = null) : IThreadAccountHost, IAccountSwitchCommitGuard, IIncomingBriefingHost, IDeferredDeliveryHost
{
    private readonly Func<SessionConfiguration, Guid, AccountSessionLaunch> create = launch ?? ((configuration, id) => AccountSessionFactory.Create(app.Providers, configuration, id, freshContext: true));
    private IAdoptableStandby? prepared;
    private string? preparedFrom;
    private long preparedEpoch;
    private long preparationGeneration;
    private SwitchTarget? preparedTarget;
    private DeferredStandby? deferredPrepared;

    // OD-0116 (lead decision 2026-10-06), refined by ED-439: how each service holds the context before the switch.
    // Primed: Claude (after its gate) and the API/Local providers read it in a verified text-only turn.
    // Injected: Codex, Pi and OpenCode load it as history with no model turn at all, each gated by a run-time check; Codex falls back to deferred delivery, Pi and OpenCode to a warm start.
    // Warm: the process and session open early and receive nothing; the context goes as the first real turn. Deferred: Command Code, nothing to open early.
    private enum StandbyPlan { Primed, InjectedThenDeferred, InjectedThenWarm, Warm, Deferred }

    private static StandbyPlan PlanFor(ProviderKind provider) => provider switch
    {
        ProviderKind.Claude or ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local => StandbyPlan.Primed,
        ProviderKind.Codex => StandbyPlan.InjectedThenDeferred,
        ProviderKind.Pi or ProviderKind.Opencode or ProviderKind.OpencodeGo => StandbyPlan.InjectedThenWarm,
        ProviderKind.Copilot or ProviderKind.Cursor or ProviderKind.Grok or ProviderKind.Devin or ProviderKind.Antigravity => StandbyPlan.Warm,
        _ => StandbyPlan.Deferred,
    };

    private static bool HasVerifiableStandby(ProviderKind provider) => PlanFor(provider) == StandbyPlan.Primed;

    public bool DeliversDeferred(SwitchTarget target) =>
        PlanFor(incomingProvider(target)) is StandbyPlan.Warm or StandbyPlan.Deferred
        || deferredPrepared is not null && string.Equals(deferredPrepared.Configuration.AccountId?.ToString("D"), target.Account.Id, StringComparison.OrdinalIgnoreCase)
        || prepared is { Kind: StandbyKind.Deferred } discarded && string.Equals(discarded.Configuration.AccountId?.ToString("D"), target.Account.Id, StringComparison.OrdinalIgnoreCase);

    // The run-time check before the user is asked "finish the reply?": a CLI that cannot hold history will not continue a reply either (ED-439).
    public async ValueTask<bool> ResolveDeliversDeferredAsync(SwitchTarget target, CancellationToken ct)
    {
        if (DeliversDeferred(target)) return true;
        var provider = incomingProvider(target);
        if (PlanFor(provider) is not (StandbyPlan.InjectedThenDeferred or StandbyPlan.InjectedThenWarm)) return false;
        try
        {
            var candidate = create(runtime.CreateHandshakeConfiguration(target), Guid.Parse(target.Account.Id));
            return InjectionCapable(candidate) is IStandbyInjectionSession injector && !await injector.SupportsStandbyInjection(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
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
        var plan = PlanFor(original.Provider);
        if (plan == StandbyPlan.Deferred) return PrepareDeferred(original, target, from, epoch, generation);
        if (plan == StandbyPlan.Warm) return await PrepareWarmAsync(original, target, id, from, epoch, generation, ct);
        if (plan is StandbyPlan.InjectedThenDeferred or StandbyPlan.InjectedThenWarm)
            return await StartInjectedAsync(original, target, id, from, epoch, generation, CanTransfer, plan == StandbyPlan.InjectedThenWarm, ct);
        var candidate = create(original, id);
        app.ApiKeys.BindPrivate(candidate.Session, Guid.Parse(target.Account.Id));
        HandshakeStandbySession standby;
        try
        {
            standby = await HandshakeStandbySession.StartAsync(candidate.Session, candidate.Configuration, ct, CanTransfer,
                observeUsage: () => MainActor.Run(() => app.AccountRouting.ObserveHandshakeCost(candidate.Configuration.AccountId!.Value)));
        }
        catch (Exception) when (original.Provider == ProviderKind.Claude && !ct.IsCancellationRequested)
        {
            // The Claude gate did not pass (old version, policy not enforced or reported, no acknowledged text-only init): the same deferred path.
            return PrepareDeferred(original, target, from, epoch, generation);
        }
        if (ActiveAccountId != from || runtime.HandshakeStamp.Epoch != epoch || generation != preparationGeneration)
        {
            await standby.DisposeAsync();
            throw new InvalidOperationException("The outgoing conversation changed while the standby was starting.");
        }
        prepared = standby;
        deferredPrepared = null;
        preparedFrom = from;
        preparedEpoch = epoch;
        preparedTarget = target;
        return standby;
    }

    // OpenCode holds the context on its own HTTP server and opens the conversation's ACP session (session/load) at the handover; every other service injects through its own session.
    private static ProviderSession InjectionCapable(AccountSessionLaunch candidate) =>
        candidate.Configuration.Provider is ProviderKind.Opencode or ProviderKind.OpencodeGo
            ? new OpenCodeInjectionSession(candidate.Configuration, configuration => AccountSessionFactory.Constructor.Current(configuration))
            : candidate.Session;

    // ED-439: no model turn at all, so nothing can act. Any refusal, old CLI, exit or activity falls back to deferred delivery with the context intact.
    private async Task<IStandbySession> StartInjectedAsync(SessionConfiguration original, SwitchTarget target, Guid id, string from, long epoch, long generation,
        Func<bool> canTransfer, bool warmFallback, CancellationToken ct)
    {
        InjectedStandbySession standby;
        try
        {
            var candidate = create(original, id);
            standby = await InjectedStandbySession.StartAsync(InjectionCapable(candidate), candidate.Configuration, ct, canTransfer);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return warmFallback ? await PrepareWarmAsync(original, target, id, from, epoch, generation, ct) : PrepareDeferred(original, target, from, epoch, generation);
        }
        if (ActiveAccountId != from || runtime.HandshakeStamp.Epoch != epoch || generation != preparationGeneration)
        {
            await standby.DisposeAsync();
            throw new InvalidOperationException("The outgoing conversation changed while the standby was starting.");
        }
        prepared = standby;
        deferredPrepared = null;
        preparedFrom = from;
        preparedEpoch = epoch;
        preparedTarget = target;
        return standby;
    }

    // ED-439: the destination's process and session open early with no context and no prompt; if it cannot, or does anything on its own, the context goes the plain deferred way.
    private async Task<IStandbySession> PrepareWarmAsync(SessionConfiguration original, SwitchTarget target, Guid id, string from, long epoch, long generation, CancellationToken ct)
    {
        WarmStandby? warm = null;
        try
        {
            var candidate = create(original, id);
            warm = await WarmStandby.StartAsync(candidate.Session, candidate.Configuration, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return PrepareDeferred(original, target, from, epoch, generation);
        }
        try { return PrepareDeferred(original, target, from, epoch, generation, warm); }
        catch
        {
            warm.Stop();
            throw;
        }
    }

    private IStandbySession PrepareDeferred(SessionConfiguration original, SwitchTarget target, string from, long epoch, long generation, WarmStandby? warm = null)
    {
        if (ActiveAccountId != from || runtime.HandshakeStamp.Epoch != epoch || generation != preparationGeneration)
            throw new InvalidOperationException("The outgoing conversation changed while the destination was being prepared.");
        var standby = new DeferredStandby(original, warm);
        prepared = null;
        deferredPrepared = standby;
        preparedFrom = from;
        preparedEpoch = epoch;
        preparedTarget = target;
        return standby;
    }
    public async Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        var account = app.Settings.Accounts.Get(Guid.Parse(ActiveAccountId));
        if (!HasVerifiableStandby(account.Service)) return null;
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
        return await privateSession.TextTurnAsync("[nth Handshake] Write a short briefing for your successor: the goal, why decisions were made, what is next, and pitfalls. Take no actions. Use only this recorded context:\n" + summary.Text, ct);
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
        if (standby is DeferredStandby deferred)
        {
            await SwitchDeferredAsync(deferred, target, ct);
            return;
        }
        if (standby is not IAdoptableStandby incoming || !ReferenceEquals(prepared, incoming) || incoming.ReceivedPackage is not { } original)
            throw new InvalidOperationException("The standby does not belong to this conversation or has not acknowledged its context.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (incoming.Kind == StandbyKind.Deferred)
            {
                await FallBackToDeferredAsync(incoming, target, ct);
                return;
            }
            CheckCandidate(incoming, target);
            CheckSource(target);
            var final = await summaries.BuildAsync(runtime.ThreadID, ct);
            var stamp = runtime.HandshakeStamp;
            var fingerprint = await FingerprintAsync(final, ct);
            var needsReadBack = incoming.ReadBackRequested;
            var readBack = await incoming.ReceiveAsync(original with { Summary = final }, needsReadBack, ct);
            if (incoming.Kind == StandbyKind.Deferred)
            {
                await FallBackToDeferredAsync(incoming, target, ct);
                return;
            }
            if (needsReadBack)
            {
                var gaps = summaries.FindGaps(final, readBack ?? "");
                if (gaps.Count > 0) await incoming.FillGapsAsync(gaps, ct);
            }
            var proof = await summaries.BuildAsync(runtime.ThreadID, ct);
            var proofFingerprint = await FingerprintAsync(proof, ct);
            if (stamp != runtime.HandshakeStamp || fingerprint != proofFingerprint) continue;
            ct.ThrowIfCancellationRequested();
            if (incoming.Kind == StandbyKind.Deferred)
            {
                await FallBackToDeferredAsync(incoming, target, ct);
                return;
            }
            await incoming.PrepareForAdoptionAsync(ct);
            if (incoming.Kind == StandbyKind.Deferred)
            {
                await FallBackToDeferredAsync(incoming, target, ct);
                return;
            }
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
                runtime.RecordAccountHandoff(final, original, target.Account.Name,
                    ready: incoming.Kind == StandbyKind.Injected || incoming.ReadBackRequested);
                prepared = null;
                preparedTarget = null;
            });
            return;
        }
        throw new InvalidOperationException("The outgoing conversation kept changing before its final context was acknowledged.");
    }

    // A loaded history that was discarded for safety (activity on the waiting thread, a refused inject, an exit): the same switch, deferred. Nothing the user said is lost.
    private async Task FallBackToDeferredAsync(IAdoptableStandby incoming, SwitchTarget target, CancellationToken ct)
    {
        var package = incoming.ReceivedPackage ?? throw new InvalidOperationException("The standby has no context to hand over.");
        var deferred = new DeferredStandby(incoming.Configuration);
        await deferred.ReceiveAsync(package, false, ct);
        await incoming.DisposeAsync();
        prepared = null;
        deferredPrepared = deferred;
        await SwitchDeferredAsync(deferred, target, ct);
    }

    private async Task SwitchDeferredAsync(DeferredStandby standby, SwitchTarget target, CancellationToken ct)
    {
        if (!ReferenceEquals(deferredPrepared, standby) || standby.ReceivedPackage is not { } package || preparedTarget is not { } original
            || !string.Equals(original.Account.Id, target.Account.Id, StringComparison.OrdinalIgnoreCase) || original.StepIndex != target.StepIndex || original.Step != target.Step
            || standby.Configuration.AccountId != Guid.Parse(target.Account.Id))
            throw new InvalidOperationException("The prepared destination does not belong to this conversation or has not received its context.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            CheckDeferred(standby, target);
            var stamp = runtime.HandshakeStamp;
            var final = await summaries.BuildAsync(runtime.ThreadID, ct);
            var context = package.Bring switch
            {
                Bring.Full => await File.ReadAllTextAsync(final.FilePath ?? throw new InvalidOperationException("The complete conversation archive is unavailable."), ct),
                Bring.AiSummary => package.Briefing + "\nRecorded context:\n" + final.Text,
                _ => final.Text,
            };
            if (package.Bring != Bring.AiSummary && !string.IsNullOrWhiteSpace(package.Briefing)) context += "\nOutgoing account briefing:\n" + package.Briefing;
            var preamble = "[nth Handshake] This conversation was handed over from another account. Treat the recorded context below as what was said so far; "
                + "answer only the message that follows it.\n" + context;
            ct.ThrowIfCancellationRequested();
            var done = await MainActor.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                CheckDeferred(standby, target);
                if (stamp != runtime.HandshakeStamp) return false;
                PillThreadState? relay = null;
                if (target.StepIndex is { } index)
                {
                    var pill = Pill ?? throw new InvalidOperationException("The pill was removed while the Handshake was preparing.");
                    if (index < 0 || index >= pill.Steps.Count || pill.Effective(index) != target.Step)
                        throw new InvalidOperationException("The pill step changed while the Handshake was preparing.");
                    relay = new(pill.Id, index, target.Account.Id);
                }
                if (standby.Warm is { IsHealthy: true } warm)
                {
                    // The warm session is open and idle: it becomes the conversation's session and its first real turn carries the context.
                    var live = warm.Take();
                    try { runtime.AdoptHandshakeSession(live, warm.SessionId, warm.Configuration, relay, false, pinAccount: manualSelection is not null, deferredContext: preamble); }
                    catch { live.OnEvent = null; live.Stop(); throw; }
                }
                else
                {
                    standby.Warm?.Stop();
                    runtime.AdoptDeferredAccount(standby.Configuration, preamble, relay, pinAccount: manualSelection is not null);
                }
                runtime.RecordAccountHandoff(final, package, target.Account.Name, ready: false);
                deferredPrepared = null;
                preparedTarget = null;
                return true;
            });
            if (done) return;
        }
        throw new InvalidOperationException("The outgoing conversation kept changing before its context could be handed over.");
    }

    private void CheckDeferred(DeferredStandby standby, SwitchTarget target)
    {
        if (!ReferenceEquals(deferredPrepared, standby) || !SameTransferSettings(standby.Configuration, runtime.CreateHandshakeConfiguration(target)))
            throw new InvalidOperationException("The destination settings changed while the Handshake was preparing.");
        CheckSource(target);
    }

    private void CheckCandidate(IAdoptableStandby incoming, SwitchTarget target)
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
