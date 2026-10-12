using System.Diagnostics;
using System.Text.Json;

namespace Marrow.Services.Tests.Fixtures.McpBridge;

// Helpers for MCPBridgeSourceTests (R-TEST-3: helper types live in Fixtures/, never inside a test class). Not
// port code (R-FILE-2).
//
// R-TEST-8/R-TEST-10: the bridge under test IS a piece of shipped JavaScript, so proving PR-MCP-194..198 means
// running it. The only process this spawns is the real Node.js binary - a system tool, like ProcessLauncherTests'
// where.exe or sort.exe, never an AI CLI - against two local fixtures: fake-stdio-server.mjs (a scripted local
// MCP server the bridge itself spawns as a child) and run-harness.mjs (which imports the installed bridge file
// and, for a remote scenario, starts its own loopback HTTP server). Nothing here reaches the real network, a
// real AI provider, or %LOCALAPPDATA%\Marrow.

/// <summary>A real temporary folder under MARROW_PORT_DATA_DIR (R-TEST-9), scoped to the MCP bridge tests.</summary>
public sealed class McpBridgeScratchFolder : IDisposable
{
    public McpBridgeScratchFolder()
    {
        var dataDirectory = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Root = Path.Combine(dataDirectory, "MCPBridgeSourceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Child(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Runs the run-harness.mjs fixture against a real Node.js process and parses its one line of JSON.</summary>
public static class NodeHarness
{
    public static readonly string FakeStdioServerPath = FixturePath("fake-stdio-server.mjs");
    public static readonly string FakeRichServerPath = FixturePath("fake-rich-server.mjs"); // W18
    private static readonly string HarnessScriptPath = FixturePath("run-harness.mjs");
    private static readonly string NodeExecutablePath = LocateNode();

    private static string FixturePath(string fileName)
    {
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null)
        {
            var candidate = Path.Combine(probe.FullName, "Fixtures", "McpBridge", fileName);
            if (File.Exists(candidate)) return candidate;
            probe = probe.Parent;
        }
        throw new FileNotFoundException("Fixture not found under " + AppContext.BaseDirectory, fileName);
    }

    private static string LocateNode()
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

    public sealed record ToolSummary(string Server, string Name, string Description, JsonElement InputSchema, string? CallResult, JsonElement? CallContent = null);

    public sealed record HttpRequestRecord(string Path, string Method, IReadOnlyDictionary<string, string> Headers, JsonElement? Body);

    public sealed record HarnessResult(int ExitCode, string Stdout, string Stderr,
        IReadOnlyList<ToolSummary> Tools, IReadOnlyList<HttpRequestRecord> HttpRequests);

    /// <summary>One tool call to make after loadMCPTools() resolves: {"tool": name, "args": {...}}.</summary>
    public sealed record ToolCall(string Tool, object Args);

    public static HarnessResult Run(Uri bridgeUri, string? configPath = null, string? configTemplate = null,
        IReadOnlyList<ToolCall>? calls = null, IReadOnlyDictionary<string, string>? env = null)
    {
        var startInfo = new ProcessStartInfo(NodeExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(HarnessScriptPath);
        startInfo.Environment["MCP_HARNESS_BRIDGE_PATH"] = bridgeUri.LocalPath;
        if (configPath is not null) startInfo.Environment["DROPPY_CODE_MCP_CONFIG"] = configPath;
        if (configTemplate is not null) startInfo.Environment["MCP_HARNESS_CONFIG_TEMPLATE"] = configTemplate;
        if (calls is not null)
        {
            var payload = calls.Select(call => new Dictionary<string, object> { ["tool"] = call.Tool, ["args"] = call.Args });
            startInfo.Environment["MCP_HARNESS_CALLS"] = JsonSerializer.Serialize(payload);
        }
        if (env is not null)
        {
            foreach (var (key, value) in env) startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start node.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("The MCP bridge harness did not exit within 30 seconds.");
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        var tools = new List<ToolSummary>();
        var requests = new List<HttpRequestRecord>();
        var trimmed = stdout.Trim();
        if (trimmed.Length > 0)
        {
            using var document = JsonDocument.Parse(trimmed);
            foreach (var tool in document.RootElement.GetProperty("tools").EnumerateArray())
            {
                var callResult = tool.GetProperty("callResult");
                tools.Add(new ToolSummary(
                    tool.GetProperty("server").GetString()!,
                    tool.GetProperty("name").GetString()!,
                    tool.GetProperty("description").GetString()!,
                    tool.GetProperty("inputSchema").Clone(),
                    callResult.ValueKind == JsonValueKind.Null ? null : callResult.GetString(),
                    tool.TryGetProperty("callContent", out var callContent) && callContent.ValueKind != JsonValueKind.Null ? callContent.Clone() : null));
            }
            foreach (var request in document.RootElement.GetProperty("httpRequests").EnumerateArray())
            {
                var headers = request.GetProperty("headers").EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
                var body = request.GetProperty("body");
                requests.Add(new HttpRequestRecord(
                    request.GetProperty("path").GetString()!,
                    request.GetProperty("method").GetString()!,
                    headers,
                    body.ValueKind == JsonValueKind.Null ? null : body.Clone()));
            }
        }
        return new HarnessResult(process.ExitCode, stdout, stderr, tools, requests);
    }
}
