using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Timeline.swift (B3 52bc792), :1-369.
// :202-207 FileEdit is already ported in Models/FileEdit.cs (pre-convention file, kept as is).
//

// :4-33
[JsonConverter(typeof(TimelineItemConverter))]
public sealed record TimelineItem
{
    public string Id { get; init; }                           // :5
    public Guid? TurnID { get; init; }                        // :6
    public DateTimeOffset Date { get; init; }                 // :7
    public Content TimelineItemContent { get; init; } // :8 Swift: content (R-NAME-3)

    // :10-15. Swift order is (id, turnID, date, content); the defaulted id and date go last here.
    public TimelineItem(Guid? turnID, Content content, string? id = null, DateTimeOffset? date = null)
    {
        Id = id ?? SwiftUUID.UuidString(Guid.NewGuid());
        TurnID = turnID;
        Date = date ?? SwiftDate.Now;
        TimelineItemContent = content;
    }

    // :17-26
    public abstract record Content
    {
        private Content() { }

        public sealed record User(UserMessage Value) : Content;                          // :18
        public sealed record Assistant(AssistantMessage Value) : Content;                // :19
        public sealed record Reasoning(ReasoningBlock Value) : Content;                  // :20
        public sealed record Tool(ToolCall Value) : Content;                             // :21
        public sealed record Plan(ProposedPlan Value) : Content;                         // :22
        public sealed record Todos(ValueList<TodoStep> Value) : Content;                 // :23
        public sealed record Notice(global::Marrow.Core.Models.Notice Value) : Content;  // :24
        public sealed record TurnEnd(TurnSummary Value) : Content;                       // :25
        // PORT-ONLY (web surfaces W25, ED-NEW-web-w25-1): an ask_user card, the model's blocking question to the person.
        public sealed record Ask(AskCard Value) : Content;
    }

    // :28-32
    public bool IsEmptyReasoning => TimelineItemContent is Content.Reasoning reasoning && reasoning.Value.Text.Trimmed().Length == 0;

    // :39-55
    public TimelineItem CleanedOfEmDashes => TimelineItemContent switch
    {
        Content.Assistant a => this with { TimelineItemContent = new Content.Assistant(a.Value with { Text = TextCleanup.WithoutEmDashes(a.Value.Text) }) },
        Content.Reasoning r => this with { TimelineItemContent = new Content.Reasoning(r.Value with { Text = TextCleanup.WithoutEmDashes(r.Value.Text) }) },
        Content.Plan p => this with { TimelineItemContent = new Content.Plan(p.Value with { Markdown = TextCleanup.WithoutEmDashes(p.Value.Markdown) }) },
        _ => this,
    };
}

// :4-15 synthesized: id, turnID, date required; content required via the union coder below.
internal sealed class TimelineItemConverter : JsonConverter<TimelineItem>
{
    public override TimelineItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static TimelineItem Decode(JsonElement element) => new(
        SwiftDecoding.DecodeIfPresentValue(element, "turnID", SwiftDecoding.ReadUUID),
        SwiftDecoding.Decode(element, "content", TimelineContentCoding.Decode),
        SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadString),
        SwiftDecoding.Decode(element, "date", SwiftDecoding.ReadStorageDate));

    public override void Write(Utf8JsonWriter writer, TimelineItem value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, TimelineItem value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        if (value.TurnID is { } turnID) writer.WriteString("turnID", SwiftUUID.UuidString(turnID));
        writer.WriteString("date", FoundationISO8601.Format(value.Date));
        writer.WritePropertyName("content");
        TimelineContentCoding.Write(writer, value.TimelineItemContent);
        writer.WriteEndObject();
    }
}

// :17-26. SE-0295 synthesized enum Codable: one key holding the case name, whose value is an object with the
// single unlabeled associated value under "_0" (SwiftDecoding.cs:187-197).
internal static class TimelineContentCoding
{
    public static TimelineItem.Content Decode(JsonElement element)
    {
        var (caseName, payload) = SwiftEnumCoding.ReadCase(element);
        return caseName switch
        {
            "user" => new TimelineItem.Content.User(SwiftDecoding.Decode(payload, "_0", UserMessageConverter.Decode)),
            "assistant" => new TimelineItem.Content.Assistant(SwiftDecoding.Decode(payload, "_0", AssistantMessageConverter.Decode)),
            "reasoning" => new TimelineItem.Content.Reasoning(SwiftDecoding.Decode(payload, "_0", ReasoningBlockConverter.Decode)),
            "tool" => new TimelineItem.Content.Tool(SwiftDecoding.Decode(payload, "_0", ToolCallConverter.Decode)),
            "plan" => new TimelineItem.Content.Plan(SwiftDecoding.Decode(payload, "_0", ProposedPlanConverter.Decode)),
            "todos" => new TimelineItem.Content.Todos(SwiftDecoding.Decode(payload, "_0", field => SwiftDecoding.ReadArray(field, TodoStepConverter.Decode))),
            "notice" => new TimelineItem.Content.Notice(SwiftDecoding.Decode(payload, "_0", NoticeConverter.Decode)),
            "turnEnd" => new TimelineItem.Content.TurnEnd(SwiftDecoding.Decode(payload, "_0", TurnSummaryConverter.Decode)),
            "ask" => new TimelineItem.Content.Ask(SwiftDecoding.Decode(payload, "_0", AskCardCoding.Decode)), // PORT-ONLY (W25)
            _ => throw new DecodingError("typeMismatch", $"Unknown TimelineItem.Content case \"{caseName}\"."),
        };
    }

