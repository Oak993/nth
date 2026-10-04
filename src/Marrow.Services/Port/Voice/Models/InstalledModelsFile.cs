// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): reading and writing installed.json (voice-SPEC V-9.3).
using System.Globalization;
using System.Text.Json;

namespace Marrow.Services.Port.Voice.Models;

/// <summary>{"models":[{"id","bytes","sha256","verified","fileTime"}]}: times are round-trip UTC strings. An unreadable file is an empty list.</summary>
internal static class InstalledModelsFile
{
    internal static List<InstalledModelRecord> Read(string path)
    {
        var records = new List<InstalledModelRecord>();
        try
        {
            if (!File.Exists(path)) return records;
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var entry in json.RootElement.GetProperty("models").EnumerateArray())
                records.Add(new InstalledModelRecord(entry.GetProperty("id").GetString()!, entry.GetProperty("bytes").GetInt64(), entry.GetProperty("sha256").GetString()!,
                    Time(entry, "verified"), Time(entry, "fileTime")));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IOException)
        {
            return [];
        }
        return records;
    }

    internal static void Write(string path, IReadOnlyList<InstalledModelRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("models");
            foreach (var r in records)
            {
                writer.WriteStartObject();
                writer.WriteString("id", r.Id);
                writer.WriteNumber("bytes", r.Bytes);
                writer.WriteString("sha256", r.Sha256);
                writer.WriteString("verified", r.VerifiedUtc.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("fileTime", r.FileTimeUtc.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        File.Move(temp, path, overwrite: true);
    }

    private static DateTime Time(JsonElement entry, string name) =>
        DateTime.Parse(entry.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
