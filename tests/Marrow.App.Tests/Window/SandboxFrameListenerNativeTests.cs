using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window.Native;

// Security review 2026-10-13, finding M5, the adversarial reviewer's residual, the native part (ED-NEW-web-sec-artifacts-8; DESIGN
// 6.5 "WebRTC ... deleted ... before any page script runs", 13.4). Write but NEVER run as an implementing agent: the lead takes
// native.lock and schedules each Fact alone. A hostile page in a real WebView2 (a) reopens a frame's document (document.open, and
// a write or writeln that opens it itself) and reaches the frame it then adds from that frame's own load handler, an inline load
// handler and a written script, then reopens its own document and does the same at the top; (b) connects frames with a src (no load
// event) through the body setter, Range.surroundContents, a select's and its options' add and a table's caption, tHead and tFoot,
// and reads window[n] right after each call. None keeps RTCPeerConnection. Run for the artifact and the inline widget.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-artifacts-8")]
public sealed class SandboxFrameListenerNativeTests(WindowManagerNativeSession native)
{
    private const string Page = """
        <!doctype html><html><head><meta charset="utf-8"><title>reopen</title></head><body><script>
        const r = { frames: {} };
        const rtc = (w) => { try { return w ? typeof w.RTCPeerConnection : 'no frame'; } catch (e) { return 'unreachable'; } };
        window.__take = (name, w) => { r.frames[name] = rtc(w); };
        const frame = () => { const f = document.createElement('iframe'); document.body.appendChild(f); return window[window.length - 1]; };
        const away = (d) => { const f = d.createElement('iframe'); f.src = 'https://evil.example/'; return f; };
        setTimeout(() => {
          { const A = frame(); const d = A.document;
            d.open(); d.write('<body>'); d.close();
            const f = d.createElement('iframe');
            f.addEventListener('load', () => { r.frames.reopened = rtc(A[0]); });
            d.body.appendChild(f); }
          { const B = frame(); B.document.write('<iframe onload="top.__take(\'writeOpens\', window[0])"></iframe>'); }
          { const C = frame(); C.document.writeln('<iframe></iframe><script>top.__take("writelnScript", window[0])<\/script>'); }
          { const D = frame(); const d = D.document; const b = d.createElement('body'); b.appendChild(away(d)); d.body = b; r.frames.bodySetter = rtc(D[0]); }
          { const E = frame(); const d = E.document; const f = d.body.appendChild(away(d));
            const range = d.createRange(); range.selectNode(f); range.surroundContents(d.createElement('div')); r.frames.surroundContents = rtc(E[0]); }
          { const F = frame(); const d = F.document; const select = d.body.appendChild(d.createElement('select'));
            const option = () => { const o = d.createElement('option'); o.appendChild(away(d)); return o; };
            select.add(option()); r.frames.selectAdd = rtc(F[0]);
            select.options.add(option()); r.frames.optionsAdd = rtc(F[1]);
            const table = d.body.appendChild(d.createElement('table'));
            const part = (tag) => { const p = d.createElement(tag); p.appendChild(away(d)); return p; };
            table.caption = part('caption'); r.frames.caption = rtc(F[2]);
            table.tHead = part('thead'); r.frames.tHead = rtc(F[3]);
            table.tFoot = part('tfoot'); r.frames.tFoot = rtc(F[4]); }
          // Last, the page reopens its own document (this empties it) and adds a frame whose load handler takes its constructor.
          document.open(); document.close();
          const f = document.createElement('iframe');
          f.addEventListener('load', () => { r.frames.reopenedTop = rtc(window[0]); });
          document.body.appendChild(f);
          document.title = 'nth-escape:' + JSON.stringify(r);
        }, 0);
        </script></body></html>
        """;

    private static readonly string[] Expected =
        ["reopened", "writeOpens", "writelnScript", "bodySetter", "surroundContents", "selectAdd", "optionsAdd", "caption", "tHead", "tFoot", "reopenedTop"];

    [Fact]
    public void A_reopened_or_unusually_built_artifact_frame_has_no_webrtc_on_the_real_engine() => native.Run(_ => Check("artifact"), factType: typeof(SandboxFrameListenerNativeTests));

    [Fact]
    public void A_reopened_or_unusually_built_inline_widget_frame_has_no_webrtc_on_the_real_engine() => native.Run(_ => Check("widget"), factType: typeof(SandboxFrameListenerNativeTests));

    private void Check(string surface)
    {
        var rig = native.OnUi(() => new SandboxEscapeNativeRig(native, surface, Page));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report(0)) is not null, "The page never reported.", 30000);
            var frames = JsonNode.Parse(native.OnUi(() => rig.Report(0))!)!["frames"]!.AsObject();
            foreach (var name in Expected)
                Assert.True(frames[name]?.GetValue<string>() == "undefined", $"{surface}: the {name} frame kept WebRTC ({frames[name]})");
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }
}
