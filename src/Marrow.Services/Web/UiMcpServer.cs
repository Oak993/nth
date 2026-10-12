// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 0 point 9, 6.2, 7.3; ED-NEW-web-w12-1). nth's built-in MCP server
// "nth-ui", which CLI providers reach through the stdio relay (UiMcpBridge): one JSON-RPC line in, one line out, in the
// shape of the advisor's and the skills' servers. A connection carries the registration of the session it serves (its
// chat, provider and model); one without a registration lists no tools and runs none. The artifact tools are its first;
// the live-answer tools (ui_show, ui_update, html_widget) join them in W24 (LiveAnswerTools). No Swift counterpart.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

/// <summary>A session the nth-ui server serves: its chat, its provider and model, and the artifact host at its launch.</summary>
public sealed record UiRegistration(Guid Thread, ProviderKind Provider, string? Model, ArtifactHost Host)
{
    /// <summary>A small local model gets the lite subset of live views and no HTML widget (DESIGN.md 7.3).</summary>
    public bool Lite => Provider == ProviderKind.Local;
}

public sealed class UiMcpServer(UiRegistration? registration)
{
    public const string ServerName = "nth-ui";

    /// <summary>
    /// The server's instructions (initialize): the teaching Marrow.Core.NthUi.NthUiTeaching writes for the tools this server serves
    /// (W17, ED-NEW-web-w17-1): when to use text, a live view or an artifact, then the view language and worked examples.
    /// </summary>
    public static string Instructions => UiTeaching.Instructions(ToolNames);

    /// <summary>The tools the server serves, in the order it lists them: the artifact tools, the live-answer tools (W24), then the read-only reference.</summary>
    public static IReadOnlyList<string> ToolNames { get; } = [.. ArtifactTools.Names, .. LiveAnswerTools.Names, UiReferenceTool.Name];

    /// <summary>True for a Claude Code tool name of this server's ("mcp__nth-ui__artifact_create").</summary>
    public static bool IsOwnTool(string? qualifiedName) =>
        qualifiedName is not null && qualifiedName.StartsWith("mcp__" + ServerName + "__", StringComparison.Ordinal)
        && ToolNames.Contains(qualifiedName[("mcp__" + ServerName + "__").Length..]);

    public async Task<string?> Handle(string line, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try { request = JsonNode.Parse(line); }
        catch (JsonException) { return Error(null, -32700, "Invalid JSON."); }
        if (request is not JsonObject message) return Error(null, -32600, "Invalid MCP request.");
        if (!message.ContainsKey("id")) return null; // MCP notifications have no response.
        var id = message["id"]?.DeepClone();
        try
        {
            var parameters = message["params"];
            JsonNode result = (message["method"] as JsonValue)?.GetValue<string>() switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion(parameters),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0.0" },
                    ["instructions"] = Serves ? UiTeaching.Instructions(OfferedNames) : "Artifacts are not available in this session.",
                },
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = Tools() },
                "tools/call" => await Call(parameters, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException("Unsupported MCP method."),
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
        }
        catch (NotSupportedException error) { return Error(id, -32601, error.Message); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { return Error(id, -32602, error.Message); }
    }

    private bool Serves => registration is { Host.IsOn: true };

    /// <summary>The tools this connection lists: the live-answer tools only while Settings has them on, and a small local model has no HTML widget.</summary>
    private IReadOnlyList<string> OfferedNames => registration is { } known && known.Host.Live.Enabled
        ? [.. ArtifactTools.Names, .. LiveAnswerTools.Offered(known.Lite), UiReferenceTool.Name]
        : [.. ArtifactTools.Names, UiReferenceTool.Name];

    private JsonArray Tools()
    {
        var tools = new JsonArray();
        if (!Serves) return tools;
        foreach (var name in ArtifactTools.Names)
        {
            var reads = name is ArtifactTools.ReadName or ArtifactTools.ListName;
            tools.Add(new JsonObject
            {
                ["name"] = name,
                ["title"] = ArtifactTools.Title,
                ["description"] = ArtifactTools.Description(name),
                ["inputSchema"] = ArtifactTools.InputSchema(name),
                ["annotations"] = new JsonObject { ["readOnlyHint"] = reads, ["destructiveHint"] = false, ["openWorldHint"] = false },
            });
        }
        foreach (var name in LiveAnswerTools.Offered(registration!.Lite).Where(_ => registration.Host.Live.Enabled))
        {
            tools.Add(new JsonObject
            {
                ["name"] = name,
                ["title"] = LiveAnswerTools.Title,
                ["description"] = LiveAnswerTools.Description(name),
                ["inputSchema"] = LiveAnswerTools.InputSchema(name),
                ["annotations"] = new JsonObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["openWorldHint"] = false },
            });
        }
        // PORT-ONLY (W17, ED-NEW-web-w17-1): the on-demand reference of the live-view language, read only.
        tools.Add(new JsonObject
        {
            ["name"] = UiReferenceTool.Name,
            ["title"] = UiReferenceTool.Title,
            ["description"] = UiReferenceTool.Description,
            ["inputSchema"] = UiReferenceTool.InputSchema(),
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["openWorldHint"] = false },
        });
        return tools;
    }

    private async Task<JsonNode> Call(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var name = (parameters?["name"] as JsonValue)?.GetValue<string>();
        if (!Serves || !ToolNames.Contains(name ?? "")) throw new ArgumentException("Unknown nth-ui tool.");
        var (thread, provider, model, host) = registration!;
        ArtifactToolResult result;
        if (name == UiReferenceTool.Name) result = UiReferenceTool.Run(parameters?["arguments"]);
        else if (LiveAnswerTools.IsName(name))
        {
            // PORT-ONLY (W24, ED-NEW-web-w24-1): the live-answer tools write only into this chat's own timeline (a view, sandboxed), so they run without a card.
            if (!LiveAnswerTools.Offered(registration.Lite).Contains(name!)) throw new ArgumentException("Unknown nth-ui tool.");
            result = await LiveAnswerTools.Run(host.Live, thread, registration.Lite, name!, parameters?["arguments"]).ConfigureAwait(false);
        }
        else
        {
            var (author, prompt) = await host.Caller(thread, provider, model).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            result = ArtifactTools.Run(host.Store!, thread, name!, parameters?["arguments"], author, prompt);
        }
        var reply = new JsonObject
        {
            ["isError"] = result.Failed,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
        };
        if (result.Structured is { } structured) reply["structuredContent"] = structured;
        return reply;
    }

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private static string ProtocolVersion(JsonNode? parameters)
    {
        var version = (parameters?["protocolVersion"] as JsonValue)?.TryGetValue<string>(out var text) == true ? text : null;
        return version is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? version : "2025-11-25";
    }
}
