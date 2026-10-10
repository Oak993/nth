// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; work/ignis/PLAN.md unit U0). No Swift twin: Droppy Code has no
// cache keep-warm or tidy-up feature. This file is the shared contract every other Ignis unit codes against: the modes,
// one turn's cache usage, the events Ignis records, one chat's status snapshot, and the four interfaces (controller,
// savings ledger, model points, runtime port). Money is US dollars as decimal; token counts are long; times are
// DateTimeOffset from SwiftDate.Now (tests run on a manual clock).
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis;

/// <summary>What Ignis does in a chat. Words: "Off", "Keep warm", "Warm + tidy up" (IgnisWords.ModeName).</summary>
public enum IgnisMode
{
    Off,
    KeepWarm,
    WarmAndTidy,
}

/// <summary>Where a saving came from; the order is the design's legend order (cache, warm, rebuild, help).</summary>
public enum IgnisSource
{
    /// <summary>"Reused quick memory": cache reads instead of fresh input.</summary>
    Reuse,
    /// <summary>"Kept warm": a ping that stopped a rebuild at full price.</summary>
    KeptWarm,
    /// <summary>"Tidy-ups": a chat rebuilt shorter at its cheapest point.</summary>
    TidyUp,
    /// <summary>"Smart helpers": look-up helpers run on the cheaper helper model.</summary>
    SmartHelpers,
    /// <summary>
    /// "Longer quick memory" (ED-NEW-ignis-runtime): rebuilds avoided because nth asked the CLI for a 1-hour cache in a
    /// bucket whose gaps pay for it (Saved), and the extra 1-hour write price it paid for that (Spent). Not a ping.
    /// </summary>
    LongerMemory,
}

/// <summary>The state of a chat's prompt cache ("quick memory").</summary>
public enum IgnisCacheState
{
    /// <summary>Ignis is off in this chat (or the provider has no keep-warm).</summary>
    Off,
    /// <summary>Within the cache's lifetime: ExpiresAt is in the future.</summary>
    Warm,
    /// <summary>Expired, or never written yet: the next message rebuilds it.</summary>
    Cold,
}

/// <summary>How the chat's account pays: a subscription plan with usage windows, or an API key paying per token.</summary>
public enum IgnisAccountKind
{
    Plan,
    ApiKey,
}

/// <summary>Why automatic keep-warm and tidy-up are paused for a chat (PLAN lead decision "Likely-to-continue").</summary>
public enum IgnisPause
{
    None,
    /// <summary>The PC is locked.</summary>
    Locked,
    /// <summary>The chat is archived.</summary>
    Archived,
    /// <summary>A plan usage window is at 95 % or more.</summary>
    PlanNearlyFull,
    /// <summary>The provider reported a usage-limit error.</summary>
    UsageLimit,
    /// <summary>The provider or model has keep-warm off in the model points (DeepSeek, Codex by default).</summary>
    NotSupported,
}

/// <summary>Which one-sentence "what happens next" line the popover shows (IgnisWords.Next gives the text).</summary>
public enum IgnisNext
{
    /// <summary>"Ignis is off in this chat, so it isn't saving anything right now."</summary>
    Off,
    /// <summary>"It's tidying up the chat now. Your recent messages stay word for word."</summary>
    Tidying,
    /// <summary>"The quick memory went cold over a break; your next message warms it again."</summary>
    Cold,
    /// <summary>"Auto is off, so Ignis waits for you to keep warm." (mode KeepWarm)</summary>
    AutoOffKeepWarm,
    /// <summary>"Auto is off, so Ignis waits for you to keep warm or tidy up." (mode WarmAndTidy)</summary>
    AutoOffWarmAndTidy,
    /// <summary>"It keeps the quick memory warm while you work. It won't tidy up this chat."</summary>
    KeepWarm,
    /// <summary>"It keeps the quick memory warm while you work and tidies up the chat before it gets costly, always between tasks."</summary>
    WarmAndTidy,
}

