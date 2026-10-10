// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; work/ignis/PLAN.md unit U3). No Swift twin. The app-wide controller:
// one state per chat, one timer per chat on the app's clocks (SwiftDate.Now for stamps, SwiftTask.TrySleep for waits,
// so tests drive it on a manual clock), the keep-warm and tidy-up decisions (asked of the engine), the pause rules, the
// ledger entries and the quiet event lines. It never talks to a model provider itself: a keep-warm is the chat's own
// hidden ping turn (CLIs) or silent request (API sessions), a tidy-up is the chat's own silent compact, both only
// between tasks. Everything runs on the main actor.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Port.Ignis.Runtime;

/// <summary>What the controller reads from the app (AppIgnisEnvironment in production, a fake in tests).</summary>
public interface IIgnisEnvironment
{
    /// <summary>The PC is locked (automatic keep-warm and tidy-up pause).</summary>
    bool IsLocked { get; }

    bool IsArchived(Guid thread);

    /// <summary>The chat is on screen in the active app.</summary>
    bool IsOnScreen(Guid thread);

    /// <summary>The chat's composer holds unsent text.</summary>
    bool HasDraft(Guid thread);

    /// <summary>The fullest of the provider's plan usage windows, in percent (0...100); null when unknown or not a plan.</summary>
    double? FullestPlanWindow(ProviderKind provider);

    IgnisAccountKind Account(ProviderKind provider);
}

public sealed class IgnisController : IIgnisController
{
    /// <summary>Automatic keep-warm and tidy-up pause once any plan window is this full (percent).</summary>
    public const double PlanPausePercent = 95;

    /// <summary>ENGINEERING DECISION: a chat that is not between tasks is asked again this often while something is due.</summary>
    public static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(15);

    /// <summary>ENGINEERING DECISION: "Tidied up" shows this long after a tidy-up ends.</summary>
    public static readonly TimeSpan JustTidiedFor = TimeSpan.FromSeconds(4);

    /// <summary>ENGINEERING DECISION: at most this many keep-warms in one idle stretch, whatever the engine says.</summary>
    public const int MaxPingsPerIdle = 6;

    /// <summary>ENGINEERING DECISION: the chat's own idle-gap history keeps this many gaps.</summary>
    public const int MaxGaps = 20;

    private readonly IIgnisEngine engine;
    private readonly IIgnisEnvironment environment;
    private readonly Dictionary<Guid, Chat> chats = [];

    public IgnisController(IgnisSettings settings, IIgnisPoints points, ISavingsLedger ledger, IIgnisEngine engine, IIgnisEnvironment environment)
    {
        Settings = settings;
        Points = points;
        Ledger = ledger;
        this.engine = engine;
        this.environment = environment;
        settings.Changed += () => Everything();
        points.Changed += () => Everything();
        ledger.Changed += () => Changed?.Invoke(Guid.Empty);
    }

    public event Action<Guid>? Changed;

    public IgnisSettings Settings { get; }

    public IIgnisPoints Points { get; }

    public ISavingsLedger Ledger { get; }

    /// <summary>
    /// How long before the cache goes cold a keep-warm is considered: a tenth of its lifetime, between 15 seconds and
    /// 5 minutes (ENGINEERING DECISION: 30 s on a 5-minute cache, 5 min on a 1-hour cache).
    /// </summary>
    public static TimeSpan Lead(TimeSpan ttl)
    {
        var lead = TimeSpan.FromTicks(ttl.Ticks / 10);
        return lead < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : lead > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : lead;
    }

