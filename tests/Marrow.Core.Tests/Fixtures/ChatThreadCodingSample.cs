using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

// Trimmed shape of a real Codable type (B2), to prove Coding.swift's storage coders end to end; not port code (R-TEST-8).

/// <summary>
/// Core/Models/Library.swift:121-265 ChatThread, trimmed to five stored properties: <c>var id: UUID</c> (:124),
/// <c>var title: String</c> (:126), <c>var createdAt: Date</c> (:127), <c>var worktreePath: String?</c> (:135) and
/// <c>var sortOrder: Double?</c> (:149). ChatThread declares no <c>encode(to:)</c>, so its encoding is synthesized:
/// a nil optional is left out (encodeIfPresent). Its <c>init(from:)</c> is hand-written (:223-264); the trimmed lines
/// are :225, :227, :228, :239 and :249.
/// </summary>
[JsonConverter(typeof(ChatThreadCodingSampleConverter))]
public sealed record ChatThreadCodingSample(Guid Id, string Title, DateTimeOffset CreatedAt, string? WorktreePath, double? SortOrder)
{
    /// <summary>Library.swift:122 <c>static let untitled = "New thread"</c>.</summary>
    public const string Untitled = "New thread";
}

/// <summary>The hand-written converter every persisted type gets (R-CODE-3): reads through SwiftDecoding, writes Swift fields.</summary>
public sealed class ChatThreadCodingSampleConverter : JsonConverter<ChatThreadCodingSample>
{
    public override ChatThreadCodingSample Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var e = document.RootElement;
        var id = SwiftDecoding.Decode(e, "id", SwiftDecoding.ReadUUID);                                                   // :225
        var title = Coding.Value(e, "title", SwiftDecoding.ReadString, () => ChatThreadCodingSample.Untitled);            // :227
        var createdAt = Coding.Value(e, "createdAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now);                 // :228
        var worktreePath = Coding.Value<string?>(e, "worktreePath", SwiftDecoding.ReadString, () => null);               // :239
        var sortOrder = Coding.Value<double?>(e, "sortOrder", element => SwiftDecoding.ReadDouble(element), () => null); // :249
        return new(id, title, createdAt, worktreePath, sortOrder);
    }

    public override void Write(Utf8JsonWriter w, ChatThreadCodingSample value, JsonSerializerOptions options)
    {
        // Synthesized encode(to:), in declaration order; the storage style sorts the keys (Coding.swift:23).
        w.WriteStartObject();
        w.WriteString("id", SwiftUUID.UuidString(value.Id));
        w.WriteString("title", value.Title);
        w.WriteString("createdAt", FoundationISO8601.Format(value.CreatedAt)); // .iso8601 (Coding.swift:22)
        if (value.WorktreePath is not null) w.WriteString("worktreePath", value.WorktreePath);
        if (value.SortOrder is { } sortOrder) SwiftJson.WriteDouble(w, "sortOrder", sortOrder);
        w.WriteEndObject();
    }
}
