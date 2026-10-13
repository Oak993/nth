using System.Diagnostics;
using System.Text.Json.Nodes;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review 2026-10-13, finding M5, the adversarial reviewer's residual (ED-NEW-web-sec-artifacts-8;
// DESIGN.md 6.5 "WebRTC ... deleted ... before any page script runs", 13.4). The hardening script, run for real in Node's VM over a
// small live DOM per realm that keeps the HTML Standard's order: a frame with no src fires its load event during its insertion
// (the document's capture listeners first, then the frame's own handlers), a frame with a src does not; document.open, and a write
// or writeln on a document no parser has open, erase every listener on the document before any markup is parsed.
// (a) After the page reopens a document, at the top or in a frame, a frame it adds there is hardened before the page's own load
// handler, inline handler or written script can reach it; the page's own addEventListener cannot keep nth's listener away; a write
// into a document that is already open never reopens (wipes) it. (b) A frame that the body setter, Range.surroundContents, a
// select's or its options' add, or a table's caption, tHead or tFoot setter connects (with a src: no load event) is hardened
// before the call returns.
[Trait("Decision", "ED-NEW-web-sec-artifacts-8")]
public sealed class SandboxFrameListenerScriptTests
{
    private const string Harness = """
        const vm = require('vm'); const fs = require('fs');
        const source = fs.readFileSync(process.argv[2], 'utf8');
        const nth = 'https://a-0123456789-weather.invalid';
        function DOMException(m, n) { const e = Reflect.construct(Error, [m], new.target || DOMException); e.name = n; return e; }
        Object.setPrototypeOf(DOMException.prototype, Error.prototype);
        const makeRealm = (origin, ancestors) => {
          const w = { length: 0, location: { origin, ancestorOrigins: ancestors }, DOMException, handlers: {} };
          const appear = (frame) => { w[w.length] = frame; w.length += 1; return frame; };
          w.child = () => makeRealm(origin, [origin].concat(ancestors || []));
          function MutationObserver(f) { this.f = f; }
          MutationObserver.prototype.observe = function (target) { this.target = target; };
          w.MutationObserver = MutationObserver;
          for (const name of ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel']) w[name] = function () {};
          w.open = () => 'a window';
          w.EventTarget = function () {};
          w.EventTarget.prototype = {
            addEventListener(type, f, capture) {
              const list = this.listeners || (this.listeners = []);
              if (!list.some((l) => l.type === type && l.f === f && l.capture === !!capture)) list.push({ type, f, capture: !!capture });
            },
          };
          const load = (target) => {
            const doc = w.document;
            for (const l of (doc.listeners || []).slice()) if (l.type === 'load' && l.capture) l.f.call(doc, { type: 'load', target });
            for (const l of (target.listeners || []).slice()) if (l.type === 'load') l.f.call(target, { type: 'load', target });
            if (typeof target.onload === 'function') target.onload({ type: 'load', target });
          };
          // A node becomes connected: a frame gets its realm in this window (window[n]); one with no src loads at once.
          const connect = (node) => {
            node.connected = true;
            if (node.tagName === 'IFRAME' && !node.inner) { node.inner = appear(w.child()); if (!node.src) load(node); }
            for (const c of node.children) connect(c);
          };
          const insert = (parent, node) => { parent.children.push(node); node.parent = parent; if (parent.connected) connect(node); return node; };
          w.Node = function () {}; w.Node.prototype = Object.create(w.EventTarget.prototype);
          w.Node.prototype.appendChild = function (node) { return insert(this, node); };
          w.Element = function () {}; w.Element.prototype = Object.create(w.Node.prototype);
          w.HTMLIFrameElement = function () {}; w.HTMLIFrameElement.prototype = Object.create(w.Element.prototype);
          Object.defineProperty(w.HTMLIFrameElement.prototype, 'contentWindow', { configurable: true, get() { return this.inner || null; } });
          w.HTMLSelectElement = function () {}; w.HTMLSelectElement.prototype = Object.create(w.Element.prototype);
          w.HTMLSelectElement.prototype.add = function (option) { insert(this, option); };
          w.HTMLOptionsCollection = function () {};
          w.HTMLOptionsCollection.prototype = { add(option) { insert(this.select, option); } };
          w.HTMLTableElement = function () {}; w.HTMLTableElement.prototype = Object.create(w.Element.prototype);
          for (const name of ['caption', 'tHead', 'tFoot']) {
            Object.defineProperty(w.HTMLTableElement.prototype, name, { configurable: true, enumerable: true, get() { return this['_' + name] || null; }, set(v) { this['_' + name] = v; insert(this, v); } });
          }
          // surroundContents empties the new parent, inserts it, and moves the range's nodes into it: a frame among them is
          // disconnected (its realm is gone) and connected again (a new realm).
          const remove = (node) => { const p = node.parent; if (p) p.children.splice(p.children.indexOf(node), 1); node.parent = null; node.connected = false; node.inner = null; };
          w.Range = function () {};
          w.Range.prototype = {
            surroundContents(parent) { const moved = this.nodes.slice(); moved.forEach(remove); parent.children = []; insert(this.container, parent); moved.forEach((n) => insert(parent, n)); },
            insertNode(node) { insert(this.container, node); },
          };
          const element = (tag) => {
            const proto = { IFRAME: w.HTMLIFrameElement, SELECT: w.HTMLSelectElement, TABLE: w.HTMLTableElement }[tag] || w.Element;
            const e = Object.create(proto.prototype);
            e.tagName = tag; e.children = []; e.connected = false;
            if (tag === 'SELECT') { e.options = Object.create(w.HTMLOptionsCollection.prototype); e.options.select = e; }
            return e;
          };
          // The document: open erases every listener and empties it; a write on a document no parser has open opens it first.
          const reopen = (doc) => { doc.listeners = []; doc.parserOpen = true; doc.bodyElement = element('BODY'); doc.bodyElement.connected = true; return doc; };
          const write = (doc, text) => {
            if (!doc.parserOpen) reopen(doc);
            for (const m of String(text).matchAll(/<iframe( src)?( onload=(\w+))?>|<script>(\w+)<\/script>/g)) {
              if (m[4]) { w.handlers[m[4]](); continue; }
              const frame = element('IFRAME');
              if (m[1]) frame.src = 'https://evil.example/';
              if (m[3]) frame.onload = w.handlers[m[3]];
              insert(doc.bodyElement, frame);
            }
          };
          w.Document = function () {}; w.Document.prototype = Object.create(w.Node.prototype);
          Object.assign(w.Document.prototype, {
            createElement(tag) { return element(String(tag).toUpperCase()); },
            open() { return reopen(this); },
            write(...parts) { write(this, parts.join('')); },
            writeln(...parts) { write(this, parts.join('') + '\n'); },
            close() { this.parserOpen = false; },
          });
          Object.defineProperty(w.Document.prototype, 'body', { configurable: true, enumerable: true, get() { return this.bodyElement; }, set(b) { this.bodyElement = b; connect(b); } });
          w.document = Object.create(w.Document.prototype);
          w.document.nodeType = 9; w.document.defaultView = w; w.document.connected = true; w.document.parserOpen = false;
          w.document.bodyElement = element('BODY'); w.document.bodyElement.connected = true;
          w.window = w;
          return w;
        };
        const rtc = (w) => (w ? typeof w.RTCPeerConnection : 'no frame');
        const boot = () => { const top = makeRealm(nth, [nth]); vm.runInContext(source, vm.createContext(top)); return top; };
        // A frame the page adds at the top (hardened by the hooked appendChild), the realm the bypasses then work in.
        const frameIn = (top) => { const f = top.document.createElement('iframe'); top.document.body.appendChild(f); return top[top.length - 1]; };
        const s = {};

        // (a) The reviewer's bypass, in a frame: reopen its document, then add a frame whose load handler takes its constructor.
        { const top = boot(); const A = frameIn(top);
          A.document.open(); A.document.write('<body>'); A.document.close();
          let got = 'never ran';
          const f = A.document.createElement('iframe');
          f.addEventListener('load', () => { got = rtc(A[0]); });
          A.document.body.appendChild(f);
          s.reopenedFrameLoadHandler = got; }
        // The same at the top: the page reopens its own document.
        { const top = boot();
          top.document.open(); top.document.close();
          let got = 'never ran';
          const f = top.document.createElement('iframe');
          f.addEventListener('load', () => { got = rtc(top[0]); });
          top.document.body.appendChild(f);
          s.reopenedTopLoadHandler = got; }
        // A write that opens the document itself, with an inline load handler in its markup.
        { const top = boot(); const A = frameIn(top);
          let got = 'never ran';
          A.handlers.take = () => { got = rtc(A[0]); };
          A.document.write('<iframe onload=take>');
          s.writeOpensInlineHandler = got; }
        // A writeln that opens the document itself, with a script after the frame in its markup.
        { const top = boot(); const A = frameIn(top);
          let got = 'never ran';
          A.handlers.take = () => { got = rtc(A[0]); };
          A.document.writeln('<iframe><script>take</script>');
          s.writelnOpensScript = got; }
        // The page's own addEventListener in that realm, made a no-op before it reopens: nth's listener is added all the same.
        { const top = boot(); const A = frameIn(top);
          A.EventTarget.prototype.addEventListener = function () {};
          A.document.open(); A.document.close();
          let got = 'never ran';
          const f = A.document.createElement('iframe');
          f.onload = () => { got = rtc(A[0]); };
          A.document.body.appendChild(f);
          s.pageAddEventListener = got; }
        // A write into a document a parser already has open adds to it and never reopens (empties) it.
        { const top = boot(); const A = frameIn(top);
          A.document.open(); A.document.write('<iframe>'); A.document.write('<iframe>'); A.document.close();
          s.keptWrites = A.document.body.children.length;
          s.keptFrames = [rtc(A[0]), rtc(A[1])].join(' ');
          s.oneListener = (A.document.listeners || []).filter((l) => l.type === 'load').length; }

        // (b) Insertions with no hook before: a frame with a src (no load event) is hardened before the call returns.
        { const top = boot();
          const b = top.document.createElement('body'); const f = top.document.createElement('iframe'); f.src = 'https://evil.example/';
          b.appendChild(f);
          top.document.body = b;
          s.bodySetter = rtc(top[0]);
          const A = frameIn(top);
          const b2 = A.document.createElement('body'); const g = A.document.createElement('iframe'); g.src = 'https://evil.example/';
          b2.appendChild(g);
          A.document.body = b2;
          s.nestedBodySetter = rtc(A[0]); }
        { const top = boot();
          const f = top.document.createElement('iframe'); f.src = 'https://evil.example/';
          top.document.body.appendChild(f);
          const range = Object.create(top.Range.prototype); range.container = top.document.body; range.nodes = [f];
          range.surroundContents(top.document.createElement('div'));
          s.surroundContents = rtc(top[top.length - 1]); }
        { const top = boot();
          const select = top.document.createElement('select'); top.document.body.appendChild(select);
          const made = () => { const o = top.document.createElement('option'); const f = top.document.createElement('iframe'); f.src = 'https://evil.example/'; o.appendChild(f); return o; };
          select.add(made()); s.selectAdd = rtc(top[0]);
          select.options.add(made()); s.optionsAdd = rtc(top[1]);
          const table = top.document.createElement('table'); top.document.body.appendChild(table);
          const part = () => { const p = top.document.createElement('caption'); const f = top.document.createElement('iframe'); f.src = 'https://evil.example/'; p.appendChild(f); return p; };
          table.caption = part(); s.caption = rtc(top[2]);
          table.tHead = part(); s.tHead = rtc(top[3]);
          table.tFoot = part(); s.tFoot = rtc(top[4]); }
        console.log(JSON.stringify(s));
        """;

