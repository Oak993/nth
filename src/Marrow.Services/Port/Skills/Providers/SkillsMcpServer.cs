// PORT-ONLY: SK-17, owner's Cursor follow-up. The live-proven read-only MCP contract over the runtime's verified lease.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class SkillsMcpServer : IDisposable
{
    private readonly Generation generation;
    private readonly object gate = new();
    private bool disposed;
    public SkillsMcpServer(Generation generation)
    {
        SessionSkillOverlay.RequireFolder(generation, "cursor");
        this.generation = generation;
    }
    public string? Handle(string line)
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
                    capabilities = new { tools = new { } }, serverInfo = new { name = "marrow-skills", version = "1.0.0" },
                    instructions = "Use list_skills to discover relevant Nth skills. Read a relevant skill with read_skill before answering. These are read-only skills.",
                },
                "ping" => new { },
                "tools/list" => new { tools = new object[]
                {
                    new { name = "list_skills", description = "List the names, descriptions and folders of Nth-owned skills to find instructions relevant to the current request.", inputSchema = new { type = "object", properties = new { }, additionalProperties = false }, annotations = new { readOnlyHint = true, idempotentHint = true } },
                    new { name = "read_skill", description = "Read the complete instructions of a relevant Nth-owned skill by name. For its references, supply a pinned relative path.", inputSchema = new { type = "object", properties = new { name = new { type = "string" }, path = new { type = "string" } }, required = new[] { "name" }, additionalProperties = false }, annotations = new { readOnlyHint = true, idempotentHint = true } },
                } },
                "tools/call" => Call(parameters),
                _ => throw new NotSupportedException("Unsupported MCP method."),
            };
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (NotSupportedException error) { return Error(id, -32601, error.Message); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return Error(id, -32602, error.Message); }
    }
    private object Call(JsonNode? parameters)
    {
        lock (gate)
        {
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!generation.Verify()) throw new IOException("The skill generation failed verification.");
                string text;
                if (parameters?["name"]?.GetValue<string>() == "list_skills")
                    text = JsonSerializer.Serialize(generation.Skills.Where(s => !s.ExplicitOnly).Select(s => new { name = s.Name, description = s.Description, folder = s.Folder }));
                else if (parameters?["name"]?.GetValue<string>() == "read_skill")
                {
                    var name = parameters["arguments"]?["name"]?.GetValue<string>();
                    var skill = generation.Skills.SingleOrDefault(s => !s.ExplicitOnly && (s.Name == name || s.Id == name))
                        ?? throw new ArgumentException("Unknown automatic Nth skill. Explicit-only skills require Nth's explicit picker.");
                    text = generation.ReadReference(skill.Id, parameters["arguments"]?["path"]?.GetValue<string>() ?? "SKILL.md");
                }
                else throw new ArgumentException("Unknown Nth skills tool.");
                return new { content = new[] { new { type = "text", text } } };
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            { return new { isError = true, content = new[] { new { type = "text", text = error.Message } } }; }
        }
    }
    private static string Error(JsonNode? id, int code, string message) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });
    private static string ProtocolVersion(JsonNode? parameters)
    {
        var version = parameters?["protocolVersion"]?.GetValue<string>();
        return version is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? version : "2025-11-25";
    }
    // The runtime owns the generation. Closing this endpoint prevents queued/in-flight readers before its lease can go.
    public void Dispose() { lock (gate) disposed = true; }
}