/// <summary>
/// One request's (or one turn's) cache-aware token usage, as a provider reported it (ProviderEvent.CacheUsage).
/// <para>
/// The four input buckets are disjoint and add up to the whole prompt: <see cref="UncachedInput"/> was read at the
/// normal input price, <see cref="CacheRead"/> at the cache-read price, and the three write buckets were written
/// into the cache (at the 5-minute premium, the 1-hour premium, or a premium whose TTL the stream did not say).
/// Claude sums its result's iterations[] into one value; Codex fills CacheRead from cachedInputTokens and has no
/// write premium (writes stay 0); OpenAI-compatible APIs fill CacheRead from cached_tokens / prompt_cache_hit_tokens.
/// </para>
/// <para>
/// <see cref="Reported"/> is false when the provider gave no cache numbers at all: the UI shows "not reported", the
/// ledger records no savings, and keep-warm falls back to the time-based timer only (PLAN lead decision).
/// </para>
/// </summary>
public sealed record IgnisTurnUsage(
    ProviderKind Provider,
    string? Model,
    string? AgentID,
    long UncachedInput,
    long CacheRead,
    long CacheWrite5m,
    long CacheWrite1h,
    long CacheWriteUnknownTtl,
    long Output,
    long? ContextUsed,
    long? ContextWindow,
    bool Reported)
{
    /// <summary>All three cache-write buckets.</summary>
    public long CacheWrite => CacheWrite5m + CacheWrite1h + CacheWriteUnknownTtl;

    /// <summary>The whole prompt: uncached + read + written.</summary>
    public long TotalInput => UncachedInput + CacheRead + CacheWrite;

    /// <summary>A usage the provider reported without any cache numbers (Reported false, every bucket 0).</summary>
    public static IgnisTurnUsage NotReported(ProviderKind provider, string? model, string? agentID, long? contextUsed = null, long? contextWindow = null) =>
        new(provider, model, agentID, 0, 0, 0, 0, 0, 0, contextUsed, contextWindow, false);
}

/// <summary>What an Ignis event line or history row says happened.</summary>
public enum IgnisEventKind
{
    /// <summary>"Ignis kept the quick memory warm" (a ping or a silent keep-warm request).</summary>
    KeptWarm,
    /// <summary>"Ignis warmed the quick memory again" (a keep-warm that found the cache already cold and rebuilt it).</summary>
    WarmedAgain,
    /// <summary>"Ignis tidied up the chat" (a rebuild at the tidy-up point).</summary>
    TidiedUp,
    /// <summary>"The quick memory went cold. Your next message warms it again".</summary>
    WentCold,
    /// <summary>"A scout read the code on {model}" (a look-up helper ran on the helper model).</summary>
    HelperRan,
    /// <summary>"Head {n} rebuilt at {point} after switching to {model}" (a head moved its tidy-up point).</summary>
    HeadTidiedAfterSwitch,
}

/// <summary>How an event's amount reads: a cost ("$0.02"), a saving ("saved $0.42") or a loss ("lost $0.21").</summary>
public enum IgnisAmountKind
{
    Cost,
    Saved,
    Lost,
}

/// <summary>
/// Something Ignis did or saw, shown as a quiet timeline line (the notice item's id is <see cref="TimelineItemID"/>)
/// and as a row in Details › History. <see cref="Amount"/> is null when the line shows no money.
/// </summary>
public sealed record IgnisEvent(
    string Id,
    Guid ThreadID,
    IgnisEventKind Kind,
    DateTimeOffset At,
    decimal? Amount = null,
    IgnisAmountKind AmountKind = IgnisAmountKind.Cost,
    string? AgentID = null,
    string? HeadLabel = null,
    string? Model = null,
    long? AtTokens = null)
{
    /// <summary>The timeline notice id prefix of an event line ("ignis:event:").</summary>
    public const string TimelinePrefix = "ignis:event:";

    /// <summary>The timeline notice id prefix of a hidden keep-warm ping's user item ("ignis:ping:").</summary>
    public const string PingPrefix = "ignis:ping:";

    /// <summary>The id of the timeline notice item that carries this event.</summary>
    public string TimelineItemID => TimelinePrefix + Id;

    /// <summary>Whether a timeline item id is an Ignis event line.</summary>
    public static bool IsEventItem(string? itemID) => itemID is not null && itemID.StartsWith(TimelinePrefix, StringComparison.Ordinal);

    /// <summary>Whether a timeline item id is a hidden keep-warm ping (filtered unless "Show keep-warm pings" is on).</summary>
    public static bool IsPingItem(string? itemID) => itemID is not null && itemID.StartsWith(PingPrefix, StringComparison.Ordinal);

    /// <summary>A fresh ping item id: "ignis:ping:" + a new lowercase GUID.</summary>
    public static string NewPingItemID() => PingPrefix + Guid.NewGuid().ToString("D");
}