    [Fact]
    public async Task A_reopened_document_keeps_the_frame_load_listener_so_a_frame_added_there_is_hardened_before_the_pages_handlers()
    {
        var s = await RunNode(Harness, Embedded("hardening.js"));

        var reached = new[] { "reopenedFrameLoadHandler", "reopenedTopLoadHandler", "writeOpensInlineHandler", "writelnOpensScript", "pageAddEventListener" }
            .Where(step => s[step]!.GetValue<string>() != "undefined").Select(step => $"{step} ({s[step]})").ToList();
        Assert.True(reached.Count == 0, "The page reached the new frame's WebRTC: " + string.Join(", ", reached));
        Assert.Equal(2, s["keptWrites"]!.GetValue<int>());
        Assert.Equal("undefined undefined", s["keptFrames"]!.GetValue<string>());
        Assert.Equal(1, s["oneListener"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_frame_the_body_setter_surroundContents_select_add_or_a_table_part_connects_is_hardened_before_the_call_returns()
    {
        var s = await RunNode(Harness, Embedded("hardening.js"));

        var kept = new[] { "bodySetter", "nestedBodySetter", "surroundContents", "selectAdd", "optionsAdd", "caption", "tHead", "tFoot" }
            .Where(step => s[step]!.GetValue<string>() != "undefined").Select(step => $"{step} ({s[step]})").ToList();
        Assert.True(kept.Count == 0, "The frame kept WebRTC after the call returned: " + string.Join(", ", kept));
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
