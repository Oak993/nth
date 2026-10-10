// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; PLAN unit U0). Every word Ignis shows, from the locked design
// (work/design/phoenix/phoenix-designs.html: "Kindle (locked)", Settings › Ignis, Settings › Updates row, timeline event
// lines, first-time notice). Units U3 to U5 take their text from here only. Curly apostrophes (U+2019) and the ellipsis
// (U+2026) are kept where the design has them. Money is US dollars in en-US format. NO keyboard shortcuts (owner).
using System.Globalization;

namespace Marrow.Core.Port.Ignis;

public static class IgnisWords
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    // ---- name, capsule, popover head ----
    public const string Title = "Ignis";
    public const string CapsuleAccessibleName = "Ignis";
    public const string PopoverAccessibleName = "Ignis";
    public const string Auto = "Auto";
    public const string AutoAccessibleName = "Auto: Ignis decides when to keep warm and tidy up";
    public const string ModeGroupAccessibleName = "Ignis mode";

    public static string ModeName(IgnisMode mode) => mode switch
    {
        IgnisMode.Off => "Off",
        IgnisMode.KeepWarm => "Keep warm",
        IgnisMode.WarmAndTidy => "Warm + tidy up",
    };

    // ---- capsule (ring + countdown) ----
    /// <summary>The capsule's short text: "Off", "{m}m" while warm, "Cold".</summary>
    public static string CapsuleText(IgnisCacheState cache, TimeSpan? left) => cache switch
    {
        IgnisCacheState.Off => "Off",
        IgnisCacheState.Cold => "Cold",
        IgnisCacheState.Warm => MinutesUp(left) + "m",
    };

    // ---- buttons ----
    public const string KeepWarmNow = "Keep warm now";
    public const string TidyUpNow = "Tidy up now";
    public const string TidyingUp = "Tidying up…";
    public const string TidiedUp = "Tidied up";
    public const string Details = "Details";

    // ---- quick memory track ----
    public const string QuickMemory = "Quick memory";
    public static string CacheStatus(IgnisCacheState cache) => cache switch
    {
        IgnisCacheState.Off => "Off",
        IgnisCacheState.Warm => "Warm",
        IgnisCacheState.Cold => "Cold",
    };

    /// <summary>The countdown on the right of the Quick memory track.</summary>
    public static string TimeLeft(IgnisCacheState cache, TimeSpan? left) => cache switch
    {
        IgnisCacheState.Off => "Ignis is off",
        IgnisCacheState.Cold => "Warms with your next message",
        IgnisCacheState.Warm => MinutesUp(left) <= 1 ? "Under a minute left" : MinutesUp(left) + " min left",
    };

    public const string TrackCold = "Cold";

    /// <summary>The tick labels under the track: "Cold", then each quarter of the TTL ("15m", "30m", "45m", "1h").</summary>
    public static IReadOnlyList<string> TrackTicks(TimeSpan ttl)
    {
        var ticks = new List<string> { TrackCold };
        for (var quarter = 1; quarter <= 4; quarter++) ticks.Add(Duration(TimeSpan.FromTicks(ttl.Ticks * quarter / 4)));
        return ticks;
    }

    // ---- chat size track ----
    public const string ChatSize = "Chat size";
    public static string PercentFull(double fill) => Percent(fill) + " full";
    public const string TidyUpPoint = "tidy-up point";

    // ---- the one sentence ----
    public static string SavedTodayLead => "Today Ignis saved ";

    /// <summary>"Today Ignis saved $3.18. {next}"</summary>
    public static string Summary(decimal savedToday, IgnisNext next) => SavedTodayLead + Money(savedToday) + ". " + Next(next);

    public static string Next(IgnisNext next) => next switch
    {
        IgnisNext.Off => "Ignis is off in this chat, so it isn’t saving anything right now.",
        IgnisNext.Tidying => "It’s tidying up the chat now. Your recent messages stay word for word.",
        IgnisNext.Cold => "The quick memory went cold over a break; your next message warms it again.",
        IgnisNext.AutoOffKeepWarm => "Auto is off, so Ignis waits for you to keep warm.",
        IgnisNext.AutoOffWarmAndTidy => "Auto is off, so Ignis waits for you to keep warm or tidy up.",
        IgnisNext.KeepWarm => "It keeps the quick memory warm while you work. It won’t tidy up this chat.",
        IgnisNext.WarmAndTidy => "It keeps the quick memory warm while you work and tidies up the chat before it gets costly, always between tasks.",
    };

    /// <summary>Shown instead of cache numbers when the provider reports none.</summary>
    public const string NotReported = "not reported";

    // ---- account line ----
    public const string OpenUsage = "Open usage";

    /// <summary>"This chat used 9% of your week." (plan accounts)</summary>
    public static string WeekShare(double share) => "This chat used " + Percent(share) + " of your week.";

    /// <summary>"At this pace you’ll reach the weekly limit Thursday evening." (<paramref name="when"/> is "Thursday evening".)</summary>
    public static string WeeklyLimitAt(string when) => "At this pace you’ll reach the weekly limit " + when + ".";

    /// <summary>"Thursday evening": the weekday plus morning (05-12), afternoon (12-17), evening (17-21) or night.</summary>
    public static string DayPart(DateTimeOffset at)
    {
        var part = at.Hour switch { >= 5 and < 12 => "morning", >= 12 and < 17 => "afternoon", >= 17 and < 21 => "evening", _ => "night" };
        return at.ToString("dddd", Us) + " " + part;
    }

    /// <summary>"API key: you pay per token. This chat: $4.12."</summary>
    public static string ApiKeyLine(decimal chatCost) => "API key: you pay per token. This chat: " + Money(chatCost) + ".";

    /// <summary>The report's API line: "API key: this chat cost $4.12 so far."</summary>
    public static string ReportApiLine(decimal chatCost) => "API key: this chat cost " + Money(chatCost) + " so far.";

    // ---- model points ----
    public const string ModelPoints = "Model points";
    public const string Update = "Update";
    public const string Checking = "Checking…";
    public const string UpToDate = "Up to date";
    /// <summary>"· up to date" right after a check found nothing newer.</summary>
    public const string PointsJustChecked = "· up to date";

    /// <summary>"· 2 days ago", "· today", "· yesterday": how old the installed points are.</summary>
    public static string PointsAge(int days) => days switch { <= 0 => "· today", 1 => "· yesterday", _ => "· " + days.ToString(Us) + " days ago" };

    // ---- (i) explanations: the button's name is "What is {name}?" ----
    public static string InfoAccessibleName(string name) => "What is " + name + "?";
    public const string InfoQuickMemoryName = "quick memory";
    public const string InfoQuickMemory = "The model’s prompt cache: a short-lived copy of this chat it can reread for a fraction of the price. It lasts an hour; Ignis refreshes it while you work.";
    /// <summary>The quick memory tip for a 5-minute cache (API, sub-agents).</summary>
    public const string InfoQuickMemoryShort = "The model’s prompt cache: a short-lived copy of this chat it can reread for a fraction of the price. It lasts five minutes; Ignis refreshes it while you work.";
    public const string InfoChatSizeName = "chat size";
    public const string InfoChatSize = "The context window: how much of the model’s working memory this chat fills. Bigger chats cost more per message.";
    public const string InfoMoneySavedName = "money saved";
    public const string InfoMoneySaved = "What this chat didn’t spend today thanks to Ignis, compared with no reuse and no tidy-ups.";
    public const string InfoWeekShareName = "share of your week";
    public static string InfoWeekShare(double share) => "Of everything your plan allows this week, this chat has used " + Percent(share) + ". The full limits live in nth’s usage panel.";
    public const string InfoChatCostName = "this chat’s cost";
    public static string InfoChatCost(decimal withoutIgnis) => "What this chat has cost on your API key so far. Without Ignis it would be about " + Money(withoutIgnis) + ".";
    public const string InfoModelPointsName = "model points";
    public const string InfoModelPoints = "Price and size facts for each model, so Ignis knows the cheapest moment to tidy up. Updating never sends anything to a model.";
    public const string InfoReuseRateName = "reuse rate";
    public const string InfoReuseRate = "Cache hits: how often the model reused its quick memory instead of reading everything again. Higher is cheaper.";
    public const string InfoMoneyLostName = "money lost";
    public const string InfoMoneyLost = "What it cost to rebuild the quick memory after it went cold over a break.";
    public const string InfoAllChatsName = "all chats";
    public const string InfoAllChats = "Everything Ignis has saved across all your chats since you turned it on.";
    public const string InfoWeightName = "this weight";
    /// <summary>"Measured in tokens, … about 2¢ a message; cold, about 47¢."</summary>
    public static string InfoWeight(decimal warmCost, decimal coldCost) =>
        "Measured in tokens, the units models read and charge by (K = thousand). With warm quick memory this costs about " + Cents(warmCost) + " a message; cold, about " + Cents(coldCost) + ".";
    public const string InfoServers = "Connected servers (MCP) add their tool descriptions to every message, even when the tools aren’t used.";
    /// <summary>Built-in tools is the remainder of the first request's cache write (PLAN lead decision).</summary>
    public const string InfoBuiltInTools = "An estimate: what the first message wrote to the quick memory beyond the parts nth sends.";

    // ---- Details › Savings ----
    public const string TabsAccessibleName = "Ignis details";
    public const string TabSavings = "Savings";
    public const string TabHistory = "History";
    public const string TabEachMessage = "Each message";
    public const string WhereTodaysSavingsCameFrom = "Where today’s savings came from";

    public static string SourceName(IgnisSource source) => source switch
    {
        IgnisSource.Reuse => "Reused quick memory",
        IgnisSource.KeptWarm => "Kept warm",
        IgnisSource.TidyUp => "Tidy-ups",
        IgnisSource.SmartHelpers => "Smart helpers",
        IgnisSource.LongerMemory => "Longer quick memory",
    };

    public const string SmartHelpersNowOff = "Smart helpers (now off)";

    public static string SourceInfo(IgnisSource source) => source switch
    {
        IgnisSource.Reuse => "Cache reads: rereading the saved copy of the chat costs about a twentieth of reading it fresh.",
        IgnisSource.KeptWarm => "Small pings that stopped the quick memory from going cold and being rebuilt at full price.",
        IgnisSource.TidyUp => "Rebuilds: long chats rewritten shorter at their cheapest point, so each message carries less.",
        IgnisSource.SmartHelpers => "Look-up helpers that ran on " + DefaultHelperModel + " instead of your main model.",
        IgnisSource.LongerMemory => "An hour-long quick memory where breaks are long enough to pay for it, so helpers and chats reread instead of rebuilding.",
    };

    // ---- cache lifetimes (ED-NEW-ignis-runtime) ----
    /// <summary>The chat runs on paid usage credits, where the main quick memory lasts five minutes.</summary>
    public const string PaidCreditsCache = "Paid credits: 5-minute cache";

    /// <summary>"Helpers rebuilt their quick memory: $0.31" (helper writes after a gap longer than their cache).</summary>
    public static string HelpersRebuilt(decimal missed) => "Helpers rebuilt their quick memory: " + Money(missed);

    public static string BucketName(Engine.IgnisCacheBucket bucket) => bucket == Engine.IgnisCacheBucket.Main ? "Chat" : "Helpers";

    /// <summary>One bucket's line in the report: "Helpers’ quick memory: 1 hour, chosen by Ignis (saves about $1.20 over 40 breaks)".</summary>
    public static string LifetimeLine(Engine.IgnisLifetimeVerdict v) => (v.Bucket == Engine.IgnisCacheBucket.Main ? "Chat’s" : "Helpers’") + " quick memory: " + v.Reason switch
    {
        Engine.IgnisLifetimeReason.Longer => "1 hour, chosen by Ignis (saves about " + Money(v.Benefit - v.Cost) + " over " + v.Gaps.ToString(Us) + " breaks)",
        Engine.IgnisLifetimeReason.IgnisOff => "Claude’s default (Ignis is off)",
        Engine.IgnisLifetimeReason.NotClaude => "the provider’s own",
        Engine.IgnisLifetimeReason.HeadsOff => "Claude’s default (Include Hydra heads is off)",
        Engine.IgnisLifetimeReason.UserChoice => "your own setting (" + (v.UserSetting ?? "Claude settings") + ")",
        Engine.IgnisLifetimeReason.CliVersionUnknown => "Claude’s default (Claude Code version not known yet)",
        Engine.IgnisLifetimeReason.CliTooOld => "Claude’s default (needs Claude Code 2.1.242 or later)",
        Engine.IgnisLifetimeReason.AlreadyLong => "already 1 hour",
        Engine.IgnisLifetimeReason.TooFewGaps => "Claude’s default (still learning: " + v.Gaps.ToString(Us) + " of " + Engine.IgnisCacheLifetime.MinimumGaps.ToString(Us) + " breaks seen)",
        Engine.IgnisLifetimeReason.NoPrices => "Claude’s default (no cache prices for this model)",
        Engine.IgnisLifetimeReason.NotWorthIt => "Claude’s default (an hour would cost more than it saves)",
    };

    /// <summary>"Reused 94% of the time"</summary>
    public static string ReusedOfTheTime(double rate) => "Reused " + Percent(rate) + " of the time";

    /// <summary>"Lost $0.21 when it went cold"</summary>
    public static string LostWhenCold(decimal lost) => "Lost " + Money(lost) + " when it went cold";

    /// <summary>The helper model the design names (the points file's helperModel display name replaces it when known).</summary>
    public const string DefaultHelperModel = "Haiku 5.5";
    public const string SmartHelpersSwitch = "Send look-up helpers to a cheaper model";
    public static string SmartHelpersDetail(string helperModel) =>
        "Helpers and Hydra scouts that only read code use " + helperModel + ", about a quarter of the cost. Helpers that write code keep your model.";
    /// <summary>The (i) for Smart helpers in a Codex chat: Codex has one sub-agent model for every helper (PLAN lead decision).</summary>
    public const string SmartHelpersCodex = "Codex chats use one model for all helpers, so Smart helpers works in Claude Code chats only.";

    // ---- Details › History ----
    public const string LastSevenDays = "Last 7 days";
    public const string AllChats = "All chats";
    public const string SaveReport = "Save report";
    public const string ReportAccessibleName = "Ignis report";
    public const string Done = "Done";
    public const string Copy = "Copy";
    public const string Copied = "Copied";
    public const string SelectedPressToCopy = "Selected. Press Ctrl+C to copy.";
    public const string Today = "Today";

    /// <summary>"Money saved per day, last 7 days: Wed $4.20, …, Today $3.18"</summary>
    public static string WeekChartAccessibleName(IEnumerable<(string Day, decimal Saved)> days) =>
        "Money saved per day, last 7 days: " + string.Join(", ", days.Select(d => d.Day + " " + Money(d.Saved)));

    /// <summary>A short weekday name for the 7-day chart ("Wed"), or "Today".</summary>
    public static string ShortDay(DateOnly day, DateOnly today) => day == today ? Today : day.ToString("ddd", Us);

    // ---- Details › Each message ----
    public const string CarriedByEveryMessage = "Carried by every message, before you type";
    public const string Instructions = "Instructions";
    public const string BuiltInTools = "Built-in tools";
    public static string ToolsFromServers(int servers) => "Tools from " + servers.ToString(Us) + (servers == 1 ? " server" : " servers");
    public const string Skills = "Skills";
    /// <summary>"2 servers unused this week."</summary>
    public static string ServersUnused(int servers) => servers.ToString(Us) + (servers == 1 ? " server" : " servers") + " unused this week.";
    /// <summary>"Turning them off saves about 9K per message."</summary>
    public static string TurningThemOffSaves(long tokens) => "Turning them off saves about " + Tokens(tokens) + " per message.";
    public const string OpenToolsSettings = "Open Tools settings";

    // ---- timeline event lines (Kindle words) and History rows ----
    /// <summary>The quiet timeline line.</summary>
    public static string EventLine(IgnisEvent e) => e.Kind switch
    {
        IgnisEventKind.KeptWarm => "Ignis kept the quick memory warm",
        IgnisEventKind.WarmedAgain => "Ignis warmed the quick memory again",
        IgnisEventKind.TidiedUp => "Ignis tidied up the chat",
        IgnisEventKind.WentCold => "The quick memory went cold. Your next message warms it again",
        IgnisEventKind.HelperRan => "A scout read the code on " + (e.Model ?? DefaultHelperModel),
        IgnisEventKind.HeadTidiedAfterSwitch => HeadRebuilt(e),
    };

    /// <summary>The Details › History row.</summary>
    public static string HistoryRow(IgnisEvent e) => e.Kind switch
    {
        IgnisEventKind.KeptWarm => "Kept the quick memory warm",
        IgnisEventKind.WarmedAgain => "Warmed the cold quick memory",
        IgnisEventKind.TidiedUp => e.AtTokens is { } at ? "Tidied up the chat at " + Tokens(at) : "Tidied up the chat",
        IgnisEventKind.WentCold => "Quick memory went cold over a break",
        IgnisEventKind.HelperRan => "A scout read the code on " + (e.Model ?? DefaultHelperModel),
        IgnisEventKind.HeadTidiedAfterSwitch => HeadRebuilt(e),
    };

    private static string HeadRebuilt(IgnisEvent e) =>
        (e.HeadLabel ?? "A head") + " rebuilt at " + (e.AtTokens is { } at ? Tokens(at) : "its point") + " after switching to " + (e.Model ?? "another model");

    /// <summary>"$0.02" (cost), "saved $0.42", "lost $0.21"; null when the event has no amount.</summary>
    public static string? EventAmount(IgnisEvent e) => e.Amount is not { } amount ? null : e.AmountKind switch
    {
        IgnisAmountKind.Cost => Money(amount),
        IgnisAmountKind.Saved => "saved " + Money(amount),
        IgnisAmountKind.Lost => "lost " + Money(amount),
    };

    /// <summary>The event's local time, "10:06".</summary>
    public static string EventTime(DateTimeOffset at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);

    // ---- first-time notice ----
    public const string NoticeTitle = "Ignis hides its keep-warm pings";
    /// <summary>"To keep this chat's cache warm, Ignis sends Codex a tiny message now and then. You'll see a short line here instead."</summary>
    public static string NoticeBody(string providerName) =>
        "To keep this chat's cache warm, Ignis sends " + providerName + " a tiny message now and then. You'll see a short line here instead.";
    public const string ShowPings = "Show pings";
    public const string HidePings = "Hide pings";
    public const string GotIt = "Got it";

    /// <summary>The CLI keep-warm ping (PLAN lead decision). Sent only between tasks; its user item is hidden.</summary>
    public const string PingText = "[nth Ignis] Automatic keep-alive, not from the user. Use no tools or agents and change nothing. Reply exactly: ok";

    // ---- Settings › Ignis ----
    public const string SettingsTitle = "Ignis";
    public const string SettingsLead = "Keeps each chat's cache warm when that saves money, and rebuilds long chats at their cheapest point. Answers keep the same quality.";
    /// <summary>Search keywords for the Settings page.</summary>
    public static IReadOnlyList<string> SettingsKeywords { get; } =
        ["ignis", "cache", "quick memory", "keep warm", "tidy up", "compact", "rebuild", "savings", "model points", "helpers", "pings", "cost"];
    public const string RowDefaultMode = "Ignis";
    public const string RowDefaultModeDetail = "Default for new chats";
    public const string DefaultModeAccessibleName = "Default mode";
    public const string RowAutomatic = "Automatic";
    public const string RowAutomaticDetail = "Ignis decides when to keep the quick memory warm and when to tidy up the chat. Never in the middle of a task.";
    public const string RowHeads = "Include Hydra heads";
    public const string RowHeadsDetail = "Each head gets its own cache and its own rebuild point.";
    public const string RowSmartHelpers = "Smart helpers";
    public static string RowSmartHelpersDetail(string helperModel) =>
        "Send look-up helpers to a cheaper model. Helpers and Hydra scouts that only read code use " + helperModel + ", about a quarter of the cost. Helpers that write code keep your model.";
    public const string RowSmartHelpersAccessibleName = "Smart helpers: send look-up helpers to a cheaper model";
    public const string RowExplanations = "Show (i) explanations";
    public const string RowExplanationsDetail = "A small (i) next to each number explains it in one plain sentence.";
    public const string RowShowPings = "Show keep-warm pings";
    public const string RowShowPingsDetail = "In Claude Code and Codex chats the pings are hidden and a short line shows instead.";
    public const string RowKeepWordForWord = "Keep word for word";
    public const string RowKeepWordForWordDetail = "Recent turns that a rebuild never shortens.";
    /// <summary>"Last 12 turns".</summary>
    public static string KeepTurns(int turns) => "Last " + turns.ToString(Us) + " turns";

    // ---- Settings › Ignis: Savings history ----
    public const string SavingsHistory = "Savings history";
    public const string SavedSinceOn = "Saved since you turned Ignis on";
    /// <summary>"Since 3 February 2026, across all chats"</summary>
    public static string SinceAcrossAllChats(DateOnly since) => "Since " + since.ToString("d MMMM yyyy", Us) + ", across all chats";
    /// <summary>"October so far" (current month) or "September".</summary>
    public static string MonthTile(DateOnly month, bool current) => MonthName(month) + (current ? " so far" : "");
    /// <summary>"6 days · about $9.73 a day"</summary>
    public static string MonthTileDetail(int days, decimal total) =>
        days.ToString(Us) + (days == 1 ? " day" : " days") + " · about " + Money(days == 0 ? 0m : total / days) + " a day";
    public const string EachMonth = "Each month";
    public const string PreviousMonth = "Previous month";
    public const string NextMonth = "Next month";
    public const string MonthChartAccessibleName = "Money saved each month. Pick a month to see its days.";
    /// <summary>"October 2026"</summary>
    public static string MonthYear(DateOnly month) => month.ToString("MMMM yyyy", Us);
    public static string MonthName(DateOnly month) => month.ToString("MMMM", Us);
    /// <summary>"Oct" under a month bar.</summary>
    public static string MonthShort(DateOnly month) => month.ToString("MMM", Us);
    /// <summary>"$58" above a month bar.</summary>
    public static string MonthBarValue(decimal total) => "$" + Math.Round(total, MidpointRounding.AwayFromZero).ToString("#,0", Us);
    /// <summary>"Where October’s savings came from"</summary>
    public static string WhereMonthsSavingsCameFrom(DateOnly month) => "Where " + MonthName(month) + "’s savings came from";
    public const string DayByDay = "Day by day";
    public const string NewestFirst = "Newest first";
    public const string AQuietDay = "A quiet day";
    /// <summary>"Today" or "Tue 6 Oct".</summary>
    public static string DayLabel(DateOnly day, DateOnly today) => day == today ? Today : day.ToString("ddd d MMM", Us);
    public static string QuickReuses(int n) => n.ToString(Us) + " quick reuses";
    public static string KeptWarmTimes(int n) => "kept warm " + n.ToString(Us) + (n == 1 ? " time" : " times");
    public static string TidyUps(int n) => n.ToString(Us) + (n == 1 ? " tidy-up" : " tidy-ups");
    public static string HelperJobs(int n) => n.ToString(Us) + (n == 1 ? " helper job" : " helper jobs");

    // ---- Settings › Ignis: Rebuild points ----
    public const string RebuildPoints = "Rebuild points";
    /// <summary>"Context sizes, price steps, cache prices and rebuild points for every model. Version 2026.10.08."</summary>
    public static string ModelPointsDetail(string version) => "Context sizes, price steps, cache prices and rebuild points for every model. Version " + version + ".";
    public const string UpdateNow = "Update now";
    public const string UpdateAutomatically = "Update automatically";
    public const string UpdateAutomaticallyDetail = "Checked with nth's own updates. Never sends anything to a model.";
    public const string UpdateAutomaticallyAccessibleName = "Update model points automatically";
    public const string ColumnModel = "Model";
    public const string ColumnCheapestPoint = "Cheapest point";
    public const string ColumnWhy = "Why";
    /// <summary>"Price step at 100K"</summary>
    public static string WhyPriceStep(long stepTokens) => "Price step at " + Tokens(stepTokens);
    public const string WhyBreakEven = "Replaying the history costs more than a rebuild";
    /// <summary>"Rebuild is cheapest before the last quarter of its 400K window"</summary>
    public static string WhyLastQuarter(long window) => "Rebuild is cheapest before the last quarter of its " + Tokens(window) + " window";
    public const string PointMovesHint = "The point moves on its own when a chat switches models.";

    // ---- Settings › Updates row ----
    public const string UpdatesRowTitle = "Ignis model points";
    public const string UpdatesRowDetail = "Context, cache and rebuild points for each model";
    /// <summary>"2026.10.08 → 2026.10.10"</summary>
    public static string UpdatesRowVersions(string installed, string latest) => installed + " → " + latest;

    // ---- Save report ----
    /// <summary>The report text (design Stage.report), lines joined with "\n".</summary>
    public static string Report(IgnisReport r)
    {
        var lines = new List<string>
        {
            "Ignis report · this chat · " + r.At.ToString("dddd", Us) + " " + EventTime(r.At),
            "Model: " + r.Model + " · Mode: " + ModeName(r.Mode) + " · Auto " + (r.Auto ? "on" : "off"),
            "",
            "Saved today: " + Money(r.SavedToday),
        };
        foreach (var source in (IgnisSource[])[IgnisSource.Reuse, IgnisSource.KeptWarm, IgnisSource.TidyUp, IgnisSource.SmartHelpers])
            lines.Add("  " + SourceName(source).PadRight(21) + Money(r.SavedBySource.TryGetValue(source, out var amount) ? amount : 0m));
        if (r.SavedBySource.TryGetValue(IgnisSource.LongerMemory, out var longer) && longer > 0m) // ED-NEW-ignis-runtime: only once it saved
            lines.Add("  " + SourceName(IgnisSource.LongerMemory).PadRight(21) + Money(longer));
        lines.Add("Lost when it went cold: " + Money(r.Lost) + (r.ReuseRate is { } rate ? " · Quick memory reused " + Percent(rate) + " of the time" : " · Quick memory reuse " + NotReported));
        lines.Add("Chat size: " + (r.ContextFill is { } fill ? PercentFull(fill) : NotReported));
        lines.AddRange(r.CacheLines);
        lines.Add("");
        lines.Add(r.AccountLine);
        lines.Add("Last 7 days: " + Money(r.LastSevenDays) + " saved · All chats: " + MoneyWhole(r.AllChats) + " saved");
        return string.Join("\n", lines);
    }

    // ---- formatting ----
    /// <summary>"$3.18", "$1,478.00".</summary>
    public static string Money(decimal amount) => (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("#,0.00", Us);

    /// <summary>"$1,478" (the popover's All chats).</summary>
    public static string MoneyWhole(decimal amount) => (amount < 0 ? "-$" : "$") + Math.Round(Math.Abs(amount), MidpointRounding.AwayFromZero).ToString("#,0", Us);

    /// <summary>"2¢" for amounts under a dollar, else money.</summary>
    public static string Cents(decimal amount) => amount < 1m ? Math.Round(amount * 100m, MidpointRounding.AwayFromZero).ToString("0", Us) + "¢" : Money(amount);

    /// <summary>"9%".</summary>
    public static string Percent(double fraction) => Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", Us) + "%";

    /// <summary>Tokens as the design writes them: "95K", "31K", "1M", "1.5M".</summary>
    public static string Tokens(long tokens) => tokens >= 1_000_000
        ? (tokens / 1_000_000m).ToString("0.#", Us) + "M"
        : Math.Round(tokens / 1000m, MidpointRounding.AwayFromZero).ToString("0", Us) + "K";

    /// <summary>"15m", "1h", "1h 30m", "45s".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return Math.Round(span.TotalSeconds).ToString("0", Us) + "s";
        var minutes = (long)Math.Round(span.TotalMinutes);
        if (minutes % 60 == 0) return (minutes / 60).ToString(Us) + "h";
        return minutes < 60 ? minutes.ToString(Us) + "m" : (minutes / 60).ToString(Us) + "h " + (minutes % 60).ToString(Us) + "m";
    }

    private static long MinutesUp(TimeSpan? left) => left is { } value && value > TimeSpan.Zero ? (long)Math.Ceiling(value.TotalMinutes) : 0;
}

/// <summary>Everything the Save report needs; the controller fills it from the chat's status and the ledger.</summary>
public sealed record IgnisReport(
    DateTimeOffset At,
    string Model,
    IgnisMode Mode,
    bool Auto,
    decimal SavedToday,
    IReadOnlyDictionary<IgnisSource, decimal> SavedBySource,
    decimal Lost,
    double? ReuseRate,
    double? ContextFill,
    string AccountLine,
    decimal LastSevenDays,
    decimal AllChats)
{
    /// <summary>Extra lines after "Chat size" (ED-NEW-ignis-runtime): paid credits, helper rebuilds, the cache-lifetime choice.</summary>
    public IReadOnlyList<string> CacheLines { get; init; } = [];
}