/// <summary>One Hydra head's own cache and tidy-up point ("Include Hydra heads").</summary>
public sealed record IgnisHeadStatus(
    string AgentID,
    string Label,
    string? Model,
    IgnisCacheState Cache,
    DateTimeOffset? ExpiresAt,
    long? ContextUsed,
    long? TidyAt);

/// <summary>
/// Details › Each message: what every message carries before the user types, in tokens. Only parts Marrow sends are
/// itemised; <see cref="BuiltInToolsEstimate"/> is the remainder of the first request's cache write and is labelled an
/// estimate (PLAN lead decision "Each-message weight").
/// </summary>
public sealed record IgnisMessageWeight(
    long Instructions,
    long BuiltInToolsEstimate,
    long ServerTools,
    int ServerCount,
    long Skills,
    int UnusedServers,
    long UnusedServerTokens)
{
    public long Total => Instructions + BuiltInToolsEstimate + ServerTools + Skills;
}

/// <summary>
/// Everything the Kindle capsule and its popover show for one chat, as one immutable snapshot. The controller builds a
/// new one whenever anything changes and raises <see cref="IIgnisController.Changed"/> with the chat's id.
/// </summary>
public sealed record IgnisChatStatus
{
    public required Guid ThreadID { get; init; }
    public required IgnisMode Mode { get; init; }
    public required bool Auto { get; init; }
    public ProviderKind? Provider { get; init; }
    public string? Model { get; init; }
    /// <summary>The model's display name from the points file ("Opus 5.5"), else the model id.</summary>
    public string? ModelDisplayName { get; init; }

    // ---- quick memory (cache) ----
    public required IgnisCacheState Cache { get; init; }
    /// <summary>When the cache goes cold; null when cold, off, or unknown.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>The cache lifetime in use (1 h on Claude Max main, 5 min on sub-agents, from the stream or the points).</summary>
    public TimeSpan? Ttl { get; init; }
    /// <summary>False when the provider gave no cache numbers: the UI shows "not reported" and no savings are recorded.</summary>
    public bool CacheReported { get; init; } = true;
    public int PingsToday { get; init; }

    // ---- chat size (context) ----
    public long? ContextUsed { get; init; }
    public long? ContextWindow { get; init; }
    /// <summary>The tidy-up point in tokens (the notch on the Chat size track); null when the model has none.</summary>
    public long? TidyAt { get; init; }
    public string? TidyReason { get; init; }

    // ---- money, today, this chat ----
    public decimal SavedToday { get; init; }
    public decimal LostToday { get; init; }
    /// <summary>Today's savings by source, every source present (0 when none).</summary>
    public required IReadOnlyDictionary<IgnisSource, decimal> SavedBySource { get; init; }
    /// <summary>Share of input read from cache (0...1); null when not reported.</summary>
    public double? ReuseRate { get; init; }

    // ---- what happens next ----
    public required IgnisNext Next { get; init; }
    public IgnisPause Pause { get; init; } = IgnisPause.None;
    /// <summary>A keep-warm (ping or silent request) is in flight.</summary>
    public bool KeepingWarm { get; init; }
    /// <summary>A tidy-up is in flight ("Tidying up…").</summary>
    public bool Tidying { get; init; }
    /// <summary>A tidy-up finished moments ago ("Tidied up" on the button for a short while).</summary>
    public bool JustTidied { get; init; }
    /// <summary>Whether "Keep warm now" can run now (between tasks, provider supports it, mode not Off).</summary>
    public bool CanKeepWarm { get; init; }
    /// <summary>Whether "Tidy up now" can run now (between tasks, mode WarmAndTidy).</summary>
    public bool CanTidy { get; init; }

