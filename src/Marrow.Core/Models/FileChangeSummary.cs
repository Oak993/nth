using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/FileChangeSummary.swift (B2 1fbf81e), :1-15.
//
// PR-RUNTIME-264, narrowed by the ruling on STOP Core.Models.FileChangeSummary #1 (2026-09-23) to what this file
// itself decides: the synthesized keys, strict synthesized decoding in declaration order, the totals stored as
// given through the memberwise init, and value equality. The turn-end-row round trip and the nil cases are
// PR-CHAT-012 (Core.Models.Timeline); the summing init(files:) is PR-GIT-092 (Services.Git.FileChangeSummary_Diff).
//
// :4-15 struct FileChangeSummary: Codable, Hashable, Sendable -> sealed record (R-VAL-1). No CodingKeys and no
// custom init(from:)/encode(to:), so both are synthesized: every key is the property name, every key is required
// on decode in declaration order (R-CODE-1), and nothing here computes the totals.
[JsonConverter(typeof(FileChangeSummaryConverter))]
public sealed record FileChangeSummary(ValueList<FileChangeSummary.File> Files, long Additions, long Deletions)
{
    // :5-10 struct File: Codable, Hashable, Sendable
    [JsonConverter(typeof(FileChangeSummaryFileConverter))]
    public sealed record File(string Path, long Additions, long Deletions, bool IsBinary);
}

// PORT-ONLY: System.Text.Json converter for FileChangeSummary, standing in for its synthesized Codable (R-CODE-1,
// R-CODE-3). Encodes members in declaration order (files, additions, deletions); SwiftJson.Write applies sorting
// for a sorted style such as Storage, so the order written here only matters for an unsorted style.
internal sealed class FileChangeSummaryConverter : JsonConverter<FileChangeSummary>
{
    public override FileChangeSummary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static FileChangeSummary Decode(JsonElement element) => new(
        SwiftDecoding.Decode(element, "files", field => SwiftDecoding.ReadArray(field, FileChangeSummaryFileConverter.Decode)),
        SwiftDecoding.Decode(element, "additions", SwiftDecoding.ReadInt),
        SwiftDecoding.Decode(element, "deletions", SwiftDecoding.ReadInt));

    public override void Write(Utf8JsonWriter writer, FileChangeSummary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("files");
        writer.WriteStartArray();
        foreach (var file in value.Files) FileChangeSummaryFileConverter.WriteMembers(writer, file);
        writer.WriteEndArray();
        writer.WriteNumber("additions", value.Additions);
        writer.WriteNumber("deletions", value.Deletions);
        writer.WriteEndObject();
    }
}

// PORT-ONLY: System.Text.Json converter for FileChangeSummary.File, standing in for its synthesized Codable.
// Declaration order: path, additions, deletions, isBinary (:6-9).
internal sealed class FileChangeSummaryFileConverter : JsonConverter<FileChangeSummary.File>
{
    public override FileChangeSummary.File Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static FileChangeSummary.File Decode(JsonElement element) => new(
        SwiftDecoding.Decode(element, "path", SwiftDecoding.ReadString),
        SwiftDecoding.Decode(element, "additions", SwiftDecoding.ReadInt),
        SwiftDecoding.Decode(element, "deletions", SwiftDecoding.ReadInt),
        SwiftDecoding.Decode(element, "isBinary", SwiftDecoding.ReadBool));

    public override void Write(Utf8JsonWriter writer, FileChangeSummary.File value, JsonSerializerOptions options) =>
        WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, FileChangeSummary.File value)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteNumber("additions", value.Additions);
        writer.WriteNumber("deletions", value.Deletions);
        writer.WriteBoolean("isBinary", value.IsBinary);
        writer.WriteEndObject();
    }
}
