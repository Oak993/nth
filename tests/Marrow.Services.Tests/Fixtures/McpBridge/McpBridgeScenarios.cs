using System.Linq;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.Fixtures.McpBridge;

// Helpers for MCPBridgeSourceTests (R-TEST-3). Not port code (R-FILE-2).
//
// Each scenario below spawns Node exactly once and every [Fact] that needs it reads the same cached result
// (a handful of ~150 ms Node starts, not dozens): one config drives several servers, in object order, so one
// run proves ordering, per-server skip-and-continue and several dialect facts together, each still traced back
// to its own record by the test that reads that part of the result.
public static class McpBridgeScenarios
{
    private static string NewScratchDir(string label)
    {
        var dataDirectory = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var path = Path.Combine(dataDirectory, "MCPBridgeSourceTests", label, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static readonly Lazy<Uri> InstalledBridgeUri = new(() => MCPBridge.InstallBridge(NewScratchDir("install")));

    /// <summary>The bridge MCPBridge.InstallBridge actually wrote, imported by every scenario below.</summary>
    public static Uri BridgeUri => InstalledBridgeUri.Value;

    // Five remote servers sharing one in-process fake HTTP server (run-harness.mjs), each exercising a
    // different facet of connectRemote/postMessage (PR-MCP-196, PR-MCP-198), plus one entry that is neither
    // an "http" nor a local member and so must be silently ignored (PR-MCP-195).
    private const string RemoteConfigTemplate = """
        {"mcpServers": {
          "normal": {"type":"http","url":"{{HTTP_URL}}/normal"},
          "sse": {"type":"http","url":"{{HTTP_URL}}/sse"},
          "empty": {"type":"http","url":"{{HTTP_URL}}/empty"},
          "noerrormsg": {"type":"http","url":"{{HTTP_URL}}/noerrormsg"},
          "badnotify": {"type":"http","url":"{{HTTP_URL}}/badnotify"},
          "ignored": {"foo": "bar"}
        }}
        """;

    private static readonly Lazy<NodeHarness.HarnessResult> RemoteResult = new(() =>
    {
        var dir = NewScratchDir("remote");
        return NodeHarness.Run(BridgeUri, configPath: Path.Combine(dir, "config.json"), configTemplate: RemoteConfigTemplate,
            calls: [new NodeHarness.ToolCall("remote-1", new { a = 1 }), new NodeHarness.ToolCall("remote-error", new { })]);
    });

    /// <summary>Five remote servers ("normal", "sse", "empty", "noerrormsg", "badnotify") plus one ignored entry.</summary>
    public static NodeHarness.HarnessResult Remote => RemoteResult.Value;

    public sealed record StdioScenario(NodeHarness.HarnessResult Result, IReadOnlyList<System.Text.Json.JsonElement> Log);

    private static readonly Lazy<StdioScenario> StdioValue = new(() =>
    {
        var dir = NewScratchDir("stdio");
        var logPath = Path.Combine(dir, "log.jsonl");
        var config = "{\"mcpServers\": {"
            + "\"stdioNormal\": {\"command\": " + Json(NodeExecutablePath) + ", \"args\": [" + Json(NodeHarness.FakeStdioServerPath) + "], "
            + "\"env\": {\"MCP_FAKE_TOOL\":\"tool\",\"MCP_FAKE_PAGES\":\"2\",\"MCP_FAKE_LOG\":" + Json(logPath) + ",\"MCP_FAKE_TAG\":\"stdioNormal\"}},"
            + "\"stdioCrash\": {\"command\": " + Json(NodeExecutablePath) + ", \"args\": [" + Json(NodeHarness.FakeStdioServerPath) + "], "
            + "\"env\": {\"MCP_FAKE_MODE\":\"crash\"}}"
            + "}}";
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, config);
        var result = NodeHarness.Run(BridgeUri, configPath: configPath,
            calls:
            [
                new NodeHarness.ToolCall("tool", new { y = 2 }),
                new NodeHarness.ToolCall("tool-error", new { }),
                new NodeHarness.ToolCall("tool-emptyerror", new { }),
                new NodeHarness.ToolCall("tool-image", new { }),
                new NodeHarness.ToolCall("tool-mixed", new { }),
                new NodeHarness.ToolCall("tool-throw", new { }),
                new NodeHarness.ToolCall("tool-bare", new { }),
            ]);
        var log = File.Exists(logPath)
            ? File.ReadAllLines(logPath).Where(line => line.Length > 0)
                .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement).ToList()
            : [];
        return new StdioScenario(result, log);
    });

    /// <summary>"stdioNormal" (every wrapTools content shape, pagination) and "stdioCrash" (exits at once), plus
    /// "stdioNormal"'s own JSON-RPC dialogue log (dialect assertions: :ED-042 and PR-MCP-196/197).</summary>
    public static StdioScenario Stdio => StdioValue.Value;

    // W18: one server ("rich") whose tools answer images, structuredContent, links and an app-only tool.
    private static readonly Lazy<NodeHarness.HarnessResult> RichResult = new(() =>
    {
        var dir = NewScratchDir("rich");
        var config = "{\"mcpServers\": {\"rich\": {\"command\": " + Json(NodeExecutablePath) + ", \"args\": [" + Json(NodeHarness.FakeRichServerPath) + "]}}}";
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, config);
        return NodeHarness.Run(BridgeUri, configPath: configPath,
            calls:
            [
                new NodeHarness.ToolCall("shown", new { }),
                new NodeHarness.ToolCall("structure", new { }),
                new NodeHarness.ToolCall("links", new { }),
                new NodeHarness.ToolCall("failing", new { }),
            ]);
    });

    /// <summary>The "rich" server's tools as the bridge hands them out, each called once (W18).</summary>
    public static NodeHarness.HarnessResult Rich => RichResult.Value;

    private static readonly Lazy<NodeHarness.HarnessResult> CmdWrapperResult = new(() =>
    {
        var dir = NewScratchDir("cmd-wrapper");
        var wrapperBase = Path.Combine(dir, "fake-stdio"); // no extension: proves ED-042's PATHEXT resolution.
        File.WriteAllText(wrapperBase + ".cmd",
            "@echo off\r\n\"" + NodeExecutablePath + "\" \"" + NodeHarness.FakeStdioServerPath + "\" %*\r\n");
        var config = "{\"mcpServers\": {\"stdioCmd\": {\"command\": " + Json(wrapperBase)
            + ", \"args\": [], \"env\": {\"MCP_FAKE_TOOL\":\"cmdtool\"}}}}";
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, config);
        return NodeHarness.Run(BridgeUri, configPath: configPath);
    });

    /// <summary>One server, "stdioCmd", whose command is a bare .cmd shim resolved through ED-042's PATHEXT logic.</summary>
    public static NodeHarness.HarnessResult CmdWrapper => CmdWrapperResult.Value;

    public static NodeHarness.HarnessResult RunConfig(string? configPath, string? configText = null)
    {
        if (configText is not null)
        {
            configPath ??= Path.Combine(NewScratchDir("config"), "config.json");
            File.WriteAllText(configPath, configText);
        }
        return NodeHarness.Run(BridgeUri, configPath: configPath);
    }

    private static readonly string NodeExecutablePath = LocateNodeExecutable();

    private static string LocateNodeExecutable()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? Environment.GetEnvironmentVariable("Path") ?? "";
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';');
        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, "node" + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException("node executable not found on PATH; the MCP bridge tests need Node.js installed.");
    }

    private static string Json(string? value) => System.Text.Json.JsonSerializer.Serialize(value);
}
