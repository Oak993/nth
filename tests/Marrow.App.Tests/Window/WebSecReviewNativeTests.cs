using System.Text;
using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Window.Native;

// Security review of the web feature, the real-engine part (ED-NEW-web-sec-rev-3, ED-NEW-web-sec-rev-4). Write but NEVER run as an
// implementing agent: the lead takes native.lock and schedules each Fact alone. A test-owned window and throwaway profile folder,
// the real WebView2 engine, a Visus tab whose every request is answered in memory (no network, no account), in
// WebSecReviewPageRig. Each page reports through its title.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-rev-4")]
public sealed class WebSecReviewNativeTests(WindowManagerNativeSession native)
{
    // S4: Visus passes the WebRTC policy in both spellings, so the installed runtime (154) blocks WebRTC UDP outside a proxy.
    private const string WebRtcPage = """
        <!doctype html><html><head><meta charset="utf-8"><title>webrtc</title></head><body><script>
        (async () => {
          const out = { ctor: typeof RTCPeerConnection, candidates: [] };
          try {
            const pc = new RTCPeerConnection({ iceServers: [] });
            pc.createDataChannel('x');
            const done = new Promise((ok) => {
              pc.onicecandidate = (e) => { if (e.candidate) out.candidates.push(e.candidate.candidate); else ok(); };
              setTimeout(ok, 5000);
            });
            await pc.setLocalDescription(await pc.createOffer());
            await done;
            pc.close();
          } catch (e) { out.error = e.name; }
          document.title = 'nth-sec-rev:' + JSON.stringify(out);
        })();
        </script></body></html>
        """;

    // S3: an exported page whose comment holds "<head>"; its own script tries the network and reports what stopped it.
    private const string CommentTrickPage = """
        <!doctype html><!-- <head> --><html><head><meta charset="utf-8"><title>export</title></head><body><p>x</p><script>
        (async () => {
          const out = { violated: [], fetched: false };
          document.addEventListener('securitypolicyviolation', (e) => out.violated.push(e.effectiveDirective));
          try { await fetch('https://nth-sec-rev-probe.invalid/x'); out.fetched = true; } catch (e) { }
          await new Promise((ok) => setTimeout(ok, 1000));
          const first = document.head.firstElementChild;
          out.first = first ? (first.getAttribute('http-equiv') || first.tagName) : null;
          document.title = 'nth-sec-rev:' + JSON.stringify(out);
        })();
        </script></body></html>
        """;

    [Fact]
    public void A_Visus_tab_gathers_no_udp_candidate_on_the_real_engine() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new WebSecReviewPageRig(native, Encoding.UTF8.GetBytes(WebRtcPage)));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report) is not null, "The WebRTC page never reported.", 45000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report)!)!.AsObject();
            var candidates = report["candidates"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
            Assert.True(candidates.All(c => !c.Contains(" udp ", StringComparison.OrdinalIgnoreCase)), $"Visus gathered UDP candidates ({string.Join(" | ", candidates)})");
            Assert.True(candidates.All(c => !c.Contains("typ srflx", StringComparison.Ordinal)), $"Visus reached a STUN server ({string.Join(" | ", candidates)})");
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    }, factType: typeof(WebSecReviewNativeTests));

    [Fact]
    [Trait("Decision", "ED-NEW-web-sec-rev-3")]
    public void An_exported_page_with_a_head_tag_in_a_comment_keeps_its_policy_on_the_real_engine() => native.Run(_ =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-sec-rev-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = new ArtifactStore(() => folder);
        var snapshot = store.Create(Guid.NewGuid(), new ArtifactDraft { Kind = ArtifactKind.Html, Title = "Export", Content = CommentTrickPage }, new ArtifactAuthor("claude", "sonnet", null), null);
        var exported = new ArtifactExporter(new ArtifactLibraryFiles(ArtifactLibraryFiles.DefaultFolder)).Export(snapshot, ArtifactExportFormat.Html);
        var rig = native.OnUi(() => new WebSecReviewPageRig(native, exported));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report) is not null, "The exported page never reported.", 45000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report)!)!.AsObject();
            Assert.False(report["fetched"]!.GetValue<bool>(), "The exported page reached the network: its policy did not apply.");
            Assert.Contains("connect-src", report["violated"]!.AsArray().Select(v => v!.GetValue<string>()));
            Assert.Equal("Content-Security-Policy", report["first"]!.GetValue<string>());
        }
        finally
        {
            native.OnUi(rig.Dispose);
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
        }
    }, factType: typeof(WebSecReviewNativeTests));
}
