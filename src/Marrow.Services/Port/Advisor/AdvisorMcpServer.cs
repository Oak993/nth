// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2). The MCP contract of nth's ask_advisor tool, in the
// shape of the skills server (SkillsMcpServer): one JSON-RPC line in, one line out. A connection carries the registration
// of the session it serves; one without a registration (an unknown or missing token) lists no tools and runs none.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;

namespace Marrow.Services.Port.Advisor;

/// <summary>An advised session: its chat, its worker, and the advisor its chat had when it launched.</summary>
public sealed record AdvisorRegistration(Guid? Thread, ProviderKind WorkerProvider, string? WorkerModel, AdvisorChoice Choice);

public sealed class AdvisorMcpServer(AdvisorRegistration? registration)
{
    public const string ServerName = "nth-advisor";

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
            object result = message["method"]?.GetValue<string>() switch
            {
                "initialize" => new
                {
                    protocolVersion = ProtocolVersion(parameters),
                    capabilities = new { tools = new { } }, serverInfo = new { name = ServerName, version = "1.0.0" },
                    instructions = registration is null ? "No advisor is set for this chat." : "Use ask_advisor only when stuck, unsure between approaches, or before a risky change.",
                },
                "ping" => new { },
                "tools/list" => new { tools = Tools() },
                "tools/call" => await Call(parameters, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException("Unsupported MCP method."),
            };
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (NotSupportedException error) { return Error(id, -32601, error.Message); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return Error(id, -32602, error.Message); }
    }

    private object[] Tools() => registration is null ? [] :
    [
        new
        {
            name = AdvisorTool.Name, title = AdvisorTool.Title, description = AdvisorTool.Description,
            inputSchema = JsonNode.Parse(AdvisorTool.InputSchema().CompactString),
            annotations = new { readOnlyHint = true, openWorldHint = true },
        },
    ];

    private async Task<object> Call(JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (registration is null || parameters?["name"]?.GetValue<string>() != AdvisorTool.Name)
            throw new ArgumentException("Unknown nth advisor tool.");
        var arguments = parameters["arguments"];
        var reply = await AdvisorHost.Ask(registration.Thread, registration.Choice, Text(arguments?["question"]), Text(arguments?["context"]), cancellationToken).ConfigureAwait(false);
        return new { isError = reply.Failed, content = new[] { new { type = "text", text = reply.Text } } };
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Error(JsonNode? id, int code, string message) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });

    private static string ProtocolVersion(JsonNode? parameters)
    {
        var version = parameters?["protocolVersion"]?.GetValue<string>();
        return version is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? version : "2025-11-25";
    }
}
