// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime). No Swift twin. The cache-lifetime choice made when nth launches the
// Claude CLI: for each of Claude Code's two TTL buckets (the main conversation, and everything else: subagents at every
// depth, workflows, forks, compaction, titles) leave the CLI's default or ask for one hour, from what Ignis learned of
// this provider's idle gaps and cache writes. Pure: no I/O and no clock; the controller fills the input.
//
// SOURCE FACT (https://code.claude.com/docs/en/prompt-caching, checked 2026-10-10): defaults are main 1 h and everything
// else 5 min on a Claude subscription within plan usage, both 5 min on usage credits, an API key or a cloud provider;
// CLAUDE_CODE_PROMPT_CACHE_TTL / CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL take only "5m" or "1h" (Claude Code v2.1.242+);
// precedence FORCE_PROMPT_CACHING_5M=1 > the bucket's env var > the bucket's setting (promptCacheTtl /
// subagentPromptCacheTtl) > subagent frontmatter experimental.cacheTtl > ENABLE_PROMPT_CACHING_1H=1 > default. Prices
// relative to base input: read 0.1x (0.05x on some models), 5-minute write 1.25x, 1-hour write 2x; a read refreshes the
// timer. Some server-controlled helper requests already get 1 h (docs; issue anthropics/claude-code#84289), so what the
// usage's cache_creation buckets show always wins over the assumed default.
//
// The rule (ENGINEERING DECISION, ED-NEW-ignis-runtime):
//   benefit(1h) = sum over observed gaps g with 5 min < g <= 1 h of prefix x (w5m - read)   [rebuilt under 5 min, read under 1 h]
//   cost(1h)    = sum over observed requests of the writes that 1 h would price at w1h instead of w5m, x (w1h - w5m)
//   ask for 1 h when benefit > cost x 1.1 and at least 12 gaps were seen; otherwise set nothing (the CLI default stays).
// Prices come from the model points of each request's own model (the main model's when a request's is unknown).
// Nothing here changes what the model sees: it only chooses how long the provider keeps the same prefix.
using System.Globalization;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>Claude Code's two prompt-cache TTL buckets.</summary>
public enum IgnisCacheBucket
{
    /// <summary>The main conversation (interactive turns and the helpers run inline with them).</summary>
    Main,
    /// <summary>Everything else: subagents at every depth, workflows, in-process teammates, forks, compaction, titles.</summary>
    Helpers,
}

/// <summary>
/// One observed request of a chat (Main) or of one helper (Helpers): the idle gap since the same stream's previous
/// request (null for its first), the prefix that was cached before the gap, and what this request wrote by TTL. No
/// message content. <see cref="LongerByNth"/>: nth had asked for 1 h in this bucket when the request ran.
/// </summary>
public sealed record IgnisRequestSample(
    DateTimeOffset At,
    string? Model,
    double? GapSeconds,
    long PrefixTokens,
    long Write5m,
    long Write1h,
    long WriteUnknown,
    bool LongerByNth);

/// <summary>Why a bucket was (or was not) asked for one hour.</summary>
public enum IgnisLifetimeReason
{
    /// <summary>One hour was asked for: the measured saving beats the extra write cost.</summary>
    Longer,
    /// <summary>Ignis is off for the chat.</summary>
    IgnisOff,
    /// <summary>Not a Claude chat (only the Claude CLI has the two buckets and their controls).</summary>
    NotClaude,
    /// <summary>"Include Hydra heads" is off (helpers bucket only).</summary>
    HeadsOff,
    /// <summary>The user already chose this bucket's lifetime (an environment variable or a Claude setting).</summary>
    UserChoice,
    /// <summary>The installed Claude CLI's version is not known.</summary>
    CliVersionUnknown,
    /// <summary>The installed Claude CLI is older than <see cref="IgnisCacheLifetime.MinimumCli"/>.</summary>
    CliTooOld,
    /// <summary>The bucket already gets one hour by default (subscription main; helpers already seen writing 1 h).</summary>
    AlreadyLong,
    /// <summary>Fewer than <see cref="IgnisCacheLifetime.MinimumGaps"/> gaps observed.</summary>
    TooFewGaps,
    /// <summary>The model points have no 5-minute and 1-hour write prices.</summary>
    NoPrices,
    /// <summary>The longer lifetime would cost more than it saves.</summary>
    NotWorthIt,
}

