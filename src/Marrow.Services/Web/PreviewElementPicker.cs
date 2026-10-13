using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.9, 8.1; ED-NEW-web-w9-2. Native CDP overlay, picked context, no model invocation.
public sealed record PreviewElementContext(string Selector, string Html, string Styles, int Width, int Height, byte[] Screenshot);

public sealed class PreviewElementPicker : IDisposable
{
    private readonly CdpBroker cdp;
    private readonly IDisposable subscription;
    private readonly CancellationTokenSource stop = new();
    public PreviewElementPicker(CdpBroker cdp) { this.cdp = cdp; subscription = cdp.Subscribe("Overlay.inspectNodeRequested", Pick); }
    public bool Enabled { get; private set; }
    public PreviewElementContext? LastPick { get; private set; }
    public Task WhenIdle { get; private set; } = Task.CompletedTask;
    public event Action<PreviewElementContext>? Picked;
    public async Task SetEnabledAsync(bool enabled)
    {
        if (stop.IsCancellationRequested) return;
        if (enabled) foreach (var domain in new[] { "DOM.enable", "CSS.enable", "Overlay.enable" }) await cdp.CallAsync(domain, cancellationToken: stop.Token);
        await cdp.CallAsync("Overlay.setInspectMode", new JsonObject
        {
            ["mode"] = enabled ? "searchForNode" : "none",
            ["highlightConfig"] = new JsonObject { ["showInfo"] = true, ["contentColor"] = new JsonObject { ["r"] = 75, ["g"] = 159, ["b"] = 252, ["a"] = .12 }, ["borderColor"] = new JsonObject { ["r"] = 75, ["g"] = 159, ["b"] = 252, ["a"] = 1 } },
        }, cancellationToken: stop.Token);
        Enabled = enabled;
    }
    private void Pick(WebCdpEvent e)
    {
        if (!Enabled || stop.IsCancellationRequested) return;
        try { using var d = JsonDocument.Parse(e.ParamsJson); var id = (int)PreviewJson.Number(d.RootElement, "backendNodeId"); if (id > 0) WhenIdle = Read(id, e.SessionId); }
        catch (JsonException) { }
    }
    private async Task Read(int backend, string? session)
    {
        try
        {
            var described = await cdp.CallAsync("DOM.describeNode", new() { ["backendNodeId"] = backend }, session, stop.Token);
            var node = PreviewJson.Field(described, "node"); var id = (int)PreviewJson.Number(node, "nodeId");
            if (id == 0)
            {
                await cdp.CallAsync("DOM.getDocument", sessionId: session, cancellationToken: stop.Token);
                var pushed = await cdp.CallAsync("DOM.pushNodesByBackendIdsToFrontend", new() { ["backendNodeIds"] = new JsonArray(backend) }, session, stop.Token);
                var ids = PreviewJson.Field(pushed, "nodeIds"); if (ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0) id = ids[0].GetInt32();
            }
            if (id <= 0) return;
            var selector = PreviewJson.Text(node, "localName");
            var attributes = PreviewJson.Field(node, "attributes");
            if (attributes.ValueKind == JsonValueKind.Array)
                for (var i = 0; i + 1 < attributes.GetArrayLength(); i += 2)
                    if (attributes[i].GetString() == "id") selector = "#" + CssEscape(attributes[i + 1].GetString() ?? "");
            var resolved = await cdp.CallAsync("DOM.resolveNode", new() { ["nodeId"] = id }, session, stop.Token);
            var objectId = PreviewJson.Text(PreviewJson.Field(resolved, "object"), "objectId");
            if (objectId.Length != 0)
            {
                try
                {
                    var path = await cdp.CallAsync("Runtime.callFunctionOn", new()
                    {
                        ["objectId"] = objectId, ["returnByValue"] = true,
                        ["functionDeclaration"] = "function(){const p=[];let e=this;for(let d=0;e&&e.nodeType===1&&d<128;d++,e=e.parentElement){if(e.id&&e.ownerDocument.querySelectorAll('#'+CSS.escape(e.id)).length===1){p.unshift('#'+CSS.escape(e.id));break;}let n=e.localName;const s=e.parentElement?Array.from(e.parentElement.children).filter(x=>x.localName===n):[];if(s.length>1)n+=':nth-of-type('+(s.indexOf(e)+1)+')';p.unshift(n);}return p.join(' > ');}",
                    }, session, stop.Token);
                    var unique = PreviewJson.Text(PreviewJson.Field(path, "result"), "value"); if (unique.Length != 0) selector = unique;
                }
                finally { await cdp.CallAsync("Runtime.releaseObject", new() { ["objectId"] = objectId }, session, stop.Token); }
            }
            var html = await cdp.CallAsync("DOM.getOuterHTML", new() { ["nodeId"] = id }, session, stop.Token);
            var styles = await cdp.CallAsync("CSS.getComputedStyleForNode", new() { ["nodeId"] = id }, session, stop.Token);
            var box = await cdp.CallAsync("DOM.getBoxModel", new() { ["nodeId"] = id }, session, stop.Token);
            var model = PreviewJson.Field(box, "model"); var width = (int)PreviewJson.Number(model, "width"); var height = (int)PreviewJson.Number(model, "height");
            var border = PreviewJson.Field(model, "border");
            var shot = new JsonObject { ["format"] = "png", ["captureBeyondViewport"] = true };
            if (border.ValueKind == JsonValueKind.Array && border.GetArrayLength() == 8)
                shot["clip"] = new JsonObject { ["x"] = border[0].GetDouble(), ["y"] = border[1].GetDouble(), ["width"] = width, ["height"] = height, ["scale"] = 1 };
            var captured = await cdp.CallAsync("Page.captureScreenshot", shot, session, stop.Token);
            var bytes = Convert.FromBase64String(PreviewJson.Text(captured, "data"));
            // Remove value attributes from input markup before even storing picked context (passwords included).
            var safeHtml = Regex.Replace(PreviewJson.Text(html, "outerHTML"), @"(<input\b[^>]*?)\svalue\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", "$1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var computed = PreviewJson.Field(styles, "computedStyle");
            var keys = new HashSet<string>(StringComparer.Ordinal) { "display", "position", "width", "height", "color", "background-color", "font-family", "font-size", "font-weight", "line-height", "margin", "padding", "border", "border-radius", "flex-direction", "align-items", "justify-content", "grid-template-columns", "gap", "overflow", "transform" };
            var summary = computed.ValueKind == JsonValueKind.Array ? string.Join("\n", computed.EnumerateArray().Where(value => keys.Contains(PreviewJson.Text(value, "name"))).Select(value => PreviewJson.Text(value, "name") + ": " + PreviewJson.Text(value, "value"))) : "";
            LastPick = new PreviewElementContext(selector, PreviewJson.Redacted(safeHtml), PreviewJson.Redacted(summary), width, height, bytes);
            await SetEnabledAsync(false);
            if (!stop.IsCancellationRequested) Picked?.Invoke(LastPick);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or FormatException or JsonException or System.Runtime.InteropServices.COMException or RegexMatchTimeoutException) { }
    }
    private static string CssEscape(string id) => string.Concat(id.Select((c, index) => char.IsAsciiLetterOrDigit(c) && !(index == 0 && char.IsAsciiDigit(c)) || c is '-' or '_' ? c.ToString() : "\\" + ((int)c).ToString("x", CultureInfo.InvariantCulture) + " "));
    public void Dispose() { stop.Cancel(); subscription.Dispose(); }
}
