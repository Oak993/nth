using Marrow.App;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.Usage;

namespace Marrow.App.Runtime;

// Source: DroppyCode/App/Runtime/AutoContinue.swift (B3 52bc792), :1-159.

// :8-159. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class AutoContinue : MainActorModel
{
    // :12
    public const string ContinueMessage = "The usage limit has reset. Continue exactly where you left off and finish the task.";

    // :15. TimeInterval is double seconds.
    public const double Margin = 60;

    // :22-26 (R-ENUM-2). HandedOff's unlabeled UUID has no binding in the source; it is named Value.
    public abstract record LimitOutcome
    {
        private LimitOutcome() { }
        public sealed record Cancelled : LimitOutcome;              // :23
        public sealed record HandedOff(Guid Value) : LimitOutcome;  // :24
        public sealed record Resumed : LimitOutcome;                // :25
    }

    private ValueMap<Guid, DateTimeOffset> resumesAt = ValueMap<Guid, DateTimeOffset>.Empty;
    private ValueMap<Guid, LimitOutcome> outcomes = ValueMap<Guid, LimitOutcome>.Empty;

    // :18
    public ValueMap<Guid, DateTimeOffset> ResumesAt { get => Read(resumesAt); private set => Set(ref resumesAt, value); }

    // :28
    public ValueMap<Guid, LimitOutcome> Outcomes { get => Read(outcomes); private set => Set(ref outcomes, value); }

    // :30-36
    private readonly WeakReference<AppModel> app;
    private readonly Dictionary<Guid, DateTimeOffset?> noted = [];
    private readonly Dictionary<Guid, UsageAccountKey> notedAccounts = [];
    private readonly Dictionary<Guid, UsageAccountKey> waitingAccounts = [];

    // DEVIATION OD-0114 / ED-406: account-store composition; absent explicit readers fail closed.
    public Func<UsageAccountKey, Task<PlanLimits?>>? ReadAccountLimits { get; set; }
    public AccountLimitsScheduler? UsageLimits { get; set; }
    internal Func<DateTimeOffset, string>? FormatForTesting { get; set; }
    public Func<Guid, UsageAccountKey?>? CurrentAccount { get; set; }
    public UsageAccountKey? LimitedAccount(Guid threadID) => notedAccounts.GetValueOrDefault(threadID) ?? waitingAccounts.GetValueOrDefault(threadID);
    private readonly Dictionary<Guid, TaskHandle> waits = [];
    private readonly Dictionary<Guid, Guid> waitTokens = [];
    private readonly Dictionary<Guid, DateTimeOffset> resumeValues = [];
    private readonly Dictionary<Guid, LimitOutcome> outcomeValues = [];

    // PORT-ONLY test boundaries: defaults retain the source's reader and notification calls.
    internal Func<ProviderKind, Task<PlanLimits?>>? ReadLimitsForTesting { get; set; }
    internal Action<Guid, string, string>? NotifyForTesting { get; set; }
    internal Action<Notice>? NoticeForTesting { get; set; }

    // :38-40
    public AutoContinue(AppModel app) =>
        this.app = new WeakReference<AppModel>(app);

    // :43-47. Swift: noteLimit(_:resetsAt:).
    public void NoteLimit(Guid threadID, DateTimeOffset? resetsAt)
    {
        NoteLimitCore(threadID, resetsAt, null);
    }

    public void NoteLimit(Guid threadID, DateTimeOffset? resetsAt, UsageAccountKey account) =>
        NoteLimitCore(threadID, resetsAt, account);

    private void NoteLimitCore(Guid threadID, DateTimeOffset? resetsAt, UsageAccountKey? account)
    {
        if (account is not null && UsageLimits is { } scheduler) _ = scheduler.LimitReachedAsync(account);
        // :44-46; an unknown reset only preserves a timed note belonging to the same account.
        if (notedAccounts.GetValueOrDefault(threadID) == account && noted.TryGetValue(threadID, out var existing) && existing is not null && resetsAt is null) return;
        noted[threadID] = resetsAt;
        if (account is null) notedAccounts.Remove(threadID);
        else notedAccounts[threadID] = account;
    }

    // :51-64. Swift: turnFinished(_:status:continues:).
    public bool TurnFinished(Guid threadID, TurnStatus status, bool continues)
    {
        // :52
        if (!noted.Remove(threadID, out var note)) return false;
        notedAccounts.Remove(threadID, out var limitedAccount);

        // :53-54
        if (!TryGetApp(out var appModel)
            || !appModel.Settings.AutoContinueAfterLimit
            || status != TurnStatus.Failed
            || continues
            || appModel.Thread(threadID) is not { } thread
            || thread.IsHydraHead
            || limitedAccount is { } sourceAccount && sourceAccount.Provider != thread.Provider)
        {
            return false;
        }

        // :55-63
        if (waits.TryGetValue(threadID, out var previousWait)) previousWait.Cancel();
        outcomeValues.Remove(threadID);
        var account = limitedAccount ?? UsageAccountKey.Default(thread.Provider);
        waitingAccounts[threadID] = account;
        UpdateOutcomes();
        var turnID = appModel.ExistingRuntime(threadID)?.Turns.Count > 0
            ? appModel.ExistingRuntime(threadID)!.Turns[appModel.ExistingRuntime(threadID)!.Turns.Count - 1].Id
            : (Guid?)null;
        var token = Guid.NewGuid();
        waitTokens[threadID] = token;
        var weakSelf = new WeakReference<AutoContinue>(this);
        waits[threadID] = MainActor.Spawn(async () =>
        {
            if (weakSelf.TryGetTarget(out var self))
                await self.Wait(threadID, turnID, note, thread.Provider, token, account);
        });
        return true;
    }

    // :67-73. Swift: cancel(_:).
    public void Cancel(Guid threadID)
    {
        // :68-72
        if (waits.TryGetValue(threadID, out var wait)) wait.Cancel();
        waits.Remove(threadID);
        waitTokens.Remove(threadID);
        waitingAccounts.Remove(threadID);
        if (notedAccounts.Remove(threadID, out var cancelledAccount) && !cancelledAccount.IsDefault) noted.Remove(threadID);
        resumeValues.Remove(threadID);
        UpdateResumesAt();
        outcomeValues[threadID] = new LimitOutcome.Cancelled();
        UpdateOutcomes();
    }

    // ED-403: a first-time card and a switch have one limit owner, including migrated/default accounts.
    public void TakeLimitOwnership(Guid threadID)
    {
        Cancel(threadID);
        noted.Remove(threadID);
        notedAccounts.Remove(threadID);
    }

    // :78-85. LimitHandoff is not ported; leave this member unchanged.
    public void HandOff(Guid threadID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:78");

    // :87-121
    private async Task Wait(Guid threadID, Guid? turnID, DateTimeOffset? resetsAt, ProviderKind provider, Guid token, UsageAccountKey account)
    {
        // :88-92
        if (!TryGetApp(out var appModel)) return;
        var resolvedReset = resetsAt;
        if (resolvedReset is null) resolvedReset = account.IsDefault && UsageLimits is null ? await ReadResetTime(account.Provider) : await ReadAccountResetTime(account);
        if (SwiftTask.IsCancelled || !IsCurrentWait(threadID, token)) return;

        // :93-99
        if (resolvedReset is null)
        {
            Notice(threadID, Marrow.Core.Models.Notice.Level.Warning,
                "Usage limit reached, and the provider did not say when it resets. Send a message to continue.");
            Notify(threadID, "Usage limit reached. Send a message to continue.");
            waits.Remove(threadID);
            waitTokens.Remove(threadID);
            waitingAccounts.Remove(threadID);
            return;
        }

        // :100-105
        var resumeAt = SwiftDate.AddingTimeInterval(resolvedReset.Value, Margin);
        var floor = SwiftDate.AddingTimeInterval(SwiftDate.Now, 5);
        if (resumeAt < floor) resumeAt = floor;
        resumeValues[threadID] = resumeAt;
        UpdateResumesAt();
        var when = FormatForTesting is { } format ? format(resumeAt) : Format(resumeAt);
        Notice(threadID, Marrow.Core.Models.Notice.Level.Info, "Usage limit reached. Continuing automatically at " + when + ".", limitMark: true);
        Notify(threadID, "Usage limit reached. Continuing at " + when + ".");
        await SwiftTask.TrySleep(TimeSpan.FromSeconds((resumeAt - SwiftDate.Now).TotalSeconds));

        // :106-121
        if (SwiftTask.IsCancelled || !IsCurrentWait(threadID, token)) return;
        resumeValues.Remove(threadID);
        UpdateResumesAt();
        waits.Remove(threadID);
        waitTokens.Remove(threadID);
        waitingAccounts.Remove(threadID);

        if (ResolveCurrentAccount(appModel, threadID) != account
            || !appModel.Settings.AutoContinueAfterLimit
            || appModel.ExistingRuntime(threadID) is not { } runtime
            || runtime.IsRunning
            || (runtime.Turns.Count > 0 ? runtime.Turns[runtime.Turns.Count - 1].Id : (Guid?)null) != turnID)
        {
            return;
        }

        outcomeValues[threadID] = new LimitOutcome.Resumed();
        UpdateOutcomes();
        runtime.EnqueueFollowUp(ContinueMessage, []);
        FollowUpPrompt? prompt = null;
        for (var index = runtime.FollowUps.Count - 1; index >= 0; index--)
        {
            if (runtime.FollowUps[index].Text == ContinueMessage)
            {
                prompt = runtime.FollowUps[index];
                break;
            }
        }
        if (prompt is not null) runtime.SendFollowUpNow(prompt.Id);
    }

    private UsageAccountKey? ResolveCurrentAccount(AppModel model, Guid threadID)
    {
        if (CurrentAccount is { } resolve) return resolve(threadID);
        if (model.Thread(threadID) is not { } thread) return null;
        var sessionAccount = model.ExistingRuntime(threadID)?.UsageAccount;
        return sessionAccount?.Provider == thread.Provider ? sessionAccount : UsageAccountKey.Default(thread.Provider);
    }

    private async Task<DateTimeOffset?> ReadAccountResetTime(UsageAccountKey account)
    {
        // Explicit accounts never use Settings' provider-global key or Registry's provider-global environment.
        try
        {
            PlanLimits? limits = null;
            if (UsageLimits is { } scheduler)
            {
                await scheduler.LimitReachedAsync(account);
                limits = scheduler.Snapshot(account)?.Limits;
            }
            else if (ReadAccountLimits is { } read) limits = await read(account);
            return SpentReset(limits);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // A rejected, throttled or cancelled account read supplies no reset. Wait checks cancellation
            // before emitting the source's unknown-reset warning; no other account's quota is consulted.
            return null;
        }
    }

    private static DateTimeOffset? SpentReset(PlanLimits? limits)
    {
        PlanLimits.Window? spent = null;
        foreach (var window in limits?.Windows ?? ValueList<PlanLimits.Window>.Empty)
        {
            if (window.ResetsAt is not { } reset || reset <= SwiftDate.Now) continue;
            if (spent is null || window.Percent > spent.Percent) spent = window;
        }
        return spent is null || spent.Percent < 95 ? null : spent.ResetsAt;
    }

    // :123-142
    private async Task<DateTimeOffset?> ReadResetTime(ProviderKind provider)
    {
        if (!TryGetApp(out var appModel) || !PlanLimitsReader.ExposesLimits(provider)) return null;
        PlanLimits? limits;
        if (ReadLimitsForTesting is { } read) limits = await read(provider);
        else if (provider.IsAPIKeyBased)
        {
            var apiKey = appModel.Settings.ApiKey(provider);
            if (apiKey.Length == 0) return null;
            limits = await PlanLimitsReader.Read(provider, apiKey);
        }
        else
        {
            if (appModel.Providers.Executable(provider) is not { } executable) return null;
            limits = await PlanLimitsReader.Read(provider, executable, appModel.Providers.Environment(provider));
        }

        return SpentReset(limits);
    }

    // :144-148
    private void Notice(Guid threadID, Notice.Level level, string message, bool limitMark = false)
    {
        if (!TryGetApp(out var appModel) || appModel.ExistingRuntime(threadID) is not { } runtime) return;
        var notice = new Notice
        {
            NoticeLevel = level,
            Message = message,
            Limit = limitMark ? new LimitNotice { ThreadID = threadID } : null,
        };
        NoticeForTesting?.Invoke(notice);
        runtime.Rehearse(new ProviderEvent.Notice(notice));
    }

    // :150-155
    private void Notify(Guid threadID, string body)
    {
        if (!TryGetApp(out var appModel)
            || !appModel.Settings.NotifyWhenFinished
            || appModel.Thread(threadID) is not { } thread)
        {
            return;
        }

        var onScreen = IsApplicationActive() && appModel.SelectedThreadID == threadID;
        if (onScreen) return;
        if (NotifyForTesting is { } notify) notify(threadID, thread.Title, body);
        else appModel.Notify(threadID, thread.Title, body);
    }

    // :158
    public static string Format(DateTimeOffset date) => UsageLimitSignal.Format(date);

    private bool TryGetApp(out AppModel appModel) => app.TryGetTarget(out appModel!);

    private bool IsCurrentWait(Guid threadID, Guid token) =>
        waitTokens.TryGetValue(threadID, out var currentToken) && currentToken == token;

    // :152 NSApp.isActive: the port's one reading of it, shared with every other site that reads it
    // (Port/ApplicationLifecycle.cs NSApplication.IsActive).
    private static bool IsApplicationActive() => NSApplication.IsActive.Current();

    private void UpdateResumesAt() =>
        ResumesAt = new ValueMap<Guid, DateTimeOffset>(new Dictionary<Guid, DateTimeOffset>(resumeValues));

    private void UpdateOutcomes() =>
        Outcomes = new ValueMap<Guid, LimitOutcome>(new Dictionary<Guid, LimitOutcome>(outcomeValues));
}
