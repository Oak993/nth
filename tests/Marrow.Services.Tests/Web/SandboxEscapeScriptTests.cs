using System.Diagnostics;
using System.Text.Json.Nodes;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W26 security review, finding F2 (ED-NEW-web-w26-2; DESIGN.md 6.5 "WebRTC is removed", 13.4).
// The hardening script, run for real in Node's VM with the browser pieces it touches stubbed. It hardens a sandboxed
// frame inside nth's sandbox even though that frame's origin is opaque ("null"), and leaves a foreign page's alone. A
// frame it never ran in (an iframe's first about:blank, reached through window[0] rather than contentWindow) loses
// WebRTC and window.open the moment anything can add one: every DOM insertion, an HTML setter, document.write,
// execCommand, a frame's load (before the page's own handler) and each mutation batch, nested frames included, and a
// cross-origin frame neither throws nor stops the rest. The real engine runs the same pages in SandboxEscapeNativeTests.
[Trait("Decision", "ED-NEW-web-w26-2")]
public sealed class SandboxEscapeScriptTests
{
    private const string Harness = """
        const vm = require('vm'); const fs = require('fs');
        const source = fs.readFileSync(process.argv[2], 'utf8');
        const realm = (inner) => {
          const child = { RTCPeerConnection: function () {}, RTCDataChannel: function () {}, open: () => 'child window', navigator: { mediaDevices: {} }, length: 0 };
          if (inner) { child[0] = inner; child.length = 1; }
          return child;
        };
        const foreign = () => new Proxy({}, {
          get() { throw new Error('SecurityError'); }, set() { throw new Error('SecurityError'); },
          defineProperty() { throw new Error('SecurityError'); }, deleteProperty() { throw new Error('SecurityError'); },
        });
        const run = (origin, ancestors) => {
          let observer = null;
          const listeners = [];
          function DOMException(m, n) { const e = Reflect.construct(Error, [m], new.target || DOMException); e.name = n; return e; }
          Object.setPrototypeOf(DOMException.prototype, Error.prototype);
          function MutationObserver(f) { this.f = f; observer = this; }
          MutationObserver.prototype.observe = function () {};
          const frameProto = () => { const p = {}; Object.defineProperty(p, 'contentWindow', { configurable: true, get() { return this.inner; } }); return p; };
          const win = {
            location: { origin, ancestorOrigins: ancestors }, DOMException, MutationObserver,
            RTCPeerConnection: function () {}, webkitRTCPeerConnection: function () {}, RTCDataChannel: function () {},
            open: () => 'a window', navigator: { mediaDevices: {} },
            Navigator: function () {}, Document: function () {}, Element: function () {}, Node: function () {},
            CharacterData: function () {}, DocumentFragment: function () {}, Range: function () {},
            HTMLIFrameElement: function () {}, HTMLFrameElement: function () {}, HTMLObjectElement: function () {},
            length: 0,
          };
          const appear = (frame) => { win[win.length] = frame; win.length += 1; };
          const html = (text) => { if (/<iframe/i.test(String(text))) appear(realm()); };
          win.Navigator.prototype = {};
          win.Node.prototype = {
            appendChild(node) { if (node.frame) appear(node.frame); return node; },
            insertBefore(node) { if (node.frame) appear(node.frame); return node; },
            replaceChild(node) { if (node.frame) appear(node.frame); throw new DOMException('refused', 'HierarchyRequestError'); },
          };
          win.Element.prototype = {
            append(...nodes) { nodes.forEach((n) => n.frame && appear(n.frame)); },
            insertAdjacentHTML(_, text) { html(text); },
          };
          Object.defineProperty(win.Element.prototype, 'innerHTML', { configurable: true, enumerable: true, get() { return this.text || ''; }, set(v) { this.text = v; html(v); } });
          win.CharacterData.prototype = { after(n) { if (n.frame) appear(n.frame); } };
          win.DocumentFragment.prototype = {};
          win.Range.prototype = { insertNode(n) { if (n.frame) appear(n.frame); } };
          win.Document.prototype = { execCommand(c, u, text) { html(text); return true; }, write(text) { html(text); } };
          win.HTMLIFrameElement.prototype = frameProto(); win.HTMLFrameElement.prototype = frameProto(); win.HTMLObjectElement.prototype = frameProto();
          win.document = { nodeType: 9, addEventListener(type, f, capture) { listeners.push({ type, f, capture }); } };
          win.window = win;
          vm.runInContext(source, vm.createContext(win));
          return { win, observer, listeners, appear };
        };
        const rtc = (w) => typeof w.RTCPeerConnection;
        const nth = 'https://a-0123456789-weather.invalid';
        const page = run(nth, [nth]);
        const { win } = page;
        const steps = {};
        const node = { frame: realm() };
        steps.returned = win.Node.prototype.appendChild.call({}, node) === node;
        steps.appendChild = rtc(win[0]);
        steps.childOpen = String(win[0].open());
        win.Node.prototype.insertBefore.call({}, { frame: realm() }, null); steps.insertBefore = rtc(win[1]);
        const element = Object.create(win.Element.prototype);
        element.innerHTML = '<iframe></iframe>'; steps.innerHTML = rtc(win[2]); steps.innerHTMLKept = element.innerHTML;
        element.insertAdjacentHTML('beforeend', '<iframe>'); steps.insertAdjacentHTML = rtc(win[3]);
        element.append({ frame: realm() }); steps.append = rtc(win[4]);
        win.CharacterData.prototype.after.call({}, { frame: realm() }); steps.after = rtc(win[5]);
        win.Range.prototype.insertNode.call({}, { frame: realm() }); steps.insertNode = rtc(win[6]);
        win.Document.prototype.write.call(win.document, '<iframe></iframe>'); steps.write = rtc(win[7]);
        steps.insertHTML = win.Document.prototype.execCommand.call(win.document, 'insertHTML', false, '<iframe></iframe>');
        steps.execCommand = rtc(win[8]);
        steps.copy = win.Document.prototype.execCommand.call(win.document, 'copy');
        const grandchild = realm();
        win.Node.prototype.appendChild.call({}, { frame: realm(grandchild) }); steps.nested = rtc(win[9]) + ' ' + rtc(grandchild);
        try { win.Node.prototype.replaceChild.call({}, { frame: realm() }, {}); steps.threw = 'no'; } catch (e) { steps.threw = e.name; }
        steps.replaceChild = rtc(win[10]);
        page.appear(foreign()); page.appear(realm());
        win.Node.prototype.appendChild.call({}, {}); steps.pastForeign = rtc(win[12]);
        const load = page.listeners.find((l) => l.type === 'load');
        steps.loadCapture = !!(load && load.capture === true);
        page.appear(realm()); load.f({ target: { tagName: 'IFRAME' } }); steps.load = rtc(win[13]);
        page.appear(realm()); page.observer.f([]); steps.mutation = rtc(win[14]);
        const sandboxed = run('null', [nth, nth]).win;
        const sandboxedForeign = run('null', ['https://evil.example']).win;
        const opaqueAlone = run('null', undefined).win;
        const foreignPage = run('https://evil.example', [nth]).win;
        console.log(JSON.stringify({
          steps,
          sandboxed: rtc(sandboxed) + ' ' + String(sandboxed.open()),
          untouched: [rtc(sandboxedForeign), rtc(opaqueAlone), rtc(foreignPage)],
        }));
        """;

