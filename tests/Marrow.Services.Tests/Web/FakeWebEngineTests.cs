using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 13.2: FakeWebEngine records CDP calls and scripts events (navigation, permission, download, console, network,
// new window, process failure), and returns canned snapshots. Every later web unit's service tests stand on this
// contract, so it is pinned here through the surface the panes use.
[Trait("Decision", "ED-NEW-web-w7-1")]
[Trait("Feature", "W-7")]
public sealed class FakeWebEngineTests
{
    [Fact]
    public void Cdp_calls_are_recorded_in_order_with_their_session()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);

        WebRig.Done(view.Cdp!.CallAsync("Page.enable"));
        WebRig.Done(view.Cdp!.CallAsync("DOM.getDocument", new JsonObject { ["depth"] = 1 }, sessionId: "frame-1"));

        Assert.Equal(
            [("Page.enable", "{}", (string?)null), ("DOM.getDocument", "{\"depth\":1}", "frame-1")],
            fake.CdpCalls);
    }

    [Fact]
    public void Scripted_events_reach_the_surface_the_panes_listen_to()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        var seen = new List<string>();
        view.NavigationStarting += (_, e) => seen.Add($"starting {e.Url}");
        view.NavigationCompleted += (_, e) => seen.Add($"completed {e.Url} {e.HttpStatusCode}");
        view.PermissionRequested += (_, e) => seen.Add($"permission {e.Origin} {e.Kind}");
        view.DownloadStarting += (_, e) => seen.Add($"download {e.SuggestedFileName}");
        view.NewWindowRequested += (_, e) => seen.Add($"window {e.Url}");
        rig.Surface.StepFailed += (_, reason) => seen.Add($"step {reason}");
        using var console = view.Cdp!.Subscribe("Runtime.consoleAPICalled", e => seen.Add($"console {e.ParamsJson}"));
        using var network = view.Cdp!.Subscribe("Network.requestWillBeSent", e => seen.Add($"network {e.ParamsJson}"));
        using var step = view.BeginModelStep();

        fake.RaiseNavigationStarting("https://b.test/");
        fake.RaiseNavigationCompleted("https://b.test/", status: 200);
        fake.RaisePermission("https://b.test", WebPermissionKind.Camera);
        fake.RaiseDownload("https://b.test/f.zip", "f.zip");
        fake.EmitCdp("Runtime.consoleAPICalled", "{\"type\":\"log\"}");
        fake.EmitCdp("Network.requestWillBeSent", "{\"requestId\":\"1\"}");
        fake.RaiseNewWindow("https://c.test/");
        fake.RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);

        Assert.Equal(
            [
                "starting https://b.test/",
                "completed https://b.test/ 200",
                "permission https://b.test Camera",
                "download f.zip",
                "console {\"type\":\"log\"}",
                "network {\"requestId\":\"1\"}",
                "window https://c.test/",
                "step page crashed",
            ],
            seen);
        Assert.Equal(WebFailureKind.RendererCrashed, view.Failure?.Kind);
    }

    [Fact]
    public void Captures_return_a_canned_frame_sized_to_the_view_unless_the_test_supplies_one()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        view.SetBounds(640, 360, 1.5);

        var frame = WebRig.Done(fake.CaptureSnapshotAsync(CancellationToken.None));
        Assert.NotNull(frame);
        Assert.Equal((960, 540, 1.5), (frame.PixelWidth, frame.PixelHeight, frame.Scale));
        Assert.Equal(FakeWebEngine.OnePixelPng, frame.Png);
        Assert.Equal(WebSnapshotSource.Fake, frame.Source);

        var custom = frame with { PixelWidth = 10, PixelHeight = 10 };
        rig.Engine.SnapshotFactory = _ => custom;
        Assert.Same(custom, WebRig.Done(fake.CaptureSnapshotAsync(CancellationToken.None)));
        Assert.Equal(2, fake.Captures);
    }
}
