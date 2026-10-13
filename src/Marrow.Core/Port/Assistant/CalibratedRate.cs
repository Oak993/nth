// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.2; unit AS-13; ED-NEW-dextra-as13-2). No Swift twin.
// What one percent of a usage window costs in tokens, for one account and model: the median, the pessimistic end the
// p90 of a step's cost is read from, and how much to trust it. A new account has no history, so it starts from a
// conservative default built from the provider's published rate.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>
/// The calibrated cost of one percent (11.2). A step of <c>t</c> tokens costs <c>t / TokensPerPercent</c> percent at the
/// median and <c>t / PessimisticTokensPerPercent</c> at its p90. The pessimistic end sits further from the median the more
/// the intervals disagree ("the p90 widens with the spread").
/// </summary>
public sealed record CalibratedRate(double TokensPerPercent, double PessimisticTokensPerPercent, double Confidence, int Intervals, bool Calibrated)
{
    /// <summary>A new account is used last until it has this many qualifying intervals (11.2).</summary>
    public const int IntervalsToCalibrate = 5;

    /// <summary>"The provider's published rate made 40% more expensive": 1% of a window is assumed to buy 0.6 of the default tokens.</summary>
    public const double ConservativeBuys = 0.6;

    /// <summary>"With the p90 doubled": the default's p90 cost is twice its median cost.</summary>
    public const double ConservativeP90Factor = 2.0;

    public double Percent(double tokens) => tokens <= 0 ? 0 : tokens / TokensPerPercent;

    public double PercentP90(double tokens) => tokens <= 0 ? 0 : tokens / PessimisticTokensPerPercent;

    /// <summary>The plan line marks an account that is not calibrated with "(estimate rough)".</summary>
    public bool Rough => !Calibrated;

    /// <summary>The conservative default for an account with no qualifying history, from the provider's published tokens per percent.</summary>
    public static CalibratedRate Conservative(double publishedTokensPerPercent)
    {
        var published = publishedTokensPerPercent > 0 && double.IsFinite(publishedTokensPerPercent) ? publishedTokensPerPercent : ReserveDefaults.FallbackTokensPerPercent;
        var median = published * ConservativeBuys;
        return new CalibratedRate(median, median / ConservativeP90Factor, 0, 0, false);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{TokensPerPercent:0} tokens/% (p90 {PessimisticTokensPerPercent:0}), confidence {Confidence:0.00}, {Intervals} intervals");
}

/// <summary>The starting numbers of 11.2 and 11.5 in one place, so a test and the engine read the same ones.</summary>
public static class ReserveDefaults
{
    /// <summary>Used when a provider's published rate is unknown: a small rate makes every step look expensive (fail safe).</summary>
    public const double FallbackTokensPerPercent = 100_000;

    /// <summary>
    /// The provider's published rate of tokens per percent, by window (ED-NEW-dextra-as13-2). Providers publish no such
    /// number; these are rough starting points that the calibrator replaces after five qualifying intervals, and the
    /// conservative default (CalibratedRate.Conservative) makes them 40% dearer with a doubled p90 until then.
    /// </summary>
    public static double PublishedTokensPerPercent(string providerId, ReserveWindowKind kind) => kind switch
    {
        ReserveWindowKind.FiveHour => 250_000,
        ReserveWindowKind.Weekly => 2_000_000,
        ReserveWindowKind.Day => 1_000_000,
        ReserveWindowKind.Month => 20_000_000,
        _ => FallbackTokensPerPercent,
    };

    /// <summary>Tokens a turn is assumed to cost until the account's own turns have been seen: a deliberately large guess.</summary>
    public const double UncalibratedTurnTokens = 120_000;

    /// <summary>A reading is whole points, so movement under a point hides inside it (11.5).</summary>
    public const double ReadingQuantum = 1.0;

    /// <summary>The plan-limit poll beats once a minute (MultiAccountRouting.cs:287), and the guard does not poll faster (11.5).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary>The recent burn is never read as less than one quantum per poll, which a whole-point reading cannot rule out.</summary>
    public static double MinimumBurnPerSecond => ReadingQuantum / PollInterval.TotalSeconds;

    /// <summary>How far back the peak of recent burn looks.</summary>
    public static readonly TimeSpan BurnLookback = TimeSpan.FromMinutes(15);

    /// <summary>A rise is never divided by less than this, so two readings close together cannot make an enormous rate.</summary>
    public static readonly TimeSpan BurnMinSpan = TimeSpan.FromSeconds(30);
}
