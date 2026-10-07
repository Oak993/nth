// PORT-ONLY: OD-0065/0066's keyless global public lineup, independent of personal provider lists.
// ED-613. No strength/shade algorithm is inferred from the missing IR-3 design.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.ModelsPalette;

public sealed record PublicModelCatalogueEntry(string Id, string Maker, string PublicFacts);
public sealed record PublicModelCatalogueSnapshot(int Version, string Fingerprint, ValueList<PublicModelCatalogueEntry> Models);

public static class PublicModelCatalogue
{
    private const int FormatVersion = 1;

    /// <summary>Checks the same public source for every account. An unsuccessful answer is never a replacement lineup.</summary>
    public static async Task<PublicModelCatalogueSnapshot?> Fetch()
    {
        // OpenRouterAPI documents that /models is public. No bearer key, environment lookup or credential store.
        var request = new URLRequest(OpenRouterAPI.ModelsURL, timeoutInterval: 15)
            .SetValue("application/json", forHTTPHeaderField: "Accept");
        var (data, response) = await URLSession.Shared.Data(request);
        return response is HTTPURLResponse { StatusCode: 200 } ? Parse(Encoding.UTF8.GetString(data)) : null;
    }

    /// <summary>
    /// Keeps every published field, including name/tier/version, release date and prices for the later
    /// strength rule. Object property order and catalogue order cannot change the lineup fingerprint.
    /// Array order and number spelling are preserved as public facts from the one fixed source.
    /// </summary>
    public static PublicModelCatalogueSnapshot? Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            var models = new SortedDictionary<string, PublicModelCatalogueEntry>(StringComparer.Ordinal);
            foreach (var item in data.EnumerateArray())
            {
                // Match the existing OpenRouter parser's missing-id policy.
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var identity) ||
                    identity.ValueKind != JsonValueKind.String || identity.GetString() is not { Length: > 0 } id)
                    continue;
                var slash = id.IndexOf('/', StringComparison.Ordinal);
                var row = new PublicModelCatalogueEntry(id, slash > 0 ? id[..slash] : id, Canonical(item));
                if (models.TryGetValue(id, out var earlier) && earlier != row) return null;
                models[id] = row;
            }
            if (models.Count == 0) return null;
            var rows = new ValueList<PublicModelCatalogueEntry>(models.Values);
            var facts = "models-palette-catalogue-v1\n" + string.Join('\n', rows.Select(row => row.PublicFacts));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)));
            return new PublicModelCatalogueSnapshot(FormatVersion, fingerprint, rows);
        }
        catch (JsonException) { return null; }
    }

    private static string Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                // JsonElement lookup, like the existing parser, uses the last repeated property.
                foreach (var property in value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal)
                    .Select(group => group.Last()).OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray()) Write(writer, element);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
