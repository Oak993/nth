using System.Diagnostics;
using System.Text.Json.Nodes;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5; 13.1). The sandbox's own JavaScript, run for real in Node's VM with the
// browser's pieces it touches stubbed: JSX and TSX compile with the vendored Sucrase and run against the vendored React;
// the bridge talks only through chrome.webview with a per-document token, and its shims map onto it; the hardening script
// removes WebRTC and window.open and strips prefetch hints. Node is a developer tool here (porting/tools already needs
// it); the real engine runs these scripts in the W21 native pass.
[Trait("Decision", "ED-NEW-web-w13-4")]
public sealed class SandboxScriptBehaviourTests
{
    private static JsonObject RunNode(string harness, params string[] files)
    {
        var data = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data), "Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var folder = Path.Combine(data!, "SandboxScripts", Guid.NewGuid().ToString("N"));
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
        var data = TestDataDirectory.Read()!;
        var path = Path.Combine(data, "SandboxScripts", "embedded-" + Guid.NewGuid().ToString("N") + "-" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SandboxScripts.Read(name));
        return path;
    }

    private static string Library(string id) =>
        Path.Combine(ArtifactLibraryFiles.DefaultFolder, ArtifactRuntimeRig.Libraries.Catalog.ById(id)!.Script.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Tsx_compiles_with_the_vendored_sucrase_and_runs_against_the_vendored_react()
    {
        const string harness = """
            const vm = require('vm'); const fs = require('fs');
            const [sucrase, react] = process.argv.slice(2);
            const ctx = vm.createContext({});
            ctx.self = ctx; // the browser's global, which React's build reaches through "self"
            vm.runInContext(fs.readFileSync(sucrase, 'utf8'), ctx);
            vm.runInContext(fs.readFileSync(react, 'utf8'), ctx);
            const source = "import { Fragment } from 'react';\ntype Props = { name: string; count?: number };\n" +
              "export default function Hello({ name, count = 2 }: Props) {\n  return <section className=\"card\"><h1>Hi {name}</h1>{[1, 2].map((n) => <i key={n}>{n * count}</i>)}<Fragment /></section>;\n}";
            const compiled = ctx.sucrase.transform(source, { transforms: ['jsx', 'typescript', 'imports'], jsxRuntime: 'automatic', production: true, filePath: 'artifact.tsx' }).code;
            const requires = Array.from(compiled.matchAll(/\brequire\((['"])([^'"\n]+)\1\)/g), (m) => m[2]);
            const React = ctx.React;
            const runtime = { Fragment: React.Fragment, jsx: (t, p, k) => React.createElement(t, k === undefined ? p : Object.assign({}, p, { key: k })) };
            runtime.jsxs = runtime.jsx;
            const mod = { exports: {} };
            const load = (s) => s === 'react' ? React : s === 'react/jsx-runtime' ? runtime : (() => { throw new Error(s); })();
            vm.runInContext('(function (module, exports, require) {\n' + compiled + '\n})', ctx)(mod, mod.exports, load);
            const element = mod.exports.default({ name: 'Orbit' });
            const items = element.props.children[1];
            console.log(JSON.stringify({ requires, type: element.type, className: element.props.className,
              title: element.props.children[0].props.children.join(''), items: items.map((i) => i.props.children), keys: items.map((i) => i.key),
              version: React.version }));
            """;
        var result = RunNode(harness, Library("sucrase"), Library("react"));
        Assert.Equal(["react", "react/jsx-runtime"], result["requires"]!.AsArray().Select(r => r!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal("section", result["type"]!.GetValue<string>());
        Assert.Equal("card", result["className"]!.GetValue<string>());
        Assert.Equal("Hi Orbit", result["title"]!.GetValue<string>());
        Assert.Equal([2, 4], result["items"]!.AsArray().Select(i => i!.GetValue<int>()));
        Assert.Equal(["1", "2"], result["keys"]!.AsArray().Select(i => i!.GetValue<string>()));
        Assert.Equal("18.3.1", result["version"]!.GetValue<string>());
    }

    private const string BridgeStubs = """
        const vm = require('vm'); const fs = require('fs');
        const make = (origin) => {
          const posted = []; const listeners = [];
          const win = {
            location: { origin },
            chrome: { webview: { postMessage: (m) => posted.push(JSON.parse(JSON.stringify(m))), addEventListener: (t, f) => listeners.push(f) } },
            matchMedia: () => ({ matches: false }),
            document: { readyState: 'complete', title: 'Weather', documentElement: { style: { setProperty() {} } }, addEventListener() {}, body: null },
            crypto: { getRandomValues: (a) => { for (let i = 0; i < a.length; i++) a[i] = 1000 + i; return a; } },
            addEventListener() {}, setTimeout, atob: (s) => Buffer.from(s, 'base64').toString('binary'),
            DataTransfer: class {}, DragEvent: class {}, Event: class {}, File: class {}, DOMParser: class {},
            HTMLInputElement: function () {},
          };
          win.window = win; win.top = win; win.HTMLInputElement.prototype = {};
          const ctx = vm.createContext(win);
          return { win, ctx, posted, deliver: (data) => listeners.forEach((f) => f({ data })) };
        };
        """;

    [Fact]
    public void The_bridge_talks_only_through_webview_messages_with_its_document_token()
    {
        const string harness = BridgeStubs + """
            const bridge = fs.readFileSync(process.argv[2], 'utf8');
            (async () => {
              const page = make('https://a-0123456789-weather.invalid');
              vm.runInContext(bridge, page.ctx);
              const nth = page.win.nth;
              const session = page.posted.find((m) => m.type === 'ready').s;
              const pending = nth.storage.get('score');
              const ask = page.posted[page.posted.length - 1];
              let settled = false; pending.then(() => { settled = true; });
              page.deliver({ nth: 1, s: 'an-older-document', id: ask.id, ok: true, result: 'stale' });
              await new Promise((r) => setTimeout(r, 10));
              const ignoredStale = !settled;
              page.deliver({ nth: 1, s: session, id: ask.id, ok: true, result: '42' });
              const value = await pending;
              const limited = nth.complete('again');
              const call = page.posted[page.posted.length - 1];
              page.deliver({ nth: 1, s: session, id: call.id, ok: false, error: { code: 'rate_limited', message: 'Paused' } });
              const code = await limited.then(() => 'resolved', (e) => e.code);
              page.win.storage.set('board', '{}', true);
              const shim = page.posted[page.posted.length - 1];
              page.win.claude.complete('hi');
              const claude = page.posted[page.posted.length - 1];
              try { nth.storage = null; } catch (_) { } const frozen = nth.storage !== null;
              const elsewhere = make('https://evil.example');
              vm.runInContext(bridge, elsewhere.ctx);
              console.log(JSON.stringify({ ready: page.posted[1], first: page.posted[0].type, ask, ignoredStale, value, code, shim, claude,
                isFrozen: Object.isFrozen(nth) && frozen, keys: Object.keys(nth), elsewhere: typeof elsewhere.win.nth, elsewherePosts: elsewhere.posted.length }));
            })().catch((e) => { console.error(e.stack); process.exit(1); });
            """;
        var result = RunNode(harness, Embedded("bridge.js"));
        var session = result["ask"]!["s"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(session));
        Assert.Equal("theme.get", result["first"]!.GetValue<string>());
        Assert.Equal("ready", result["ready"]!["type"]!.GetValue<string>());
        Assert.Equal("storage.get", result["ask"]!["type"]!.GetValue<string>());
        Assert.Equal("score", result["ask"]!["key"]!.GetValue<string>());
        Assert.True(result["ignoredStale"]!.GetValue<bool>());
        Assert.Equal("42", result["value"]!.GetValue<string>());
        Assert.Equal("rate_limited", result["code"]!.GetValue<string>());
        Assert.Equal("shared:board", result["shim"]!["key"]!.GetValue<string>());
        Assert.Equal("storage.set", result["shim"]!["type"]!.GetValue<string>());
        Assert.Equal("complete", result["claude"]!["type"]!.GetValue<string>());
        Assert.Equal("hi", result["claude"]!["messages"]![0]!["content"]!.GetValue<string>());
        Assert.True(result["isFrozen"]!.GetValue<bool>());
        Assert.Equal(["storage", "complete", "callTool", "files", "send", "theme"], result["keys"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal("undefined", result["elsewhere"]!.GetValue<string>());
        Assert.Equal(0, result["elsewherePosts"]!.GetValue<int>());
    }

    [Fact]
    public void Hardening_removes_webrtc_and_windows_and_strips_prefetch_hints()
    {
        const string harness = """
            const vm = require('vm'); const fs = require('fs');
            const run = (origin) => {
              let observer = null;
              class DOMException extends Error { constructor(m, n) { super(m); this.name = n; } }
              class MutationObserver { constructor(f) { this.f = f; observer = this; } observe(target, options) { this.target = target; this.options = options; } }
              const frameProto = () => { const p = {}; Object.defineProperty(p, 'contentWindow', { configurable: true, get() { return this.inner; } }); return p; };
              const win = {
                location: { origin }, DOMException, MutationObserver,
                RTCPeerConnection: function () {}, webkitRTCPeerConnection: function () {}, RTCDataChannel: function () {},
                open: () => 'a window', navigator: { mediaDevices: {}, credentials: {}, share() {}, sendBeacon() { return true; } },
                Navigator: function () {}, Document: function () {}, Element: function () {}, Clipboard: function () {}, Notification: function () {},
                HTMLIFrameElement: function () {}, HTMLFrameElement: function () {}, HTMLObjectElement: function () {},
              };
              win.Navigator.prototype = { share() {}, sendBeacon() { return true; } };
              win.Document.prototype = { execCommand: () => true };
              win.Element.prototype = {};
              win.Clipboard.prototype = { writeText: () => Promise.resolve() };
              win.HTMLIFrameElement.prototype = frameProto(); win.HTMLFrameElement.prototype = frameProto(); win.HTMLObjectElement.prototype = frameProto();
              win.document = { nodeType: 9 };
              win.window = win;
              const ctx = vm.createContext(win);
              vm.runInContext(fs.readFileSync(process.argv[2], 'utf8'), ctx);
              return { win, observer };
            };
            (async () => {
              const { win, observer } = run('https://a-0123456789-weather.invalid');
              const inner = { RTCPeerConnection: function () {}, open: () => 'inner window' };
              const frame = Object.create(win.HTMLIFrameElement.prototype); frame.inner = inner;
              const reached = frame.contentWindow;
              let removed = 0;
              const hint = { nodeType: 1, tagName: 'LINK', getAttribute: (n) => n === 'rel' ? 'preconnect' : null, remove: () => { removed++; } };
              const style = { nodeType: 1, tagName: 'LINK', getAttribute: (n) => n === 'rel' ? 'stylesheet' : null, remove: () => { removed += 100; }, querySelectorAll: () => [] };
              observer.f([{ type: 'childList', addedNodes: [hint, style] }]);
              const fullscreen = await win.Element.prototype.requestFullscreen().then(() => 'allowed', (e) => e.name);
              const clipboard = await win.Clipboard.prototype.writeText('x').then(() => 'allowed', (e) => e.name);
              const plain = run('https://example.com').win;
              console.log(JSON.stringify({
                rtc: [typeof win.RTCPeerConnection, typeof win.webkitRTCPeerConnection, typeof win.RTCDataChannel],
                open: win.open('https://evil.example'), media: typeof win.navigator.mediaDevices, credentials: typeof win.navigator.credentials,
                innerRtc: typeof reached.RTCPeerConnection, innerOpen: reached.open(),
                observed: observer.target === win.document && observer.options.subtree === true, removed,
                copy: win.Document.prototype.execCommand.call(win.document, 'copy'), fullscreen, clipboard,
                beacon: win.Navigator.prototype.sendBeacon(), notification: typeof win.Notification,
                untouched: typeof plain.RTCPeerConnection + ' ' + plain.open() }));
            })().catch((e) => { console.error(e.stack); process.exit(1); });
            """;
        var result = RunNode(harness, Embedded("hardening.js"));
        Assert.Equal(["undefined", "undefined", "undefined"], result["rtc"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Null(result["open"]);
        Assert.Equal("undefined", result["media"]!.GetValue<string>());
        Assert.Equal("undefined", result["credentials"]!.GetValue<string>());
        Assert.Equal("undefined", result["innerRtc"]!.GetValue<string>());
        Assert.Null(result["innerOpen"]);
        Assert.True(result["observed"]!.GetValue<bool>());
        Assert.Equal(1, result["removed"]!.GetValue<int>());
        Assert.False(result["copy"]!.GetValue<bool>());
        Assert.Equal("NotAllowedError", result["fullscreen"]!.GetValue<string>());
        Assert.Equal("NotAllowedError", result["clipboard"]!.GetValue<string>());
        Assert.False(result["beacon"]!.GetValue<bool>());
        Assert.Equal("undefined", result["notification"]!.GetValue<string>());
        Assert.Equal("function a window", result["untouched"]!.GetValue<string>());
    }
}
