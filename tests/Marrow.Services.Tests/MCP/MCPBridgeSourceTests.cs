using System.Text;
using System.Text.Json;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures.McpBridge;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPBridgeSource.cs, the port of DroppyCode/Services/MCP/MCPBridgeSource.swift
// (B2 1fbf81e), :1-253, and records PR-MCP-193 to PR-MCP-198.
//
// installBridge (:240-251, PR-MCP-193) is ordinary C# and is tested directly against a throwaway folder
// (McpBridgeScratchFolder, R-TEST-9): MCPBridge.InstallBridge(string) takes the base directory explicitly, the
// same split HangWatchdog.ReportsFolder(localApplicationData) uses to keep a test from ever touching the real
// %LOCALAPPDATA%\Marrow Code.
//
// Everything else the Swift file declares (:12-235, mcpBridgeSource) is not C# logic at all: it is a big string
// constant of plain JavaScript that Pi and Command Code dynamic-import and run under Node. Proving PR-MCP-194
// through PR-MCP-198 therefore means running that JavaScript, so most of this file spawns the real Node.js binary
// (R-TEST-10 allows a system tool; this is not an AI CLI) against two local, scripted fixtures under
// Fixtures/McpBridge/: fake-stdio-server.mjs (a stand-in local MCP server the bridge itself spawns) and
// run-harness.mjs (which imports the actual file MCPBridge.InstallBridge wrote and, for the remote scenarios,
// starts its own loopback HTTP server). Nothing here reaches the real network, a real AI provider, or a real MCP
// server; every fact asserted below is something the fake server actually received or the bridge module actually
// returned or printed. McpBridgeScenarios runs each scenario once and caches it, so many facts share one Node
// process instead of spawning one per assertion.
//
// DEVIATION OD-0012 F1 (:138, :203): clientInfo.name is "Marrow", never "Droppy Code".
// DEVIATION ED-042 (:76-152 connectStdio's spawn): a resolveSpawnTarget helper resolves entry.command against
// PATH using PATHEXT before spawning, so a bare npx-style .cmd/.bat name can be launched on Windows; :250's cmd
// wrapper scenario below is what proves it.
public sealed class MCPBridgeSourceTests
{
    // ---- installBridge (:240-251, PR-MCP-193) ----

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:240-251
    public void InstallBridge_WritesTheBridgeUnderNthMcp()
    {
        using var scratch = new McpBridgeScratchFolder();
        var uri = MCPBridge.InstallBridge(scratch.Root);

        var expectedFile = Path.Combine(scratch.Root, "nth", "mcp", "droppy-mcp-bridge.mjs");
        Assert.Equal(Path.GetFullPath(expectedFile), Path.GetFullPath(uri.LocalPath));
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(Encoding.UTF8.GetBytes(MCPBridge.McpBridgeSource), File.ReadAllBytes(expectedFile));
    }

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:244 createDirectory(withIntermediateDirectories: true)
    public void InstallBridge_CreatesEveryIntermediateDirectory()
    {
        using var scratch = new McpBridgeScratchFolder();
        var nestedBase = Path.Combine(scratch.Root, "does", "not", "exist", "yet");
        Assert.False(Directory.Exists(nestedBase));

        MCPBridge.InstallBridge(nestedBase);

        Assert.True(Directory.Exists(Path.Combine(nestedBase, "nth", "mcp")));
    }

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:250 return file, a URL
    public void InstallBridge_ReturnsAFileUrlNotABareDriveLetterPath()
    {
        using var scratch = new McpBridgeScratchFolder();
        var uri = MCPBridge.InstallBridge(scratch.Root);

        Assert.True(uri.IsFile);
        Assert.Equal("file", uri.Scheme);
        Assert.StartsWith("file:///", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:247-249, written atomically only when changed
    public void InstallBridge_DoesNotRewriteAnUnchangedFile()
    {
        using var scratch = new McpBridgeScratchFolder();
        var uri = MCPBridge.InstallBridge(scratch.Root);
        var firstWrite = File.GetLastWriteTimeUtc(uri.LocalPath);
        Thread.Sleep(50);

        MCPBridge.InstallBridge(scratch.Root);

        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(uri.LocalPath));
    }

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:246-249, a new build's bridge replaces the old one
    public void InstallBridge_RewritesAFileWhoseBytesDiffer()
    {
        using var scratch = new McpBridgeScratchFolder();
        var directory = Path.Combine(scratch.Root, "nth", "mcp");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "droppy-mcp-bridge.mjs");
        File.WriteAllText(file, "stale bridge text from an earlier build");

        MCPBridge.InstallBridge(scratch.Root);

        Assert.Equal(Encoding.UTF8.GetBytes(MCPBridge.McpBridgeSource), File.ReadAllBytes(file));
    }