    public static void Write(Utf8JsonWriter writer, TimelineItem.Content value)
    {
        switch (value)
        {
            case TimelineItem.Content.User v:
                SwiftEnumCoding.WriteCase(writer, "user", w => { w.WritePropertyName("_0"); UserMessageConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Assistant v:
                SwiftEnumCoding.WriteCase(writer, "assistant", w => { w.WritePropertyName("_0"); AssistantMessageConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Reasoning v:
                SwiftEnumCoding.WriteCase(writer, "reasoning", w => { w.WritePropertyName("_0"); ReasoningBlockConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Tool v:
                SwiftEnumCoding.WriteCase(writer, "tool", w => { w.WritePropertyName("_0"); ToolCallConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Plan v:
                SwiftEnumCoding.WriteCase(writer, "plan", w => { w.WritePropertyName("_0"); ProposedPlanConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Todos v:
                SwiftEnumCoding.WriteCase(writer, "todos", w =>
                {
                    w.WritePropertyName("_0");
                    w.WriteStartArray();
                    foreach (var step in v.Value) TodoStepConverter.WriteMembers(w, step);
                    w.WriteEndArray();
                });
                break;
            case TimelineItem.Content.Notice v:
                SwiftEnumCoding.WriteCase(writer, "notice", w => { w.WritePropertyName("_0"); NoticeConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.TurnEnd v:
                SwiftEnumCoding.WriteCase(writer, "turnEnd", w => { w.WritePropertyName("_0"); TurnSummaryConverter.WriteMembers(w, v.Value); });
                break;
            case TimelineItem.Content.Ask v: // PORT-ONLY (W25)
                SwiftEnumCoding.WriteCase(writer, "ask", w => { w.WritePropertyName("_0"); AskCardCoding.WriteMembers(w, v.Value); });
                break;
            default:
                throw new UnreachableException();
        }
    }
}

// :58-70
[JsonConverter(typeof(UserMessageConverter))]
public sealed record UserMessage
{
    public required string Text { get; init; }                                  // :59
    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty; // :60
    public ValueList<long>? HydraHeads { get; init; }                           // :63
    public bool? HydraBrief { get; init; }                                      // :66

    // PORT-ONLY (web surfaces W19, DESIGN.md 7.6, ED-NEW-web-w19-3): the MCP server whose App posted this message (ui/message). The row
    // reads "Message from <server>", never as the user's bubble, and the model gets it wrapped as untrusted app content. Null for the user's own.
    public string? AppSource { get; init; }

    // :68
    public bool IsFromHydra => HydraHeads is not null;

    // :69
    public bool IsHydraBrief => HydraBrief == true;

    // :58 Hashable: the synthesized equality and hash compare the String text canonically (R-STR-2), where a record's
    // own is ordinal, so "e" + U+0301 and U+00E9 are one text.
    public bool Equals(UserMessage? other) => other is not null && SwiftString.Equal(Text, other.Text) &&
        Attachments.Equals(other.Attachments) && Equals(HydraHeads, other.HydraHeads) && HydraBrief == other.HydraBrief && AppSource == other.AppSource;

    public override int GetHashCode() =>
        HashCode.Combine(SwiftStringComparer.Instance.GetHashCode(Text), Attachments, HydraHeads, HydraBrief, AppSource);
}

internal sealed class UserMessageConverter : JsonConverter<UserMessage>
{
    public override UserMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static UserMessage Decode(JsonElement element) => new()
    {
        Text = SwiftDecoding.Decode(element, "text", SwiftDecoding.ReadString),
        Attachments = SwiftDecoding.Decode(element, "attachments", field => SwiftDecoding.ReadArray(field, AttachmentConverter.Decode)),
        HydraHeads = SwiftDecoding.DecodeIfPresent(element, "hydraHeads", field => SwiftDecoding.ReadArray(field, SwiftDecoding.ReadInt)),
        HydraBrief = SwiftDecoding.DecodeIfPresentValue(element, "hydraBrief", SwiftDecoding.ReadBool),
        AppSource = SwiftDecoding.Value<string?>(element, "appSource", field => SwiftDecoding.ReadString(field), () => null), // PORT-ONLY (W19)
    };

    public override void Write(Utf8JsonWriter writer, UserMessage value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, UserMessage value)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WritePropertyName("attachments");
        writer.WriteStartArray();
        foreach (var attachment in value.Attachments) AttachmentConverter.WriteMembers(writer, attachment);
        writer.WriteEndArray();
        if (value.HydraHeads is { } heads)
        {
            writer.WritePropertyName("hydraHeads");
            writer.WriteStartArray();
            foreach (var head in heads) writer.WriteNumberValue(head);
            writer.WriteEndArray();
        }
        if (value.HydraBrief is { } brief) writer.WriteBoolean("hydraBrief", brief);
        if (value.AppSource is { } app) writer.WriteString("appSource", app); // PORT-ONLY (W19)
        writer.WriteEndObject();
    }
}

// :75-87
[JsonConverter(typeof(FollowUpPromptConverter))]
public sealed record FollowUpPrompt
{
    public Guid Id { get; init; } = Guid.NewGuid();                             // :76
    public required string Text { get; init; }                                  // :77
    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty; // :78
    public DateTimeOffset CreatedAt { get; init; } = SwiftDate.Now;             // :79
    public Guid? BundleID { get; init; } = null;                                // :82

    // :84-86
    public bool IsEmpty => Attachments.Count == 0 && !SwiftString.Characters(Text).Any(character => !SwiftCharacter.IsWhitespace(character));
}

internal sealed class FollowUpPromptConverter : JsonConverter<FollowUpPrompt>
{
    public override FollowUpPrompt Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static FollowUpPrompt Decode(JsonElement element) => new()
    {
        Id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadUUID),
        Text = SwiftDecoding.Decode(element, "text", SwiftDecoding.ReadString),
        Attachments = SwiftDecoding.Decode(element, "attachments", field => SwiftDecoding.ReadArray(field, AttachmentConverter.Decode)),
        CreatedAt = SwiftDecoding.Decode(element, "createdAt", SwiftDecoding.ReadStorageDate),
        BundleID = SwiftDecoding.DecodeIfPresentValue(element, "bundleID", SwiftDecoding.ReadUUID),
    };

    public override void Write(Utf8JsonWriter writer, FollowUpPrompt value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, FollowUpPrompt value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("text", value.Text);
        writer.WritePropertyName("attachments");
        writer.WriteStartArray();
        foreach (var attachment in value.Attachments) AttachmentConverter.WriteMembers(writer, attachment);
        writer.WriteEndArray();
        writer.WriteString("createdAt", FoundationISO8601.Format(value.CreatedAt));
        if (value.BundleID is { } bundleID) writer.WriteString("bundleID", SwiftUUID.UuidString(bundleID));
        writer.WriteEndObject();
    }
}

// :89-138
[JsonConverter(typeof(AttachmentConverter))]
public sealed record Attachment
{
    public Guid Id { get; init; } = Guid.NewGuid(); // :90
    public required string Name { get; init; }      // :91
    public required string Path { get; init; }      // :92
    public required string MimeType { get; init; }  // :93

    // :95 URL(fileURLWithPath: path). ENGINEERING DECISION: F.URL (SwiftURL) only builds URL(string:); there is no
    // ported fileURLWithPath initializer, and the windows_path note on PR-CHAT-005 says the stored Path is what
    // Windows reads back for file I/O (Base64Images below reads Path directly, never this property). Built as an
    // absolute file URI so any other caller of Url still gets a working SwiftURL.
    public SwiftURL Url =>
        SwiftURL.Create("file://" + PercentEncodeFileUriPath(Path)) ?? throw SwiftTraps.Fatal("Invalid attachment path.");

    // :96
    public bool IsImage => SwiftString.HasPrefix(MimeType, "image/");

    // :113-114
    private const long MaxPixelSize = 2000;

    // :102-111 @concurrent static func base64Images(_:). Runs off the calling thread (Task.Run), matching "read
    // and encoded off the main actor" (:98-100).
    public static Task<ValueList<(Attachment Image, string Base64)>> Base64Images(IReadOnlyList<Attachment> attachments) =>
        Task.Run(() =>
        {
            var results = new List<(Attachment, string)>();
            foreach (var attachment in attachments)
            {
                if (!attachment.IsImage) continue; // :105
                byte[] data;
                try { data = File.ReadAllBytes(attachment.Path); }
                catch (Exception error) when (SwiftTry.IsThrownError(error)) { continue; } // :105 try?
                var downscaled = WicImageDecoder.DownscaleToJpeg(data, MaxPixelSize, 0.8); // :106, :120-137
                var sent = downscaled is not null ? attachment with { MimeType = "image/jpeg" } : attachment; // :107-108
                results.Add((sent, Convert.ToBase64String(downscaled ?? data))); // :109
            }
            return new ValueList<(Attachment, string)>(results);
        });

    // Manual, minimal RFC3986 percent-encoding for the Url property above (not a Swift twin; see the DEVIATION note
    // on Url). Keeps the ASCII unreserved set and the path delimiters '/' and ':' (a Windows drive letter) literal.
    private static string PercentEncodeFileUriPath(string path)
    {
        var builder = new StringBuilder();
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized[0] != '/') builder.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

internal sealed class AttachmentConverter : JsonConverter<Attachment>
{
    public override Attachment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static Attachment Decode(JsonElement element) => new()
    {
        Id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadUUID),
        Name = SwiftDecoding.Decode(element, "name", SwiftDecoding.ReadString),
        Path = SwiftDecoding.Decode(element, "path", SwiftDecoding.ReadString),
        MimeType = SwiftDecoding.Decode(element, "mimeType", SwiftDecoding.ReadString),
    };

    public override void Write(Utf8JsonWriter writer, Attachment value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, Attachment value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("name", value.Name);
        writer.WriteString("path", value.Path);
        writer.WriteString("mimeType", value.MimeType);
        writer.WriteEndObject();
    }
}

// :140-143
[JsonConverter(typeof(AssistantMessageConverter))]
public sealed record AssistantMessage
{
    public required string Text { get; init; }       // :141
    public bool IsStreaming { get; init; } = false;  // :142
}

internal sealed class AssistantMessageConverter : JsonConverter<AssistantMessage>
{
    public override AssistantMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static AssistantMessage Decode(JsonElement element) => new()
    {
        Text = SwiftDecoding.Decode(element, "text", SwiftDecoding.ReadString),
        IsStreaming = SwiftDecoding.Decode(element, "isStreaming", SwiftDecoding.ReadBool),
    };

    public override void Write(Utf8JsonWriter writer, AssistantMessage value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, AssistantMessage value)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WriteBoolean("isStreaming", value.IsStreaming);
        writer.WriteEndObject();
    }
}

// :145-148
[JsonConverter(typeof(ReasoningBlockConverter))]
public sealed record ReasoningBlock
{
    public required string Text { get; init; }       // :146
    public bool IsStreaming { get; init; } = false;  // :147
}

internal sealed class ReasoningBlockConverter : JsonConverter<ReasoningBlock>
{
    public override ReasoningBlock Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ReasoningBlock Decode(JsonElement element) => new()
    {
        Text = SwiftDecoding.Decode(element, "text", SwiftDecoding.ReadString),
        IsStreaming = SwiftDecoding.Decode(element, "isStreaming", SwiftDecoding.ReadBool),
    };

    public override void Write(Utf8JsonWriter writer, ReasoningBlock value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ReasoningBlock value)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WriteBoolean("isStreaming", value.IsStreaming);
        writer.WriteEndObject();
    }
}

// :150-200
[JsonConverter(typeof(ToolCallConverter))]
public sealed record ToolCall
{
    // :151-160
    public enum Kind
    {
        [RawValue("command")] Command,
        [RawValue("read")] Read,
        [RawValue("edit")] Edit,
        [RawValue("search")] Search,
        [RawValue("web")] Web,
        [RawValue("mcp")] Mcp,
        [RawValue("agent")] Agent,
        [RawValue("other")] Other,
    }

    // :162-167
    public enum Status
    {
        [RawValue("running")] Running,
        [RawValue("completed")] Completed,
        [RawValue("failed")] Failed,
        [RawValue("declined")] Declined,
    }

    public const long OutputLimit = 60_000; // :169

    public required Kind ToolCallKind { get; init; }                           // :171 Swift: kind (R-NAME-3)
    public required string Title { get; init; }                                // :172
    public string? Detail { get; init; }                                       // :173
    public string Output { get; init; } = "";                                  // :174
    public Status ToolCallStatus { get; init; } = Status.Running;              // :175 Swift: status (R-NAME-3)
    public long? ExitCode { get; init; }                                       // :176
    public ValueList<FileEdit> Edits { get; init; } = ValueList<FileEdit>.Empty; // :177
    public DateTimeOffset StartedAt { get; init; } = SwiftDate.Now;            // :178
    public DateTimeOffset? FinishedAt { get; init; }                           // :179

    // PORT-ONLY (web surfaces W18, ED-NEW-web-w18-1): the structured part of an MCP tool result (images, resource
    // links, the UI resource uri, structuredContent, _meta) next to the text Output. Null for every other tool.
    public ToolPayload? Payload { get; init; }

    // The mutating methods return the changed copy (R-VAL-1).
    // :181-184
    public ToolCall AppendOutput(string text) => this with { Output = TrimOutput(Output + text) };

    // :186-189
    public ToolCall SetOutput(string text) => this with { Output = TrimOutput(text) };

    // :191-194
    public ToolCall Finish(Status status) => this with { ToolCallStatus = status, FinishedAt = FinishedAt ?? SwiftDate.Now };

    // :196-199
    private static string TrimOutput(string output) =>
        SwiftString.Utf8Count(output) > OutputLimit ? "…" + SwiftString.Suffix(output, (int)(OutputLimit / 2)) : output;
}

internal sealed class ToolCallConverter : JsonConverter<ToolCall>
{
    public override ToolCall Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ToolCall Decode(JsonElement element) => new()
    {
        ToolCallKind = SwiftDecoding.Decode(element, "kind", SwiftDecoding.ReadRawValue<ToolCall.Kind>),
        Title = SwiftDecoding.Decode(element, "title", SwiftDecoding.ReadString),
        Detail = SwiftDecoding.DecodeIfPresent(element, "detail", SwiftDecoding.ReadString),
        Output = SwiftDecoding.Decode(element, "output", SwiftDecoding.ReadString),
        ToolCallStatus = SwiftDecoding.Decode(element, "status", SwiftDecoding.ReadRawValue<ToolCall.Status>),
        ExitCode = SwiftDecoding.DecodeIfPresentValue(element, "exitCode", SwiftDecoding.ReadInt),
        Edits = SwiftDecoding.Decode(element, "edits", field => SwiftDecoding.ReadArray(field, FileEditConverter_Decode)),
        StartedAt = SwiftDecoding.Decode(element, "startedAt", SwiftDecoding.ReadStorageDate),
        FinishedAt = SwiftDecoding.DecodeIfPresentValue(element, "finishedAt", SwiftDecoding.ReadStorageDate),
        // PORT-ONLY (W18): optional and lenient, a malformed payload never makes a thread unreadable.
        // An empty payload (nothing a row could show) reads as none, as it is written as none.
        Payload = SwiftDecoding.Value<ToolPayload?>(element, "payload",
            field => ToolPayloadConverter.Decode(field) is { IsEmpty: false } payload ? payload : null, () => null),
    };

    // FileEdit already has its own JsonConverter (Models/FileEdit.cs); reuse it through JsonSerializer for one
    // element rather than duplicating its field reads here.
    private static FileEdit FileEditConverter_Decode(JsonElement element) => element.Deserialize<FileEdit>(Coding.StorageOptions)!;

    public override void Write(Utf8JsonWriter writer, ToolCall value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ToolCall value)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", SwiftEnum<ToolCall.Kind>.RawValue(value.ToolCallKind));
        writer.WriteString("title", value.Title);
        if (value.Detail is { } detail) writer.WriteString("detail", detail);
        writer.WriteString("output", value.Output);
        writer.WriteString("status", SwiftEnum<ToolCall.Status>.RawValue(value.ToolCallStatus));
        if (value.ExitCode is { } exitCode) writer.WriteNumber("exitCode", exitCode);
        writer.WritePropertyName("edits");
        writer.WriteStartArray();
        foreach (var edit in value.Edits) JsonSerializer.Serialize(writer, edit, Coding.StorageOptions);
        writer.WriteEndArray();
        writer.WriteString("startedAt", FoundationISO8601.Format(value.StartedAt));
        if (value.FinishedAt is { } finishedAt) writer.WriteString("finishedAt", FoundationISO8601.Format(finishedAt));
        if (value.Payload is { IsEmpty: false } payload)
        {
            writer.WritePropertyName("payload");
            ToolPayloadConverter.WriteMembers(writer, payload);
        }
        writer.WriteEndObject();
    }
}

// :209-219
[JsonConverter(typeof(ProposedPlanConverter))]
public sealed record ProposedPlan
{
    // :210-215
    public enum State
    {
        [RawValue("drafting")] Drafting,
        [RawValue("proposed")] Proposed,
        [RawValue("accepted")] Accepted,
        [RawValue("dismissed")] Dismissed,
    }

    public required string Markdown { get; init; }            // :217
    public required State ProposedPlanState { get; init; }    // :218 Swift: state (R-NAME-3)
}

internal sealed class ProposedPlanConverter : JsonConverter<ProposedPlan>
{
    public override ProposedPlan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ProposedPlan Decode(JsonElement element) => new()
    {
        Markdown = SwiftDecoding.Decode(element, "markdown", SwiftDecoding.ReadString),
        ProposedPlanState = SwiftDecoding.Decode(element, "state", SwiftDecoding.ReadRawValue<ProposedPlan.State>),
    };

    public override void Write(Utf8JsonWriter writer, ProposedPlan value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ProposedPlan value)
    {
        writer.WriteStartObject();
        writer.WriteString("markdown", value.Markdown);
        writer.WriteString("state", SwiftEnum<ProposedPlan.State>.RawValue(value.ProposedPlanState));
        writer.WriteEndObject();
    }
}

// :221-230
[JsonConverter(typeof(TodoStepConverter))]
public sealed record TodoStep
{
    // :222-226
    public enum Status
    {
        [RawValue("pending")] Pending,
        [RawValue("active")] Active,
        [RawValue("done")] Done,
    }

    public required string Text { get; init; }             // :228
    public required Status TodoStepStatus { get; init; }    // :229 Swift: status (R-NAME-3)
}

internal sealed class TodoStepConverter : JsonConverter<TodoStep>
{
    public override TodoStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static TodoStep Decode(JsonElement element) => new()
    {
        Text = SwiftDecoding.Decode(element, "text", SwiftDecoding.ReadString),
        TodoStepStatus = SwiftDecoding.Decode(element, "status", SwiftDecoding.ReadRawValue<TodoStep.Status>),
    };

    public override void Write(Utf8JsonWriter writer, TodoStep value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, TodoStep value)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WriteString("status", SwiftEnum<TodoStep.Status>.RawValue(value.TodoStepStatus));
        writer.WriteEndObject();
    }
}

// :232-244
[JsonConverter(typeof(NoticeConverter))]
public sealed record Notice
{
    // :233-237
    public enum Level
    {
        [RawValue("info")] Info,
        [RawValue("warning")] Warning,
        [RawValue("error")] Error,
    }

    public required Level NoticeLevel { get; init; }   // :239 Swift: level (R-NAME-3)
    public required string Message { get; init; }      // :240
    public LimitNotice? Limit { get; init; } = null;   // :243
}

internal sealed class NoticeConverter : JsonConverter<Notice>
{
    public override Notice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static Notice Decode(JsonElement element) => new()
    {
        NoticeLevel = SwiftDecoding.Decode(element, "level", SwiftDecoding.ReadRawValue<Notice.Level>),
        Message = SwiftDecoding.Decode(element, "message", SwiftDecoding.ReadString),
        Limit = SwiftDecoding.DecodeIfPresent(element, "limit", LimitNoticeConverter.Decode),
    };

    public override void Write(Utf8JsonWriter writer, Notice value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, Notice value)
    {
        writer.WriteStartObject();
        writer.WriteString("level", SwiftEnum<Notice.Level>.RawValue(value.NoticeLevel));
        writer.WriteString("message", value.Message);
        if (value.Limit is { } limit)
        {
            writer.WritePropertyName("limit");
            LimitNoticeConverter.WriteMembers(writer, limit);
        }
        writer.WriteEndObject();
    }
}

// :248-250
[JsonConverter(typeof(LimitNoticeConverter))]
public sealed record LimitNotice
{
    public required Guid ThreadID { get; init; } // :249
}

internal sealed class LimitNoticeConverter : JsonConverter<LimitNotice>
{
    public override LimitNotice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static LimitNotice Decode(JsonElement element) => new() { ThreadID = SwiftDecoding.Decode(element, "threadID", SwiftDecoding.ReadUUID) };

    public override void Write(Utf8JsonWriter writer, LimitNotice value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, LimitNotice value)
    {
        writer.WriteStartObject();
        writer.WriteString("threadID", SwiftUUID.UuidString(value.ThreadID));
        writer.WriteEndObject();
    }
}

// :252-257
public enum TurnStatus
{
    [RawValue("running")] Running,
    [RawValue("completed")] Completed,
    [RawValue("interrupted")] Interrupted,
    [RawValue("failed")] Failed,
}

// :259-267.
[JsonConverter(typeof(TurnSummaryConverter))]
public sealed record TurnSummary
{
    public required Guid TurnID { get; init; }         // :260
    public required TurnStatus Status { get; init; }   // :261
    public required double Duration { get; init; }     // :262
    public required long FilesChanged { get; init; }   // :263
    public required long Additions { get; init; }      // :264
    public required long Deletions { get; init; }      // :265
    public FileChangeSummary? Changes { get; init; }    // :266
}

internal sealed class TurnSummaryConverter : JsonConverter<TurnSummary>
{
    public override TurnSummary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static TurnSummary Decode(JsonElement element) => new()
    {
        TurnID = SwiftDecoding.Decode(element, "turnID", SwiftDecoding.ReadUUID),
        Status = SwiftDecoding.Decode(element, "status", SwiftDecoding.ReadRawValue<TurnStatus>),
        Duration = SwiftDecoding.Decode(element, "duration", SwiftDecoding.ReadDouble),
        FilesChanged = SwiftDecoding.Decode(element, "filesChanged", SwiftDecoding.ReadInt),
        Additions = SwiftDecoding.Decode(element, "additions", SwiftDecoding.ReadInt),
        Deletions = SwiftDecoding.Decode(element, "deletions", SwiftDecoding.ReadInt),
        Changes = SwiftDecoding.DecodeIfPresent(element, "changes", FileChangeSummaryConverterDecode),
    };

    // FileChangeSummary already has its own JsonConverter (Models/FileChangeSummary.cs).
    private static FileChangeSummary FileChangeSummaryConverterDecode(JsonElement element) =>
        element.Deserialize<FileChangeSummary>(Coding.StorageOptions)!;

    public override void Write(Utf8JsonWriter writer, TurnSummary value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, TurnSummary value)
    {
        writer.WriteStartObject();
        writer.WriteString("turnID", SwiftUUID.UuidString(value.TurnID));
        writer.WriteString("status", SwiftEnum<TurnStatus>.RawValue(value.Status));
        SwiftJson.WriteDouble(writer, "duration", value.Duration);
        writer.WriteNumber("filesChanged", value.FilesChanged);
        writer.WriteNumber("additions", value.Additions);
        writer.WriteNumber("deletions", value.Deletions);
        if (value.Changes is { } changes)
        {
            writer.WritePropertyName("changes");
            JsonSerializer.Serialize(writer, changes, Coding.StorageOptions);
        }
        writer.WriteEndObject();
    }
}

// :270-284. B3 adds Retryable (:281-283); the doc comment on Held (:271-272) now says it is legacy-decode-only.
[JsonConverter(typeof(HydraMergeRecordConverter))]
public sealed record HydraMergeRecord
{
    // :271-272. `Held` is no longer written by B3: a merge never waits on another chat's files now. It stays a
    // valid raw value so records written by earlier versions still decode.
    // :273
    public enum Outcome
    {
        [RawValue("merged")] Merged,
        [RawValue("failed")] Failed,
        [RawValue("stray")] Stray,
        [RawValue("held")] Held,
        [RawValue("settled")] Settled,
    }

    public required DateTimeOffset At { get; init; }                  // :274
    public required Outcome HydraMergeRecordOutcome { get; init; }    // :275 Swift: outcome (R-NAME-3)
    public string? Project { get; init; }                             // :276
    public string? Label { get; init; }                               // :277
    public SwiftURL? Url { get; init; }                                // :278
    public required long Files { get; init; }                         // :279
    public string? Detail { get; init; }                              // :280

    // :281-283. NEW at B3. A failure that clears by itself and is retried quietly (the forge still checking, a
    // remote that moved); nil on the ones that need the user (PR-HYDRAMERGE-132).
    public bool? Retryable { get; init; }
}

internal sealed class HydraMergeRecordConverter : JsonConverter<HydraMergeRecord>
{
    public override HydraMergeRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraMergeRecord Decode(JsonElement element) => new()
    {
        At = SwiftDecoding.Decode(element, "at", SwiftDecoding.ReadStorageDate),
        HydraMergeRecordOutcome = SwiftDecoding.Decode(element, "outcome", SwiftDecoding.ReadRawValue<HydraMergeRecord.Outcome>),
        Project = SwiftDecoding.DecodeIfPresent(element, "project", SwiftDecoding.ReadString),
        Label = SwiftDecoding.DecodeIfPresent(element, "label", SwiftDecoding.ReadString),
        Url = SwiftDecoding.DecodeIfPresent(element, "url", SwiftURL.JsonConverter.Decode),
        Files = SwiftDecoding.Decode(element, "files", SwiftDecoding.ReadInt),
        Detail = SwiftDecoding.DecodeIfPresent(element, "detail", SwiftDecoding.ReadString),
        Retryable = SwiftDecoding.DecodeIfPresentValue(element, "retryable", SwiftDecoding.ReadBool), // :281-283, absent decodes as null
    };

    public override void Write(Utf8JsonWriter writer, HydraMergeRecord value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, HydraMergeRecord value)
    {
        writer.WriteStartObject();
        writer.WriteString("at", FoundationISO8601.Format(value.At));
        writer.WriteString("outcome", SwiftEnum<HydraMergeRecord.Outcome>.RawValue(value.HydraMergeRecordOutcome));
        if (value.Project is { } project) writer.WriteString("project", project);
        if (value.Label is { } label) writer.WriteString("label", label);
        if (value.Url is { } url)
        {
            writer.WritePropertyName("url");
            SwiftURL.JsonConverter.WriteMembers(writer, url);
        }
        writer.WriteNumber("files", value.Files);
        if (value.Detail is { } detail) writer.WriteString("detail", detail);
        if (value.Retryable is { } retryable) writer.WriteBoolean("retryable", retryable); // :281-283, omitted when null
        writer.WriteEndObject();
    }
}

// :286-310
[JsonConverter(typeof(TurnRecordConverter))]
public sealed record TurnRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();                     // :287
    public required long Index { get; init; }                           // :288
    public string? ProviderTurnID { get; init; }                        // :289
    public DateTimeOffset StartedAt { get; init; } = SwiftDate.Now;     // :290
    public DateTimeOffset? CompletedAt { get; init; }                   // :291
    public TurnStatus Status { get; init; } = TurnStatus.Running;       // :292
    public string? BaseCheckpoint { get; init; }                        // :293
    public string? EndCheckpoint { get; init; }                         // :294
    public string? UserItemID { get; init; }                            // :295
    public string? ProviderDiff { get; init; }                          // :297
    public string? ProviderAnchor { get; init; }                        // :299
    public ValueList<string>? TouchedPaths { get; init; }               // :302
    public ValueList<TokenSpend>? TokenSpends { get; init; }            // :306
    public bool HydraMerged { get; init; } = false;                     // :309
}

// :312-332 init(from:): every field read with a default, so an older turn still decodes (PR-GIT-148). The
// encoder is synthesized (declaration order, nil optionals omitted); there is no custom encode(to:) in the Swift.
internal sealed class TurnRecordConverter : JsonConverter<TurnRecord>
{
    public override TurnRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static TurnRecord Decode(JsonElement element)
    {
        // :316 decoder.container(keyedBy:) throws typeMismatch for a non-object element, before any field is read;
        // every field below is then read leniently through Coding.Value, which never itself throws.
        if (element.ValueKind != JsonValueKind.Object) throw new DecodingError("typeMismatch", "Expected to decode Dictionary<String, Any>.");
        return DecodeFields(element);
    }

    private static TurnRecord DecodeFields(JsonElement element) => new()
    {
        Id = Coding.Value(element, "id", SwiftDecoding.ReadUUID, () => Guid.NewGuid()),                                   // :317
        Index = Coding.Value(element, "index", SwiftDecoding.ReadInt, () => 0L),                                          // :318
        ProviderTurnID = Coding.Value(element, "providerTurnID", SwiftDecoding.ReadString, () => (string?)null),          // :319
        StartedAt = Coding.Value(element, "startedAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now),               // :320
        CompletedAt = Coding.Value(element, "completedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null), // :321
        Status = Coding.Value(element, "status", SwiftDecoding.ReadRawValue<TurnStatus>, () => TurnStatus.Running),       // :322
        BaseCheckpoint = Coding.Value(element, "baseCheckpoint", SwiftDecoding.ReadString, () => (string?)null),          // :323
        EndCheckpoint = Coding.Value(element, "endCheckpoint", SwiftDecoding.ReadString, () => (string?)null),            // :324
        UserItemID = Coding.Value(element, "userItemID", SwiftDecoding.ReadString, () => (string?)null),                  // :325
        ProviderDiff = Coding.Value(element, "providerDiff", SwiftDecoding.ReadString, () => (string?)null),              // :326
        ProviderAnchor = Coding.Value(element, "providerAnchor", SwiftDecoding.ReadString, () => (string?)null),          // :327
        TouchedPaths = Coding.Value(element, "touchedPaths",
            field => (ValueList<string>?)SwiftDecoding.ReadArray(field, SwiftDecoding.ReadString), () => (ValueList<string>?)null), // :328
        TokenSpends = Coding.Value(element, "tokenSpends",
            field => (ValueList<TokenSpend>?)SwiftDecoding.ReadArray(field, TokenSpend.Decode), () => (ValueList<TokenSpend>?)null), // :329
        HydraMerged = Coding.Value(element, "hydraMerged", SwiftDecoding.ReadBool, () => false),                          // :330
    };

    public override void Write(Utf8JsonWriter writer, TurnRecord value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, TurnRecord value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteNumber("index", value.Index);
        if (value.ProviderTurnID is { } providerTurnID) writer.WriteString("providerTurnID", providerTurnID);
        writer.WriteString("startedAt", FoundationISO8601.Format(value.StartedAt));
        if (value.CompletedAt is { } completedAt) writer.WriteString("completedAt", FoundationISO8601.Format(completedAt));
        writer.WriteString("status", SwiftEnum<TurnStatus>.RawValue(value.Status));
        if (value.BaseCheckpoint is { } baseCheckpoint) writer.WriteString("baseCheckpoint", baseCheckpoint);
        if (value.EndCheckpoint is { } endCheckpoint) writer.WriteString("endCheckpoint", endCheckpoint);
        if (value.UserItemID is { } userItemID) writer.WriteString("userItemID", userItemID);
        if (value.ProviderDiff is { } providerDiff) writer.WriteString("providerDiff", providerDiff);
        if (value.ProviderAnchor is { } providerAnchor) writer.WriteString("providerAnchor", providerAnchor);
        if (value.TouchedPaths is { } touchedPaths)
        {
            writer.WritePropertyName("touchedPaths");
            writer.WriteStartArray();
            foreach (var path in touchedPaths) writer.WriteStringValue(path);
            writer.WriteEndArray();
        }
        if (value.TokenSpends is { } tokenSpends) // :306 synthesized encodeIfPresent
        {
            writer.WritePropertyName("tokenSpends");
            writer.WriteStartArray();
            foreach (var spend in tokenSpends) TokenSpend.Write(writer, spend);
            writer.WriteEndArray();
        }
        writer.WriteBoolean("hydraMerged", value.HydraMerged);
        writer.WriteEndObject();
    }
}

// :334-342
[JsonConverter(typeof(ContextUsageConverter))]
public sealed record ContextUsage
{
    public required long UsedTokens { get; init; }  // :335
    public long? WindowTokens { get; init; }        // :336

    // :338-341
    public double? Fraction => WindowTokens is { } window && window > 0 ? Math.Min(1, (double)UsedTokens / window) : null;
}

internal sealed class ContextUsageConverter : JsonConverter<ContextUsage>
{
    public override ContextUsage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ContextUsage Decode(JsonElement element) => new()
    {
        UsedTokens = SwiftDecoding.Decode(element, "usedTokens", SwiftDecoding.ReadInt),
        WindowTokens = SwiftDecoding.DecodeIfPresentValue(element, "windowTokens", SwiftDecoding.ReadInt),
    };

    public override void Write(Utf8JsonWriter writer, ContextUsage value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ContextUsage value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("usedTokens", value.UsedTokens);
        if (value.WindowTokens is { } windowTokens) writer.WriteNumber("windowTokens", windowTokens);
        writer.WriteEndObject();
    }
}

// :344-369
[JsonConverter(typeof(ThreadDocumentConverter))]
public sealed record ThreadDocument
{
    public Guid ThreadID { get; init; }                                                        // :345
    public ValueList<TimelineItem> Items { get; init; } = ValueList<TimelineItem>.Empty;       // :346
    public ValueList<TurnRecord> Turns { get; init; } = ValueList<TurnRecord>.Empty;           // :347
    public ContextUsage? Usage { get; init; }                                                  // :348
    public ValueList<FollowUpPrompt> FollowUps { get; init; } = ValueList<FollowUpPrompt>.Empty; // :349
    public ValueList<HydraMergeRecord> HydraMerges { get; init; } = ValueList<HydraMergeRecord>.Empty; // :350
    public ThreadContinuation? Continuation { get; init; } = null;                             // :353

    // :355-357
    public ThreadDocument(Guid threadID)
    {
        ThreadID = threadID;
    }
}

// :359-368 init(from:): items, turns, followUps and hydraMerges decode leniently, dropping any element that fails
// (PR-CHAT-019); usage defaults to null; threadID is the one field that is not lenient (a missing threadID fails
// the whole decode). The encoder is synthesized (declaration order, nil optionals omitted).
internal sealed class ThreadDocumentConverter : JsonConverter<ThreadDocument>
{
    public override ThreadDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ThreadDocument Decode(JsonElement element) => new(SwiftDecoding.Decode(element, "threadID", SwiftDecoding.ReadUUID)) // :361
    {
        Items = Coding.LenientList(element, "items", TimelineItemConverter.Decode, () => ValueList<TimelineItem>.Empty),           // :362
        Turns = Coding.LenientList(element, "turns", TurnRecordConverter.Decode, () => ValueList<TurnRecord>.Empty),               // :363
        Usage = Coding.Value(element, "usage", ContextUsageConverter.Decode, () => (ContextUsage?)null),                           // :364
        FollowUps = Coding.LenientList(element, "followUps", FollowUpPromptConverter.Decode, () => ValueList<FollowUpPrompt>.Empty), // :365
        HydraMerges = Coding.LenientList(element, "hydraMerges", HydraMergeRecordConverter.Decode, () => ValueList<HydraMergeRecord>.Empty), // :366
        Continuation = Coding.Value(element, "continuation", ThreadContinuationConverter.Decode, () => (ThreadContinuation?)null), // :367
    };

    public override void Write(Utf8JsonWriter writer, ThreadDocument value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("threadID", SwiftUUID.UuidString(value.ThreadID));
        writer.WritePropertyName("items");
        writer.WriteStartArray();
        foreach (var item in value.Items) TimelineItemConverter.WriteMembers(writer, item);
        writer.WriteEndArray();
        writer.WritePropertyName("turns");
        writer.WriteStartArray();
        foreach (var turn in value.Turns) TurnRecordConverter.WriteMembers(writer, turn);
        writer.WriteEndArray();
        if (value.Usage is { } usage)
        {
            writer.WritePropertyName("usage");
            ContextUsageConverter.WriteMembers(writer, usage);
        }
        writer.WritePropertyName("followUps");
        writer.WriteStartArray();
        foreach (var followUp in value.FollowUps) FollowUpPromptConverter.WriteMembers(writer, followUp);
        writer.WriteEndArray();
        writer.WritePropertyName("hydraMerges");
        writer.WriteStartArray();
        foreach (var merge in value.HydraMerges) HydraMergeRecordConverter.WriteMembers(writer, merge);
        writer.WriteEndArray();
        if (value.Continuation is { } continuation) // :353 synthesized encodeIfPresent
        {
            writer.WritePropertyName("continuation");
            ThreadContinuationConverter.WriteMembers(writer, continuation);
        }
        writer.WriteEndObject();
    }
}