    // ---- account line ----
    public required IgnisAccountKind Account { get; init; }
    /// <summary>Plan: this chat's share of the weekly window (0...1).</summary>
    public double? WeekShare { get; init; }
    /// <summary>Plan: when, at this pace, the weekly limit is reached; null when not within the window.</summary>
    public DateTimeOffset? WeeklyLimitAt { get; init; }
    /// <summary>API key: what this chat has cost so far.</summary>
    public decimal? ChatCost { get; init; }
    /// <summary>API key: what it would have cost without Ignis.</summary>
    public decimal? ChatCostWithoutIgnis { get; init; }

    // ---- heads and weight ----
    public IReadOnlyList<IgnisHeadStatus> Heads { get; init; } = [];
    public IgnisMessageWeight? Weight { get; init; }

    /// <summary>Context fill 0...1, or null when either number is unknown.</summary>
    public double? ContextFill => ContextUsed is { } used && ContextWindow is { } window && window > 0 ? (double)used / window : null;

    // ---- cache lifetimes (ED-NEW-ignis-runtime) ----
    /// <summary>
    /// The main chat runs on paid usage credits: its writes are 5-minute ones where the points say 1 hour (or the CLI
    /// said it is using overage). The lifetime and keep-warm timing follow the 5 minutes until 1-hour writes return.
    /// </summary>
    public bool PaidCredits { get; init; }
    /// <summary>What nth chose for the Claude CLI's two cache buckets at the chat's last launch, and why; null: not asked.</summary>
    public Engine.IgnisLifetimeChoice? CacheLifetimes { get; init; }
    /// <summary>Today's helper cache rebuilds in this chat (gaps longer than a helper's lifetime).</summary>
    public decimal MissedToday { get; init; }

    /// <summary>The all-sources zero map, for building a status before any saving.</summary>
    public static IReadOnlyDictionary<IgnisSource, decimal> NoSavings { get; } = new Dictionary<IgnisSource, decimal>
    {
        [IgnisSource.Reuse] = 0m, [IgnisSource.KeptWarm] = 0m, [IgnisSource.TidyUp] = 0m, [IgnisSource.SmartHelpers] = 0m, [IgnisSource.LongerMemory] = 0m,
    };

    /// <summary>The status of a chat Ignis knows nothing about yet.</summary>
    public static IgnisChatStatus Unknown(Guid thread, IgnisMode mode, bool auto, IgnisAccountKind account = IgnisAccountKind.Plan) => new()
    {
        ThreadID = thread, Mode = mode, Auto = auto, Cache = mode == IgnisMode.Off ? IgnisCacheState.Off : IgnisCacheState.Cold,
        SavedBySource = NoSavings, Account = account,
        Next = mode switch
        {
            IgnisMode.Off => IgnisNext.Off,
            IgnisMode.KeepWarm => auto ? IgnisNext.KeepWarm : IgnisNext.AutoOffKeepWarm,
            IgnisMode.WarmAndTidy => auto ? IgnisNext.WarmAndTidy : IgnisNext.AutoOffWarmAndTidy,
        },
    };
}

/// <summary>Where the model points in use came from.</summary>
public enum IgnisPointsOrigin
{
    /// <summary>The copy compiled into Marrow (IgnisPointsData).</summary>
    Bundled,
    /// <summary>A newer copy fetched from the release feed, kept at Storage.Root/ignis/model-points.json.</summary>
    Stored,
}

/// <summary>The result of checking the points feed: the version installed after the check and the feed's latest.</summary>
public sealed record IgnisPointsCheck(string Installed, string? Latest, bool Updated, string? Error = null)
{
    /// <summary>Installed is the latest (or the feed is not published yet): the Updates row says "Up to date".</summary>
    public bool UpToDate => Error is null && (Latest is null || string.CompareOrdinal(Installed, Latest) >= 0);
}

