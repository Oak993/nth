using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window.Native;

// Security review residual R1, the native part (ED-NEW-web-sec-res-1; DESIGN 6.5 "WebRTC: blocked for the artifacts
// profile", 13.4). Write but NEVER run as an implementing agent: the lead takes native.lock and schedules each Fact alone.
// A hostile page in a real WebView2, as an
// artifact and as an inline widget, takes RTCPeerConnection from new frames through the paths the hardening script cannot hook
// (an iframe inside an option put in place by the indexed setters select[i] = option and select.options[i] = option, and a script
// inside the very insertion that adds a frame with a src), then gathers ICE candidates with each constructor it got. The engine
// layer must hold even where the script layer lost: no UDP candidate (no host, no server-reflexive) from any of them, because the
// sandboxed views run in their own environment whose browser arguments block non-proxied UDP. The sandbox environment lives in
// its own folder (WebView2\Sandbox) and Visus's folder is never touched by a sandboxed view.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-res-1")]
public sealed class SandboxWebRtcEngineNativeTests(WindowManagerNativeSession native)
{
    private const string Hostile = """
        <!doctype html><html><head><meta charset="utf-8"><title>webrtc</title></head><body><script>
        (async () => {
          const r = { paths: {} };
          const grab = (w) => { try { return w.RTCPeerConnection || null; } catch (e) { return null; } };
          const gather = async (Ctor) => {
            if (typeof Ctor !== 'function') return { ctor: typeof Ctor, candidates: [] };
            const out = { ctor: 'function', candidates: [] };
            try {
              const pc = new Ctor({ iceServers: [] });
              pc.createDataChannel('x');
              const done = new Promise((ok) => {
                pc.onicecandidate = (e) => { if (e.candidate) out.candidates.push(e.candidate.candidate); else ok(); };
                setTimeout(ok, 5000);
              });
              await pc.setLocalDescription(await pc.createOffer());
              await done;
              pc.close();
            } catch (e) { out.error = e.name; }
            return out;
          };
          const select = document.createElement('select');
          document.body.appendChild(select);
          const first = document.createElement('option');
          const one = document.createElement('iframe');
          first.appendChild(one);
          select[0] = first;
          const bySelect = grab(one.contentWindow);
          const second = document.createElement('option');
          const two = document.createElement('iframe');
          second.appendChild(two);
          select.options[1] = second;
          const byOptions = grab(two.contentWindow);
          document.body.appendChild(document.createRange().createContextualFragment(
            '<iframe src="about:blank#nth"></iframe><script>window.nthSrc = (() => { try { return window[window.length - 1].RTCPeerConnection || null; } catch (e) { return null; } })();<\/script>'));
          const bySrc = window.nthSrc;
          r.paths.select = await gather(bySelect);
          r.paths.options = await gather(byOptions);
          r.paths.src = await gather(bySrc);
          document.title = 'nth-escape:' + JSON.stringify(r);
        })();
        </script></body></html>
        """;

    [Fact]
    public void An_artifact_that_keeps_a_frames_webrtc_gathers_no_udp_candidate_on_the_real_engine() => native.Run(_ => Check("artifact"), factType: typeof(SandboxWebRtcEngineNativeTests));

    [Fact]
    public void An_inline_widget_that_keeps_a_frames_webrtc_gathers_no_udp_candidate_on_the_real_engine() => native.Run(_ => Check("widget"), factType: typeof(SandboxWebRtcEngineNativeTests));

    private void Check(string surface)
    {
        var rig = native.OnUi(() => new SandboxEscapeNativeRig(native, surface, Hostile));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report(0)) is not null, "The hostile page never reported.", 45000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report(0))!)!.AsObject();
            foreach (var (path, result) in report["paths"]!.AsObject())
            {
                var candidates = result!["candidates"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
                // The constructor may be gone (the script layer won) or kept (the engine layer must hold): either way no UDP leaves.
                Assert.True(candidates.All(c => !c.Contains(" udp ", StringComparison.OrdinalIgnoreCase)), $"{surface}: the {path} path gathered UDP candidates ({string.Join(" | ", candidates)})");
                Assert.True(candidates.All(c => !c.Contains("typ srflx", StringComparison.Ordinal)), $"{surface}: the {path} path reached a server ({string.Join(" | ", candidates)})");
            }

            var folder = native.OnUi(() => rig.Folder);
            Assert.True(Directory.Exists(Path.Combine(folder, "WebView2", "Sandbox", "EBWebView")), "The sandboxed view did not run in the sandbox environment's folder.");
            Assert.False(Directory.Exists(Path.Combine(folder, "WebView2", "EBWebView")), "A sandboxed view started Visus's environment.");
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }
}
