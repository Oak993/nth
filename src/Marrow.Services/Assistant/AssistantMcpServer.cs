// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.5; unit AS-4; ED-NEW-dextra-as4-3). No Swift twin.
// The MCP contract of the nth-dextra server, in the shape of the advisor's (AdvisorMcpServer): one JSON-RPC line in, one
// line out. A connection carries the registration of the chat it serves; one without a live registration (an unknown or
// revoked token) lists no tools and runs none. Every tools/call goes through AssistantToolRunner, so the policy engine
// is asked before any tool acts. Requests are read strictly: a repeated property name anywhere refuses the line.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Services.Assistant;

public sealed class AssistantMcpServer(AssistantRegistration? registration, Func<Classification, PolicyContext, PolicyDecision>? policy = null)
{
    public const string ServerName = AssistantNames.Server;

    /// <summary>The longest request line read, in characters.</summary>
    public const int MaxLine = 1024 * 1024;

    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false, MaxDepth = 64 };
    private readonly AssistantToolRunner? runner = registration is null ? null : new AssistantToolRunner(registration, policy);

    private bool Live => registration is { IsLive: true };

    public async Task<string?> Handle(string line, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try { request = JsonNode.Parse(line, documentOptions: Strict); }
        catch (Exception error) when (error is JsonException or ArgumentException) { return Error(null, -32700, "Invalid JSON."); }
        if (request is not JsonObject message) return Error(null, -32600, "Invalid MCP request.");
        if (!message.ContainsKey("id")) return null; // MCP notifications have no response.
        var id = message["id"]?.DeepClone();
        try
        {
            var parameters = message["params"];
            object result = (message["method"] is JsonValue method && method.TryGetValue<string>(out var name) ? name : null) switch
            {
                "initialize" => new
                {
                    protocolVersion = ProtocolVersion(parameters),
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = ServerName, version = "1.0.0" },
                    instructions = Live ? AssistantToolText.Instructions(registration!.Chat.Role, registration.MemoryBlock()) : "This chat has no " + AssistantNames.Assistant + " tools.",
                },
                "ping" => new { },
                "tools/list" => new { tools = Tools() },
                "tools/call" => await Call(parameters, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException("Unsupported MCP method."),
            };
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (NotSupportedException error) { return Error(id, -32601, error.Message); }
        catch (ArgumentException error) { return Error(id, -32602, error.Message); }
    }

    private object[] Tools() => !Live ? [] :
    [
        .. AssistantToolCatalog.For(registration!.Chat.Role).Select(tool => (object)new
        {
            name = tool.Name,
            title = tool.Title,
            description = tool.Description,
            inputSchema = JsonNode.Parse(tool.InputSchema().CompactString),
            annotations = new { readOnlyHint = tool.ReadOnly, destructiveHint = tool.Destructive, idempotentHint = false, openWorldHint = tool.OpenWorld },
        }),
    ];

    private async Task<object> Call(JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (runner is null || parameters is not JsonObject call || call["name"] is not JsonValue nameNode || !nameNode.TryGetValue<string>(out var name))
            throw new ArgumentException("Unknown " + ServerName + " tool.");
        JSONValue? arguments = call["arguments"] is { } given ? JSONValue.Parse(given.ToJsonString()) : null;
        var reply = await runner.Call(name, arguments, AssistantRoute.Mcp, cancellationToken).ConfigureAwait(false);
        return new { isError = reply.IsError, content = new[] { new { type = "text", text = reply.Text } } };
    }

    private static string Error(JsonNode? id, int code, string message) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });

    private static string ProtocolVersion(JsonNode? parameters)
    {
        var version = parameters?["protocolVersion"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        return version is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? version : "2025-11-25";
    }
}
