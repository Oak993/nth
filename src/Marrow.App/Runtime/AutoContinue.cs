using Marrow.App;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

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
        // :44-46
        if (noted.TryGetValue(threadID, out var existing) && existing is not null && resetsAt is null) return;
        noted[threadID] = resetsAt;
    }

    // :51-64. Swift: turnFinished(_:status:continues:).
    public bool TurnFinished(Guid threadID, TurnStatus status, bool continues)
    {
        // :52
        if (!noted.Remove(threadID, out var note)) return false;

        // :53-54
        if (!TryGetApp(out var appModel)
            || !appModel.Settings.AutoContinueAfterLimit
            || status != TurnStatus.Failed
            || continues
            || appModel.Thread(threadID) is not { } thread
            || thread.IsHydraHead)
        {
            return false;
        }

        // :55-63
        if (waits.TryGetValue(threadID, out var previousWait)) previousWait.Cancel();
        outcomeValues.Remove(threadID);
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
                await self.Wait(threadID, turnID, note, thread.Provider, token);
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
        resumeValues.Remove(threadID);
        UpdateResumesAt();
        outcomeValues[threadID] = new LimitOutcome.Cancelled();
        UpdateOutcomes();
    }

    // :78-85 handOff is intentionally absent: PR-RUNTIME-263, dead_code.md entry 22.
    // B3 deleted its only caller (LimitResumeBadge); the live path is Wait/Resume below.

    // :87-121
    private async Task Wait(Guid threadID, Guid? turnID, DateTimeOffset? resetsAt, ProviderKind provider, Guid token)
    {
        // :88-92
        if (!TryGetApp(out var appModel)) return;
        var resolvedReset = resetsAt;
        if (resolvedReset is null) resolvedReset = await ReadResetTime(provider);
        if (SwiftTask.IsCancelled || !IsCurrentWait(threadID, token)) return;

        // :93-99
        if (resolvedReset is null)
        {
            Notice(threadID, Marrow.Core.Models.Notice.Level.Warning,
                "Usage limit reached, and the provider did not say when it resets. Send a message to continue.");
            Notify(threadID, "Usage limit reached. Send a message to continue.");
            waits.Remove(threadID);
            waitTokens.Remove(threadID);
            return;
        }

        // :100-105
        var resumeAt = SwiftDate.AddingTimeInterval(resolvedReset.Value, Margin);
        var floor = SwiftDate.AddingTimeInterval(SwiftDate.Now, 5);
        if (resumeAt < floor) resumeAt = floor;
        resumeValues[threadID] = resumeAt;
        UpdateResumesAt();
        var when = Format(resumeAt);
        Notice(threadID, Marrow.Core.Models.Notice.Level.Info, "Usage limit reached. Continuing automatically at " + when + ".", limitMark: true);
        Notify(threadID, "Usage limit reached. Continuing at " + when + ".");
        await SwiftTask.TrySleep(TimeSpan.FromSeconds((resumeAt - SwiftDate.Now).TotalSeconds));

        // :106-121
        if (SwiftTask.IsCancelled || !IsCurrentWait(threadID, token)) return;
        resumeValues.Remove(threadID);
        UpdateResumesAt();
        waits.Remove(threadID);
        waitTokens.Remove(threadID);

        if (!appModel.Settings.AutoContinueAfterLimit
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

        PlanLimits.Window? spent = null;
        foreach (var window in limits?.Windows ?? ValueList<PlanLimits.Window>.Empty)
        {
            if (window.ResetsAt is not { } reset || reset <= SwiftDate.Now) continue;
            if (spent is null || window.Percent > spent.Percent) spent = window;
        }
        if (spent is null || spent.Percent < 95) return null;
        return spent.ResetsAt;
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
