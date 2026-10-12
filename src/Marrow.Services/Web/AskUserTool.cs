// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 `ask_user`; ED-NEW-web-w25-1). The ask_user tool, in one place for the nth-ui MCP server
// (CLI providers) and the native tool loop (API providers): its name, description and input schema, how a call becomes a card
// (Marrow.Core.Models.AskUserSchema), the wait on the chat's host, and what the model reads back. Our own words; nothing here is
// copied from another product. No Swift counterpart.
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Web;

public static class AskUserTool
{
    public const string Name = NthUiTeaching.AskTool;

    /// <summary>The row a CLI provider would draw for this tool ("nth-ui", a middle dot, the tool); the card takes its place, so the chat draws no row for it.</summary>
    public const string ProviderRowTitle = UiMcpServer.ServerName + " · " + Name;

    public static string Description { get; } =
        "Ask the person one to four questions and wait for the answers. nth shows a card in the chat and the turn pauses until they answer, skip or stop. "
        + "Use it only when you cannot go on without a choice or a fact that only they can give and you cannot reasonably decide for them; "
        + "never for what you can find out by reading the project, never for a password or key, and not to ask whether you may continue. "
        + "Each question has a type: single (pick one of two to twelve options), multiple (pick several), text (their own words) or number (optional min, max, step, integer, unit). "
        + "A choice question also lets them write their own answer unless allow_other is false; put the option you would pick first. "
        + "An option is a short label; to add a line under it send {\"label\":\"...\",\"description\":\"...\"}. "
        + "The answers come back as text. If they skip, carry on with your best judgement and say what you assumed.";

    private const string SchemaText = """
        {"type":"object","properties":{
        "questions":{"type":"array","minItems":1,"maxItems":4,"description":"One to four questions, shown together on one card.","items":{"type":"object","properties":{
        "question":{"type":"string","description":"The question, in one or two plain sentences."},
        "header":{"type":"string","description":"A short label above the question, like Database (optional)."},
        "type":{"type":"string","enum":["single","multiple","text","number"],"description":"single: pick one option. multiple: pick several. text: their own words. number: a number. Left out, it is single when options are given and text otherwise."},
        "options":{"type":"array","items":{"type":"string"},"description":"Two to twelve short choices, for single and multiple."},
        "allow_other":{"type":"boolean","description":"Let them write their own answer to a choice question. Default true."},
        "required":{"type":"boolean","description":"Default true. False lets them leave the question empty."},
        "placeholder":{"type":"string","description":"Hint text inside an empty text or number field."},
        "multiline":{"type":"boolean","description":"For text: room for several lines."},
        "min":{"type":"number","description":"For number: the lowest allowed."},
        "max":{"type":"number","description":"For number: the highest allowed."},
        "step":{"type":"number","description":"For number: how far the arrow buttons move it."},
        "integer":{"type":"boolean","description":"For number: whole numbers only."},
        "unit":{"type":"string","description":"For number: a unit shown beside it, like GB."},
        "default":{"type":"string","description":"The answer the card starts with: an option's label, a number or text."}},
        "required":["question"]}}},
        "required":["questions"]}
        """;

    /// <summary>The tool's input schema (a fresh node each call).</summary>
    public static JsonObject InputSchema() => (JsonObject)JsonNode.Parse(SchemaText)!;

    /// <summary>The OpenAI-style function definition the API providers' tool loop sends.</summary>
    public static JSONValue FunctionDefinition() => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", Name), ("description", Description), ("parameters", JSONValue.Parse(SchemaText)!))));

    /// <summary>True for the row a provider draws for this tool (its title with any spacing around the middle dot), which the card replaces.</summary>
    public static bool IsProviderRow(ToolCall call)
    {
        if (call.ToolCallKind is not (ToolCall.Kind.Mcp or ToolCall.Kind.Other)) return false;
        var title = string.Concat(call.Title.Where(c => !char.IsWhiteSpace(c)));
        return string.Equals(title, UiMcpServer.ServerName + "·" + Name, StringComparison.Ordinal)
            || string.Equals(title, "mcp__" + UiMcpServer.ServerName + "__" + Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs one call: reads the questions (errors go back to the model, which can call again), shows the card on the chat's
    /// <paramref name="host"/> and waits. A failure to read is a failed result, never a throw.
    /// </summary>
    public static async Task<ArtifactToolResult> Run(JsonNode? arguments, IAskUserHost? host, CancellationToken cancellation)
    {
        if (host is null) return new ArtifactToolResult("Error: this session cannot show a question to the user. Ask in your reply instead.", true, null);
        var parse = AskUserSchema.Parse(arguments, SwiftUUID.UuidString(Guid.NewGuid()));
        if (parse.Card is not { } card)
            return new ArtifactToolResult("Error: " + string.Join(" ", parse.Errors) + " Call ask_user again with this fixed.", true, null);
        var outcome = await host.Ask(card, cancellation).ConfigureAwait(false);
        return outcome.Kind switch
        {
            AskOutcomeKind.Answered => new ArtifactToolResult(AskUserWords.ToolResult(outcome.Card), false, Structured(outcome.Card)),
            AskOutcomeKind.Skipped => new ArtifactToolResult(AskUserWords.SkippedResult, false, new JsonObject { ["skipped"] = true }),
            AskOutcomeKind.Stopped => new ArtifactToolResult(AskUserWords.StoppedResult, false, new JsonObject { ["stopped"] = true }),
            _ => new ArtifactToolResult(AskUserWords.DetachedResult, true, null),
        };
    }

    private static JsonObject Structured(AskCard card)
    {
        var answers = new JsonArray();
        foreach (var question in card.Questions)
        {
            var values = new JsonArray();
            if (card.Answers.TryGetValue(question.Id, out var given))
                foreach (var value in given) values.Add(question.Kind == AskKind.Number && AskUserSchema.TryNumber(value, System.Globalization.CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : JsonValue.Create(value));
            answers.Add(new JsonObject { ["id"] = question.Id, ["question"] = question.Prompt, ["answer"] = values });
        }
        return new JsonObject { ["answers"] = answers };
    }
}
