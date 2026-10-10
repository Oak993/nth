// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-integration). No Swift twin. Whether nth asks pi for its long prompt-cache
// retention (PI_CACHE_RETENTION=long) when it launches a pi session: the same gap rule that chooses Claude's one-hour
// lifetime (IgnisCacheLifetime.Worth, MinimumGaps, Margin) applied to the idle gaps Ignis learned for provider "pi", priced
// on the model pi talks to. Pure: no I/O and no clock; the controller fills the input.
//
// SOURCE FACT (pi-coding-agent README, 0.80.9): PI_CACHE_RETENTION=long is "extended prompt cache (Anthropic: 1h, OpenAI: 24h)".
// An Anthropic 1-hour write costs 2x input against 1.25x for 5 minutes; an OpenAI 24-hour retention has no write premium.
//
// The rule (ENGINEERING DECISION, ED-NEW-ignis-integration), by the upstream name in pi's "provider/model" id:
//   openai, openai-codex    : no extra cost, a longer memory only ever saves: ask for long.
//   anthropic               : the Claude rule over pi's gaps and its Anthropic requests' writes (pi reports one cacheWrite number, so
//                             every write counts as a 5-minute write that 1 h would reprice): long when benefit > cost x 1.1 and at
//                             least 12 gaps were seen.
//   anything else / unknown : pi's setting does nothing there, so nth sets nothing.
// A PI_CACHE_RETENTION the user set (any letter case) is never replaced.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>Why pi was (or was not) asked for its long retention.</summary>
public enum IgnisPiRetentionReason
{
    /// <summary>Anthropic upstream: the measured saving beats the extra write cost.</summary>
    Longer,
    /// <summary>OpenAI upstream: the 24-hour retention has no write premium, so it always pays.</summary>
    LongerFree,
    /// <summary>Ignis is off for the chat.</summary>
    IgnisOff,
    /// <summary>Not a pi session.</summary>
    NotPi,
    /// <summary>The user already set PI_CACHE_RETENTION.</summary>
    UserChoice,
    /// <summary>The upstream provider is not known, or is neither Anthropic nor OpenAI (pi's setting does not apply).</summary>
    UnknownUpstream,
    /// <summary>Fewer than <see cref="IgnisCacheLifetime.MinimumGaps"/> gaps observed on an Anthropic upstream.</summary>
    TooFewGaps,
    /// <summary>The model points have no 5-minute and 1-hour write prices.</summary>
    NoPrices,
    /// <summary>The longer retention would cost more than it saves.</summary>
    NotWorthIt,
}

/// <summary>The decision with the numbers behind it (USD, API-equivalent on plans).</summary>
public sealed record IgnisPiRetentionVerdict(IgnisPiRetentionReason Reason, string? Upstream = null, int Gaps = 0, decimal Benefit = 0m, decimal Cost = 0m)
{
    /// <summary>nth sets PI_CACHE_RETENTION=long.</summary>
    public bool SetsLong => Reason is IgnisPiRetentionReason.Longer or IgnisPiRetentionReason.LongerFree;
}

/// <summary>Everything one pi launch decision needs.</summary>
public sealed record IgnisPiRetentionInput
{
    public required ProviderKind Provider { get; init; }
    public required IgnisMode Mode { get; init; }
    /// <summary>The environment pi inherits (before nth adds anything).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    /// <summary>The model the session launches on, "provider/id" as pi names it ("anthropic/claude-sonnet-4-5"); null: pi's own default.</summary>
    public string? Model { get; init; }
    /// <summary>pi's learned requests (provider "pi"), oldest first; each sample's model is pi's "provider/id".</summary>
    public IReadOnlyList<IgnisRequestSample> Samples { get; init; } = [];
    /// <summary>The Anthropic points for a bare model id.</summary>
    public required Func<string?, IgnisModelPoints?> PointsFor { get; init; }
}

public static class IgnisPiRetention
{
    public const string Variable = "PI_CACHE_RETENTION";
    public const string Long = "long";

    /// <summary>The upstream name pi uses for Anthropic.</summary>
    public const string AnthropicUpstream = "anthropic";

    /// <summary>The upstream names pi uses for OpenAI (API key, and the ChatGPT-subscription Codex backend).</summary>
    public static IReadOnlyList<string> OpenAiUpstreams { get; } = ["openai", "openai-codex"];

    /// <summary>"anthropic/claude-sonnet-4-5" as (anthropic, claude-sonnet-4-5); null when there is no "provider/" prefix.</summary>
    public static (string Upstream, string Id)? Split(string? model)
    {
        if (model is not { Length: > 0 }) return null;
        var slash = model.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < model.Length - 1 ? (model[..slash].ToLowerInvariant(), model[(slash + 1)..]) : null;
    }

    public static IgnisPiRetentionVerdict Decide(IgnisPiRetentionInput input)
    {
        if (input.Provider != ProviderKind.Pi) return new(IgnisPiRetentionReason.NotPi);
        if (input.Mode == IgnisMode.Off) return new(IgnisPiRetentionReason.IgnisOff);
        foreach (var key in input.Environment.Keys)
            if (string.Equals(key, Variable, StringComparison.OrdinalIgnoreCase)) return new(IgnisPiRetentionReason.UserChoice);

        var launch = Split(input.Model);
        // The model pi launches on says where it talks; with no model named, the newest request pi was seen to make.
        var upstream = launch?.Upstream ?? input.Samples.Select(sample => Split(sample.Model)).LastOrDefault(split => split is not null)?.Upstream;
        if (upstream is null) return new(IgnisPiRetentionReason.UnknownUpstream);
        if (OpenAiUpstreams.Contains(upstream)) return new(IgnisPiRetentionReason.LongerFree, upstream);
        if (upstream != AnthropicUpstream) return new(IgnisPiRetentionReason.UnknownUpstream, upstream);

        // Only the Anthropic requests count, priced on the model id without pi's "anthropic/" prefix.
        var samples = input.Samples
            .Select(sample => (Sample: sample, Split: Split(sample.Model)))
            .Where(pair => pair.Split is { Upstream: AnthropicUpstream })
            .Select(pair => pair.Sample with { Model = pair.Split!.Value.Id })
            .ToList();
        IgnisModelPoints? PointsFor(string? id) => input.PointsFor(id ?? launch?.Id);

        var gaps = samples.Count(sample => sample.GapSeconds is not null);
        if (gaps < IgnisCacheLifetime.MinimumGaps) return new(IgnisPiRetentionReason.TooFewGaps, upstream, gaps);
        if (!HasWritePrices(PointsFor(null)) && samples.All(sample => !HasWritePrices(PointsFor(sample.Model))))
            return new(IgnisPiRetentionReason.NoPrices, upstream, gaps);

        var (benefit, cost) = IgnisCacheLifetime.Worth(samples, PointsFor);
        return benefit > 0m && benefit > cost * IgnisCacheLifetime.Margin
            ? new(IgnisPiRetentionReason.Longer, upstream, gaps, benefit, cost)
            : new(IgnisPiRetentionReason.NotWorthIt, upstream, gaps, benefit, cost);
    }

    private static bool HasWritePrices(IgnisModelPoints? points) => points?.Prices is { CacheWrite5m: not null, CacheWrite1h: not null };
}