    [Fact]
    public async Task Hardening_reaches_sandboxed_frames_and_every_frame_the_page_can_reach()
    {
        var result = await RunNode(Harness, Embedded("hardening.js"));
        var steps = result["steps"]!.AsObject();

        Assert.True(steps["returned"]!.GetValue<bool>());
        foreach (var step in new[] { "appendChild", "insertBefore", "innerHTML", "insertAdjacentHTML", "append", "after", "insertNode", "write", "execCommand", "replaceChild", "pastForeign", "load", "mutation" })
            Assert.True(steps[step]!.GetValue<string>() == "undefined", $"{step} left WebRTC in the new frame");
        Assert.Equal("null", steps["childOpen"]!.GetValue<string>());
        Assert.Equal("<iframe></iframe>", steps["innerHTMLKept"]!.GetValue<string>());
        Assert.True(steps["insertHTML"]!.GetValue<bool>());
        Assert.False(steps["copy"]!.GetValue<bool>());
        Assert.Equal("undefined undefined", steps["nested"]!.GetValue<string>());
        Assert.Equal("HierarchyRequestError", steps["threw"]!.GetValue<string>());
        Assert.True(steps["loadCapture"]!.GetValue<bool>());
        Assert.Equal("undefined null", result["sandboxed"]!.GetValue<string>());
        Assert.Equal(["function", "function", "function"], result["untouched"]!.AsArray().Select(r => r!.GetValue<string>()));
    }

    private static async Task<JsonObject> RunNode(string harness, params string[] files)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // node did not finish
            throw;
        }

        Assert.True(process.ExitCode == 0, await error);
        return JsonNode.Parse(await output)!.AsObject();
    }

    private static string Embedded(string name)
    {
        var data = TestDataDirectory.Read()!;
        var path = Path.Combine(data, "SandboxScripts", "embedded-" + Guid.NewGuid().ToString("N") + "-" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SandboxScripts.Read(name));
        return path;
    }
}
