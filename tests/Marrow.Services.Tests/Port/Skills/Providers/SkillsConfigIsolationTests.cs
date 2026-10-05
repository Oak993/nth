using System.Text.Json;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// R-TEST-9: concurrent offline provider fixtures must never replace each other's config path.
public sealed class SkillsConfigIsolationTests
{
    [Theory, InlineData(false), InlineData(true), Trait("Rule", "R-TEST-9")]
    public async Task Concurrent_provider_config_scopes_read_only_their_own_fixture(bool copilot)
    {
        using var files = new OpenCodeFixture();
        var outerPath = files.Write(Path.Combine(files.Root,"outer.json"), "{\"mcpServers\":{\"outer\":{\"command\":\"fixture-only\"}}}");
        var innerPath = files.Write(Path.Combine(files.Root,"inner.json"), "{\"mcpServers\":{\"inner\":{\"command\":\"fixture-only\"}}}");
        using IDisposable outer = copilot ? new CopilotConfigOverride(outerPath) : new ClaudeConfigOverride(outerPath);
        string[] Names()
        {
            if (copilot) return MCPSessionOverrides.CopilotUserServerNames().ToArray();
            using var doc = JsonDocument.Parse(MCPSessionOverrides.OpencodeEnvironment()["OPENCODE_CONFIG_CONTENT"]);
            return doc.RootElement.GetProperty("mcp").EnumerateObject().Select(p=>p.Name).ToArray();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Task.Run(async () =>
        {
            using IDisposable inner = copilot ? new CopilotConfigOverride(innerPath) : new ClaudeConfigOverride(innerPath);
            Assert.Equal(new[] { "inner" }, Names());
            entered.TrySetResult();
            await finish.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The child is still inside its scope. Every read therefore uses an explicit scratch path,
            // even on the old broken global seam; no fallback can reach a real user profile.
            Assert.Equal(new[] { "outer" }, Names());
        }
        finally { finish.TrySetResult(); await child; }
    }
}