/// <summary>
/// The model points (context windows, price steps, cache prices, rebuild points). Updating only ever fetches the
/// GitHub release feed; it never sends anything to a model provider.
/// </summary>
public interface IIgnisPoints
{
    event Action? Changed;

    /// <summary>The points in use: the stored copy when it is valid and newer than the bundled one, else the bundled one.</summary>
    IgnisPointsFile Current { get; }

    IgnisPointsOrigin Origin { get; }

    /// <summary>When the feed was last checked successfully; null when never.</summary>
    DateTimeOffset? CheckedAt { get; }

    /// <summary>The model whose ids or aliases match (ordinal, case-insensitive); null when the model is unknown.</summary>
    IgnisModelPoints? Find(ProviderKind provider, string? model);

    /// <summary>Checks the feed; installs a valid newer file when <paramref name="install"/> is true.</summary>
    Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken);
}

/// <summary>Whether a ledger entry is money saved, money lost (a cold rebuild), or money spent (a keep-warm ping).</summary>
public enum IgnisLedgerKind
{
    Saved,
    Lost,
    Spent,
    /// <summary>A helper's cache rebuilt after a gap longer than its lifetime (ED-NEW-ignis-runtime); kept apart from the chat's Lost.</summary>
    Missed,
}

/// <summary>
/// One line in the savings ledger (Storage.Root/ignis/ledger-v1.json, bucketed by local day, chat and source).
/// <see cref="Source"/> is set for Saved entries (and for Spent pings: KeptWarm); <see cref="Count"/> counts the
/// actions behind it (quick reuses, pings, tidy-ups, helper jobs) for "Day by day".
/// </summary>
public sealed record IgnisLedgerEntry(DateTimeOffset At, Guid ThreadID, IgnisLedgerKind Kind, IgnisSource? Source, decimal Amount, int Count = 1);

/// <summary>One local day's totals (all chats, or one chat).</summary>
public sealed record IgnisDayTotals(
    DateOnly Day,
    decimal Reuse,
    decimal KeptWarm,
    decimal TidyUp,
    decimal SmartHelpers,
    decimal Lost,
    decimal Spent,
    int Reuses,
    int Pings,
    int TidyUps,
    int HelperJobs)
{
    /// <summary>"Longer quick memory" saved (ED-NEW-ignis-runtime): rebuilds avoided by nth's 1-hour choice.</summary>
    public decimal LongerMemory { get; init; }

    /// <summary>What nth's 1-hour choice paid on top for its writes (also inside <see cref="Spent"/>).</summary>
    public decimal LongerMemorySpent { get; init; }

    /// <summary>Helper cache rebuilds: a helper's write after a gap longer than its cache lifetime (not inside <see cref="Lost"/>).</summary>
    public decimal Missed { get; init; }

    /// <summary>Money saved that day: every source.</summary>
    public decimal Saved => Reuse + KeptWarm + TidyUp + SmartHelpers + LongerMemory;

    public decimal SavedFrom(IgnisSource source) => source switch
    {
        IgnisSource.Reuse => Reuse,
        IgnisSource.KeptWarm => KeptWarm,
        IgnisSource.TidyUp => TidyUp,
        IgnisSource.SmartHelpers => SmartHelpers,
        IgnisSource.LongerMemory => LongerMemory,
    };

    public static IgnisDayTotals Empty(DateOnly day) => new(day, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0);
}

/// <summary>The savings ledger: what Ignis saved, lost and spent, by local day, chat and source, plus its events.</summary>
public interface ISavingsLedger
{
    event Action? Changed;

    void Record(IgnisLedgerEntry entry);

    void Record(IgnisEvent @event);

    /// <summary>All chats, one local day.</summary>
    IgnisDayTotals Day(DateOnly day);

    /// <summary>One chat, one local day.</summary>
    IgnisDayTotals ChatDay(Guid thread, DateOnly day);

