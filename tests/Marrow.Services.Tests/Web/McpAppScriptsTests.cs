using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Bridge", "Compatibility", "Inline"; 13.1). The MCP Apps bridge script, run for
// real in Node's VM with the browser pieces it touches stubbed (as the W13 script tests do): JSON-RPC the app posts to its
// parent goes to nth through chrome.webview and nothing else does; nth's messages come back as message events from the
// app's own window; window.openai starts the handshake on first use and maps onto the same protocol. The inline widget mount
// serves its page from its own site with no network and answers only the theme. Node is a developer tool here; the real
// engine runs these in the W21 native pass.
[Trait("Decision", "ED-NEW-web-w19-3")]
public sealed class McpAppScriptsTests
{
    private static JsonObject RunNode(string harness, params string[] files)
    {
        var data = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data), "Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var folder = Path.Combine(data!, "McpAppScripts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "harness.cjs");
        File.WriteAllText(script, harness);
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir.Trim('"'), "node.exe")).FirstOrDefault(File.Exists);
        Assert.True(node is not null, "Node.js (node.exe on PATH) runs the sandbox script tests.");
        var start = new ProcessStartInfo(node!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = folder };
        start.ArgumentList.Add(script);
        foreach (var file in files) start.ArgumentList.Add(file);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "node did not finish");
        Assert.True(process.ExitCode == 0, error.Result);
        return JsonNode.Parse(output.Result)!.AsObject();
    }

    private static string Embedded(string name)
    {
        var path = Path.Combine(TestDataDirectory.Read()!, "McpAppScripts", "embedded-" + Guid.NewGuid().ToString("N") + "-" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SandboxScripts.Read(name));
        return path;
    }

    private const string Stubs = """
        const vm = require('vm'); const fs = require('fs');
        const make = (origin) => {
          const toNth = []; const native = []; const events = []; const listeners = []; const frames = [];
          class MessageEvent { constructor(type, init) { this.type = type; Object.assign(this, init); } }
          class CustomEvent { constructor(type, init) { this.type = type; this.detail = init && init.detail; } }
          const win = {
            location: { origin },
            chrome: { webview: { postMessage: (m) => toNth.push(JSON.parse(JSON.stringify(m))), addEventListener: (t, f) => listeners.push(f) } },
            matchMedia: () => ({ matches: false }), navigator: { language: 'en-GB' },
            document: { readyState: 'complete', documentElement: { scrollWidth: 600, scrollHeight: 312 }, body: { scrollHeight: 300 }, addEventListener() {} },
            requestAnimationFrame: (f) => frames.push(f), ResizeObserver: class { observe() {} },
            MessageEvent, CustomEvent, setTimeout,
            dispatchEvent: (e) => { events.push(e); return true; },
          };
          win.postMessage = (m, o) => native.push(m);
          win.window = win; win.top = win; win.parent = win;
          const ctx = vm.createContext(win);
          return { win, ctx, toNth, native, events, frames, deliver: (data) => listeners.forEach((f) => f({ data })), tick: () => frames.splice(0).forEach((f) => f()) };
        };
        """;

    [Fact]
    public void The_bridge_carries_json_rpc_both_ways_and_nothing_else()
    {
        const string harness = Stubs + """
            const bridge = fs.readFileSync(process.argv[2], 'utf8');
            const page = make('https://m-shipyard-0a1b2c3d.invalid');
            vm.runInContext(bridge, page.ctx);
            page.win.parent.postMessage({ jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: { protocolVersion: '2026-01-26' } }, '*');
            page.win.parent.postMessage({ hello: 'a game event' }, '*');
            page.deliver({ jsonrpc: '2.0', id: 1, result: { protocolVersion: '2026-01-26', hostContext: { theme: 'dark' } } });
            page.deliver({ not: 'json-rpc' });
            const event = page.events.find((e) => e.type === 'message');
            const elsewhere = make('https://evil.example');
            vm.runInContext(bridge, elsewhere.ctx);
            elsewhere.win.parent.postMessage({ jsonrpc: '2.0', id: 1, method: 'ui/initialize' }, '*');
            let locked = false; try { page.win.postMessage = () => {}; } catch (_) { } locked = page.win.postMessage.toString().includes('isRpc');
            console.log(JSON.stringify({ toNth: page.toNth, native: page.native, messages: page.events.filter((e) => e.type === 'message').length,
              data: event.data, sameSource: event.source === vm.runInContext('globalThis', page.ctx), origin: event.origin, elsewhereToNth: elsewhere.toNth.length,
              elsewhereNative: elsewhere.native.length, locked }));
            """;
        var result = RunNode(harness, Embedded("mcp-app-bridge.js"));
        var toNth = result["toNth"]!.AsArray();
        Assert.Equal("ui/initialize", Assert.Single(toNth)!["method"]!.GetValue<string>());
        Assert.Equal("a game event", result["native"]![0]!["hello"]!.GetValue<string>());
        Assert.Equal(1, result["messages"]!.GetValue<int>());
        Assert.Equal("dark", result["data"]!["result"]!["hostContext"]!["theme"]!.GetValue<string>());
        Assert.True(result["sameSource"]!.GetValue<bool>());
        Assert.Equal("https://m-shipyard-0a1b2c3d.invalid", result["origin"]!.GetValue<string>());
        Assert.Equal(0, result["elsewhereToNth"]!.GetValue<int>());
        Assert.Equal(1, result["elsewhereNative"]!.GetValue<int>());
        Assert.True(result["locked"]!.GetValue<bool>());
    }

    [Fact]
    public void Window_openai_starts_the_handshake_on_first_use_and_maps_onto_the_same_protocol()
    {
        const string harness = Stubs + """
            const bridge = fs.readFileSync(process.argv[2], 'utf8');
            (async () => {
              const page = make('https://m-shipyard-0a1b2c3d.invalid');
              vm.runInContext(bridge, page.ctx);
              const before = page.toNth.length;
              const openai = page.win.openai;
              const firstInput = openai.toolInput;
              const init = page.toNth[0];
              page.deliver({ jsonrpc: '2.0', id: init.id, result: { protocolVersion: '2026-01-26', hostContext: { theme: 'dark', displayMode: 'inline', containerDimensions: { maxHeight: 540 } } } });
              await new Promise((r) => setTimeout(r, 5));
              page.tick();
              page.deliver({ jsonrpc: '2.0', method: 'ui/notifications/tool-input', params: { arguments: { stage: 'production' } } });
              page.deliver({ jsonrpc: '2.0', method: 'ui/notifications/tool-result', params: { content: [], structuredContent: { count: 3 } } });
              const call = openai.callTool('promote_release', { version: 'v2.4.1' });
              const ask = page.toNth[page.toNth.length - 1];
              page.deliver({ jsonrpc: '2.0', id: ask.id, result: { structuredContent: { ok: true } } });
              const answer = await call;
              const follow = openai.sendFollowUpMessage({ prompt: 'Promote it' });
              const message = page.toNth[page.toNth.length - 1];
              page.deliver({ jsonrpc: '2.0', id: message.id, error: { code: -32000, message: 'The user did not allow messages from shipyard.' } });
              const refused = await follow.then(() => 'sent', (e) => e.message);
              const mode = openai.requestDisplayMode({ mode: 'fullscreen' });
              const modeAsk = page.toNth[page.toNth.length - 1];
              const ownIdsOnly = page.events.filter((e) => e.type === 'message').every((e) => !String(e.data.id || '').startsWith('nth-openai-'));
              console.log(JSON.stringify({ before, firstInput, init, sent: page.toNth.map((m) => m.method || 'reply'),
                theme: openai.theme, maxHeight: openai.maxHeight, toolInput: openai.toolInput, toolOutput: openai.toolOutput,
                ask, answer, message, refused, modeAsk, ownIdsOnly, globals: page.events.filter((e) => e.type === 'openai:set_globals').length }));
            })().catch((e) => { console.error(e.stack); process.exit(1); });
            """;
        var result = RunNode(harness, Embedded("mcp-app-bridge.js"));
        Assert.Equal(0, result["before"]!.GetValue<int>()); // an Apps SDK page that never touches window.openai starts nothing
        Assert.Equal("ui/initialize", result["init"]!["method"]!.GetValue<string>());
        Assert.StartsWith("nth-openai-", result["init"]!["id"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["ui/initialize", "ui/notifications/initialized", "ui/notifications/size-changed", "tools/call", "ui/message", "ui/request-display-mode"],
            result["sent"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Equal("dark", result["theme"]!.GetValue<string>());
        Assert.Equal(540, result["maxHeight"]!.GetValue<int>());
        Assert.Equal("production", result["toolInput"]!["stage"]!.GetValue<string>());
        Assert.Equal(3, result["toolOutput"]!["count"]!.GetValue<int>());
        Assert.Equal("promote_release", result["ask"]!["params"]!["name"]!.GetValue<string>());
        Assert.True(result["answer"]!["structuredContent"]!["ok"]!.GetValue<bool>());
        Assert.Equal("Promote it", result["message"]!["params"]!["content"]!["text"]!.GetValue<string>());
        Assert.Equal("user", result["message"]!["params"]!["role"]!.GetValue<string>());
        Assert.Equal("The user did not allow messages from shipyard.", result["refused"]!.GetValue<string>());
        Assert.Equal("fullscreen", result["modeAsk"]!["params"]!["mode"]!.GetValue<string>());
        Assert.True(result["ownIdsOnly"]!.GetValue<bool>());
        Assert.True(result["globals"]!.GetValue<int>() >= 3);
    }

    [Fact]
    public void The_widget_reports_its_height_once_per_change()
    {
        const string harness = Stubs + """
            const page = make('https://w-board.invalid');
            vm.runInContext(fs.readFileSync(process.argv[2], 'utf8'), page.ctx);
            page.tick(); page.tick();
            page.win.document.documentElement.scrollHeight = 480;
            page.win.requestAnimationFrame(() => {}); page.tick();
            const elsewhere = make('https://m-shipyard-0a1b2c3d.invalid');
            vm.runInContext(fs.readFileSync(process.argv[2], 'utf8'), elsewhere.ctx);
            elsewhere.tick();
            console.log(JSON.stringify({ sent: page.toNth, elsewhere: elsewhere.toNth.length }));
            """;
        var result = RunNode(harness, Embedded("widget-size.js"));
        var sent = result["sent"]!.AsArray();
        Assert.Equal("size", Assert.Single(sent)!["type"]!.GetValue<string>());
        Assert.Equal(312, sent[0]!["height"]!.GetValue<int>());
        Assert.Equal(0, result["elsewhere"]!.GetValue<int>());
    }

    [Fact]
    public void An_inline_widget_has_its_own_site_no_network_and_only_the_theme()
    {
        using var web = new WebRig();
        var view = web.OpenLive(WebSurfaceKind.InlineWidget, url: null);
        var heights = new List<double>();
        var theme = new ArtifactTheme(true, new Dictionary<string, string> { ["--nth-text"] = "#ECECEE", ["--other"] = "x" });
        using var widget = new InlineWidgetMount(view, "board-1", "<h1>Score</h1>", ArtifactRuntimeRig.Libraries, () => theme);
        widget.SizeRequested += heights.Add;
        var fake = WebRig.Fake(view);

        Assert.Equal(SandboxSites.Origin(SandboxSites.WidgetHost("board-1")) + "/", fake.Url);
        Assert.Equal([.. SandboxScripts.DocumentScripts, SandboxScripts.WidgetSize], fake.DocumentScripts);
        var page = fake.Request(widget.Address, WebResourceKind.Document)!;
        Assert.Equal(200, page.StatusCode);
        Assert.Contains("<h1>Score</h1>", Encoding.UTF8.GetString(page.Body), StringComparison.Ordinal);
        Assert.Equal(CspBuilder.Artifact(), page.Headers.First(h => h.Key == "Content-Security-Policy").Value);
        Assert.Equal(403, fake.Request("https://api.example.com/x", WebResourceKind.Fetch)!.StatusCode);
        Assert.Equal(200, fake.Request("https://" + SandboxSites.LibraryHost + "/nth/loader.js", WebResourceKind.Script)!.StatusCode);

        fake.RaiseMessage(widget.Origin, """{"nth":1,"s":"doc","type":"size","height":312}""");
        fake.RaiseMessage("https://evil.example", """{"nth":1,"s":"doc","type":"size","height":900}""");
        Assert.Equal([312d], heights);

        fake.RaiseMessage(widget.Origin, """{"nth":1,"s":"doc","id":1,"type":"theme.get"}""");
        fake.RaiseMessage(widget.Origin, """{"nth":1,"s":"doc","id":2,"type":"send","text":"hi"}""");
        fake.RaiseMessage(widget.Origin, """{"nth":1,"s":"doc","id":3,"type":"storage.get","key":"k"}""");
        var replies = fake.Log.Where(l => l.StartsWith("Post ", StringComparison.Ordinal)).Select(l => JsonNode.Parse(l[5..])!).ToList();
        Assert.True(replies[0]!["ok"]!.GetValue<bool>());
        Assert.Equal("#ECECEE", replies[0]!["result"]!["variables"]!["--nth-text"]!.GetValue<string>());
        Assert.Null(replies[0]!["result"]!["variables"]!["--other"]);
        Assert.All(replies.Skip(1), r => Assert.Equal(ArtifactBridgeErrors.Unavailable, r!["error"]!["code"]!.GetValue<string>()));
        Assert.Equal(3, replies.Count);
    }
}
