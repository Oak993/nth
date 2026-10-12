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
}
