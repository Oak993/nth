using Marrow.Services.MCP;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Overrides USERPROFILE for the duration of a test so MCPSessionOverrides.CopilotUserServerNames
/// resolves its ~/.copilot/mcp-config.json path (LoginEnvironment.HomeDirectory) under a scratch folder.
/// </summary>
public sealed class HomeEnvironmentOverride : IDisposable
{
    private readonly string? originalHome;

    public HomeEnvironmentOverride(string homeDir)
    {
        originalHome = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", homeDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("USERPROFILE", originalHome);
    }
}

/// <summary>
/// Overrides the claude.json path MCPSessionOverrides.OpencodeEnvironment reads, for the duration of a test.
/// </summary>
public sealed class ClaudeConfigOverride : IDisposable
{
    public ClaudeConfigOverride(string configPath)
    {
        MCPSessionOverrides.SetClaudeConfigPathForTesting(configPath);
    }

    public void Dispose()
    {
        MCPSessionOverrides.ResetClaudeConfigPathForTesting();
    }
}

/// <summary>
/// Overrides the ~/.copilot/mcp-config.json path MCPSessionOverrides.CopilotUserServerNames reads, for the
/// duration of a test.
/// </summary>
public sealed class CopilotConfigOverride : IDisposable
{
    public CopilotConfigOverride(string configPath)
    {
        MCPSessionOverrides.SetCopilotConfigPathForTesting(configPath);
    }

    public void Dispose()
    {
        MCPSessionOverrides.ResetCopilotConfigPathForTesting();
    }
}
