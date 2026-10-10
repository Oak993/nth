// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; work/ignis/PLAN.md unit U3). No Swift twin. The app-wide controller:
// one state per chat, one timer per chat on the app's clocks (SwiftDate.Now for stamps, SwiftTask.TrySleep for waits,
// so tests drive it on a manual clock), the keep-warm and tidy-up decisions (asked of the engine), the pause rules, the
// ledger entries and the quiet event lines. It never talks to a model provider itself: a keep-warm is the chat's own
// hidden ping turn (CLIs) or silent request (API sessions), a tidy-up is the chat's own silent compact, both only
// between tasks. Everything runs on the main actor.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.App.Port.Ignis.Runtime;

/// <summary>What the controller reads from the app (AppIgnisEnvironment in production, a fake in tests).</summary>
public interface IIgnisEnvironment
{
    /// <summary>The PC is locked. ED-NEW-ignis-guards: read no longer (owner 2026-10-10: he often starts work and locks the PC).</summary>
    bool IsLocked { get; }

    bool IsArchived(Guid thread);

    /// <summary>The chat is on screen in the active app.</summary>
    bool IsOnScreen(Guid thread);

    /// <summary>The chat's composer holds unsent text.</summary>
    bool HasDraft(Guid thread);

    /// <summary>The fullest of the provider's plan usage windows, in percent (0...100); null when unknown or not a plan.</summary>
    double? FullestPlanWindow(ProviderKind provider);

    IgnisAccountKind Account(ProviderKind provider);

    /// <summary>The installed CLI's version as the provider check read it ("2.1.296"); null: unknown (ED-NEW-ignis-runtime).</summary>
    string? CliVersion(ProviderKind provider) => null;

    /// <summary>The user's own prompt-cache lifetime choices in the Claude settings files a launch reads (read only).</summary>
    IReadOnlySet<string> ClaudeCacheSettings(IReadOnlyDictionary<string, string> environment, string? workingDirectory) => new HashSet<string>();
}

public sealed class IgnisController : IIgnisController
{
    /// <summary>Automatic keep-warm and tidy-up pause once any plan window is this full (percent).</summary>
    public const double PlanPausePercent = 95;

    /// <summary>ENGINEERING DECISION: a chat that is not between tasks is asked again this often while something is due.</summary>
    public static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(15);

    /// <summary>ENGINEERING DECISION: "Tidied up" shows this long after a tidy-up ends.</summary>
    public static readonly TimeSpan JustTidiedFor = TimeSpan.FromSeconds(4);

    /// <summary>ENGINEERING DECISION: the chat's own idle-gap history keeps this many gaps.</summary>
    public const int MaxGaps = 20;

    private static readonly Logger Log = new(Logger.Subsystem, "ignis");

    private readonly IIgnisEngine engine;
    private readonly IIgnisEnvironment environment;
    private readonly IIgnisGapHistory gaps;
    private readonly Dictionary<Guid, Chat> chats = [];
    private int ledgerRevision;
    private (DateOnly Day, int Revision, bool Over)? dailyLimit;

    public IgnisController(IgnisSettings settings, IIgnisPoints points, ISavingsLedger ledger, IIgnisEngine engine, IIgnisEnvironment environment, IIgnisGapHistory? gaps = null)
    {
        Settings = settings;
        Points = points;
        Ledger = ledger;
        this.engine = engine;
        this.environment = environment;
        this.gaps = gaps ?? new IgnisGapMemory();
        settings.Changed += () => Everything();
        points.Changed += () => Everything();
        ledger.Changed += () => { ledgerRevision++; Changed?.Invoke(Guid.Empty); };
    }

    public event Action<Guid>? Changed;

    public IgnisSettings Settings { get; }

    public IIgnisPoints Points { get; }

    public ISavingsLedger Ledger { get; }

