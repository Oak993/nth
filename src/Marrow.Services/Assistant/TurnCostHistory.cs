// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.4, 11.5; unit AS-13; ED-NEW-dextra-as13-6). No Swift twin.
// What an agent's turn has cost in tokens, per model, over the last 14 days: the p50 and the p90 the plan and the pause rule
// read ("calibrated history of similar steps, a p50 to p90 range"). A model with too few turns on record takes a deliberately
// large guess, and a p90 taken from few turns is widened, so a thin history never makes a turn look cheap.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class TurnCostHistory
{
    /// <summary>Fewer turns than this on record and the model takes the default guess.</summary>
    public const int MinTurns = 3;

    /// <summary>The most turns kept per model.</summary>
    public const int MaxTurns = 200;

    private sealed record Turn(string Model, DateTimeOffset At, double Tokens)
    {
        /// <summary>Fields of the stored line this build does not know: written back (15.3).</summary>
        public JsonObject? Unknown { get; init; }
    }

    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly List<Turn> turns = [];

    public TurnCostHistory(TimeProvider? time = null) => clock = time ?? TimeProvider.System;

    public event Action? Changed;

    /// <summary>A turn ended having cost <paramref name="tokens"/> in all (the ledger's spends of the turn, added up).</summary>
    public void Record(string? model, long tokens, DateTimeOffset at)
    {
        if (tokens <= 0) return;
        var name = ModelName(model);
        lock (gate)
        {
            turns.Add(new Turn(name, at, tokens));
            var own = turns.Where(t => t.Model == name).ToList();
            foreach (var old in own.OrderBy(t => t.At).Take(Math.Max(0, own.Count - MaxTurns))) turns.Remove(old);
        }
        Changed?.Invoke();
    }

    /// <summary>The turns of the last 14 days for the model.</summary>
    public int Count(string? model)
    {
        lock (gate) return Recent(ModelName(model)).Count;
    }

    /// <summary>The median turn in tokens; the default guess for a model with a thin history.</summary>
    public double P50(string? model)
    {
        lock (gate)
        {
            var costs = Recent(ModelName(model));
            return costs.Count < MinTurns ? ReserveDefaults.UncalibratedTurnTokens / 2 : Percentile(costs, 0.5);
        }
    }

    /// <summary>
    /// The 90th percentile of a turn in tokens, widened by 1 + 1/sqrt(n) for n turns on record (a p90 from few turns is rough);
    /// the large default guess for a model with fewer than three.
    /// </summary>
    public double P90(string? model)
    {
        lock (gate)
        {
            var costs = Recent(ModelName(model));
            if (costs.Count < MinTurns) return ReserveDefaults.UncalibratedTurnTokens;
            return Percentile(costs, 0.9) * (1 + 1 / Math.Sqrt(costs.Count));
        }
    }

    private List<double> Recent(string model)
    {
        var cutoff = clock.GetUtcNow() - UsageCalibrator.FitWindow;
        return [.. turns.Where(t => t.Model == model && t.At >= cutoff).Select(t => t.Tokens).Order()];
    }

    private static double Percentile(List<double> sorted, double p)
    {
        var position = p * (sorted.Count - 1);
        var low = (int)Math.Floor(position);
        var high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    private static string ModelName(string? model) => string.IsNullOrWhiteSpace(model) ? UsageCalibrator.AnyModel : model;

    // ---- the stored document ----

    public JsonArray ToJson()
    {
        lock (gate)
        {
            var cutoff = clock.GetUtcNow() - UsageCalibrator.FitWindow;
            return new JsonArray([.. turns.Where(t => t.At >= cutoff).OrderBy(t => t.At).Select(t =>
            {
                var line = t.Unknown is null ? new JsonObject() : (JsonObject)t.Unknown.DeepClone();
                line["model"] = t.Model;
                line["at"] = t.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                line["tokens"] = t.Tokens;
                return (JsonNode)line;
            })]);
        }
    }

    /// <summary>Loads the stored turns; a line that is not one (wrong type, not finite, not positive) is skipped.</summary>
    public void Load(JsonNode? stored)
    {
        lock (gate)
        {
            turns.Clear();
            if (stored is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
            {
                try
                {
                    if (line["model"] is JsonValue m && m.TryGetValue<string>(out var model) && model.Length > 0
                        && line["at"] is JsonValue a && a.TryGetValue<string>(out var atText)
                        && DateTimeOffset.TryParse(atText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                        && AssistantJson.Number(line["tokens"]) is { } tokens && tokens > 0 && double.IsFinite(tokens))
                    {
                        var unknown = new JsonObject();
                        foreach (var (name, value) in line)
                            if (name is not ("model" or "at" or "tokens")) unknown[name] = value?.DeepClone();
                        turns.Add(new Turn(model, at, tokens) { Unknown = unknown.Count > 0 ? unknown : null });
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or FormatException)
                {
                    // a line that is not a turn is skipped
                }
            }
        }
    }
}
