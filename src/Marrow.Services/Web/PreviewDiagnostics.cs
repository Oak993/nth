using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.8, 8.1; ED-NEW-web-w9-2. Redacted CDP data is the only data stored or handed to chrome/chat.
public sealed record PreviewConsoleEntry(string Level, string Text, string Detail);
public sealed record PreviewNetworkEntry(string Id, string? SessionId, string Method, string Path, string Detail, double Started)
{
    public int Status { get; init; }
    public string Type { get; init; } = "";
    public double Size { get; init; }
    public double Milliseconds { get; init; }
    public string Failure { get; init; } = "";
    public bool Failed => Failure.Length != 0 || Status >= 400;
}

public sealed class PreviewDiagnostics : IDisposable
{
    private readonly CdpBroker cdp;
    private readonly List<IDisposable> subscriptions = [];
    private readonly List<PreviewConsoleEntry> console = [];
    private readonly List<PreviewNetworkEntry> network = [];
    private bool disposed;
    public PreviewDiagnostics(CdpBroker cdp)
    {
        this.cdp = cdp;
        foreach (var name in new[] { "Runtime.consoleAPICalled", "Runtime.exceptionThrown", "Log.entryAdded", "Network.requestWillBeSent", "Network.responseReceived", "Network.loadingFailed", "Network.loadingFinished", "Network.requestWillBeSentExtraInfo", "Network.responseReceivedExtraInfo" })
            subscriptions.Add(cdp.Subscribe(name, OnEvent));
    }
    public IReadOnlyList<PreviewConsoleEntry> Console => console;
    public IReadOnlyList<PreviewNetworkEntry> Network => network;
    public int Errors => console.Count(row => row.Level == "error");
    public bool PreserveLog { get; set; }
    public event Action? Changed;
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var domain in new[] { "Runtime.enable", "Log.enable", "Network.enable", "Page.enable" })
            await cdp.CallAsync(domain, cancellationToken: cancellationToken);
    }
    public IReadOnlyList<PreviewConsoleEntry> FindConsole(string search = "", string? level = null) => console
        .Where(row => (level is null || row.Level == level) && row.Text.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    public IReadOnlyList<PreviewNetworkEntry> FindNetwork(string search = "") => network.Where(row => row.Path.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    public void Clear() { console.Clear(); network.Clear(); Changed?.Invoke(); }
    public void Navigation() { if (!PreserveLog) Clear(); }
    public string ToChat(PreviewConsoleEntry row) => "[Untrusted preview console]\n" + row.Detail;
    public string ToChat(PreviewNetworkEntry row) => "[Untrusted preview network]\n" + row.Method + " " + row.Path + " " + row.Status.ToString(CultureInfo.InvariantCulture) + "\n" + row.Detail;
    public async Task<string> ResponsePreviewAsync(PreviewNetworkEntry row, CancellationToken cancellationToken = default)
    {
        var result = await cdp.CallAsync("Network.getResponseBody", new JsonObject { ["requestId"] = row.Id }, row.SessionId, cancellationToken);
        var body = PreviewJson.Text(result, "body");
        if (PreviewJson.Field(result, "base64Encoded").ValueKind == JsonValueKind.True)
        {
            try { body = Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
            catch (FormatException) { return ""; }
        }
        return PreviewJson.Redacted(PreviewJson.Bounded(body, 65_536));
    }
    private void OnEvent(WebCdpEvent e)
    {
        if (disposed || e.ParamsJson.Length > 1024 * 1024) return;
        try
        {
            using var document = JsonDocument.Parse(e.ParamsJson);
            var root = document.RootElement;
            if (e.Name == "Runtime.consoleAPICalled")
            {
                var args = PreviewJson.Field(root, "args");
                var text = args.ValueKind == JsonValueKind.Array ? string.Join(" ", args.EnumerateArray().Select(a =>
                    PreviewJson.Field(a, "value") is { ValueKind: not JsonValueKind.Undefined } value
                        ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                        : PreviewJson.Text(a, "description"))) : "";
                AddConsole(PreviewJson.Text(root, "type"), text, e.ParamsJson);
                return;
            }
            if (e.Name == "Runtime.exceptionThrown")
            {
                var detail = PreviewJson.Field(root, "exceptionDetails");
                var description = PreviewJson.Text(PreviewJson.Field(detail, "exception"), "description");
                AddConsole("error", description.Length == 0 ? PreviewJson.Text(detail, "text") : description, e.ParamsJson);
                return;
            }
            if (e.Name == "Log.entryAdded")
            {
                var entry = PreviewJson.Field(root, "entry");
                AddConsole(PreviewJson.Text(entry, "level"), PreviewJson.Text(entry, "text"), e.ParamsJson); return;
            }
            var id = PreviewJson.Text(root, "requestId");
            if (id.Length == 0) return;
            var at = network.FindIndex(row => row.Id == id && row.SessionId == e.SessionId);
            if (e.Name == "Network.requestWillBeSent")
            {
                var request = PreviewJson.Field(root, "request"); var url = PreviewJson.Text(request, "url");
                var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;
                var next = new PreviewNetworkEntry(id, e.SessionId, PreviewJson.Text(request, "method"), PreviewJson.Redacted(path), PreviewJson.Redacted(e.ParamsJson), PreviewJson.Number(root, "timestamp"));
                if (at >= 0) network[at] = next; else network.Add(next);
                if (network.Count > 1000) network.RemoveAt(0);
            }
            else if (at >= 0)
            {
                var row = network[at]; var time = PreviewJson.Number(root, "timestamp");
                var next = row with { Milliseconds = time > 0 ? Math.Max(0, (time - row.Started) * 1000) : row.Milliseconds };
                if (e.Name == "Network.responseReceived")
                    next = next with { Status = (int)PreviewJson.Number(PreviewJson.Field(root, "response"), "status"), Type = PreviewJson.Text(root, "type"), Detail = PreviewJson.Bounded(row.Detail + "\n" + PreviewJson.Redacted(e.ParamsJson)) };
                else if (e.Name == "Network.loadingFailed") next = next with { Failure = PreviewJson.Redacted(PreviewJson.Text(root, "errorText")) };
                else if (e.Name == "Network.loadingFinished") next = next with { Size = PreviewJson.Number(root, "encodedDataLength") };
                else next = next with { Detail = PreviewJson.Bounded(row.Detail + "\n" + PreviewJson.Redacted(e.ParamsJson)) };
                network[at] = next;
            }
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or OverflowException) { }
    }
    private void AddConsole(string level, string text, string detail)
    {
        console.Add(new PreviewConsoleEntry(level == "warning" ? "warn" : level, PreviewJson.Redacted(text), PreviewJson.Redacted(detail)));
        if (console.Count > 2000) console.RemoveAt(0);
        Changed?.Invoke();
    }
    public void Dispose() { disposed = true; foreach (var subscription in subscriptions) subscription.Dispose(); subscriptions.Clear(); }
}
