// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.2; unit AS-13; ED-NEW-dextra-as13-6). No Swift twin.
// Learns, per account, model and window, how many tokens one percent of the window costs. Windows report whole percent, so
// one reading says little: the calibrator pairs the ledger's tokens with the readings over an interval that changed by at
// least 3 points, drops any interval in which use outside nth was detected, and fits the rate of the intervals of the last 14
// days by their median, dropping outliers beyond 3 MAD. The spread of the rates is the confidence, and the p90 end of a
// cost widens with it. An account with fewer than 5 qualifying intervals keeps the conservative default (CalibratedRate).
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>One qualifying interval: the tokens the ledger saw while a window moved <see cref="Points"/> points.</summary>
public sealed record CalibrationInterval(string AccountKey, string Model, ReserveWindowKind Kind, DateTimeOffset At, double Points, double Tokens)
{
    /// <summary>Fields of the stored interval this build does not know: kept and written back (15.3 "unknown fields survive").</summary>
    internal JsonObject? Unknown { get; init; }

    public double TokensPerPercent => Tokens / Points;
}

public sealed class UsageCalibrator
{
    /// <summary>"Fits only intervals that span at least 3 points of change" (11.2).</summary>
    public const double MinPoints = 3;

    /// <summary>"A rolling 14-day fit" (11.2).</summary>
    public static readonly TimeSpan FitWindow = TimeSpan.FromDays(14);

    /// <summary>"Outliers beyond 3 MAD dropped" (11.2).</summary>
    public const double MadCut = 3;

    /// <summary>The model name of the pool every interval also joins: the fallback for a model not seen on its own.</summary>
    public const string AnyModel = "*";

    /// <summary>An interval belongs to a model when that model spent at least this share of its tokens.</summary>
    public const double DominantShare = 0.9;

    /// <summary>The most intervals kept per account, model and window (the oldest go first).</summary>
    public const int MaxIntervals = 200;

    /// <summary>The stored format's version (15.3).</summary>
    public const int FormatVersion = 1;

    private const double MadScale = 1.4826;
    private const double Z90 = 1.2816;

    private sealed class Open(double percent, DateTimeOffset at, DateTimeOffset? resetsAt)
    {
        public double Percent { get; } = percent;
        public DateTimeOffset At { get; } = at;
        public DateTimeOffset? ResetsAt { get; } = resetsAt;
        public Dictionary<string, double> Tokens { get; } = new(StringComparer.Ordinal);
    }

    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly Func<string, ReserveWindowKind, double> published;
    private readonly Dictionary<(string Account, string Window), Open> open = [];
    private readonly List<CalibrationInterval> intervals = [];
    private JsonObject? root;

    /// <summary>The stored file's version was newer than this build knows: it is read for nothing and never written over (15.3).</summary>
    public bool ReadOnly { get; private set; }

    public UsageCalibrator(TimeProvider? time = null, Func<string, ReserveWindowKind, double>? publishedRate = null)
    {
        clock = time ?? TimeProvider.System;
        published = publishedRate ?? ReserveDefaults.PublishedTokensPerPercent;
    }

    /// <summary>Raised after an interval is added (the caller saves the document).</summary>
    public event Action? Changed;

    // ---- feeding ----

    /// <summary>The ledger saw tokens spent on an account: they count in every window's open interval.</summary>
    public void Spend(string accountKey, string? model, long tokens)
    {
        if (tokens <= 0) return;
        lock (gate)
        {
            var name = string.IsNullOrWhiteSpace(model) ? AnyModel : model;
            foreach (var ((account, _), interval) in open)
                if (account == accountKey) interval.Tokens[name] = interval.Tokens.GetValueOrDefault(name) + tokens;
        }
    }

