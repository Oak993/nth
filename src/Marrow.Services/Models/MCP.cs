// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :236-253. Moved up from Core (IMPLEMENTING 2.1, architecture
// V2): MCPPaths builds its locations from Storage.root, a Services type (Services/Store/Storage.swift:18). The rest of
// MCP.swift is src/Marrow.Core/Models/MCP.cs. File URLs are paths (R-PATH-1).

namespace Marrow.Services.Models;

// :236-253
public static class MCPPaths
{
    // :237-241. A static let whose initializer creates the directory on first access (R-LIFE-2).
    public static string Directory => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:237");

    // :244
    public static string ConnectionsURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:244");

    // :246
    public static string CustomsURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:246");

    // :248
    public static string ClaudeConfigURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:248");

    // :250
    public static string CodexConfigURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:250");

    // :252
    public static string CopilotConfigURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/MCP.swift:252");
}
