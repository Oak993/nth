// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime). The learned request rhythm (IIgnisGapHistory) on disk at
// Storage.Root/ignis/gaps-v1.json: per provider and bucket the last IgnisGapMemory.Window requests' gap, prefix, writes
// by TTL, model and whether nth had lengthened the bucket. No message content, no chat ids.
//
// File shape (compact JSON, fixed member order): {"version":1,"lists":[{"provider":"claude","bucket":"main|helpers",
// "samples":[{"at":"<utc>","model"?:"...","gap"?:312.5,"prefix":120000,"w5m":0,"w1h":2000,"wu":0,"nth":false}]}]}.
// Every change is written at once (atomic replace). An unreadable file is moved aside (gaps-v1.corrupt-<stamp>.json,
// never deleted) and learning starts over; an unreadable entry is skipped.
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Ignis;

public sealed class IgnisGapStore : IIgnisGapHistory
{
    private const string AtFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private readonly Lock gate = new();
    private readonly IgnisGapMemory memory = new();

    /// <param name="dataRoot">The app's data root (Storage.Root); the file is dataRoot/ignis/gaps-v1.json.</param>
    public IgnisGapStore(string dataRoot)
    {
        FilePath = Path.Combine(dataRoot, "ignis", "gaps-v1.json");
        Load();
    }

    public string FilePath { get; }

    public void Add(ProviderKind provider, IgnisCacheBucket bucket, IgnisRequestSample sample)
    {
        lock (gate)
        {
            memory.Add(provider, bucket, sample);
            Save();
        }
    }

    public IReadOnlyList<IgnisRequestSample> Samples(ProviderKind provider, IgnisCacheBucket bucket) => memory.Samples(provider, bucket);

    private static string BucketName(IgnisCacheBucket bucket) => bucket == IgnisCacheBucket.Main ? "main" : "helpers";

    private static IgnisCacheBucket? BucketFrom(string? raw) => raw switch { "main" => IgnisCacheBucket.Main, "helpers" => IgnisCacheBucket.Helpers, _ => null };

    private void Load()
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(FilePath)) return;
            bytes = File.ReadAllBytes(FilePath);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                throw new JsonException("not a version 1 gap file");
            if (!root.TryGetProperty("lists", out var lists) || lists.ValueKind != JsonValueKind.Array) return;
            foreach (var list in lists.EnumerateArray())
            {
                if (list.ValueKind != JsonValueKind.Object) continue;
                if (SwiftEnum<ProviderKind>.FromRawValue(Text(list, "provider")) is not { } provider || BucketFrom(Text(list, "bucket")) is not { } bucket) continue;
                if (!list.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in samples.EnumerateArray()) if (Read(row) is { } sample) memory.Add(provider, bucket, sample);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            MoveAside();
        }
    }

    private static IgnisRequestSample? Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (!DateTimeOffset.TryParseExact(Text(row, "at"), AtFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
        double? gap = row.TryGetProperty("gap", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetDouble(out var seconds) && seconds >= 0 ? seconds : null;
        long Number(string name) => row.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var value) && value >= 0 ? value : 0;
        var nth = row.TryGetProperty("nth", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new IgnisRequestSample(at, Text(row, "model"), gap, Number("prefix"), Number("w5m"), Number("w1h"), Number("wu"), nth);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void MoveAside()
    {
        var stamp = SwiftDate.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.GetDirectoryName(FilePath)!;
        var aside = Path.Combine(folder, "gaps-v1.corrupt-" + stamp + ".json");
        for (var n = 2; File.Exists(aside); n++) aside = Path.Combine(folder, "gaps-v1.corrupt-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
        try { File.Move(FilePath, aside); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The file's bytes for the current state (also what a test compares).</summary>
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("lists");
            foreach (var (provider, bucket, samples) in memory.All().OrderBy(list => list.Provider.Id, StringComparer.Ordinal).ThenBy(list => list.Bucket))
            {
                writer.WriteStartObject();
                writer.WriteString("provider", provider.Id);
                writer.WriteString("bucket", BucketName(bucket));
                writer.WriteStartArray("samples");
                foreach (var sample in samples)
                {
                    writer.WriteStartObject();
                    writer.WriteString("at", sample.At.ToUniversalTime().ToString(AtFormat, CultureInfo.InvariantCulture));
                    if (sample.Model is { } model) writer.WriteString("model", model);
                    if (sample.GapSeconds is { } gap) writer.WriteNumber("gap", Math.Round(gap, 3));
                    writer.WriteNumber("prefix", sample.PrefixTokens);
                    writer.WriteNumber("w5m", sample.Write5m);
                    writer.WriteNumber("w1h", sample.Write1h);
                    writer.WriteNumber("wu", sample.WriteUnknown);
                    writer.WriteBoolean("nth", sample.LongerByNth);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            FileOps.WriteAtomic(FilePath, Encode());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
