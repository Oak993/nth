// PORT-ONLY: OD-0065/0066's independent, versioned public-lineup cache; ED-613.
// Does not lock logo fallback colours or rank models. The missing locked colour design owns those rules.
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Port.ModelsPalette;

public sealed class PublicModelCatalogueStore : IDisposable
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private PublicModelCatalogueSnapshot? current;

    /// <param name="dataRoot">The app's isolated data root; no credential or personal-provider lookup.</param>
    public PublicModelCatalogueStore(string dataRoot)
    {
        CachePath = Path.Combine(dataRoot, "models-palette", "catalogue-v1.json");
        current = Load();
    }

    public string CachePath { get; }
    public PublicModelCatalogueSnapshot? Current => Volatile.Read(ref current);

    /// <summary>
    /// A new lineup becomes visible only after it has been validated and persisted atomically.
    /// An HTTP/parse failure returns false, a transport/disk error propagates; neither replaces known facts.
    /// The same method can serve automatic and manual checks, without account-dependent inputs.
    /// </summary>
    public async Task<bool> Refresh()
    {
        await refreshGate.WaitAsync();
        try
        {
            var next = await PublicModelCatalogue.Fetch();
            if (next is null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, Encode(next));
                File.Move(temporary, CachePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Volatile.Write(ref current, next);
            return true;
        }
        finally { refreshGate.Release(); }
    }

    private PublicModelCatalogueSnapshot? Load()
    {
        try
        {
            var body = File.ReadAllText(CachePath, Encoding.UTF8);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != 1 ||
                !root.TryGetProperty("fingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String)
                return null;
            var snapshot = PublicModelCatalogue.Parse(body);
            return snapshot is not null && string.Equals(snapshot.Fingerprint, fingerprint.GetString(), StringComparison.Ordinal)
                ? snapshot : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static byte[] Encode(PublicModelCatalogueSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", snapshot.Version);
            writer.WriteString("fingerprint", snapshot.Fingerprint);
            writer.WriteStartArray("data");
            foreach (var model in snapshot.Models) writer.WriteRawValue(model.PublicFacts);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public void Dispose() => refreshGate.Dispose();
}
