using System.Text.Json;
using Marrow.Services.Tests.Fixtures.McpBridge;

namespace Marrow.Services.Tests.MCP;

// Audit fix (svc): ED-042 rejects shell: true because a catalog or hand-edited args value holding & | ^ would run arbitrary commands.
// MCPBridgeSource.swift:79-84 passes every args item as argv data with no shell, so a value such as a""&echo X&""b stays data
// when the command is a .cmd shim launched through %COMSPEC% /d /s /c. The stand-in is a local .cmd shim around the fake stdio server.
public sealed class MCPBridgeSourceCmdInjectionTests
{
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:79-84; ED-042
    [Trait("Decision", "ED-042")]
    [Fact]
    public void A_cmd_shim_argument_holding_shell_syntax_stays_argument_data()
    {
        using var scratch = new McpBridgeScratchFolder();
        var marker = scratch.Child("injected.txt");
        var shim = scratch.Child("fake-stdio");
        File.WriteAllText(shim + ".cmd", "@echo off\r\nnode \"" + NodeHarness.FakeStdioServerPath + "\" %*\r\n");
        var argument = "a\"\"&echo INJECTED>" + marker + "&\"\"b";
        var config = "{\"mcpServers\": {\"stdioCmd\": {\"command\": " + JsonSerializer.Serialize(shim)
            + ", \"args\": [" + JsonSerializer.Serialize(argument) + "], \"env\": {\"MCP_FAKE_TOOL\":\"cmdtool\"}}}}";
        var configPath = scratch.Child("config.json");
        File.WriteAllText(configPath, config);

        var result = NodeHarness.Run(McpBridgeScenarios.BridgeUri, configPath: configPath);

        Assert.False(File.Exists(marker), "the argument ran as a command");
        Assert.True(result.Tools.Any(tool => tool.Name == "cmdtool"), result.Stderr);
    }
}
