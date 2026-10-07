// PORT-ONLY: RR-50. Official public /models and provider.max_price contracts: OpenRouterTeam/typescript-sdk 3c88d707, src/models/model.ts, publicpricing.ts, providerpreferences.ts.
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

internal static class OpenRouterPriceSource
{
    internal static Ambient<Func<Uri, CancellationToken, Task<string?>>> Fetch { get; } = new(FetchPublic);

    private static async Task<string?> FetchPublic(Uri url, CancellationToken ct)
    {
        var (data, response) = await URLSession.Shared.Data(new URLRequest(url, timeoutInterval: 15) { FollowRedirects = false, UseCookies = false }).WaitAsync(ct);
        return response is HTTPURLResponse { StatusCode: 200 } ? System.Text.Encoding.UTF8.GetString(data) : null;
    }

    internal static async Task<ModelTokenPrice?> ResolveAsync(string model, CancellationToken ct)
    {
        // Auto routing has no fixed per-model rate. Do not invent a routing price.
        if (model == OpenRouterAPI.AutoModelId) return null;
        try
        {
            var json = await Fetch.Current(OpenRouterAPI.ModelsURL, ct);
            ct.ThrowIfCancellationRequested();
            if (json is null) return null;
            using var parsed = JsonDocument.Parse(json);
            var rows = parsed.RootElement.GetProperty("data").EnumerateArray().Where(row => row.GetProperty("id").GetString() == model).ToArray();
            if (rows.Length != 1) return null;
            var row = rows[0]; var rates = row.GetProperty("pricing");
            double? Rate(string key, bool optional = false)
            {
                if (!rates.TryGetProperty(key, out var value)) return optional ? 0 : null;
                if (value.ValueKind != JsonValueKind.String || !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number) || number < 0) return null;
                return number;
            }
            if (Rate("prompt") is not { } input || Rate("completion") is not { } output || Rate("request", true) is not { } request) return null;
            // A documented discount can only reduce the bound. Malformed/negative values might increase it.
            if (rates.TryGetProperty("discount", out var discount) && (discount.ValueKind != JsonValueKind.Number
                || !discount.TryGetDouble(out var reduction) || !double.IsFinite(reduction) || reduction is < 0 or > 1)) return null;
            // Unhandled conditional/extra billing cannot be treated as a flat text price.
            foreach (var property in rates.EnumerateObject())
            {
                if (property.Name is "prompt" or "completion" or "request" or "discount") continue;
                if (property.Name == "overrides" && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0) continue;
                if (Rate(property.Name) is not 0) return null;
            }
            long? inputLimit = row.TryGetProperty("context_length", out var context) && context.TryGetInt64(out var tokens) && tokens > 0 ? tokens : null;
            long? outputLimit = row.TryGetProperty("top_provider", out var top) && top.ValueKind == JsonValueKind.Object
                && top.TryGetProperty("max_completion_tokens", out var max) && max.TryGetInt64(out var completion) && completion > 0 ? completion : null;
            var price = new ModelTokenPrice(ProviderKind.OpenRouter, model, input * 1_000_000, output * 1_000_000, null, request,
                inputLimit, outputLimit, SwiftClock.Provider.Current.GetUtcNow(), OpenRouterAPI.ModelsURL);
            return price.Valid ? price : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException
            or System.Net.Http.HttpRequestException or System.IO.IOException)
        { return null; }
    }
}
