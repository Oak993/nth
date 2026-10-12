using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-w9-2")]
public sealed class PreviewDiagnosticsTests
{
    [Fact]
    public async Task Events_are_bounded_redacted_searchable_and_disposed()
    {
        var engine = new FakeWebEngine();
        var view = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\fake", "en-US").For(WebSurfaceKind.PreviewTab));
        using var cdp = new CdpBroker(view);
        using var diagnostics = new PreviewDiagnostics(cdp);
        await diagnostics.StartAsync();
        for (var i = 0; i < 2005; i++) view.EmitCdp("Runtime.consoleAPICalled", "{\"type\":\"error\",\"args\":[{\"value\":\"oops\"}]}");
        Assert.Equal(2000, diagnostics.Console.Count); Assert.Equal(2000, diagnostics.Errors);
        view.EmitCdp("Network.requestWillBeSent", "{\"requestId\":\"1\",\"timestamp\":1,\"request\":{\"url\":\"http://localhost/api\",\"method\":\"GET\",\"headers\":{\"Authorization\":\"fake-secret\",\"Cookie\":\"fake-cookie\"}}}");
        view.EmitCdp("Network.responseReceived", "{\"requestId\":\"1\",\"timestamp\":1.05,\"type\":\"Fetch\",\"response\":{\"status\":404,\"headers\":{\"Set-Cookie\":\"fake-cookie\"}}}");
        var request = Assert.Single(diagnostics.Network);
        Assert.Equal(404, request.Status); Assert.Equal("/api", request.Path);
        Assert.DoesNotContain("fake-secret", diagnostics.ToChat(request)); Assert.DoesNotContain("fake-cookie", diagnostics.ToChat(request));
        Assert.Contains("[redacted]", diagnostics.ToChat(request));
        Assert.Single(diagnostics.FindNetwork("api"));
        diagnostics.Clear(); Assert.Empty(diagnostics.Console);
        diagnostics.Dispose(); view.EmitCdp("Runtime.exceptionThrown", "{}"); Assert.Empty(diagnostics.Console);
    }

    [Fact]
    public async Task Device_calls_are_coalesced_while_the_readout_tracks_every_size()
    {
        var engine = new FakeWebEngine();
        var view = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\fake", "en-US").For(WebSurfaceKind.PreviewTab));
        using var broker = new CdpBroker(view);
        var held = new TaskCompletionSource<string>();
        view.CdpHandler = (method, _, _) => method == "Emulation.setDeviceMetricsOverride" && view.CdpCalls.Count == 1 ? held.Task : Task.FromResult("{}");
        using var device = new PreviewEmulation(broker);
        device.Update(new PreviewState { Preset = DevicePreset.Responsive, Width = 390 }, 900, 900);
        for (var width = 400; width < 500; width++) device.Update(new PreviewState { Preset = DevicePreset.Responsive, Width = width }, 900, 900);
        Assert.Equal(499, device.Latest.Width); Assert.Single(view.CdpCalls);
        held.SetResult("{}"); await device.WhenIdle;
        Assert.Equal(2, view.CdpCalls.Count(x => x.Method == "Emulation.setDeviceMetricsOverride"));
        Assert.Contains("499", view.CdpCalls.Last(x => x.Method == "Emulation.setDeviceMetricsOverride").Params);
    }

    [Fact]
    public async Task Network_ring_failures_response_preview_and_preserve_log_are_per_tab()
    {
        using var rig = new WebRig();
        var surface = rig.OpenLive(WebSurfaceKind.PreviewTab);
        var view = WebRig.Fake(surface);
        using var diagnostics = new PreviewDiagnostics(surface.Cdp!);
        await diagnostics.StartAsync();
        for (var i = 0; i < 1005; i++)
            view.EmitCdp("Network.requestWillBeSent", "{\"requestId\":\"" + i + "\",\"request\":{\"url\":\"http://localhost/\",\"method\":\"GET\"}}");
        Assert.Equal(1000, diagnostics.Network.Count); Assert.Equal("5", diagnostics.Network[0].Id);
        view.EmitCdp("Network.loadingFailed", "{\"requestId\":\"1004\",\"errorText\":\"net::ERR_CONNECTION_REFUSED\"}");
        Assert.True(diagnostics.Network[^1].Failed);
        view.CdpHandler = (_, _, _) => Task.FromResult("{\"body\":\"Cookie: fake-cookie\\r\\nAuthorization: fake-secret\",\"base64Encoded\":false}");
        var body = await diagnostics.ResponsePreviewAsync(diagnostics.Network[^1]);
        Assert.DoesNotContain("fake-secret", body); Assert.DoesNotContain("fake-cookie", body);
        diagnostics.PreserveLog = true; diagnostics.Navigation(); Assert.Equal(1000, diagnostics.Network.Count);
        diagnostics.PreserveLog = false; diagnostics.Navigation(); Assert.Empty(diagnostics.Network);
        view.EmitCdp("Runtime.exceptionThrown", "{\"exceptionDetails\":{\"text\":\"Uncaught\",\"exception\":{\"description\":\"TypeError: fixture\"}}}");
        Assert.Contains("TypeError", Assert.Single(diagnostics.FindConsole("fixture", "error")).Text);
    }

    [Fact]
    public async Task Element_pick_is_a_context_draft_with_styles_html_box_and_screenshot_never_a_model_send()
    {
        using var rig = new WebRig();
        var surface = rig.OpenLive(WebSurfaceKind.PreviewTab);
        var view = WebRig.Fake(surface);
        view.CdpHandler = (method, _, _) => Task.FromResult(method switch
        {
            "DOM.describeNode" => "{\"node\":{\"nodeId\":7,\"localName\":\"div\",\"attributes\":[\"id\",\"hero\"]}}",
            "DOM.getOuterHTML" => "{\"outerHTML\":\"<div id='hero'>fixture</div>\"}",
            "CSS.getComputedStyleForNode" => "{\"computedStyle\":[{\"name\":\"width\",\"value\":\"390px\"}]}",
            "DOM.getBoxModel" => "{\"model\":{\"width\":390,\"height\":100,\"border\":[0,0,390,0,390,100,0,100]}}",
            "Page.captureScreenshot" => "{\"data\":\"" + Convert.ToBase64String(FakeWebEngine.OnePixelPng) + "\"}",
            _ => "{}"
        });
        using var picker = new PreviewElementPicker(surface.Cdp!);
        await picker.SetEnabledAsync(true);
        view.EmitCdp("Overlay.inspectNodeRequested", "{\"backendNodeId\":7}");
        await picker.WhenIdle;
        Assert.Equal("#hero", picker.LastPick!.Selector); Assert.Equal(390, picker.LastPick.Width);
        Assert.Contains("fixture", picker.LastPick.Html); Assert.Contains("390px", picker.LastPick.Styles);
        Assert.Equal(FakeWebEngine.OnePixelPng, picker.LastPick.Screenshot);
        Assert.False(picker.Enabled);
    }
}
