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
    public static string Instructions => UiTeaching.Instructions(Offered);

    /// <summary>The tools the server can serve, in the order it lists them: the artifact tools, the live-answer tools (W24), the read-only reference, then ask_user (W25).</summary>
    public static IReadOnlyList<string> ToolNames { get; } = [.. ArtifactTools.Names, .. LiveAnswerTools.Names, UiReferenceTool.Name, AskUserTool.Name];

    /// <summary>
    /// The tools the process serves now, which is what the teaching says (so it never names a tool the model cannot call): the artifact
    /// tools and the reference while the artifact store is on, the live-answer tools (W24) while Settings has them on, ask_user (W25)
    /// while a host for it is bound.
    /// </summary>
    public static IReadOnlyList<string> Offered =>
        [.. ToolNames.Where(name => name == AskUserTool.Name ? AskUserDirectory.Shared.Current.IsBound
            : ArtifactHost.Shared.Current.IsOn && (!LiveAnswerTools.IsName(name)
                || (ArtifactHost.Shared.Current.Live.Enabled && (name != LiveAnswerTools.HtmlName || ArtifactHost.Shared.Current.Live.HtmlEnabled))))];

    /// <summary>True for a Claude Code tool name of this server's ("mcp__nth-ui__artifact_create").</summary>
    public static bool IsOwnTool(string? qualifiedName) =>
        qualifiedName is not null && qualifiedName.StartsWith("mcp__" + ServerName + "__", StringComparison.Ordinal)
        && ToolNames.Contains(qualifiedName[("mcp__" + ServerName + "__").Length..]);

    /// <summary>
    /// ED-NEW-web-sec-rev-6: the nth-ui tool a protocol identity names (its server is nth-ui and its tool one of <see cref="ToolNames"/>),
    /// or null. What a row, an approval or a stream is for is read only from this identity, never from a title or detail.
    /// </summary>
    public static string? OwnToolOf(Providers.McpToolIdentity? identity) =>
        identity is { } named && string.Equals(named.Server, ServerName, StringComparison.Ordinal) && ToolNames.Contains(named.Tool) ? named.Tool : null;

    /// <summary>ED-NEW-web-sec-rev-6: the identity of a Claude Code tool name of this server's in a session nth gave nth-ui, or null.</summary>
    public static Providers.McpToolIdentity? ClaudeTool(string? qualifiedName, bool givenNthUi) =>
        givenNthUi && IsOwnTool(qualifiedName) ? new Providers.McpToolIdentity(ServerName, qualifiedName![("mcp__" + ServerName + "__").Length..]) : null;

    /// <summary>
    /// ED-NEW-web-sec-rev-1 and -6: the nth-ui tool an ACP agent's tool call is for, or null. ACP names no server or tool field, so only
    /// a call of kind "other" (the kind agents give MCP tools; a task, fetch, command or edit never counts) whose whole title is one of the
    /// forms agents print for an MCP tool counts: "mcp__nth-ui__ask_user" (Claude Code), "nth-ui-ask_user: ask_user" (the form the skills
    /// relay proved live, SK-17), "nth-ui__ask_user", "nth-ui_ask_user", "nth-ui/ask_user", "nth-ui: ask_user", "nth-ui.ask_user" and
    /// "nth-ui · ask_user". Anything more or less in the title (an agent that titles a call with its arguments, a model's own words) is
    /// not one. The caller asks only in a session nth gave nth-ui.
    /// </summary>
    public static Providers.McpToolIdentity? AcpTool(string? kind, string? title)
    {
        if (kind != "other" || title is null) return null;
        var trimmed = title.Trim();
        foreach (var tool in ToolNames)
            if (AcpForms(tool).Contains(trimmed)) return new Providers.McpToolIdentity(ServerName, tool);
        return null;
    }

    private static string[] AcpForms(string tool) =>
    [
        "mcp__" + ServerName + "__" + tool, ServerName + "-" + tool + ": " + tool, ServerName + "__" + tool, ServerName + "_" + tool,
        ServerName + "/" + tool, ServerName + ": " + tool, ServerName + "." + tool, ServerName + " · " + tool,
    ];

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

    private bool Serves => ServesArtifacts || ServesAsk;

    private bool ServesArtifacts => registration is { Host.IsOn: true };

    // PORT-ONLY (W25): ask_user is the chat's own, so it needs only a host bound for the chat's runtime, not the artifact store.
    private bool ServesAsk => registration is not null && AskUserDirectory.Shared.Current.IsBound;

    /// <summary>The tools this connection lists: the live-answer tools only while Settings has them on, and a small local model has no HTML widget.</summary>
    private IReadOnlyList<string> OfferedNames => [.. ToolNames.Where(OffersTool)];

    private bool OffersTool(string name) =>
        name == AskUserTool.Name ? ServesAsk
        : ServesArtifacts && (!LiveAnswerTools.IsName(name) || (registration!.Host.Live.Enabled && LiveAnswerTools.Offered(registration.Lite, registration.Host.Live.HtmlEnabled).Contains(name)));

    private JsonArray Tools()
    {
        var tools = new JsonArray();
        if (ServesArtifacts)
        {
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
            foreach (var name in LiveAnswerTools.Offered(registration!.Lite, registration.Host.Live.HtmlEnabled).Where(_ => registration.Host.Live.Enabled))
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
        }
        if (ServesAsk) tools.Add(AskTool());
        return tools;
    }

    // PORT-ONLY (W25): the person is asked, nothing in the world changes.
    private static JsonObject AskTool() => new()
    {
        ["name"] = AskUserTool.Name,
        ["title"] = "Ask the user",
        ["description"] = AskUserTool.Description,
        ["inputSchema"] = AskUserTool.InputSchema(),
        ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["openWorldHint"] = false },
    };

    private async Task<JsonNode> Call(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var name = (parameters?["name"] as JsonValue)?.GetValue<string>();
        if (!Serves || !ToolNames.Contains(name ?? "") || (name == AskUserTool.Name ? !ServesAsk : !ServesArtifacts)) throw new ArgumentException("Unknown nth-ui tool.");
        var (thread, provider, model, host) = registration!;
        ArtifactToolResult result;
        if (name == AskUserTool.Name) result = await AskUserTool.Run(parameters?["arguments"], AskUserDirectory.Shared.Current.Find(thread), cancellationToken).ConfigureAwait(false);
        else if (name == UiReferenceTool.Name) result = UiReferenceTool.Run(parameters?["arguments"]);
        else if (LiveAnswerTools.IsName(name))
        {
            // PORT-ONLY (W24, ED-NEW-web-w24-1): the live-answer tools write only into this chat's own timeline (a view, sandboxed), so they run without a card.
            if (!OffersTool(name!)) throw new ArgumentException("Unknown nth-ui tool.");
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
