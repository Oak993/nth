// PORT-ONLY: RR-50 / ED-461. Exact model/service binding, dated source validation, and explicitly unfilled page-only data.
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

public sealed class ModelPriceCatalog(IEnumerable<ModelTokenPrice> prices)
{
    private readonly ModelTokenPrice[] entries = prices.ToArray();
    public static Ambient<ModelPriceCatalog> Current { get; } = new(FromJson(ModelPriceData.Json));

    public ModelTokenPrice? Find(ProviderKind service, string model, DateTimeOffset now) =>
        entries.Where(price => price.Service == service && string.Equals(price.Model, model, StringComparison.Ordinal)
            && price.Valid && Authoritative(price.Service, price.Source) && price.RetrievedAt <= now && now - price.RetrievedAt < TimeSpan.FromDays(1))
            .OrderByDescending(price => price.RetrievedAt).FirstOrDefault();

    internal static bool Authoritative(ProviderKind service, Uri source) => source.IsAbsoluteUri && source.Scheme == "https"
        && source.Port == 443 && source.UserInfo.Length == 0 && service switch
        {
            ProviderKind.Deepseek => source.Host == "api-docs.deepseek.com",
            ProviderKind.Meta => source.Host == "dev.meta.ai",
            ProviderKind.Zai => source.Host == "docs.z.ai",
            ProviderKind.OpenRouter => source.Host == "openrouter.ai",
            _ => false,
        };

    public static ModelPriceCatalog FromJson(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("state").GetString() != "filled") return new([]);
            var prices = new List<ModelTokenPrice>();
            foreach (var row in root.GetProperty("prices").EnumerateArray())
            {
                var service = SwiftEnum<ProviderKind>.FromRawValue(row.GetProperty("service").GetString()!);
                if (service is null) return new([]);
                double? OptionalDouble(string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetDouble() : null;
                long? OptionalLong(string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt64() : null;
                var price = new ModelTokenPrice(service.Value, row.GetProperty("model").GetString()!, row.GetProperty("inputUsdPerMillion").GetDouble(),
                    row.GetProperty("outputUsdPerMillion").GetDouble(), OptionalDouble("cachedInputUsdPerMillion"), row.GetProperty("requestUsd").GetDouble(),
                    OptionalLong("maximumInputTokens"), OptionalLong("maximumOutputTokens"), row.GetProperty("retrievedAt").GetDateTimeOffset(), new Uri(row.GetProperty("sourceUrl").GetString()!));
                if (!price.Valid || !Authoritative(service.Value, price.Source) || row.GetProperty("sourceSha256").GetString() is not { Length: 64 } hash
                    || !hash.All(Uri.IsHexDigit)) return new([]);
                prices.Add(price);
            }
            if (prices.GroupBy(price => (price.Service, price.Model)).Any(group => group.Count() != 1)) return new([]);
            return new(prices);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { return new([]); }
    }
}