    /// <summary>
    /// A new reading of one window. <paramref name="outsideUse"/> is what the guard found in it (11.5): an interval that had
    /// outside use in it says nothing about nth's rate, so it is dropped. A window that reset starts a new interval.
    /// </summary>
    public void Observe(string accountKey, ReserveWindow reading, bool outsideUse)
    {
        if (!double.IsFinite(reading.Percent)) return;
        var added = false;
        lock (gate)
        {
            var key = (accountKey, reading.Id);
            if (!open.TryGetValue(key, out var started))
            {
                open[key] = new Open(reading.Percent, reading.ObservedAt, reading.ResetsAt);
                return;
            }
            var change = reading.Percent - started.Percent;
            var otherWindow = started.ResetsAt is { } before && reading.ResetsAt is { } after && (after - before).Duration() > AccountWindowWatcher.ResetSlack;
            if (otherWindow || change < 0 || outsideUse || reading.ObservedAt < started.At)
            {
                open[key] = new Open(reading.Percent, reading.ObservedAt, reading.ResetsAt); // nothing before this is comparable
                return;
            }
            if (change < MinPoints) return;                 // keep the interval open until it spans enough points
            open[key] = new Open(reading.Percent, reading.ObservedAt, reading.ResetsAt);
            added = Close(accountKey, reading.Kind, reading.ObservedAt, change, started.Tokens);
        }
        if (added) Changed?.Invoke();
    }

    private bool Close(string accountKey, ReserveWindowKind kind, DateTimeOffset at, double points, Dictionary<string, double> tokens)
    {
        var total = tokens.Values.Sum();
        if (total <= 0) return false;                       // nth spent nothing: the points were somebody else's
        intervals.Add(new CalibrationInterval(accountKey, AnyModel, kind, at, points, total));
        var dominant = tokens.OrderByDescending(t => t.Value).First();
        if (dominant.Key != AnyModel && dominant.Value / total >= DominantShare)
            intervals.Add(new CalibrationInterval(accountKey, dominant.Key, kind, at, points, total));
        Trim(accountKey);
        return true;
    }

    private void Trim(string accountKey)
    {
        foreach (var group in intervals.Where(i => i.AccountKey == accountKey).GroupBy(i => (i.Model, i.Kind)))
        {
            var excess = group.Count() - MaxIntervals;
            if (excess <= 0) continue;
            foreach (var old in group.OrderBy(i => i.At).Take(excess).ToList()) intervals.Remove(old);
        }
    }

    // ---- the fit ----

