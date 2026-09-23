// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :236-253. Moved up from Core (IMPLEMENTING 2.1, architecture
// V2): MCPPaths builds its locations from Storage.root, a Services type (Services/Store/Storage.swift:18). The rest of
// MCP.swift is src/Marrow.Core/Models/MCP.cs. File URLs are paths (R-PATH-1).
// ED-177 / PR-MCP-084 / PR-STORE-011: implemented on Storage.Root (Services/Store/Storage.cs).
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.Services.Models;

// :236-253
public static class MCPPaths
{
    // :237-241. ENGINEERING DECISION: Storage.Root is itself an Ambient (Storage.cs) scoped per async flow so
    // tests never share one root; a process-wide cached Lazy here (matching Swift's static let literally) would
    // freeze on whichever test's root won the race to evaluate it first and leak across parallel test classes.
    // Recomputed (and mkdir'd, idempotent) on every access instead; the observable result is identical.
    public static string Directory
    {
        get
        {
            var url = SwiftPath.AppendingPathComponent(Storage.Root, "mcp"); // :237
            SwiftTry.Ignore(() => System.IO.Directory.CreateDirectory(url)); // :238
            return url;
        }
    }

    // :244
    public static string ConnectionsURL => SwiftPath.AppendingPathComponent(Storage.Root, "mcp.json");

    // :246
    public static string CustomsURL => SwiftPath.AppendingPathComponent(Storage.Root, "custom-mcp.json");

    // :248
    public static string ClaudeConfigURL => SwiftPath.AppendingPathComponent(Directory, "claude.json");

    // :250
    public static string CodexConfigURL => SwiftPath.AppendingPathComponent(Directory, "codex.json");

    // :252
    public static string CopilotConfigURL => SwiftPath.AppendingPathComponent(Directory, "copilot.json");
}
