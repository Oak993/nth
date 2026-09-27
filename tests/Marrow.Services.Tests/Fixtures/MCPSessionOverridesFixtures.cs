using Marrow.Services.MCP;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Redirects the config path under a scratch home without changing or reading the real user profile.
/// </summary>
public sealed class HomeEnvironmentOverride : IDisposable
{
    private readonly CopilotConfigOverride config;

    public HomeEnvironmentOverride(string homeDir)
    {
        config = new CopilotConfigOverride(Path.Combine(homeDir, ".copilot", "mcp-config.json"));
    }

    public void Dispose()
    {
        config.Dispose();
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