    /// <summary>All chats, every day from <paramref name="from"/> to <paramref name="to"/> inclusive, oldest first (empty days included).</summary>
    IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to);

    /// <summary>Saved across all chats since the first entry ("Saved since you turned Ignis on").</summary>
    decimal AllTimeSaved { get; }

    /// <summary>The local day of the first entry; null when the ledger is empty.</summary>
    DateOnly? Since { get; }

    /// <summary>One chat's events of one local day, newest first (Details › History).</summary>
    IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day);

    /// <summary>One event by id (the timeline event line looks its amount up by the item id).</summary>
    IgnisEvent? Event(string id);
}

/// <summary>
/// What Ignis needs from one chat's runtime (ThreadRuntime implements it in its Ignis partial). Every action runs only
/// between tasks and returns false, doing nothing, when it cannot run now.
/// </summary>
public interface IIgnisRuntimePort
{
    Guid ThreadID { get; }
    ProviderKind Provider { get; }
    string? Model { get; }

    /// <summary>Idle, no queued follow-ups, no running heads, no open approval or question, not reverting, not archived.</summary>
    bool IsBetweenTasks { get; }

    /// <summary>When the chat last sent a request to the model (the cache's clock starts here); null when never.</summary>
    DateTimeOffset? LastRequestAt { get; }

    /// <summary>When the running (or just finished) turn started; the gap Ignis learns ends here (ED-NEW-ignis-runtime). Null: unknown.</summary>
    DateTimeOffset? TurnStartedAt => null;

    /// <summary>Whether this session keeps warm without a visible turn (API sessions); CLIs use hidden pings instead.</summary>
    bool CanKeepWarmSilently { get; }

    /// <summary>CLI keep-warm: one turn with IgnisWords.PingText whose user item becomes an "ignis:ping:" notice; skips chime, notification, unread and auto-continue.</summary>
    bool SendHiddenPing();

    /// <summary>API keep-warm: the same payload plus one user message, max_tokens 1, not added to history.</summary>
    bool KeepWarmSilently();

    /// <summary>Rebuilds the chat shorter, keeping the last <paramref name="keepTurns"/> turns word for word (exact on API; a /compact instruction on Claude; plain compact on Codex). Silent: no "Compacting context." notices.</summary>
    bool Tidy(int keepTurns);
}

/// <summary>
/// The app-wide Ignis controller: per-chat timers, decisions and status. The UI reads <see cref="Status"/> and calls the
/// actions; ThreadRuntime attaches its port and forwards cache usage and turn ends.
/// </summary>
public interface IIgnisController
{
    /// <summary>Raised (on the main thread) with the id of the chat whose status changed; Guid.Empty: every chat (settings or points changed).</summary>
    event Action<Guid>? Changed;

    IgnisSettings Settings { get; }

    IIgnisPoints Points { get; }

    ISavingsLedger Ledger { get; }

    IgnisChatStatus Status(Guid thread);

    void SetMode(Guid thread, IgnisMode mode);

    void SetAuto(Guid thread, bool auto);

    /// <summary>"Keep warm now". Ignored unless the status says CanKeepWarm.</summary>
    void KeepWarmNow(Guid thread);

    /// <summary>"Tidy up now". Ignored unless the status says CanTidy.</summary>
    void TidyUpNow(Guid thread);

    /// <summary>The "Save report" text for one chat, today (IgnisWords.Report lines).</summary>
    string Report(Guid thread);

    void Attach(IIgnisRuntimePort port);

    void Detach(Guid thread);

    /// <summary>A provider's CacheUsage event for a chat (AgentID set: a head's).</summary>
    void Observe(Guid thread, IgnisTurnUsage usage);

    /// <summary>A turn ended in the chat (ping turns included); the controller re-plans its timers.</summary>
    void TurnFinished(Guid thread);

    /// <summary>The provider reported a usage-limit error in the chat: pause until it resets.</summary>
    void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt);

    /// <summary>The provider said the chat now runs on (or off) paid usage credits (ED-NEW-ignis-runtime); the observed writes still win.</summary>
    void CreditsHint(Guid thread, bool onCredits) { }
}