    /// <summary>
    /// The cache lifetime (PLAN lead decision "TTLs"): from the stream's write fields when it wrote (1 h writes win over
    /// 5 min writes), else the model points (main or sub-agent), else unknown.
    /// </summary>
    public static TimeSpan? Ttl(IgnisTurnUsage? usage, IgnisModelPoints? points, bool subagent)
    {
        if (usage is { Reported: true, CacheWrite1h: > 0 }) return TimeSpan.FromHours(1);
        if (usage is { Reported: true, CacheWrite5m: > 0 }) return TimeSpan.FromMinutes(5);
        var seconds = subagent ? points?.Cache.TtlSubagentSeconds : points?.Cache.TtlMainSeconds;
        return seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    private static DateOnly Today => DateOnly.FromDateTime(SwiftDate.Now.ToLocalTime().DateTime);

    // ---- one chat ----

    private sealed class Head
    {
        public required string AgentID;
        public required string Label;
        public string? Model;
        public TimeSpan? Ttl;
        public DateTimeOffset? WarmAt;
        public long? ContextUsed;
        public long? TidyAt;
    }

    private sealed class Chat
    {
        public WeakReference<IIgnisRuntimePort>? Port;
        public ProviderKind? Provider;
        public string? Model;
        public TimeSpan? Ttl;
        public DateTimeOffset? WarmAt;
        public bool CacheReported = true;
        public long? ContextUsed;
        public long? ContextWindow;
        public long ReadTotal;
        public long InputTotal;
        public bool PingInFlight;
        public IgnisTurnUsage? PingUsage;
        public bool PingSinceUserTurn;
        public int PingsThisIdle;
        public bool Tidying;
        public long? TidyFrom;
        public DateTimeOffset? JustTidiedUntil;
        public DateTimeOffset? IdleSince;
        public readonly List<TimeSpan> Gaps = [];
        public bool UsageLimited;
        public DateTimeOffset? UsageLimitUntil;
        public bool ColdSeen;
        public TaskHandle? Timer;
        public long TimerEpoch;
        public readonly List<Head> Heads = [];

        public IIgnisRuntimePort? Runtime => Port is { } weak && weak.TryGetTarget(out var port) ? port : null;

        public DateTimeOffset? ExpiresAt => WarmAt is { } at && Ttl is { } ttl ? at + ttl : null;
    }

    private Chat Get(Guid thread)
    {
        if (!chats.TryGetValue(thread, out var chat)) chats[thread] = chat = new Chat();
        return chat;
    }

    private IgnisModelPoints? PointsOf(Chat chat) => chat.Provider is { } provider ? Points.Find(provider, chat.Model) : null;

    // ---- IIgnisController ----

    public void Attach(IIgnisRuntimePort port)
    {
        var chat = Get(port.ThreadID);
        chat.Port = new WeakReference<IIgnisRuntimePort>(port);
        chat.Provider = port.Provider;
        chat.Model ??= port.Model;
        if (port.LastRequestAt is { } last && chat.WarmAt is null) chat.WarmAt = last;
        Plan(port.ThreadID);
    }

    public void Detach(Guid thread)
    {
        if (!chats.Remove(thread, out var chat)) return;
        chat.Timer?.Cancel();
        chat.TimerEpoch++;
        Changed?.Invoke(thread);
    }

    public void SetMode(Guid thread, IgnisMode mode) => Settings.SetChat(thread, Settings.ForChat(thread) with { Mode = mode });

    public void SetAuto(Guid thread, bool auto) => Settings.SetChat(thread, Settings.ForChat(thread) with { Auto = auto });

    public void Observe(Guid thread, IgnisTurnUsage usage)
    {
        var chat = Get(thread);
        var now = SwiftDate.Now;
        if (usage.AgentID is { } agent)
        {
            if (Settings.Value.IncludeHeads) ObserveHead(thread, chat, agent, usage, now);
            return;
        }

        if (chat.PingInFlight)
        {
            chat.PingUsage = usage;
            Warm(chat, usage, now);
            Changed?.Invoke(thread);
            return;
        }

        // The tidy-up's own request (the API summary, Claude's /compact): not a user turn and not the chat's size after
        // it; the next real request reports the shorter chat (and the tidy-up's saving against TidyFrom).
        if (chat.Tidying)
        {
            Changed?.Invoke(thread);
            return;
        }

        if (chat.IdleSince is { } idle)
        {
            chat.Gaps.Add(now - idle);
            if (chat.Gaps.Count > MaxGaps) chat.Gaps.RemoveAt(0);
            chat.IdleSince = null;
        }
        if (chat.UsageLimited && chat.UsageLimitUntil is null) chat.UsageLimited = false; // a request went through

        chat.Provider = usage.Provider;
        chat.Model = usage.Model ?? chat.Model;
        var points = PointsOf(chat);
        var mode = Settings.ForChat(thread).Mode;
        if (mode != IgnisMode.Off && usage.Reported && points is not null)
        {
            var saved = engine.ReuseSaved(points, usage);
            if (saved > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.Reuse, saved));
            if (chat.PingSinceUserTurn && usage.CacheRead > 0)
            {
                var kept = engine.RebuildPremium(points, usage.CacheRead);
                if (kept > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.KeptWarm, kept, Count: 0));
            }
            if (chat.TidyFrom is { } before && (usage.ContextUsed ?? usage.TotalInput) is var after && after < before)
            {
                var tidied = engine.TidySaved(points, before, after);
                if (tidied > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.TidyUp, tidied, Count: 0));
            }
        }
        chat.TidyFrom = null;
        chat.PingSinceUserTurn = false;
        chat.PingsThisIdle = 0;
        if (usage.Reported)
        {
            chat.ReadTotal += usage.CacheRead;
            chat.InputTotal += usage.TotalInput;
        }
        Warm(chat, usage, now);
        Changed?.Invoke(thread);
    }

    // The cache was just read or written: its clock restarts, the chat size and lifetime follow the newest numbers.
    private void Warm(Chat chat, IgnisTurnUsage usage, DateTimeOffset now)
    {
        var points = PointsOf(chat);
        chat.CacheReported = usage.Reported;
        chat.ContextUsed = usage.ContextUsed ?? (usage.Reported ? usage.TotalInput : chat.ContextUsed);
        chat.ContextWindow = usage.ContextWindow ?? points?.ContextWindow ?? chat.ContextWindow;
        chat.Ttl = Ttl(usage, points, subagent: false) ?? chat.Ttl;
        chat.WarmAt = now;
        chat.ColdSeen = false;
    }

    private void ObserveHead(Guid thread, Chat chat, string agent, IgnisTurnUsage usage, DateTimeOffset now)
    {
        var head = chat.Heads.Find(candidate => candidate.AgentID == agent);
        if (head is null)
        {
            head = new Head { AgentID = agent, Label = "Head " + (chat.Heads.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            chat.Heads.Add(head);
        }
        var provider = usage.Provider;
        var points = Points.Find(provider, usage.Model);
        var switched = head.Model is { } previous && usage.Model is { } next && !string.Equals(previous, next, StringComparison.Ordinal);
        head.Model = usage.Model ?? head.Model;
        head.Ttl = Ttl(usage, points, subagent: true) ?? head.Ttl;
        head.WarmAt = now;
        head.ContextUsed = usage.ContextUsed ?? (usage.Reported ? usage.TotalInput : head.ContextUsed);
        var tidyAt = points is null ? null : engine.TidyPoint(points);
        var mode = Settings.ForChat(thread).Mode;
        if (switched && mode != IgnisMode.Off && tidyAt != head.TidyAt)
            Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.HeadTidiedAfterSwitch, now,
                AgentID: agent, HeadLabel: head.Label, Model: points?.DisplayName ?? head.Model, AtTokens: tidyAt));
        head.TidyAt = tidyAt;
        if (mode != IgnisMode.Off && usage.Reported && points is not null)
        {
            var saved = engine.ReuseSaved(points, usage);
            if (saved > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.Reuse, saved));
        }
        Changed?.Invoke(thread);
    }

    public void TurnFinished(Guid thread)
    {
        var chat = Get(thread);
        var now = SwiftDate.Now;
        var runtime = chat.Runtime;
        if (chat.Tidying && runtime is IIgnisRuntimeTimeline { IsTidying: false, LastTidyFailed: true })
        {
            chat.Tidying = false; // nothing was tidied: no line, no ledger entry; TidyFrom stays, so no retry before the next request
        }
        else if (chat.Tidying && runtime is IIgnisRuntimeTimeline { IsTidying: false })
        {
            chat.Tidying = false;
            chat.JustTidiedUntil = now + JustTidiedFor;
            chat.WarmAt = now; // the rebuilt, shorter chat was just written
            chat.ColdSeen = false;
            Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.TidyUp, 0m));
            Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.TidiedUp, now, AtTokens: chat.TidyFrom));
            ArmJustTidied(thread, chat);
        }
        else if (chat.PingInFlight)
        {
            chat.PingInFlight = false;
            FinishPing(thread, chat, now);
        }
        else if (!chat.Tidying)
        {
            chat.IdleSince = now;
            chat.PingsThisIdle = 0;
        }
        Plan(thread);
        Changed?.Invoke(thread);
    }

    private void FinishPing(Guid thread, Chat chat, DateTimeOffset now)
    {
        var usage = chat.PingUsage;
        chat.PingUsage = null;
        var points = PointsOf(chat);
        decimal? cost = usage is { Reported: true } && points is not null ? engine.PingCost(points, chat.ContextUsed ?? usage.TotalInput) : null;
        var kind = usage is { Reported: true, CacheRead: 0 } && usage.CacheWrite > 0 ? IgnisEventKind.WarmedAgain : IgnisEventKind.KeptWarm;
        if (usage is null) chat.WarmAt = now; // nothing reported: the request itself restarted the cache's clock
        Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Spent, IgnisSource.KeptWarm, cost ?? 0m));
        Record(thread, chat, new IgnisEvent(NewEventID(), thread, kind, now, cost, IgnisAmountKind.Cost));
    }

    public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt)
    {
        var chat = Get(thread);
        chat.UsageLimited = true;
        chat.UsageLimitUntil = resetsAt;
        Plan(thread);
        Changed?.Invoke(thread);
    }

    public void KeepWarmNow(Guid thread)
    {
        var chat = Get(thread);
        if (!Status(thread).CanKeepWarm || chat.Runtime is not { } runtime) return;
        if (KeepWarm(chat, runtime)) Changed?.Invoke(thread);
    }

    public void TidyUpNow(Guid thread)
    {
        var chat = Get(thread);
        if (!Status(thread).CanTidy || chat.Runtime is not { } runtime) return;
        if (Tidy(chat, runtime)) Changed?.Invoke(thread);
    }

    // ---- planning ----

    private bool SupportsKeepWarm(Chat chat) => PointsOf(chat) is { Cache.KeepWarm: true };

    /// <summary>Why automatic work is paused (the first that applies), NotSupported when only keep-warm is off for the model.</summary>
    private IgnisPause Pause(Guid thread, Chat chat)
    {
        if (environment.IsLocked) return IgnisPause.Locked;
        if (environment.IsArchived(thread)) return IgnisPause.Archived;
        if (chat.UsageLimited)
        {
            if (chat.UsageLimitUntil is { } until && SwiftDate.Now >= until) { chat.UsageLimited = false; chat.UsageLimitUntil = null; }
            else return IgnisPause.UsageLimit;
        }
        if (chat.Provider is { } provider && environment.FullestPlanWindow(provider) is { } percent && percent >= PlanPausePercent)
            return IgnisPause.PlanNearlyFull;
        if (chat.Provider is not null && !SupportsKeepWarm(chat)) return IgnisPause.NotSupported;
        return IgnisPause.None;
    }

    private long? TidyPoint(Chat chat) => PointsOf(chat) is { } points ? engine.TidyPoint(points) : null;

    // TidyFrom stays set from a tidy-up until the next request reports the shorter chat: no second tidy-up before that.
    private bool NeedsTidy(Chat chat) =>
        !chat.Tidying && chat.TidyFrom is null && chat.ContextUsed is { } used && TidyPoint(chat) is { } at && used >= at;

    private void Plan(Guid thread)
    {
        var chat = Get(thread);
        chat.Timer?.Cancel();
        chat.Timer = null;
        chat.TimerEpoch++;
        if (chat.Runtime is not { } runtime) return;
        var preference = Settings.ForChat(thread);
        if (preference.Mode == IgnisMode.Off) return;

        var now = SwiftDate.Now;
        var pause = Pause(thread, chat);
        var blocked = pause is not (IgnisPause.None or IgnisPause.NotSupported);
        var expires = chat.ExpiresAt;
        DateTimeOffset? wake = expires is { } cold && cold > now ? cold : null;
        if (expires is { } passed && passed <= now) WentCold(thread, chat, passed);

        void WakeBy(DateTimeOffset at) => wake = wake is { } current && current <= at ? current : at;

        if (preference.Auto && !blocked && !chat.PingInFlight)
        {
            if (preference.Mode == IgnisMode.WarmAndTidy && NeedsTidy(chat))
            {
                if (runtime.IsBetweenTasks) { Tidy(chat, runtime); return; }
                WakeBy(now + BusyRetry);
            }
            else if (pause == IgnisPause.None && expires is { } exp && exp > now && chat.Ttl is { } ttl
                && chat.PingsThisIdle < MaxPingsPerIdle && !chat.Tidying)
            {
                var due = exp - Lead(ttl);
                if (due > now) WakeBy(due);
                else if (!runtime.IsBetweenTasks) WakeBy(now + BusyRetry < exp ? now + BusyRetry : exp);
                else if (ShouldKeepWarm(thread, chat, runtime, exp, ttl, now)) { KeepWarm(chat, runtime); return; }
            }
        }

        if (wake is not { } at) return;
        var epoch = chat.TimerEpoch;
        chat.Timer = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(at - SwiftDate.Now);
            if (SwiftTask.IsCancelled || !chats.TryGetValue(thread, out var current) || current.TimerEpoch != epoch) return;
            current.Timer = null;
            Plan(thread);
            Changed?.Invoke(thread);
        });
    }

    private bool ShouldKeepWarm(Guid thread, Chat chat, IIgnisRuntimePort runtime, DateTimeOffset expires, TimeSpan ttl, DateTimeOffset now)
    {
        if (PointsOf(chat) is not { } points) return false;
        var question = new IgnisKeepWarmQuestion(
            thread, runtime.Provider, points, chat.ContextUsed ?? 0, ttl,
            chat.IdleSince is { } idle ? now - idle : TimeSpan.Zero, expires - now, chat.Gaps.ToArray(),
            environment.HasDraft(thread), environment.IsOnScreen(thread));
        return engine.ShouldKeepWarm(question);
    }

    private bool KeepWarm(Chat chat, IIgnisRuntimePort runtime)
    {
        var sent = runtime.CanKeepWarmSilently ? runtime.KeepWarmSilently() : runtime.SendHiddenPing();
        if (!sent) return false;
        chat.PingInFlight = true;
        chat.PingUsage = null;
        chat.PingSinceUserTurn = true;
        chat.PingsThisIdle++;
        return true;
    }

    private bool Tidy(Chat chat, IIgnisRuntimePort runtime)
    {
        var before = chat.ContextUsed;
        if (!runtime.Tidy(Settings.Value.KeepTurns)) return false;
        chat.Tidying = true;
        chat.TidyFrom = before;
        return true;
    }

    // The cache's lifetime ran out with no keep-warm: one quiet line and the loss its rebuild will cost (once per warm spell).
    private void WentCold(Guid thread, Chat chat, DateTimeOffset at)
    {
        if (chat.ColdSeen) return;
        chat.ColdSeen = true;
        if (environment.IsArchived(thread)) return;
        decimal? lost = null;
        if (chat.CacheReported && chat.ContextUsed is { } context && context > 0 && PointsOf(chat) is { } points)
        {
            lost = engine.RebuildPremium(points, context);
            if (lost > 0m) Ledger.Record(new IgnisLedgerEntry(at, thread, IgnisLedgerKind.Lost, null, lost.Value));
        }
        Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.WentCold, at, lost, IgnisAmountKind.Lost));
    }

    private void ArmJustTidied(Guid thread, Chat chat)
    {
        var until = chat.JustTidiedUntil;
        _ = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(JustTidiedFor);
            if (chats.TryGetValue(thread, out var current) && current.JustTidiedUntil == until) Changed?.Invoke(thread);
        });
    }

    private void Record(Guid thread, Chat chat, IgnisEvent @event)
    {
        Ledger.Record(@event);
        if (chat.Runtime is IIgnisRuntimeTimeline timeline) timeline.AppendEvent(@event);
    }

    private static string NewEventID() => Guid.NewGuid().ToString("D");

    private void Everything()
    {
        foreach (var thread in chats.Keys.ToArray()) Plan(thread);
        Changed?.Invoke(Guid.Empty);
    }

    // ---- status ----

    public IgnisChatStatus Status(Guid thread)
    {
        var preference = Settings.ForChat(thread);
        var chat = chats.GetValueOrDefault(thread);
        var account = chat?.Provider is { } kind ? environment.Account(kind) : IgnisAccountKind.Plan;
        if (chat is null) return IgnisChatStatus.Unknown(thread, preference.Mode, preference.Auto, account);

        var now = SwiftDate.Now;
        var points = PointsOf(chat);
        var day = Ledger.ChatDay(thread, Today);
        var off = preference.Mode == IgnisMode.Off;
        var expires = chat.ExpiresAt;
        var cache = off ? IgnisCacheState.Off : expires is { } at && at > now ? IgnisCacheState.Warm : IgnisCacheState.Cold;
        var pause = Pause(thread, chat);
        var runtime = chat.Runtime;
        var between = runtime?.IsBetweenTasks == true;
        var next = off ? IgnisNext.Off
            : chat.Tidying ? IgnisNext.Tidying
            : !preference.Auto ? (preference.Mode == IgnisMode.KeepWarm ? IgnisNext.AutoOffKeepWarm : IgnisNext.AutoOffWarmAndTidy)
            : cache == IgnisCacheState.Cold && chat.WarmAt is not null ? IgnisNext.Cold
            : preference.Mode == IgnisMode.KeepWarm ? IgnisNext.KeepWarm : IgnisNext.WarmAndTidy;
        var heads = Settings.Value.IncludeHeads
            ? chat.Heads.Select(head =>
            {
                var headExpires = head.WarmAt is { } warm && head.Ttl is { } ttl ? warm + ttl : (DateTimeOffset?)null;
                var headCache = off ? IgnisCacheState.Off : headExpires is { } e && e > now ? IgnisCacheState.Warm : IgnisCacheState.Cold;
                return new IgnisHeadStatus(head.AgentID, head.Label, head.Model, headCache, headCache == IgnisCacheState.Warm ? headExpires : null, head.ContextUsed, head.TidyAt);
            }).ToArray()
            : [];
        return new IgnisChatStatus
        {
            ThreadID = thread,
            Mode = preference.Mode,
            Auto = preference.Auto,
            Provider = chat.Provider,
            Model = chat.Model,
            ModelDisplayName = points?.DisplayName ?? chat.Model,
            Cache = cache,
            ExpiresAt = cache == IgnisCacheState.Warm ? expires : null,
            Ttl = chat.Ttl,
            CacheReported = chat.CacheReported,
            PingsToday = day.Pings,
            ContextUsed = chat.ContextUsed,
            ContextWindow = chat.ContextWindow ?? points?.ContextWindow,
            TidyAt = points is null ? null : engine.TidyPoint(points),
            TidyReason = points?.TidyReason,
            SavedToday = day.Saved,
            LostToday = day.Lost,
            SavedBySource = new Dictionary<IgnisSource, decimal>
            {
                [IgnisSource.Reuse] = day.Reuse, [IgnisSource.KeptWarm] = day.KeptWarm,
                [IgnisSource.TidyUp] = day.TidyUp, [IgnisSource.SmartHelpers] = day.SmartHelpers,
            },
            ReuseRate = chat.CacheReported && chat.InputTotal > 0 ? (double)chat.ReadTotal / chat.InputTotal : null,
            Next = next,
            Pause = off ? IgnisPause.None : pause,
            KeepingWarm = chat.PingInFlight,
            Tidying = chat.Tidying,
            JustTidied = chat.JustTidiedUntil is { } until && until > now,
            CanKeepWarm = !off && between && !chat.PingInFlight && !chat.Tidying && SupportsKeepWarm(chat) && pause != IgnisPause.UsageLimit,
            CanTidy = preference.Mode == IgnisMode.WarmAndTidy && between && !chat.Tidying && !chat.PingInFlight,
            Account = account,
            Heads = heads,
        };
    }

    public string Report(Guid thread)
    {
        var status = Status(thread);
        var today = Today;
        var week = Ledger.Days(today.AddDays(-6), today).Sum(day => day.Saved);
        var accountLine = status.Account == IgnisAccountKind.ApiKey && status.ChatCost is { } cost
            ? IgnisWords.ReportApiLine(cost)
            : status.WeekShare is { } share ? IgnisWords.WeekShare(share) : "";
        return IgnisWords.Report(new IgnisReport(
            SwiftDate.Now.ToLocalTime(), status.ModelDisplayName ?? status.Model ?? "", status.Mode, status.Auto,
            status.SavedToday, status.SavedBySource, status.LostToday, status.ReuseRate, status.ContextFill,
            accountLine, week, Ledger.AllTimeSaved));
    }
}