/// <summary>One bucket's decision with the numbers behind it (USD, API-equivalent on plans).</summary>
public sealed record IgnisLifetimeVerdict(IgnisCacheBucket Bucket, IgnisLifetimeReason Reason, int Gaps = 0, decimal Benefit = 0m, decimal Cost = 0m, string? UserSetting = null)
{
    /// <summary>nth sets this bucket to one hour.</summary>
    public bool SetsLonger => Reason == IgnisLifetimeReason.Longer;

    public static IgnisLifetimeVerdict Because(IgnisCacheBucket bucket, IgnisLifetimeReason reason, string? userSetting = null) => new(bucket, reason, UserSetting: userSetting);
}

/// <summary>Both buckets' decisions for one CLI launch, and the environment variables they add.</summary>
public sealed record IgnisLifetimeChoice(IgnisLifetimeVerdict Main, IgnisLifetimeVerdict Helpers)
{
    /// <summary>The variables to add to the CLI's environment (empty: nothing set, every default stays).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment
    {
        get
        {
            var list = new List<KeyValuePair<string, string>>();
            if (Main.SetsLonger) list.Add(new(IgnisCacheLifetime.MainVariable, IgnisCacheLifetime.OneHour));
            if (Helpers.SetsLonger) list.Add(new(IgnisCacheLifetime.HelpersVariable, IgnisCacheLifetime.OneHour));
            return list;
        }
    }

    public IgnisLifetimeVerdict For(IgnisCacheBucket bucket) => bucket == IgnisCacheBucket.Main ? Main : Helpers;
}

/// <summary>Everything one launch decision needs.</summary>
public sealed record IgnisLifetimeInput
{
    public required ProviderKind Provider { get; init; }
    public required IgnisMode Mode { get; init; }
    public bool IncludeHeads { get; init; } = true;
    /// <summary>The installed CLI's version as nth's provider check read it ("2.1.296"); null: unknown.</summary>
    public string? CliVersion { get; init; }
    /// <summary>The environment the CLI inherits (before nth adds anything).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    /// <summary>The TTL settings and env-block variables found in the user's Claude settings files (<see cref="SettingsKeys"/>).</summary>
    public IReadOnlySet<string> UserSettings { get; init; } = new HashSet<string>();
    public IReadOnlyList<IgnisRequestSample> Main { get; init; } = [];
    public IReadOnlyList<IgnisRequestSample> Helpers { get; init; } = [];
    public string? MainModel { get; init; }
    /// <summary>The points for a model id (null id: the main model).</summary>
    public required Func<string?, IgnisModelPoints?> PointsFor { get; init; }
}

public static class IgnisCacheLifetime
{
    public const string MainVariable = "CLAUDE_CODE_PROMPT_CACHE_TTL";
    public const string HelpersVariable = "CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL";
    public const string ForceShortVariable = "FORCE_PROMPT_CACHING_5M";
    public const string LongForBothVariable = "ENABLE_PROMPT_CACHING_1H";
    public const string MainSetting = "promptCacheTtl";
    public const string HelpersSetting = "subagentPromptCacheTtl";
    public const string OneHour = "1h";

    /// <summary>SOURCE FACT: both settings and both variables need Claude Code v2.1.242 or later.</summary>
    public static readonly Version MinimumCli = new(2, 1, 242);

    /// <summary>ENGINEERING DECISION: at least this many observed gaps before nth changes a bucket.</summary>
    public const int MinimumGaps = 12;

    /// <summary>ENGINEERING DECISION: the saving must beat the extra write cost by this factor.</summary>
    public const decimal Margin = 1.1m;

    public static readonly TimeSpan Short = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Long = TimeSpan.FromHours(1);

    /// <summary>Variables whose presence (in the inherited environment or a settings "env" block) means the user chose.</summary>
    public static IReadOnlyList<string> Variables { get; } = [ForceShortVariable, MainVariable, HelpersVariable, LongForBothVariable];