    /// <summary>What Ignis learned of each provider's request rhythm (ED-NEW-ignis-runtime).</summary>
    public IIgnisGapHistory Gaps => gaps;

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
        public TimeSpan? ObservedTtl;
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
        /// <summary>The lifetime the main chat's writes last showed (1 h or 5 min); it outlives read-only turns.</summary>
        public TimeSpan? ObservedTtl;
        /// <summary>The CLI said the chat now runs on paid usage credits (5-minute main cache) until 1-hour writes return.</summary>
        public bool CreditsHint;
        /// <summary>The inherited environment says the main conversation defaults to 5 minutes (API key, cloud provider).</summary>
        public bool ShortByEnvironment;
        /// <summary>What nth chose for the two cache buckets at the chat's last Claude launch.</summary>
        public IgnisLifetimeChoice? Lifetimes;
        // ---- ED-NEW-ignis-guards ----
        /// <summary>The user's last real turn (its end, or its first usage); ranks the three chats kept warm.</summary>
        public DateTimeOffset? LastUsedAt;
        /// <summary>The user put the flame out; the next real turn relights it.</summary>
        public bool Cooled;
        /// <summary>The user turned keep-warm back on ("Turn on") while the cache was cold: lit until the next message warms it.</summary>
        public bool Armed;
        /// <summary>Ignis has kept this chat warm (its sidebar flame shows, lit or cooled).</summary>
        public bool EverLit;
        /// <summary>The hold already written to History in this idle stretch.</summary>
        public IgnisKeepWarmHold LoggedHold;
        /// <summary>The main chat's last turns' cache numbers (the never-reused check).</summary>
        public readonly List<IgnisCacheTurn> CacheTurns = [];
        public IgnisCacheMissCause? NotReused;
        /// <summary>The model a switch-cost notice was shown for, until the next real turn.</summary>
        public (ProviderKind Provider, string? Model)? NoticeFor;

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
        chat.LastUsedAt ??= port.LastRequestAt;
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
            ObserveHead(thread, chat, agent, usage, now); // learns always; its list, lines and money follow "Include Hydra heads"
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