    [Fact]
    [Trait("Req", "PR-MCP-193")] // Services/MCP/MCPBridgeSource.swift:12-235, the written bytes are the bridge, importable
    public void InstallBridge_WritesAFileThatNodeCanActuallyImportAndRun()
    {
        // Also exercises PR-MCP-194's "unset -> nothing registered" through the real installed bytes, not a
        // hand-copied string, end to end.
        var result = NodeHarness.Run(McpBridgeScenarios.BridgeUri);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Tools);
        Assert.Equal("", result.Stderr);
    }

    // ---- configServers (:19-29, PR-MCP-194) ----

    [Fact]
    [Trait("Req", "PR-MCP-194")] // Services/MCP/MCPBridgeSource.swift:19-22, DROPPY_CODE_MCP_CONFIG unset
    public void LoadMCPTools_UnsetConfigVariable_RegistersNothing()
    {
        var result = McpBridgeScenarios.RunConfig(configPath: null);
        Assert.Empty(result.Tools);
        Assert.Equal("", result.Stderr);
    }

    [Fact]
    [Trait("Req", "PR-MCP-194")] // Services/MCP/MCPBridgeSource.swift:23,25-27, a missing or unreadable file
    public void LoadMCPTools_ConfigFileMissing_LogsCouldNotReadConfig()
    {
        using var scratch = new McpBridgeScratchFolder();
        var missing = scratch.Child("does-not-exist.json");

        var result = McpBridgeScenarios.RunConfig(configPath: missing);

        Assert.Empty(result.Tools);
        Assert.StartsWith("droppy mcp: could not read config: ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-194")] // Services/MCP/MCPBridgeSource.swift:23,25-27, a file that is not valid JSON
    public void LoadMCPTools_ConfigFileIsNotValidJson_LogsCouldNotReadConfig()
    {
        var result = McpBridgeScenarios.RunConfig(configPath: null, configText: "not json");

        Assert.Empty(result.Tools);
        Assert.StartsWith("droppy mcp: could not read config: ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-194")] // Services/MCP/MCPBridgeSource.swift:24, no usable mcpServers object
    public void LoadMCPTools_ConfigWithoutAnMcpServersObject_RegistersNothing()
    {
        var result = McpBridgeScenarios.RunConfig(configPath: null, configText: "{}");

        Assert.Empty(result.Tools);
        Assert.Equal("", result.Stderr);
    }

    // ---- loadMCPTools ordering, shape recognition and per-server failure (:218-234, PR-MCP-195) ----

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:218-232 CONNECT_TIMEOUT_MS = 15000
    public void McpBridgeSource_TimesOutAConnectionAfterFifteenSeconds()
    {
        Assert.Contains("const CONNECT_TIMEOUT_MS = 15000;", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
        Assert.Contains("new Error(\"timed out\")", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:50-56,76-152,218-234, no kill/unref/signal anywhere
    public void McpBridgeSource_NeverKillsOrUnrefsASpawnedChild()
    {
        Assert.DoesNotContain(".kill(", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".unref(", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGTERM", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGKILL", MCPBridge.McpBridgeSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:218-221, servers run in object key order
    public void LoadMCPTools_RunsServersInConfigKeyOrder()
    {
        var servers = McpBridgeScenarios.Remote.Tools.Select(tool => tool.Server).Distinct().ToList();
        Assert.Equal(["normal", "sse", "badnotify"], servers);
    }

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:223-231, a failing server is skipped, the rest continue
    public void LoadMCPTools_SkipsAFailingServerAndKeepsGoingToTheNextOne()
    {
        var result = McpBridgeScenarios.Remote;
        Assert.Contains(result.Tools, tool => tool.Server == "normal");
        Assert.Contains(result.Tools, tool => tool.Server == "sse");
        Assert.Contains(result.Tools, tool => tool.Server == "badnotify");
        Assert.DoesNotContain(result.Tools, tool => tool.Server == "empty");
        Assert.DoesNotContain(result.Tools, tool => tool.Server == "noerrormsg");
        Assert.Contains("droppy mcp: skipping empty: empty response (status 204)", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("droppy mcp: skipping noerrormsg: request failed", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:224-228, neither http nor a local member: ignored
    public void LoadMCPTools_IgnoresAnEntryThatIsNeitherRemoteNorLocal()
    {
        var result = McpBridgeScenarios.Remote;
        Assert.DoesNotContain(result.Tools, tool => tool.Server == "ignored");
        Assert.DoesNotContain("ignored", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-195")] // Services/MCP/MCPBridgeSource.swift:226, a stdio member whose child dies is skipped too
    public void LoadMCPTools_SkipsAStdioServerThatExitsImmediately()
    {
        var result = McpBridgeScenarios.Stdio.Result;
        Assert.DoesNotContain(result.Tools, tool => tool.Server == "stdioCrash");
        Assert.Contains("droppy mcp: skipping stdioCrash: process exited", result.Stderr, StringComparison.Ordinal);
    }

    // ---- dialect: clientInfo, headers, session id, SSE, content mapping (PR-MCP-196) ----

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:138,203; DEVIATION OD-0012 F1
    public void Initialize_SendsClientInfoNameMarrowWithNoVersionField()
    {
        var initialize = McpBridgeScenarios.Remote.HttpRequests.First(r => r.Body!.Value.GetProperty("method").GetString() == "initialize");
        var clientInfo = initialize.Body!.Value.GetProperty("params").GetProperty("clientInfo");
        Assert.Equal("nth", clientInfo.GetProperty("name").GetString());
        Assert.False(clientInfo.TryGetProperty("version", out _));

        var stdioInitialize = McpBridgeScenarios.Stdio.Log.First(entry => entry.GetProperty("method").GetString() == "initialize");
        var stdioClientInfo = stdioInitialize.GetProperty("params").GetProperty("clientInfo");
        Assert.Equal("nth", stdioClientInfo.GetProperty("name").GetString());
        Assert.False(stdioClientInfo.TryGetProperty("version", out _));
    }

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:155, no MCP-Protocol-Version header, and Accept/Content-Type
    public void RemoteRequests_CarryNoProtocolVersionHeaderAndTheDialectsAcceptAndContentType()
    {
        foreach (var request in McpBridgeScenarios.Remote.HttpRequests)
        {
            Assert.False(request.Headers.ContainsKey("mcp-protocol-version"));
            Assert.Equal("application/json", request.Headers["content-type"]);
            Assert.Equal("application/json, text/event-stream", request.Headers["accept"]);
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:154,158, the lowercase session header, adopted and kept
    public void RemoteRequests_AdoptAndRetainTheLowercaseSessionHeader()
    {
        var normal = McpBridgeScenarios.Remote.HttpRequests.Where(r => r.Path == "/normal").ToList();
        // Request 0 is initialize (no session header sent yet); every request after it must carry the session
        // id the initialize response gave, even across the header-less notify and tools/list responses.
        Assert.False(normal[0].Headers.ContainsKey("mcp-session-id"));
        for (var i = 1; i < normal.Count; i++)
        {
            Assert.Equal("sess-normal", normal[i].Headers["mcp-session-id"]);
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:161-172, SSE: result-or-error accepted, [DONE] skipped
    public void RemoteToolsList_AcceptsAnSseFrameJustLikeAPlainJsonOne()
    {
        var sseTools = McpBridgeScenarios.Remote.Tools.Where(tool => tool.Server == "sse").Select(tool => tool.Name).ToList();
        var normalTools = McpBridgeScenarios.Remote.Tools.Where(tool => tool.Server == "normal").Select(tool => tool.Name).ToList();
        Assert.Equal(normalTools, sseTools);
        Assert.Contains(["remote-1", "remote-2", "remote-error"], name => sseTools.Contains(name));
        Assert.Equal(3, sseTools.Count);
    }

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:40-43, describe() falls back to the tool's own name
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:45-48, schemaOf() falls back to {type:"object"}
    public void DescribeAndSchemaOf_FallBackWhenTheServerOmitsThem()
    {
        var bare = McpBridgeScenarios.Stdio.Result.Tools.Single(tool => tool.Name == "tool-bare");
        Assert.Equal("tool-bare", bare.Description);
        Assert.Equal(JsonValueKind.Object, bare.InputSchema.ValueKind);
        Assert.Equal("object", bare.InputSchema.GetProperty("type").GetString());
        Assert.False(bare.InputSchema.TryGetProperty("properties", out _));

        var described = McpBridgeScenarios.Stdio.Result.Tools.Single(tool => tool.Name == "tool");
        Assert.Equal("does the thing", described.Description);
        Assert.True(described.InputSchema.GetProperty("properties").TryGetProperty("x", out _));
    }

    [Fact]
    [Trait("Req", "PR-MCP-196")] // Services/MCP/MCPBridgeSource.swift:35-38,58-74, wrapTools' content-shape mapping
    public void ToolCall_MapsEveryContentShapeTheSameWayOnStdioAndRemote()
    {
        var stdio = McpBridgeScenarios.Stdio.Result.Tools;
        Assert.Equal("called:{\"y\":2}", stdio.Single(t => t.Name == "tool").CallResult);
        Assert.Equal("called:{}", stdio.Single(t => t.Name == "tool-bare").CallResult);
        Assert.Equal("Error: boom", stdio.Single(t => t.Name == "tool-error").CallResult);
        Assert.Equal("Error: tool failed", stdio.Single(t => t.Name == "tool-emptyerror").CallResult);
        Assert.Equal("[image]", stdio.Single(t => t.Name == "tool-image").CallResult);
        Assert.Equal("part1\n[image]", stdio.Single(t => t.Name == "tool-mixed").CallResult);
        Assert.Equal("Error: tool threw", stdio.Single(t => t.Name == "tool-throw").CallResult);

        var remote = McpBridgeScenarios.Remote.Tools;
        Assert.Equal("ok:remote-1", remote.First(t => t is { Server: "normal", Name: "remote-1" }).CallResult);
        Assert.Equal("Error: server refused", remote.First(t => t is { Server: "normal", Name: "remote-error" }).CallResult);
    }

    // ---- connectStdio (:76-152, PR-MCP-197) ----

    [Fact]
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:137-147, the handshake order and empty-params notify/first page
    public void ConnectStdio_HandshakeIsInitializeThenEmptyNotifyThenPagedToolsList()
    {
        var log = McpBridgeScenarios.Stdio.Log;
        var methods = log.Select(entry => entry.GetProperty("method").GetString()).ToList();
        Assert.Equal(["initialize", "notifications/initialized", "tools/list", "tools/list"], methods.Take(4).ToList());

        var notify = log.First(entry => entry.GetProperty("method").GetString() == "notifications/initialized");
        Assert.Equal(JsonValueKind.Object, notify.GetProperty("params").ValueKind);
        Assert.Empty(notify.GetProperty("params").EnumerateObject());

        var firstPage = log.First(entry => entry.GetProperty("method").GetString() == "tools/list");
        Assert.Equal(JsonValueKind.Object, firstPage.GetProperty("params").ValueKind);
        Assert.Empty(firstPage.GetProperty("params").EnumerateObject());
    }

    [Fact]
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:88,119 ids start at 0 and increment
    public void ConnectStdio_MessageIdsStartAtZeroAndIncrement()
    {
        var ids = McpBridgeScenarios.Stdio.Log
            .Where(entry => entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
            .Select(entry => entry.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(0, ids.Count).ToList(), ids);
    }

    [Fact]
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:142-146, tools/list pages until nextCursor is falsy
    public void ConnectStdio_PagesToolsListUntilNextCursorIsFalsy()
    {
        var log = McpBridgeScenarios.Stdio.Log.Where(entry => entry.GetProperty("method").GetString() == "tools/list").ToList();
        Assert.Equal(2, log.Count);
        Assert.False(log[0].GetProperty("params").TryGetProperty("cursor", out _));
        Assert.Equal("page2", log[1].GetProperty("params").GetProperty("cursor").GetString());

        var names = McpBridgeScenarios.Stdio.Result.Tools.Where(t => t.Server == "stdioNormal").Select(t => t.Name).ToList();
        Assert.Contains("tool-page1", names); // from the first page, proving the loop kept it.
        Assert.Contains("tool", names); // from the second (final) page.
    }

    [Fact]
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:81, entry.env is merged into the child's environment
    public void ConnectStdio_MergesTheEntrysEnvIntoTheChild()
    {
        // The fake server's own tool name ("tool") and its two-page behaviour both come only from the
        // config entry's "env" (MCP_FAKE_TOOL, MCP_FAKE_PAGES), so their presence in the result proves the
        // merge reached the spawned child.
        var names = McpBridgeScenarios.Stdio.Result.Tools.Where(t => t.Server == "stdioNormal").Select(t => t.Name).ToList();
        Assert.Contains("tool-page1", names);
        Assert.Contains("tool", names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-197")] // Services/MCP/MCPBridgeSource.swift:100 "process exited"
    public void ConnectStdio_AChildThatExitsFailsWithProcessExited()
    {
        Assert.Contains("droppy mcp: skipping stdioCrash: process exited", McpBridgeScenarios.Stdio.Result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-197")] // windows_path (ED-042): a bare .cmd/.bat command resolves through %COMSPEC% /d /s /c
    [Trait("Decision", "ED-042")]
    public void ConnectStdio_ResolvesABareCmdCommandThroughComspec()
    {
        var result = McpBridgeScenarios.CmdWrapper;
        Assert.Equal("", result.Stderr);
        Assert.Contains(result.Tools, tool => tool.Name == "cmdtool");
        Assert.Contains(result.Tools, tool => tool.Name == "cmdtool-bare");
    }

    // ---- connectRemote and postMessage (:154-216, PR-MCP-198) ----

    [Fact]
    [Trait("Req", "PR-MCP-198")] // Services/MCP/MCPBridgeSource.swift:187-189 ids start at 0 and increment
    public void ConnectRemote_MessageIdsStartAtZeroAndIncrement()
    {
        var normal = McpBridgeScenarios.Remote.HttpRequests.Where(r => r.Path == "/normal" && r.Body!.Value.TryGetProperty("id", out _)).ToList();
        var ids = normal.Select(r => r.Body!.Value.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(0, ids.Count).ToList(), ids);
    }

    [Fact]
    [Trait("Req", "PR-MCP-198")] // Services/MCP/MCPBridgeSource.swift:177, no usable message: "empty response (status <status>)"
    public void ConnectRemote_NoUsableMessage_ThrowsEmptyResponseWithStatus()
    {
        Assert.Contains("droppy mcp: skipping empty: empty response (status 204)", McpBridgeScenarios.Remote.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-198")] // Services/MCP/MCPBridgeSource.swift:192 error member with no message: "request failed"
    public void ConnectRemote_ErrorWithNoMessage_ThrowsRequestFailed()
    {
        Assert.Contains("droppy mcp: skipping noerrormsg: request failed", McpBridgeScenarios.Remote.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-198")] // Services/MCP/MCPBridgeSource.swift:198-201 notify's bare try/catch swallows a failure
    public void ConnectRemote_SwallowsANotificationFailureAndKeepsGoing()
    {
        // "badnotify" answers notifications/initialized with a non-JSON 500 (an "empty response" failure): the
        // server must still finish tools/list normally afterwards, proving notify's own catch{} ate the error.
        var badnotify = McpBridgeScenarios.Remote.Tools.Where(t => t.Server == "badnotify").Select(t => t.Name).ToList();
        Assert.Equal(["remote-1", "remote-2", "remote-error"], badnotify);
        Assert.DoesNotContain("badnotify", McpBridgeScenarios.Remote.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-198")] // Services/MCP/MCPBridgeSource.swift:213 tools/call bound the same way as stdio's
    public void ConnectRemote_ToolsCallSendsNameAndArguments()
    {
        var call = McpBridgeScenarios.Remote.HttpRequests.First(r =>
            r.Path == "/normal" && r.Body!.Value.TryGetProperty("method", out var method) && method.GetString() == "tools/call"
            && r.Body!.Value.GetProperty("params").GetProperty("name").GetString() == "remote-1");
        var arguments = call.Body!.Value.GetProperty("params").GetProperty("arguments");
        Assert.Equal(1, arguments.GetProperty("a").GetInt32());
    }
}
