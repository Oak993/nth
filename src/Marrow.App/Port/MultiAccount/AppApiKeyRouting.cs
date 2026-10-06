using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

// PORT-ONLY: OD-0114/0116. Shared service pools keep request identities independent.
public sealed class AppApiKeyRouting(AppModel app) : IKeyMovePolicy, IApiKeyCreditObservations
{
    private readonly Dictionary<ProviderKind, ApiKeyPool> pools = [];
    private readonly Dictionary<Guid, AppApiKeyRotation> live = [];
    private readonly DecisionPolicy decisions = new(app.Settings.AccountDecisions);
    private readonly TimeProvider clock = SwiftClock.Provider.Current;
    public event EventHandler<HandshakeEvent>? Changed;
    public KeyMoveSetting Setting(Guid threadId) => decisions.Resolve(threadId, DecisionKind.OnLimit).Value switch
    {
        DecisionValues.Switch => KeyMoveSetting.Allow,
        DecisionValues.Wait => KeyMoveSetting.Deny,
        _ => KeyMoveSetting.Unset,
    };
    private Marrow.Services.Port.Usage.AccountPlanLimitsSnapshot? Report(string keyId) => Guid.TryParse(keyId, out var id)
        && app.Settings.Accounts.Accounts.Any(account => account.Id == id) ? app.AccountSnapshot(id) : null;
    public double? CreditsLeft(string keyId) => Report(keyId)?.Limits?.AccountCredits is { Currency: "USD", Total: var total }
        && double.IsFinite(total) && total >= 0 ? total : null;
    public DateTimeOffset? ObservedAt(string keyId) => Report(keyId) is { Limits.AccountCredits: not null } report ? report.ReadStartedAt : null;
    public bool? IsUsable(string keyId) => Report(keyId)?.Limits?.AccountCredits is { } credits ? credits.IsUsable && credits.Total > 0 : null;
    public bool CanUseAccount(Guid id)
    {
        var account = app.Settings.Accounts.Accounts.FirstOrDefault(account => account.Id == id);
        return account is not null && (!account.Service.IsAPIKeyBased || Pool(account.Service).IsAvailable(id.ToString("D")));
    }
    private ApiKeyPool Pool(ProviderKind service)
    {
        MainActor.AssumeIsolated();
        if (!pools.TryGetValue(service, out var pool)) pools.Add(service, pool = new(service.Id, app.Settings.AccountKeys, this, app.Settings.AccountKeys, clock, moves: this));
        return pool;
    }
    public void BindLive(OpenAICompatibleSession provider, ThreadRuntime runtime, Guid accountId, Action<KeyChoice> selected, Func<bool> owns)
    {
        var pool = Pool(provider.Configuration.Provider);
        if (provider.KeyRotation is IDisposable privateRotation) privateRotation.Dispose();
        if (live.Remove(runtime.ThreadID, out var previous)) previous.Dispose();
        var rotation = new AppApiKeyRotation(this, pool, runtime.ThreadID, accountId, selected, () =>
        {
            var thread = app.Thread(runtime.ThreadID);
            return thread?.ProviderAccountPinID?.ToString("D") ?? (thread?.PillRelay is null ? null : thread.ProviderAccountID?.ToString("D") ?? accountId.ToString("D"));
        }, owns);
        live.Add(runtime.ThreadID, rotation);
        provider.KeyRotation = rotation;
    }
    public void BindPrivate(ProviderSession provider, Guid accountId)
    {
        if (provider is not OpenAICompatibleSession api || !api.Configuration.Provider.IsAPIKeyBased) return;
        api.KeyRotation = new AppApiKeyRotation(this, Pool(api.Configuration.Provider), Guid.NewGuid(), accountId,
            _ => { }, () => accountId.ToString("D"), () => api.IsRunning, canAsk: false);
    }
    public HandshakeEvent? State(Guid thread) => live.GetValueOrDefault(thread)?.State;
    public bool AwaitingChoice(Guid thread) => live.GetValueOrDefault(thread)?.Request is not null;
    internal bool TakeWait(Guid thread, Guid source) => live.GetValueOrDefault(thread)?.TakeWait(source.ToString("D")) == true;
    public void Cancel(Guid thread) { if (live.Remove(thread, out var rotation)) rotation.Dispose(); }
    internal void Retired(AppApiKeyRotation rotation) { if (live.GetValueOrDefault(rotation.ThreadId) == rotation) live.Remove(rotation.ThreadId); }
    internal void Publish(HandshakeEvent value) => Changed?.Invoke(this, value);
    internal Resolution Policy(Guid thread) => decisions.Resolve(thread, DecisionKind.OnLimit);
    internal HandshakeEvent Card(Guid thread, ApiKeyEvent value)
    {
        var request = new DecisionRequest(thread, [DecisionKind.OnLimit], decisions.Resolve(thread, DecisionKind.OnLimit).FirstTime, value.FromKeyId!, value.ToKeyId, null);
        return new(thread, HandshakeEventKind.DecisionRequested, HandshakePhase.Idle, HandshakePhase.Asking,
            value.FromKeyId, value.ToKeyId, HandshakeReason.HardLimit, clock.GetUtcNow(), Request: request);
    }
    public bool TryAnswer(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope)
    {
        MainActor.AssumeIsolated();
        if (live.GetValueOrDefault(thread) is not { Request: not null } rotation) return false;
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (choices.Count != 1 || choices[0].Kind != DecisionKind.OnLimit || choices[0].Value is not (DecisionValues.Switch or DecisionValues.Wait))
            throw new ArgumentException("The API key card needs one switch or wait choice.", nameof(choices));
        var request = rotation.BeginAnswer();
        try
        {
            var before = Policy(thread);
            var expected = scope switch
            {
                RememberScope.ThisThread => new Resolution(DecisionKind.OnLimit, choices[0].Value, ResolutionSource.Thread, false),
                RememberScope.AllThreads when before.Source != ResolutionSource.Thread => new Resolution(DecisionKind.OnLimit, choices[0].Value, ResolutionSource.Settings, false),
                _ => before,
            };
            decisions.Record(thread, choices, scope);
            rotation.ValidatePending(request);
            var allow = choices[0].Value == DecisionValues.Switch && Policy(thread) == expected;
            if (allow && request.FirstTime) app.Settings.AccountHandshakeEnabled = true;
            rotation.ValidatePending(request);
            rotation.Answer(allow, expected, choices[0].Value == DecisionValues.Wait);
        }
        finally { rotation.EndAnswer(); }
        return true;
    }
}
