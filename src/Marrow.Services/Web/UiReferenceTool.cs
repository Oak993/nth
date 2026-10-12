// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.3 "a skill with the full reference"; ED-NEW-web-w17-1). The on-demand
// reference every model can read: the same text through the nth-ui MCP server (CLI providers) and as a native function
// tool (API providers). It is nth's own skill: docs/nth-ui.md by topic, and twelve worked examples. Read only, nothing
// to approve, nothing written. A model reads it when the short guide in its prompt does not cover what the reader asked.
using System.Text.Json.Nodes;
using Marrow.Core.NthUi;
using Marrow.Core.Support;

namespace Marrow.Services.Web;

public static class UiReferenceTool
{
    public const string Name = NthUiTeaching.ReferenceTool;

    /// <summary>The timeline row's title: the feature's plain name (DESIGN.md 15).</summary>
    public const string Title = "Live answers";

    public static string Description { get; } =
        "Read nth's live-view language when the short guide is not enough: every component with its options, expressions and fmt patterns, actions, "
        + "and worked examples. Topics: "
        + string.Join("; ", NthUiTeaching.Topics.Select(topic => topic + " (" + NthUiTeaching.TopicSummary(topic) + ")")) + ". "
        + "Read-only; call it before writing a view that uses something you are unsure of.";

    /// <summary>The input schema (a fresh node each call).</summary>
    public static JsonObject InputSchema() => (JsonObject)JsonNode.Parse(SchemaText)!;

    private static string SchemaText { get; } =
        "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\",\"enum\":["
        + string.Join(",", NthUiTeaching.Topics.Select(topic => "\"" + topic + "\""))
        + "],\"description\":\"What to read.\"}},\"required\":[\"topic\"]}";

    /// <summary>The OpenAI-style function definition the API providers' tool loop sends.</summary>
    public static JSONValue FunctionDefinition() => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", Name), ("description", Description), ("parameters", JSONValue.Parse(SchemaText)!))));

    /// <summary>The timeline row's title for a call: "Live answers" and the topic, with a middle dot between.</summary>
    public static string RowTitle(JsonNode? arguments) =>
        Topic(arguments) is { Length: > 0 } topic && NthUiTeaching.Topics.Contains(topic.ToLowerInvariant()) ? Title + " · " + topic.ToLowerInvariant() : Title;

    /// <summary>Runs the tool: the topic's text, or a failure that lists the topics.</summary>
    public static ArtifactToolResult Run(JsonNode? arguments)
    {
        var topic = Topic(arguments);
        if (NthUiTeaching.Reference(topic) is { } text) return new ArtifactToolResult(text, false, null);
        var named = string.IsNullOrWhiteSpace(topic) ? "A topic is required" : $"\"{topic}\" is not a topic";
        return new ArtifactToolResult($"Error: {named}. Topics: {string.Join(", ", NthUiTeaching.Topics)}.", true, null);
    }

    private static string? Topic(JsonNode? arguments) =>
        arguments is JsonObject { } obj && obj["topic"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
