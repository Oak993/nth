using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/TokenSpend.swift (B3 52bc792), :1-66.
//
// PR-USAGE-087: totalTokens is the only authoritative amount an event is responsible for (one request or one
// turn's consumption), never a context window's occupancy (ContextUsage carries that separately, :3-9).
// cachedInputTokens is a subset of totalTokens, never added on top of inputTokens (:7-9). agentID names the
// head a provider ran inside the session; a consumer must not also attribute an event carrying an agentID to
// the session's lead (:11-14). Both are documentation-only contracts with no code of their own in this file;
// callers that aggregate TokenSpend values must honor them.

// :15-66
[JsonConverter(typeof(TokenSpend.Converter))]
public sealed record TokenSpend
{
    public ProviderKind Provider { get; init; }     // :16
    public long TotalTokens { get; init; }          // :17
    public long? InputTokens { get; init; }         // :18
    public long? OutputTokens { get; init; }        // :19
    public long? CachedInputTokens { get; init; }   // :20
    public string? Model { get; init; }             // :21
    public string? AgentID { get; init; }           // :22

    // :24-40
    public TokenSpend(ProviderKind provider, long totalTokens, long? inputTokens = null, long? outputTokens = null,
        long? cachedInputTokens = null, string? model = null, string? agentID = null)
    {
        Provider = provider;
        TotalTokens = totalTokens;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CachedInputTokens = cachedInputTokens;
        Model = model;
        AgentID = agentID;
    }

    // :42-47 mutating func merge(_:): returns the merged copy (R-VAL-1). totalTokens is always added; each
    // optional breakdown sums only when both sides carry a value, else the merged field is nil (PR-USAGE-088).
    // provider, model and agentID are untouched, so the receiver's identity always wins.
    public TokenSpend Merge(TokenSpend other) => this with
    {
        TotalTokens = TotalTokens + other.TotalTokens, // :43
        InputTokens = Sum(InputTokens, other.InputTokens), // :44
        OutputTokens = Sum(OutputTokens, other.OutputTokens), // :45
        CachedInputTokens = Sum(CachedInputTokens, other.CachedInputTokens), // :46
    };

    // :49-52 private static func sum(_:_:). Nil-propagating: either side missing gives nil, never treated as 0.
    private static long? Sum(long? lhs, long? rhs) => lhs is { } l && rhs is { } r ? l + r : null;

    // :15-22 synthesized encode(to:): declaration order, encodeIfPresent for every Optional.
    public static void Write(Utf8JsonWriter writer, TokenSpend value)
    {
        writer.WriteStartObject();
        writer.WriteString("provider", SwiftEnum<ProviderKind>.RawValue(value.Provider));
        writer.WriteNumber("totalTokens", value.TotalTokens);
        if (value.InputTokens is { } input) writer.WriteNumber("inputTokens", input);
        if (value.OutputTokens is { } output) writer.WriteNumber("outputTokens", output);
        if (value.CachedInputTokens is { } cached) writer.WriteNumber("cachedInputTokens", cached);
        if (value.Model is { } model) writer.WriteString("model", model);
        if (value.AgentID is { } agent) writer.WriteString("agentID", agent);
        writer.WriteEndObject();
    }

    // :15 Codable routes ordinary persistence through the same writer and lenient custom decoder.
    public sealed class Converter : JsonConverter<TokenSpend>
    {
        public override TokenSpend Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Decode(document.RootElement);
        }
        public override void Write(Utf8JsonWriter writer, TokenSpend value, JsonSerializerOptions options) => TokenSpend.Write(writer, value);
    }

    // :56-65 init(from decoder:). Read field by field, so a record written by a build that knew fewer of
    // these fields still reads as the observation it was (PR-USAGE-086). provider is required and strictly
    // typed; totalTokens defaults to 0; the four optionals default to nil on any missing, null or malformed
    // value (Coding.Value's lenient fallback, R-CODE-2).
    public static TokenSpend Decode(JsonElement element) => new(
        provider: SwiftDecoding.Decode(element, "provider", SwiftDecoding.ReadRawValue<ProviderKind>), // :58
        totalTokens: Coding.Value(element, "totalTokens", SwiftDecoding.ReadInt, () => 0L), // :59
        inputTokens: Coding.Value(element, "inputTokens", e => (long?)SwiftDecoding.ReadInt(e), () => (long?)null), // :60
        outputTokens: Coding.Value(element, "outputTokens", e => (long?)SwiftDecoding.ReadInt(e), () => (long?)null), // :61
        cachedInputTokens: Coding.Value(element, "cachedInputTokens", e => (long?)SwiftDecoding.ReadInt(e), () => (long?)null), // :62
        model: Coding.Value(element, "model", e => (string?)SwiftDecoding.ReadString(e), () => (string?)null), // :63
        agentID: Coding.Value(element, "agentID", e => (string?)SwiftDecoding.ReadString(e), () => (string?)null)); // :64
}
