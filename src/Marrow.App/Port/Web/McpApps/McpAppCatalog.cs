// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "When a tool with a UI resource is called"; ED-NEW-web-w19-3). Which tool calls in a
// thread draw an MCP App, decided without waiting on a server: a call the API providers made carries its ui:// address in its
// payload; a CLI provider's call ("shipyard · list_releases") is looked up in the MCP tools nth last listed, which this
// catalog keeps (refreshed at start and whenever a server says its tools changed). No Swift counterpart.
using System.Text.Json.Nodes;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;

namespace Marrow.App.Port.Web.McpApps;

/// <summary>One tool that draws an app.</summary>
public sealed record McpAppToolEntry(string Server, string Tool, string ResourceUri)
{
    public string? Title { get; init; }

    public JsonObject? Definition { get; init; }
}

public static class McpAppCatalog
{
    private static readonly object Gate = new();
    private static Dictionary<string, McpAppToolEntry> tools = new(StringComparer.Ordinal);

    /// <summary>Raised on the main actor when the list changed.</summary>
    public static event Action? Changed;

    /// <summary>The app tools now known.</summary>
    public static IReadOnlyCollection<McpAppToolEntry> Tools
    {
        get
        {
            lock (Gate) return tools.Values.ToList();
        }
    }

    /// <summary>Replaces the list (the refresh, and tests).</summary>
    public static void Set(IEnumerable<McpAppToolEntry> entries)
    {
        var next = new Dictionary<string, McpAppToolEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) next[Key(entry.Server, entry.Tool)] = entry;
        lock (Gate) tools = next;
        Changed?.Invoke();
    }

    /// <summary>Reads the hub's tools again (start-up, and a server's list-changed notice).</summary>
    public static async Task RefreshAsync(MCPHub hub)
    {
        try
        {
            var all = await hub.AllTools();
            var entries = all.Where(t => t.UiResourceUri is { Length: > 0 }).Select(t => new McpAppToolEntry(t.Server, t.Name, t.UiResourceUri!)
            {
                Title = t.Title,
                Definition = Definition(t),
            }).ToList();
            MainActor.Post(() => Set(entries));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // No list: calls with a payload still draw their app; the others stay plain tool rows until a refresh works.
        }
    }

    private static JsonObject Definition(MCPHub.Tool tool)
    {
        var definition = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description };
        if (tool.Title is { } title) definition["title"] = title;
        if (JsonNode.Parse(tool.InputSchema.CompactString) is JsonObject schema) definition["inputSchema"] = schema;
        return definition;
    }

    /// <summary>The app a tool call draws, or null for a plain tool call.</summary>
    public static McpAppToolEntry? For(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.ToolCallKind != ToolCall.Kind.Mcp) return null;
        var (server, tool) = Split(call.Title);
        if (call.Payload is { UiResourceUri: { Length: > 0 } uri } payload)
        {
            var known = Lookup(payload.Server ?? server, payload.Tool ?? tool);
            return new McpAppToolEntry(payload.Server ?? server ?? "", payload.Tool ?? tool ?? "", uri) { Title = known?.Title, Definition = known?.Definition };
        }

        return Lookup(server, tool);
    }

    /// <summary>True when the entry is a tool call that draws an app.</summary>
    public static bool IsApp(TimelineEntry entry) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool && For(tool.Value) is { Server.Length: > 0 };

    private static McpAppToolEntry? Lookup(string? server, string? tool)
    {
        if (server is null || tool is null) return null;
        lock (Gate) return tools.GetValueOrDefault(Key(server, tool));
    }

    /// <summary>"shipyard · list_releases" as its server and tool.</summary>
    public static (string? Server, string? Tool) Split(string title)
    {
        var at = title.IndexOf(" · ", StringComparison.Ordinal);
        return at <= 0 ? (null, null) : (title[..at].Trim(), title[(at + 3)..].Trim());
    }

    private static string Key(string server, string tool) => server + "\n" + tool;
}
