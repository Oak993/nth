// PORT-ONLY: Dextra (DESIGN.md rev 5, 0 point 12, 15.5 "Transport"; unit AS-4; ED-NEW-dextra-as4-5, -6). No Swift twin.
// The nth-dextra tools for the API providers, which run nth's own tool loop (OpenAICompatibleSession, LocalSession):
// function definitions and a system prompt block when the model takes tool calls, and the fenced-text fallback when it
// does not (a local model with tool calling off). Both run through the same AssistantToolRunner as the MCP server, so
// the policy engine is asked before every tool. A session whose chat is not Dextra's gets none of it (null), and its
// requests stay byte for byte as before.
using System.Text;
using System.Text.Json;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Assistant;

public sealed class AssistantNativeTools(AssistantRegistration registration, Func<Classification, PolicyContext, PolicyDecision>? policy = null)
{
    /// <summary>The longest result text one fenced block sends back.</summary>
    public const int MaxFencedResult = 4_000;

    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false, MaxDepth = 64 };
    private readonly AssistantToolRunner runner = new(registration, policy);

    /// <summary>The tools for a session whose chat is Dextra's; null for every other session.</summary>
    public static AssistantNativeTools? For(SessionConfiguration configuration) =>
        AssistantMcpBridge.Register(configuration) is { } registration ? new AssistantNativeTools(registration) : null;

    public AssistantRegistration Registration => registration;

    private IEnumerable<AssistantToolDefinition> Offered => registration.IsLive ? AssistantToolCatalog.For(registration.Chat.Role) : [];

    /// <summary>The OpenAI-style function definitions to add to a request: none once the chat's tools are revoked.</summary>
    public IReadOnlyList<JSONValue> Definitions => [.. Offered.Select(FunctionDefinition)];

    public static JSONValue FunctionDefinition(AssistantToolDefinition tool) => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", tool.Name), ("description", tool.Description), ("parameters", tool.InputSchema()))));

    /// <summary>Whether a tool call names one of the tools this session offers.</summary>
    public bool Owns(string? name) => AssistantToolCatalog.Find(name) is { } tool && registration.IsLive && tool.OfferedTo(registration.Chat.Role);

    /// <summary>The timeline row's title for a call: "Dextra · Recall".</summary>
    public static string Title(string name) => AssistantNames.Assistant + " · " + (AssistantToolCatalog.Find(name)?.Title ?? name);

    /// <summary>The system prompt block: the instructions, and with <paramref name="fenced"/> the fenced format and tool list.</summary>
    public string? InstructionsBlock(bool fenced) => !registration.IsLive ? null
        : fenced ? AssistantToolText.FencedInstructions(registration.Chat.Role, registration.MemoryBlock()) : AssistantToolText.Instructions(registration.Chat.Role, registration.MemoryBlock());

    /// <summary>Runs one native tool call.</summary>
    public Task<AssistantToolResult> Execute(string name, JSONValue? arguments, CancellationToken cancellationToken) =>
        runner.Call(name, arguments, AssistantRoute.Native, cancellationToken);

    /// <summary>The fenced calls of an answer (one more than may run, so the extra one can be named); none once revoked.</summary>
    public IReadOnlyList<AssistantFencedCall> FencedCalls(string? answer) =>
        registration.IsLive ? AssistantFencedCalls.Parse(answer, AssistantFencedCalls.MaxPerAnswer + 1) : [];

    /// <summary>
    /// Runs one fenced call: its body is the arguments as one JSON object (an empty body is no arguments), read strictly.
    /// A block that names no tool, a body that is not one JSON object, or a block past the limit runs nothing.
    /// </summary>
    public async Task<AssistantToolResult> ExecuteFenced(AssistantFencedCall call, int index, CancellationToken cancellationToken)
    {
        if (index >= AssistantFencedCalls.MaxPerAnswer)
            return AssistantToolResult.Failed($"Not run: at most {AssistantFencedCalls.MaxPerAnswer} blocks run per answer.");
        if (call.Tool.Length == 0)
            return AssistantToolResult.Failed($"Not run: name the tool after {AssistantNames.Server} on the block's first line.");
        if (call.Body.Length > AssistantFencedCalls.MaxBody)
            return AssistantToolResult.Failed("Not run: the block is too long.");
        if (ReadBody(call.Body) is not { } arguments)
            return AssistantToolResult.Failed("Not run: the block must hold the arguments as one JSON object.");
        return await runner.Call(call.Tool, arguments, AssistantRoute.Fenced, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The message that carries an answer's fenced results back to the model.</summary>
    public static string FencedReply(IReadOnlyList<(AssistantFencedCall Call, AssistantToolResult Result)> results)
    {
        var text = new StringBuilder(AssistantNames.Server + " results for the blocks in your last answer:");
        for (var i = 0; i < results.Count; i++)
        {
            var (call, result) = results[i];
            var body = result.Text.Length <= MaxFencedResult ? result.Text : result.Text[..MaxFencedResult] + " [cut]";
            text.Append('\n').Append('[').Append(i + 1).Append("] ").Append(call.Tool.Length == 0 ? "(no tool)" : call.Tool)
                .Append(result.IsError ? " (error): " : ": ").Append(body);
        }
        return text.ToString();
    }

    private static JSONValue? ReadBody(string body)
    {
        if (body.Trim().Length == 0) return JSONValue.FromDictionaryLiteral();
        try
        {
            using var document = JsonDocument.Parse(body, Strict);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        }
        catch (JsonException) { return null; }
        return JSONValue.Parse(body) is { JSONValueObject: not null } parsed ? parsed : null;
    }
}
