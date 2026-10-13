using System.Text.Json.Nodes;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.7; ED-NEW-web-w9-2. One emulation call in flight, one latest replacement, no size queue.
public sealed class PreviewEmulation(CdpBroker cdp) : IDisposable
{
    private DeviceMetrics? pending;
    private readonly CancellationTokenSource stop = new();
    private bool running;
    private string? userAgent;
    private bool haveAgent;
    public DeviceMetrics Latest { get; private set; } = new(390, 844, 3, true, true, 1);
    public Task WhenIdle { get; private set; } = Task.CompletedTask;
    public string? Error { get; private set; }
    public void Update(PreviewState state, double width, double height)
    {
        if (stop.IsCancellationRequested) return;
        Latest = state.Metrics(width, height); pending = Latest;
        if (!running) { running = true; WhenIdle = Drain(); }
    }
    private async Task Drain()
    {
        try
        {
            while (pending is { } size && !stop.IsCancellationRequested)
            {
                pending = null;
                await cdp.CallAsync("Emulation.setDeviceMetricsOverride", new JsonObject
                {
                    ["width"] = size.Width, ["height"] = size.Height, ["deviceScaleFactor"] = size.DeviceScaleFactor,
                    ["mobile"] = size.Mobile, ["scale"] = size.Scale,
                }, cancellationToken: stop.Token);
                await cdp.CallAsync("Emulation.setTouchEmulationEnabled", new() { ["enabled"] = size.Touch, ["maxTouchPoints"] = size.Touch ? 5 : 1 }, cancellationToken: stop.Token);
                if (!haveAgent)
                {
                    var version = await cdp.CallAsync("Browser.getVersion", cancellationToken: stop.Token);
                    userAgent = PreviewJson.Text(version, "userAgent"); haveAgent = true;
                }
                // Preserve the engine's version/engine tokens, changing only the platform/mobile tokens.
                var agent = userAgent ?? "";
                if (size.Mobile && agent.Length != 0)
                {
                    var begin = agent.IndexOf('(', StringComparison.Ordinal); var end = agent.IndexOf(')', StringComparison.Ordinal);
                    if (begin >= 0 && end > begin) agent = agent[..begin] + "(Linux; Android 13)" + agent[(end + 1)..];
                    if (size.Width <= 844) agent = agent.Replace(" Safari/", " Mobile Safari/", StringComparison.Ordinal);
                }
                await cdp.CallAsync("Emulation.setUserAgentOverride", new() { ["userAgent"] = agent }, cancellationToken: stop.Token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException) { Error = e.Message; }
        finally { running = false; }
    }
    public void Dispose() { stop.Cancel(); pending = null; }
}