    /// <summary>The setting and variable names that mean the user chose a bucket's lifetime themselves.</summary>
    public static IReadOnlyList<string> UserChoiceKeys(IgnisCacheBucket bucket) => bucket == IgnisCacheBucket.Main
        ? [ForceShortVariable, MainVariable, LongForBothVariable, MainSetting]
        : [ForceShortVariable, HelpersVariable, LongForBothVariable, HelpersSetting];

    /// <summary>Signs in the inherited environment that the main conversation defaults to 5 minutes (API key, cloud provider).</summary>
    private static readonly string[] ShortMainMarkers = ["ANTHROPIC_API_KEY", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY"];

    public static IgnisLifetimeChoice Decide(IgnisLifetimeInput input) => new(Bucket(input, IgnisCacheBucket.Main), Bucket(input, IgnisCacheBucket.Helpers));

    public static IgnisLifetimeVerdict Bucket(IgnisLifetimeInput input, IgnisCacheBucket bucket)
    {
        if (input.Provider != ProviderKind.Claude) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.NotClaude);
        if (input.Mode == IgnisMode.Off) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.IgnisOff);
        if (bucket == IgnisCacheBucket.Helpers && !input.IncludeHeads) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.HeadsOff);
        if (UserChoice(input, bucket) is { } chosen) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.UserChoice, chosen);
        if (ParseVersion(input.CliVersion) is not { } version) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.CliVersionUnknown);
        if (version < MinimumCli) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.CliTooOld);

        var samples = bucket == IgnisCacheBucket.Main ? input.Main : input.Helpers;
        if (bucket == IgnisCacheBucket.Main && !MainDefaultIsShort(input)) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.AlreadyLong);
        if (bucket == IgnisCacheBucket.Helpers && HelpersAlreadyLong(samples)) return IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.AlreadyLong);

        var gaps = samples.Count(sample => sample.GapSeconds is not null);
        if (gaps < MinimumGaps) return new(bucket, IgnisLifetimeReason.TooFewGaps, gaps);
        if (Prices(input.PointsFor(null)) is null && samples.All(sample => Prices(input.PointsFor(sample.Model)) is null))
            return new(bucket, IgnisLifetimeReason.NoPrices, gaps);

        var (benefit, cost) = Worth(samples, input.PointsFor);
        return benefit > 0m && benefit > cost * Margin
            ? new(bucket, IgnisLifetimeReason.Longer, gaps, benefit, cost)
            : new(bucket, IgnisLifetimeReason.NotWorthIt, gaps, benefit, cost);
    }

    /// <summary>The benefit and the cost of one hour over the samples (USD; API-equivalent on plans).</summary>
    public static (decimal Benefit, decimal Cost) Worth(IReadOnlyList<IgnisRequestSample> samples, Func<string?, IgnisModelPoints?> pointsFor)
    {
        decimal benefit = 0m, cost = 0m;
        foreach (var sample in samples)
        {
            var points = pointsFor(sample.Model) ?? pointsFor(null);
            if (Prices(points) is not { } p) continue;
            if (sample.GapSeconds is { } gap && gap > Short.TotalSeconds && gap <= Long.TotalSeconds && sample.PrefixTokens > 0)
            {
                var tier = IgnisMoney.Tier(points!.Prices, sample.PrefixTokens);
                benefit += sample.PrefixTokens * ((tier.CacheWrite5m ?? p.W5) - tier.CacheRead) / 1_000_000m;
            }
            // Writes 1 h would price higher: every 5-minute or unlabelled write, and the 1-hour writes nth itself caused.
            // A 1-hour write the provider chose on its own (server-controlled helpers) costs the same either way.
            var raised = sample.Write5m + sample.WriteUnknown + (sample.LongerByNth ? sample.Write1h : 0);
            if (raised > 0)
            {
                var tier = IgnisMoney.Tier(points!.Prices, sample.PrefixTokens + raised);
                cost += raised * Math.Max(0m, (tier.CacheWrite1h ?? p.W1) - (tier.CacheWrite5m ?? p.W5)) / 1_000_000m;
            }
        }
        return (benefit, cost);
    }

    private static (decimal W5, decimal W1)? Prices(IgnisModelPoints? points) =>
        points?.Prices is { CacheWrite5m: { } w5, CacheWrite1h: { } w1 } ? (w5, w1) : null;

    /// <summary>
    /// The main conversation defaults to 5 minutes: an API key or a cloud provider in the inherited environment, or
    /// the newest main request nth did not lengthen wrote 5-minute entries only (usage credits).
    /// </summary>
    public static bool MainDefaultIsShort(IgnisLifetimeInput input)
    {
        if (EnvironmentSaysShortMain(input.Environment)) return true;
        var newest = input.Main.LastOrDefault(sample => !sample.LongerByNth && sample.Write5m + sample.Write1h > 0);
        return newest is { Write5m: > 0, Write1h: 0 };
    }

    /// <summary>An API key or a cloud provider in the inherited environment: the main conversation defaults to 5 minutes.</summary>
    public static bool EnvironmentSaysShortMain(IReadOnlyDictionary<string, string> environment)
    {
        foreach (var marker in ShortMainMarkers)
            if (Lookup(environment, marker) is { Length: > 0 } value && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Helpers already write 1-hour entries without nth (more 1 h than 5 min write tokens, nth not involved).</summary>
    public static bool HelpersAlreadyLong(IReadOnlyList<IgnisRequestSample> samples)
    {
        var own = samples.Where(sample => !sample.LongerByNth).ToList();
        var oneHour = own.Sum(sample => sample.Write1h);
        return oneHour > 0 && oneHour > own.Sum(sample => sample.Write5m);
    }

    /// <summary>The first user choice that decides this bucket (variable or setting name); null: none.</summary>
    public static string? UserChoice(IgnisLifetimeInput input, IgnisCacheBucket bucket)
    {
        foreach (var key in UserChoiceKeys(bucket))
            if (Lookup(input.Environment, key) is { Length: > 0 } || input.UserSettings.Contains(key)) return key;
        return null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> environment, string name)
    {
        if (environment.TryGetValue(name, out var exact)) return exact;
        foreach (var (key, value) in environment) if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value; // Windows names ignore case
        return null;
    }

    /// <summary>"2.1.296" (or "2.1.296 (Claude Code)") as a version; null when it has no major.minor.patch.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (text is null) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        try
        {
            return new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        catch (OverflowException) { return null; }
    }

    /// <summary>
    /// The TTL settings and env-block variables in Claude settings files' text (user, project, local, managed): the
    /// top-level "promptCacheTtl" / "subagentPromptCacheTtl" keys and any of <see cref="Variables"/> inside "env".
    /// Unreadable text is skipped. Read only: nth never writes the user's Claude settings.
    /// </summary>
    public static IReadOnlySet<string> SettingsKeys(IEnumerable<string> settingsTexts)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in settingsTexts)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(text, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = document.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                foreach (var key in (string[])[MainSetting, HelpersSetting])
                    if (root.TryGetProperty(key, out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null) found.Add(key);
                if (root.TryGetProperty("env", out var env) && env.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var property in env.EnumerateObject())
                        foreach (var name in Variables)
                            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) found.Add(name);
            }
            catch (System.Text.Json.JsonException) { }
        }
        return found;
    }

    // ---- savings of a lengthened bucket ----

    /// <summary>
    /// What one request saved because nth lengthened its bucket: after a gap of 5 min to the lifetime it read
    /// <paramref name="readTokens"/> of the prefix that a 5-minute cache would have rebuilt at the 5-minute write price.
    /// </summary>
    public static decimal AvoidedRebuild(IgnisModelPoints points, long prefixTokens, long readTokens, TimeSpan gap, TimeSpan lifetime)
    {
        if (gap <= Short || gap > lifetime || prefixTokens <= 0 || readTokens <= 0) return 0m;
        return Math.Max(0m, IgnisMoney.RebuildPremium(points, Math.Min(prefixTokens, readTokens), Short));
    }

    /// <summary>What nth's longer lifetime added to a request: its 1-hour writes at (w1h - w5m).</summary>
    public static decimal ExtraWriteCost(IgnisModelPoints points, long oneHourWrites, long promptTokens)
    {
        if (oneHourWrites <= 0 || Prices(points) is not { } p) return 0m;
        var tier = IgnisMoney.Tier(points.Prices, promptTokens);
        return oneHourWrites * Math.Max(0m, (tier.CacheWrite1h ?? p.W1) - (tier.CacheWrite5m ?? p.W5)) / 1_000_000m;
    }
}
