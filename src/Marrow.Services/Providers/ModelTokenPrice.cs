// PORT-ONLY: RR-50 / ED-461. Rates are USD per million tokens; ceilings require authoritative model limits.
using Marrow.Core.Models;

namespace Marrow.Services.Providers;

public sealed record ModelTokenPrice(ProviderKind Service, string Model, double InputPerMillion, double OutputPerMillion,
    double? CachedInputPerMillion, double RequestUsd, long? MaximumInputTokens, long? MaximumOutputTokens,
    DateTimeOffset RetrievedAt, Uri Source)
{
    public bool Valid => !string.IsNullOrWhiteSpace(Model) && Source.IsAbsoluteUri && Source.Scheme == "https"
        && double.IsFinite(InputPerMillion) && InputPerMillion >= 0 && double.IsFinite(OutputPerMillion) && OutputPerMillion >= 0
        && double.IsFinite(RequestUsd) && RequestUsd >= 0 && (CachedInputPerMillion is null || double.IsFinite(CachedInputPerMillion.Value) && CachedInputPerMillion >= 0)
        && (MaximumInputTokens is null || MaximumInputTokens > 0) && (MaximumOutputTokens is null || MaximumOutputTokens > 0);

    public double? CeilingUsd => Valid && MaximumInputTokens is { } input && MaximumOutputTokens is { } output
        && SafeCost(input * Math.Max(InputPerMillion, CachedInputPerMillion ?? InputPerMillion) / 1_000_000d + output * OutputPerMillion / 1_000_000d + RequestUsd) is { } cost ? cost : null;

    public double? Estimate(OpenAICompatibleSession.ReportedTokenUsage usage)
    {
        if (!Valid || usage.Total < 0 || usage.Input < 0 || usage.Output < 0 || usage.CachedInput < 0) return null;
        if (usage.Input is not { } input || usage.Output is not { } output)
        {
            // The SSE parser derives Total from available input/output when absent. One partial count is not a complete bill.
            if (usage.Total <= 0 || usage.Total < (usage.CachedInput ?? 0)
                || (usage.Input is not null || usage.Output is not null) && usage.Total <= Math.Max(usage.Input ?? 0, usage.Output ?? 0)) return null;
            return SafeCost(usage.Total * Math.Max(OutputPerMillion, Math.Max(InputPerMillion, CachedInputPerMillion ?? InputPerMillion)) / 1_000_000d + RequestUsd);
        }
        var cached = usage.CachedInput ?? 0;
        if (cached > input || input > usage.Total || output != usage.Total - input) return null;
        var inputRate = usage.CachedInput is null ? Math.Max(InputPerMillion, CachedInputPerMillion ?? InputPerMillion) : InputPerMillion;
        return SafeCost((input - cached) * inputRate / 1_000_000d + cached * (CachedInputPerMillion ?? InputPerMillion) / 1_000_000d
            + output * OutputPerMillion / 1_000_000d + RequestUsd);
    }

    private static double? SafeCost(double cost) => double.IsFinite(cost) && cost >= 0 ? cost : null;
}
