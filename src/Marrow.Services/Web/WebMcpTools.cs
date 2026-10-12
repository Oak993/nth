// PORT-ONLY: web surfaces W11, the web tools on the nth-pc MCP server (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "Tools
// (web_*), exposed on the nth-pc MCP server for CLI providers ... listed when Settings › Visus › 'Let models use Visus' is on
// (default on), independent of Tactus's own switch"; ED-NEW-web-w11-1). No Swift counterpart. The server (PcControlMcpServer)
// lists these next to the Tactus tools and hands their calls here; each answer is the tool's text, plus its JPEG for a model
// that sees images.
using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Web;

public sealed class WebMcpTools(Func<WebAgentHub> hub)
{
    /// <summary>The process's web tools, over the shared hub.</summary>
    public static WebMcpTools Shared { get; } = new(() => WebAgentHub.Shared.Current);

    /// <summary>Whether a connection's chat is offered the web tools now.</summary>
    public bool Offered(PcRegistration? registration) => registration is not null && hub().Offered;

    /// <summary>The web tools as tools/list entries.</summary>
    public static IEnumerable<JsonObject> Tools() => WebToolSchemas.All.Select(spec => new JsonObject
    {
        ["name"] = spec.Name,
        ["title"] = spec.Title,
        ["description"] = spec.Description,
        ["inputSchema"] = spec.Schema(),
        ["annotations"] = new JsonObject { ["readOnlyHint"] = spec.ReadOnly, ["openWorldHint"] = true },
    });

    /// <summary>Runs one web tool for the connection's chat and returns its tools/call result.</summary>
    public async Task<JsonObject> Call(PcRegistration registration, string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var engine = hub().Engine(registration.Thread) ?? throw new ArgumentException("Visus is not available here.");
        var result = await engine.Call(name, arguments, new WebAgentCaller(registration.Thread, registration.Model, registration.Vision), cancellationToken).ConfigureAwait(false);
        return Content(result, registration.Vision);
    }

    /// <summary>A tool's MCP content: its text, and its JPEG for a model that sees images. waiting_for_user is not an error.</summary>
    public static JsonObject Content(WebToolResult result, bool vision)
    {
        ArgumentNullException.ThrowIfNull(result);
        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.Render() } };
        if (vision && result.Jpeg is { Length: > 0 } jpeg)
            parts.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(jpeg), ["mimeType"] = "image/jpeg" });
        return new JsonObject { ["content"] = parts, ["isError"] = result.Failed };
    }
}
