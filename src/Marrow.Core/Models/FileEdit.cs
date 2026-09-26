using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Timeline.swift:202-207, with the convenience initializer from Text.swift:213-216 (B3).
[JsonConverter(typeof(FileEditConverter))]
public sealed record FileEdit(string Path, string? Diff = null, long Additions = 0, long Deletions = 0)
{
    public FileEdit(string path, string oldText, string newText)
        : this(path, SimpleDiff.Unified(oldText, newText)) { }

    private FileEdit(string path, FileEditStats stats)
        : this(path, stats.Diff, stats.Additions, stats.Deletions) { }
}

internal sealed class FileEditConverter : JsonConverter<FileEdit>
{
    public override bool HandleNull => true;

    public override FileEdit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("additions", out var additions) || additions.ValueKind != JsonValueKind.Number ||
            !root.TryGetProperty("deletions", out var deletions) || deletions.ValueKind != JsonValueKind.Number)
            throw new JsonException();
        string? diff = null;
        if (root.TryGetProperty("diff", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String) throw new JsonException();
            diff = value.GetString();
        }
        return new(path.GetString()!, diff,
            additions.Deserialize<long>(Coding.StorageOptions), deletions.Deserialize<long>(Coding.StorageOptions));
    }

    public override void Write(Utf8JsonWriter writer, FileEdit value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        if (value.Diff is not null) writer.WriteString("diff", value.Diff);
        writer.WriteNumber("additions", value.Additions);
        writer.WriteNumber("deletions", value.Deletions);
        writer.WriteEndObject();
    }
}