    /// <summary>
    /// The rate of one account, model and window: the fit of its own intervals when it has five that qualify, else the fit of the
    /// account's all-model pool (a little less sure of), else the conservative default from the provider's published rate.
    /// </summary>
    public CalibratedRate RateFor(string accountKey, string providerId, string? model, ReserveWindowKind kind)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(model) && Fit(accountKey, model, kind, now, pool: false) is { } own) return own;
            if (Fit(accountKey, AnyModel, kind, now, pool: true) is { } pooled) return pooled;
            return CalibratedRate.Conservative(published(providerId, kind));
        }
    }

    /// <summary>How many intervals of the last 14 days qualify for this account, model and window (before outliers are dropped).</summary>
    public int IntervalCount(string accountKey, string? model, ReserveWindowKind kind)
    {
        lock (gate)
        {
            var cutoff = clock.GetUtcNow() - FitWindow;
            var name = string.IsNullOrWhiteSpace(model) ? AnyModel : model;
            return intervals.Count(i => i.AccountKey == accountKey && i.Model == name && i.Kind == kind && i.At >= cutoff);
        }
    }

    private CalibratedRate? Fit(string accountKey, string model, ReserveWindowKind kind, DateTimeOffset now, bool pool)
    {
        var cutoff = now - FitWindow;
        var rates = intervals.Where(i => i.AccountKey == accountKey && i.Model == model && i.Kind == kind && i.At >= cutoff && i.Points >= MinPoints)
            .Select(i => i.TokensPerPercent).Where(r => r > 0 && double.IsFinite(r)).ToList();
        if (rates.Count < CalibratedRate.IntervalsToCalibrate) return null;
        return FitRates(rates, pool);
    }

    /// <summary>
    /// The robust fit (11.2): the median of the interval rates, outliers beyond 3 MAD dropped, the spread of what is left as the
    /// confidence, and the pessimistic end (where a step's p90 cost is read) pulled below the median by that spread. Null when
    /// fewer than five rates are left after the outliers go.
    /// </summary>
    public static CalibratedRate? FitRates(IReadOnlyList<double> rates, bool pooled = false)
    {
        if (rates.Count < CalibratedRate.IntervalsToCalibrate) return null;
        var median = Median(rates);
        var mad = Median([.. rates.Select(r => Math.Abs(r - median))]);
        var cut = MadCut * Math.Max(mad, 0.01 * median);    // identical rates have a MAD of 0: nothing may then be dropped for a hair
        var kept = rates.Where(r => Math.Abs(r - median) <= cut).ToList();
        if (kept.Count < CalibratedRate.IntervalsToCalibrate) return null;
        var center = Median(kept);
        var spread = MadScale * Median([.. kept.Select(r => Math.Abs(r - center))]) / center * Math.Sqrt(1.0 + 1.0 / kept.Count);
        var pessimistic = center * Math.Clamp(1 - Z90 * spread, 0.3, 1.0);
        var confidence = Math.Clamp(1 - spread, 0, 1) * Math.Min(1.0, kept.Count / 10.0) * (pooled ? 0.8 : 1.0);
        if (pooled) pessimistic *= 0.9;                      // the pool mixes models: widen a little more
        return new CalibratedRate(center, pessimistic, confidence, kept.Count, true);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // ---- the stored document ----

    /// <summary>Every interval of the last 14 days, oldest first (for the document and the tests).</summary>
    public IReadOnlyList<CalibrationInterval> Intervals
    {
        get { lock (gate) return [.. intervals.OrderBy(i => i.At)]; }
    }

    /// <summary>
    /// The document to store, or null when the file read was newer than this build knows (it is left as it was). Fields this
    /// build does not know, at the root and in each interval, are written back.
    /// </summary>
    public JsonObject? ToJson(JsonArray? turns = null)
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            var cutoff = clock.GetUtcNow() - FitWindow;
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            document["intervals"] = new JsonArray([.. intervals.Where(i => i.At >= cutoff).OrderBy(i => i.At).Select(Write)]);
            if (turns is not null) document["turns"] = turns;
            return document;
        }
    }

    private static JsonNode Write(CalibrationInterval interval)
    {
        var node = interval.Unknown is null ? new JsonObject() : (JsonObject)interval.Unknown.DeepClone();
        node["account"] = interval.AccountKey;
        node["model"] = interval.Model;
        node["kind"] = interval.Kind.ToString();
        node["at"] = interval.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        node["points"] = interval.Points;
        node["tokens"] = interval.Tokens;
        return node;
    }

    /// <summary>
    /// Loads a stored document. A newer version than this build knows makes the calibrator read-only and loads nothing; a
    /// line that is not an interval (wrong type, not finite, under 3 points) is skipped, never repaired.
    /// </summary>
    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            intervals.Clear();
            root = null;
            ReadOnly = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } number && number > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            root.Remove("intervals");
            root.Remove("version");
            if (document["intervals"] is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
                if (Read(line) is { } interval) intervals.Add(interval);
            foreach (var account in intervals.Select(i => i.AccountKey).Distinct().ToList()) Trim(account);
        }
    }

    private static CalibrationInterval? Read(JsonObject line)
    {
        try
        {
            if (line["account"] is not JsonValue account || !account.TryGetValue<string>(out var accountKey) || accountKey.Length == 0) return null;
            if (line["model"] is not JsonValue model || !model.TryGetValue<string>(out var modelName) || modelName.Length == 0) return null;
            if (line["kind"] is not JsonValue kindNode || !kindNode.TryGetValue<string>(out var kindText) || !Enum.TryParse<ReserveWindowKind>(kindText, out var kind)) return null;
            if (line["at"] is not JsonValue atNode || !atNode.TryGetValue<string>(out var atText)
                || !DateTimeOffset.TryParse(atText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
            if (AssistantJson.Number(line["points"]) is not { } points || !double.IsFinite(points) || points < MinPoints) return null;
            if (AssistantJson.Number(line["tokens"]) is not { } tokens || !double.IsFinite(tokens) || tokens <= 0) return null;
            var unknown = new JsonObject();
            foreach (var (name, value) in line)
                if (name is not ("account" or "model" or "kind" or "at" or "points" or "tokens")) unknown[name] = value?.DeepClone();
            return new CalibrationInterval(accountKey, modelName, kind, at, points, tokens) { Unknown = unknown.Count > 0 ? unknown : null };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
