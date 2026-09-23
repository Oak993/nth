using Marrow.Services.Models;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Models;

// Tests for src/Marrow.Services/Models/MCP.cs (MCPPaths), the Services-layer part of the port of
// DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :236-253, moved up per IMPLEMENTING.md 2.1 (ED-177).
public sealed class MCPTests
{
    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:237-241
    public void Directory_IsMcpUnderTheStoreRootAndIsCreated()
    {
        using var scratch = new StorageScratchFolder();
        var directory = MCPPaths.Directory;
        Assert.Equal(Path.Combine(Marrow.Services.Store.Storage.Root, "mcp"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:244
    [Trait("Req", "PR-STORE-011")]
    public void ConnectionsURL_IsMcpJsonAtTheRootNotInsideMcpDirectory()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(Marrow.Services.Store.Storage.Root, "mcp.json"), MCPPaths.ConnectionsURL);
    }

    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:246
    [Trait("Req", "PR-STORE-011")]
    public void CustomsURL_IsCustomMcpJsonAtTheRootNotInsideMcpDirectory()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(Marrow.Services.Store.Storage.Root, "custom-mcp.json"), MCPPaths.CustomsURL);
    }

    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:248
    public void ClaudeConfigURL_IsClaudeJsonInsideMcpDirectory()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(MCPPaths.Directory, "claude.json"), MCPPaths.ClaudeConfigURL);
    }

    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:250
    public void CodexConfigURL_IsCodexJsonInsideMcpDirectory()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(MCPPaths.Directory, "codex.json"), MCPPaths.CodexConfigURL);
    }

    [Fact]
    [Trait("Req", "PR-MCP-084")] // Core/Models/MCP.swift:252
    public void CopilotConfigURL_IsCopilotJsonInsideMcpDirectory()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(MCPPaths.Directory, "copilot.json"), MCPPaths.CopilotConfigURL);
    }
}