        var idleSince = chat.IdleSince;
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
        // The idle gap Ignis learns ends where the turn's first request went out (the turn's start), not at its result.
        TimeSpan? gap = idleSince is { } since
            ? (chat.Runtime?.TurnStartedAt is { } started && started >= since && started <= now ? started : now) - since
            : null;
        var longerMain = chat.Lifetimes?.Main.SetsLonger == true;
        if (mode != IgnisMode.Off && usage.Reported)
            gaps.Add(usage.Provider, IgnisCacheBucket.Main, new IgnisRequestSample(now, usage.Model ?? chat.Model, gap?.TotalSeconds,
                chat.ContextUsed ?? 0, usage.CacheWrite5m, usage.CacheWrite1h, usage.CacheWriteUnknownTtl, longerMain));
        if (mode != IgnisMode.Off && usage.Reported && points is not null)
        {
            var saved = engine.ReuseSaved(points, usage);
            if (saved > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.Reuse, saved));
            if (chat.PingSinceUserTurn && usage.CacheRead > 0)
            {
                var kept = engine.RebuildPremium(points, usage.CacheRead);
                if (kept > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.KeptWarm, kept, Count: 0));
            }
            else if (longerMain && gap is { } idleGap && chat.ContextUsed is { } prefix)
            {
                // nth's 1-hour main cache and no ping in the break: a read after more than 5 minutes is a rebuild
                // avoided. Credited here only, never as "Kept warm" (a ping since the user's turn takes the branch above).
                var longer = engine.LongerMemorySaved(points, prefix, usage.CacheRead, idleGap, IgnisCacheLifetime.Long);
                if (longer > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.LongerMemory, longer, Count: 0));
            }
            if (longerMain)
            {
                var extra = engine.LongerMemoryCost(points, usage.CacheWrite1h, usage.TotalInput);
                if (extra > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Spent, IgnisSource.LongerMemory, extra, Count: 0));
            }
            if (chat.TidyFrom is { } before && (usage.ContextUsed ?? usage.TotalInput) is var after && after < before)
            {
                var tidied = engine.TidySaved(points, before, after);
                if (tidied > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.TidyUp, tidied, Count: 0));
            }
        }
        chat.TidyFrom = null;
        chat.PingSinceUserTurn = false;
        // ED-NEW-ignis-guards: a real turn relights the flame, starts a new idle stretch and ends a switch notice.
        chat.Cooled = false;
        chat.Armed = false;
        chat.LoggedHold = IgnisKeepWarmHold.None;
        chat.LastUsedAt = now;
        chat.NoticeFor = null;
        if (usage.Reported)
        {
            chat.ReadTotal += usage.CacheRead;
            chat.InputTotal += usage.TotalInput;
            CheckReuse(thread, chat, usage, now, mode);
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
        // The lifetime the writes show wins and lasts until other writes show another (a read-only turn proves nothing);
        // 5-minute writes on a plan whose points say 1 hour mean paid usage credits, and the 5 minutes rule the keep-warm.
        chat.ObservedTtl = (usage.Reported ? IgnisKeepWarm.TtlFromUsage(usage) : null) ?? chat.ObservedTtl;
        if (chat.ObservedTtl >= IgnisCacheLifetime.Long) chat.CreditsHint = false;
        chat.Ttl = ChatTtl(chat, points) ?? chat.Ttl;
        chat.WarmAt = now;
        chat.ColdSeen = false;
    }

    // Every helper at every depth (Claude subagents of subagents, Codex sub-threads, Hydra heads) is its own head, keyed by
    // the id its usage carries: one entry per agent, each request counted once. Learning runs whenever Ignis is on; the
    // head list, its lines and its money follow "Include Hydra heads".
    private void ObserveHead(Guid thread, Chat chat, string agent, IgnisTurnUsage usage, DateTimeOffset now)
    {
        var include = Settings.Value.IncludeHeads;
        var head = chat.Heads.Find(candidate => candidate.AgentID == agent);
        if (head is null)
        {
            head = new Head { AgentID = agent, Label = "Head " + (chat.Heads.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            chat.Heads.Add(head);
        }
        var provider = usage.Provider;
        var points = Points.Find(provider, usage.Model ?? head.Model);
        var previousAt = head.WarmAt;
        var previousContext = head.ContextUsed;
        var previousTtl = head.Ttl;
        var switched = head.Model is { } previous && usage.Model is { } next && !string.Equals(previous, next, StringComparison.Ordinal);
        head.Model = usage.Model ?? head.Model;
        head.ObservedTtl = (usage.Reported ? IgnisKeepWarm.TtlFromUsage(usage) : null) ?? head.ObservedTtl;
        head.Ttl = head.ObservedTtl ?? Ttl(null, points, subagent: true) ?? head.Ttl;
        head.WarmAt = now;
        head.ContextUsed = usage.ContextUsed ?? (usage.Reported ? usage.TotalInput : head.ContextUsed);
        var tidyAt = points is null ? null : engine.TidyPoint(points);
        var mode = Settings.ForChat(thread).Mode;
        if (include && switched && mode != IgnisMode.Off && tidyAt != head.TidyAt)
            Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.HeadTidiedAfterSwitch, now,
                AgentID: agent, HeadLabel: head.Label, Model: points?.DisplayName ?? head.Model, AtTokens: tidyAt));
        head.TidyAt = tidyAt;
        var longer = provider == ProviderKind.Claude && chat.Lifetimes?.Helpers.SetsLonger == true;
        TimeSpan? gap = previousAt is { } at ? now - at : null;
        if (mode != IgnisMode.Off && usage.Reported)
            gaps.Add(provider, IgnisCacheBucket.Helpers, new IgnisRequestSample(now, head.Model, gap?.TotalSeconds, previousContext ?? 0,
                usage.CacheWrite5m, usage.CacheWrite1h, usage.CacheWriteUnknownTtl, longer));
        if (include && mode != IgnisMode.Off && usage.Reported && points is not null)
        {
            var saved = engine.ReuseSaved(points, usage);
            if (saved > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.Reuse, saved));
            if (gap is { } idle && previousContext is { } prefix && prefix > 0)
            {
                if (previousTtl is { } ttl && idle > ttl && usage.CacheWrite > 0 && Math.Min(prefix - usage.CacheRead, usage.CacheWrite) > 0)
                {
                    // The helper's cache ran out between its requests and this one wrote it again: a missed rebuild. The
                    // part it still read was alive (a longer lifetime than assumed), so only the rewritten rest counts.
                    var missed = engine.RebuildPremium(points, Math.Min(prefix - usage.CacheRead, usage.CacheWrite), ttl);
                    if (missed > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Missed, null, missed, Count: 0));
                }
                else if (longer && usage.CacheRead > 0)
                {
                    var avoided = engine.LongerMemorySaved(points, prefix, usage.CacheRead, idle, previousTtl ?? IgnisCacheLifetime.Long);
                    if (avoided > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Saved, IgnisSource.LongerMemory, avoided, Count: 0));
                }
            }
            if (longer)
            {
                var extra = engine.LongerMemoryCost(points, usage.CacheWrite1h, usage.TotalInput);
                if (extra > 0m) Ledger.Record(new IgnisLedgerEntry(now, thread, IgnisLedgerKind.Spent, IgnisSource.LongerMemory, extra, Count: 0));
            }
        }
        Changed?.Invoke(thread);
    }

    /// <summary>The main chat's lifetime: 5 minutes while paid credits are hinted and no 1-hour write contradicts it; else what its writes showed; else the points'.</summary>
    private static TimeSpan? ChatTtl(Chat chat, IgnisModelPoints? points) =>
        chat.CreditsHint && !(chat.ObservedTtl >= IgnisCacheLifetime.Long) && chat.Lifetimes?.Main.SetsLonger != true ? IgnisCacheLifetime.Short
        : chat.ObservedTtl ?? Ttl(null, points, subagent: false);

    /// <summary>The main chat runs on paid usage credits: hinted by the CLI, or 5-minute writes where the points promise 1 hour.</summary>
    private bool OnPaidCredits(Chat chat)
    {
        if (chat.Provider != ProviderKind.Claude || chat.Lifetimes?.Main.SetsLonger == true) return false;
        if (chat.CreditsHint && !(chat.ObservedTtl >= IgnisCacheLifetime.Long)) return true;
        return !chat.ShortByEnvironment && chat.ObservedTtl is { } observed && observed < IgnisCacheLifetime.Long
            && PointsOf(chat)?.Cache.TtlMainSeconds is { } main && main >= IgnisCacheLifetime.Long.TotalSeconds;
    }

    public void CreditsHint(Guid thread, bool onCredits)
    {
        var chat = Get(thread);
        if (chat.CreditsHint == onCredits) return;
        chat.CreditsHint = onCredits;
        // The 1-hour writes seen before the switch are stale now; the next writes decide again (1 h clears the hint).
        if (onCredits && chat.ObservedTtl >= IgnisCacheLifetime.Long) chat.ObservedTtl = null;
        chat.Ttl = ChatTtl(chat, PointsOf(chat)) ?? chat.Ttl;
        Plan(thread);
        Changed?.Invoke(thread);
    }

    /// <summary>
    /// The prompt-cache lifetimes for a Claude CLI launch (IgnisSeams.CacheLifetimes, ED-NEW-ignis-runtime): the pure
    /// decision over what Ignis learned, the chat's mode, the installed CLI and the user's own choices. The verdicts are
    /// kept for the chat's status and its accounting. Null: not a Claude launch.
    /// </summary>
    public IgnisLifetimeChoice? ChooseCacheLifetimes(IgnisLifetimeQuery query)
    {
        if (query.Provider != ProviderKind.Claude) return null;
        var preferences = Settings.Value;
        var mode = query.ThreadID is { } id ? Settings.ForChat(id).Mode : preferences.DefaultMode;
        var choice = IgnisCacheLifetime.Decide(new IgnisLifetimeInput
        {
            Provider = query.Provider,
            Mode = mode,
            IncludeHeads = preferences.IncludeHeads,
            CliVersion = environment.CliVersion(query.Provider),
            Environment = query.Environment,
            UserSettings = mode == IgnisMode.Off ? new HashSet<string>() : environment.ClaudeCacheSettings(query.Environment, query.WorkingDirectory),
            Main = gaps.Samples(query.Provider, IgnisCacheBucket.Main),
            Helpers = gaps.Samples(query.Provider, IgnisCacheBucket.Helpers),
            MainModel = query.MainModel,
            PointsFor = model => Points.Find(query.Provider, model ?? query.MainModel),
        });
        Log.Info("cache lifetimes for " + (query.ThreadID?.ToString("D") ?? "a chat") + ": main " + choice.Main.Reason + ", helpers " + choice.Helpers.Reason
            + " (" + choice.Helpers.Gaps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " gaps)");
        if (query.ThreadID is { } thread)
        {
            var chat = Get(thread);
            chat.Lifetimes = choice;
            chat.ShortByEnvironment = IgnisCacheLifetime.EnvironmentSaysShortMain(query.Environment);
            Changed?.Invoke(thread);
        }
        return choice;
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
            chat.LastUsedAt = now;
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

    private bool SupportsKeepWarm(Chat chat) => chat.Provider is { } provider && KeepWarmAllowed(provider) && PointsOf(chat) is { Cache.KeepWarm: true };

    /// <summary>
    /// ENGINEERING DECISION (ED-NEW-ignis-runtime, lead research 2026-10-10): keep-warm can run only where a ping is worth
    /// money and can reach the same cache without changing what the model reads beyond today's Claude ping turn: Claude,
    /// and the API routes OpenRouter (Anthropic/Gemini upstreams, 5 minutes) and OpenCode Zen; the points still decide per
    /// model. Never: Codex (a ping would be a turn in the thread's history; the 30-minute cache is refreshed by every
    /// read anyway), DeepSeek (hours to days), Meta and pi (retention hints instead), Grok, Z.ai (TTL unknown; a Coding Plan
    /// ping spends a prompt), Antigravity, Copilot, Cursor, Devin, Command Code, OpenCode Go (quota or no token view) and
    /// local servers (no money). Those keep their savings report.
    /// </summary>
    public static bool KeepWarmAllowed(ProviderKind provider) => provider is ProviderKind.Claude or ProviderKind.OpenRouter or ProviderKind.Opencode;

    /// <summary>
    /// Why automatic work is paused (the first that applies), NotSupported when only keep-warm is off for the model.
    /// ED-NEW-ignis-guards: a locked PC no longer pauses (owner 2026-10-10: he often starts work and locks the PC; a PC that
    /// is asleep runs no timer, and nothing here ever wakes it).
    /// </summary>
    private IgnisPause Pause(Guid thread, Chat chat)
    {
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
        if (Lit(thread, chat, now)) chat.EverLit = true; // the sidebar flame shows from here on (lit, later cooled)
        var expires = chat.ExpiresAt;
        DateTimeOffset? wake = expires is { } cold && cold > now ? cold : null;
        if (expires is { } passed && passed <= now) WentCold(thread, chat, passed);

        void WakeBy(DateTimeOffset at) => wake = wake is { } current && current <= at ? current : at;

        // turned back on while cold: the flame dims at its new idle limit
        if (chat.Armed && IdleLimitAt(thread, chat) is { } armedUntil && armedUntil > now) WakeBy(armedUntil);

        if (preference.Auto && !blocked && !chat.PingInFlight)
        {
            if (preference.Mode == IgnisMode.WarmAndTidy && NeedsTidy(chat))
            {
                if (runtime.IsBetweenTasks) { Tidy(chat, runtime); return; }
                WakeBy(now + BusyRetry);
            }
            else if (pause == IgnisPause.None && expires is { } exp && exp > now && chat.Ttl is { } ttl && !chat.Tidying)
            {
                var due = exp - Lead(ttl);
                var hold = Hold(thread, chat, now);
                // the flame dims at the idle limit even when no keep-warm is due then
                if (hold == IgnisKeepWarmHold.None && IdleLimitAt(thread, chat) is { } limit && limit > now) WakeBy(limit);
                if (due > now) WakeBy(due);
                else if (hold != IgnisKeepWarmHold.None) LogHold(thread, chat, hold, now);
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
            environment.HasDraft(thread), environment.IsOnScreen(thread)) { GlobalGaps = GlobalGaps(chat.Provider) };
        return engine.ShouldKeepWarm(question);
    }

    private bool KeepWarm(Chat chat, IIgnisRuntimePort runtime)
    {
        var sent = runtime.CanKeepWarmSilently ? runtime.KeepWarmSilently() : runtime.SendHiddenPing();
        if (!sent) return false;
        chat.PingInFlight = true;
        chat.PingUsage = null;
        chat.PingSinceUserTurn = true;
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
        // ED-NEW-ignis-guards: a chat the user let cool is no loss; they said they were done with it.
        if (!chat.Cooled && chat.CacheReported && chat.ContextUsed is { } context && context > 0 && PointsOf(chat) is { } points)
        {
            lost = engine.RebuildPremium(points, context);
            if (lost > 0m) Ledger.Record(new IgnisLedgerEntry(at, thread, IgnisLedgerKind.Lost, null, lost.Value));
        }
        Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.WentCold, at, lost, IgnisAmountKind.Lost));
    }

    // ---- keep-warm guards (ED-NEW-ignis-guards) ----

    private IReadOnlyList<TimeSpan> GlobalGaps(ProviderKind? provider) => provider is { } kind
        ? [.. gaps.Samples(kind, IgnisCacheBucket.Main).Where(sample => sample.GapSeconds is > 0).Select(sample => TimeSpan.FromSeconds(sample.GapSeconds!.Value))]
        : [];

    /// <summary>When the chat's idle limit cools it (Settings › Ignis "Cool idle chats after" and "Learn when to cool"); null: no limit or not idle.</summary>
    private DateTimeOffset? IdleLimitAt(Guid thread, Chat chat)
    {
        if ((chat.IdleSince ?? chat.LastUsedAt) is not { } since) return null;
        var preferences = Settings.Value;
        var learned = IgnisKeepWarmGuards.LearnedReturn(chat.Gaps, preferences.LearnCooling ? GlobalGaps(chat.Provider) : [], chat.Ttl);
        return IgnisKeepWarmGuards.IdleLimit(preferences.CoolAfterHours, preferences.LearnCooling, learned, environment.HasDraft(thread)) is { } limit ? since + limit : null;
    }

    /// <summary>Today's pings reached today's allowance (read again only when the day or the ledger changed).</summary>
    private bool OverDailyLimit()
    {
        var today = Today;
        if (dailyLimit is { } known && known.Day == today && known.Revision == ledgerRevision) return known.Over;
        var over = IgnisKeepWarmGuards.OverDailyLimit(Ledger.Days(today.AddDays(-6), today));
        dailyLimit = (today, ledgerRevision, over);
        return over;
    }

    /// <summary>A chat Ignis would keep warm but for the daily allowance and the three-chat limit.</summary>
    private bool Candidate(Guid thread, Chat chat, DateTimeOffset now)
    {
        var preference = Settings.ForChat(thread);
        return preference.Mode != IgnisMode.Off && preference.Auto && chat.Runtime is not null && !chat.Cooled
            && chat.ExpiresAt is { } expires && expires > now && Pause(thread, chat) == IgnisPause.None
            && !(IdleLimitAt(thread, chat) is { } limit && now >= limit);
    }

    private IReadOnlySet<Guid> WarmSlots(DateTimeOffset now) => IgnisKeepWarmGuards.WarmSlots(chats
        .Where(pair => Candidate(pair.Key, pair.Value, now))
        .Select(pair => (pair.Key, pair.Value.LastUsedAt ?? DateTimeOffset.MinValue, environment.IsOnScreen(pair.Key) || environment.HasDraft(pair.Key))));

    /// <summary>Why a warm chat is not kept warm (the first that applies): the user's cool, the idle limit, the daily allowance, three newer chats.</summary>
    private IgnisKeepWarmHold Hold(Guid thread, Chat chat, DateTimeOffset now)
    {
        if (chat.Cooled) return IgnisKeepWarmHold.LetCool;
        if (IdleLimitAt(thread, chat) is { } limit && now >= limit) return IgnisKeepWarmHold.IdleLimit;
        if (OverDailyLimit()) return IgnisKeepWarmHold.DailyLimit;
        return WarmSlots(now).Contains(thread) ? IgnisKeepWarmHold.None : IgnisKeepWarmHold.OtherChats;
    }

    private bool Lit(Guid thread, Chat chat, DateTimeOffset now) => Candidate(thread, chat, now) && !OverDailyLimit() && WarmSlots(now).Contains(thread);

    // A keep-warm was due and a guard held it: one History row per reason and idle stretch (never a timeline line).
    private void LogHold(Guid thread, Chat chat, IgnisKeepWarmHold hold, DateTimeOffset now)
    {
        if (chat.LoggedHold == hold) return;
        chat.LoggedHold = hold;
        Log.Info("keep-warm held for " + thread.ToString("D") + ": " + hold);
        Ledger.Record(new IgnisEvent(NewEventID(), thread, IgnisEventKind.KeepWarmHeld, now, Reason: IgnisWords.RawName(hold)));
    }

    // The never-reused check: the main chat's last turns; flagged once per streak in History.
    private void CheckReuse(Guid thread, Chat chat, IgnisTurnUsage usage, DateTimeOffset now, IgnisMode mode)
    {
        chat.CacheTurns.Add(new IgnisCacheTurn(usage.TotalInput, usage.CacheRead, usage.CacheWrite, usage.Model ?? chat.Model, chat.Runtime?.Effort));
        if (chat.CacheTurns.Count > 8) chat.CacheTurns.RemoveAt(0);
        var cause = IgnisKeepWarmGuards.NotReused(chat.CacheTurns);
        if (cause is { } found && chat.NotReused is null && mode != IgnisMode.Off)
            Ledger.Record(new IgnisEvent(NewEventID(), thread, IgnisEventKind.CacheNotReused, now, Reason: IgnisWords.RawName(found)));
        chat.NotReused = cause;
    }

    public IgnisEmberState Ember(Guid thread)
    {
        if (!chats.TryGetValue(thread, out var chat) || Settings.ForChat(thread).Mode == IgnisMode.Off) return IgnisEmberState.None;
        var now = SwiftDate.Now;
        if (Lit(thread, chat, now) || Armed(thread, chat, now))
        {
            chat.EverLit = true;
            return new IgnisEmberState(IgnisEmber.Lit, chat.ExpiresAt);
        }
        return chat.EverLit ? new IgnisEmberState(IgnisEmber.Cooled, null) : IgnisEmberState.None;
    }

    public void LetItCool(Guid thread)
    {
        if (!chats.TryGetValue(thread, out var chat) || Ember(thread).Ember != IgnisEmber.Lit) return;
        chat.Cooled = true;
        chat.LoggedHold = IgnisKeepWarmHold.LetCool;
        Ledger.Record(new IgnisEvent(NewEventID(), thread, IgnisEventKind.LetCool, SwiftDate.Now));
        Everything(); // another chat may take the freed place among the three
    }

    /// <summary>
    /// "Turn on" on a cooled chat (owner 2026-10-10: "the pill must then change to turn on, to provide the user an option to turn
    /// Ignis back on for that chat"): keep-warm resumes as if the chat had just been used (its idle limit starts again and it
    /// ranks first among the three). A warm cache is kept warm from here; a cold one is never rebuilt by this (that would spend
    /// a cache write the user did not ask for): the flame stays lit and keep-warm starts with the next message.
    /// </summary>
    public void KeepWarmAgain(Guid thread)
    {
        if (!chats.TryGetValue(thread, out var chat) || Ember(thread).Ember != IgnisEmber.Cooled) return;
        var now = SwiftDate.Now;
        chat.Cooled = false;
        chat.Armed = true;
        chat.LoggedHold = IgnisKeepWarmHold.None;
        chat.LastUsedAt = now;
        if (chat.IdleSince is not null) chat.IdleSince = now;
        Ledger.Record(new IgnisEvent(NewEventID(), thread, IgnisEventKind.KeepWarmOn, now));
        Everything();
    }

    // Turned back on while cold: lit while Ignis is on for it and its new idle limit has not passed.
    private bool Armed(Guid thread, Chat chat, DateTimeOffset now)
    {
        if (!chat.Armed || chat.Cooled || chat.Runtime is null) return false;
        var preference = Settings.ForChat(thread);
        return preference.Mode != IgnisMode.Off && preference.Auto && Pause(thread, chat) == IgnisPause.None
            && !(IdleLimitAt(thread, chat) is { } limit && now >= limit);
    }

    public void ModelChanged(Guid thread, ProviderKind provider, string? model)
    {
        if (!chats.TryGetValue(thread, out var chat) || Settings.ForChat(thread).Mode == IgnisMode.Off) return;
        // back to the model the cache was written with: nothing to rebuild
        if (provider == chat.Provider && string.Equals(model, chat.Model, StringComparison.OrdinalIgnoreCase)) { chat.NoticeFor = null; return; }
        var now = SwiftDate.Now;
        if (!IgnisKeepWarmGuards.ShowsSwitchNotice(chat.ExpiresAt is { } expires && expires > now, chat.ContextUsed)) return;
        if (chat.NoticeFor is { } shown && shown.Provider == provider && string.Equals(shown.Model, model, StringComparison.OrdinalIgnoreCase)) return;
        if (Points.Find(provider, model) is not { } next) return;
        var prefix = chat.ContextUsed!.Value;
        var cost = IgnisKeepWarmGuards.SwitchCost(next, prefix, provider == chat.Provider ? chat.Ttl : null);
        chat.NoticeFor = (provider, model);
        Record(thread, chat, new IgnisEvent(NewEventID(), thread, IgnisEventKind.SwitchCost, now, cost, IgnisAmountKind.Cost, Model: next.DisplayName, AtTokens: prefix));
        Changed?.Invoke(thread);
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
                [IgnisSource.LongerMemory] = day.LongerMemory,
            },
            PaidCredits = !off && OnPaidCredits(chat),
            CacheLifetimes = chat.Lifetimes,
            MissedToday = day.Missed,
            Hold = !off && preference.Auto && cache == IgnisCacheState.Warm && pause == IgnisPause.None ? Hold(thread, chat, now) : IgnisKeepWarmHold.None,
            Ember = Ember(thread).Ember,
            IdleLimitAt = off ? null : IdleLimitAt(thread, chat),
            PingSpentToday = IgnisKeepWarmGuards.PingSpent(day),
            CacheNotReused = off ? null : chat.NotReused,
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
            accountLine, week, Ledger.AllTimeSaved) { CacheLines = IgnisReportText.CacheLines(status) });
    }
}
