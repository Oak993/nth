// PORT-ONLY: web surfaces W11, web_console and web_network (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.8, 5.10 "web_console
// {tab, since?} / web_network {tab, since?}: redacted logs", 8.1 "Redaction: cookies, Set-Cookie, Authorization,
// Proxy-Authorization ... never reach the model or chat"; ED-NEW-web-w11-1). No Swift counterpart. One ring of console lines and
// one of requests per driven tab, numbered so a later call asks only for what is new; the CDP events arrive already redacted
// (CdpBroker.Subscribe), and only methods, statuses, addresses, types, sizes and times are kept from requests, never headers or
// bodies.
using System.Globalization;
using System.Text.Json;

namespace Marrow.Services.Web;

/// <summary>One numbered log line.</summary>
public sealed record WebLogLine(long Number, string Text);

public sealed class WebAgentLogs : IDisposable
{
    /// <summary>The lines each ring keeps.</summary>
    public const int Kept = 500;

    private readonly List<IDisposable> subscriptions = [];
    private readonly LinkedList<WebLogLine> console = new();
    private readonly LinkedList<WebLogLine> network = new();
    private readonly Dictionary<string, (string Method, string Url, string Type, long Started)> requests = new(StringComparer.Ordinal);
    private readonly Func<long> nowMs;
    private long number;

    public WebAgentLogs(Func<long>? nowMs = null) => this.nowMs = nowMs ?? (() => Environment.TickCount64);

    /// <summary>Requests started and not yet finished or failed (web_wait for idle).</summary>
    public int InFlight => requests.Count;

    /// <summary>When the last request started or ended (engine clock, ms).</summary>
    public long LastNetworkAt { get; private set; }

    /// <summary>Turns the domains on and listens; safe to call again for a new view.</summary>
    public async Task AttachAsync(CdpBroker cdp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cdp);
        DisposeSubscriptions();
        subscriptions.Add(cdp.Subscribe("Runtime.consoleAPICalled", e => OnConsole(e.ParamsJson)));
        subscriptions.Add(cdp.Subscribe("Runtime.exceptionThrown", e => OnException(e.ParamsJson)));
        subscriptions.Add(cdp.Subscribe("Log.entryAdded", e => OnLog(e.ParamsJson)));
        subscriptions.Add(cdp.Subscribe("Network.requestWillBeSent", e => OnRequest(e.ParamsJson)));
        subscriptions.Add(cdp.Subscribe("Network.responseReceived", e => OnResponse(e.ParamsJson)));
        subscriptions.Add(cdp.Subscribe("Network.loadingFinished", e => OnFinished(e.ParamsJson, failed: false)));
        subscriptions.Add(cdp.Subscribe("Network.loadingFailed", e => OnFinished(e.ParamsJson, failed: true)));
        foreach (var domain in new[] { "Runtime.enable", "Log.enable", "Network.enable" })
        {
            try
            {
                await cdp.CallAsync(domain, cancellationToken: cancellationToken).ConfigureAwait(true);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
            }
        }
    }

    /// <summary>The console lines after <paramref name="since"/>, and the number to ask from next time.</summary>
    public (IReadOnlyList<WebLogLine> Lines, long Next) Console(long since) => Since(console, since);

    public (IReadOnlyList<WebLogLine> Lines, long Next) Network(long since) => Since(network, since);

    public void Dispose() => DisposeSubscriptions();

    private (IReadOnlyList<WebLogLine> Lines, long Next) Since(LinkedList<WebLogLine> ring, long since)
    {
        lock (ring) return ([.. ring.Where(l => l.Number > since)], number);
    }

    private void Add(LinkedList<WebLogLine> ring, string text)
    {
        lock (ring)
        {
            ring.AddLast(new WebLogLine(Interlocked.Increment(ref number), text.Length <= 600 ? text : text[..597] + "…"));
            while (ring.Count > Kept) ring.RemoveFirst();
        }
    }

    private void OnConsole(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var level = Str(root, "type") ?? "log";
        var parts = new List<string>();
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in args.EnumerateArray())
            {
                if (arg.TryGetProperty("value", out var value)) parts.Add(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText());
                else if (Str(arg, "description") is { } description) parts.Add(description);
                else if (Str(arg, "type") is { } type) parts.Add("[" + type + "]");
            }
        }
        Add(console, level + ": " + string.Join(' ', parts));
    }

    private void OnException(string json)
    {
        using var document = JsonDocument.Parse(json);
        var details = document.RootElement.TryGetProperty("exceptionDetails", out var d) ? d : default;
        var text = details.ValueKind == JsonValueKind.Object
            ? (details.TryGetProperty("exception", out var ex) && Str(ex, "description") is { } description ? description : Str(details, "text") ?? "exception")
            : "exception";
        var where = details.ValueKind == JsonValueKind.Object && Str(details, "url") is { Length: > 0 } url
            ? " (" + url + ":" + (details.TryGetProperty("lineNumber", out var line) ? (line.GetInt32() + 1).ToString(CultureInfo.InvariantCulture) : "?") + ")"
            : "";
        Add(console, "error: " + text.Split('\n')[0] + where);
    }

    private void OnLog(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("entry", out var entry)) return;
        Add(console, (Str(entry, "level") ?? "info") + ": " + (Str(entry, "text") ?? "") + (Str(entry, "url") is { Length: > 0 } url ? " (" + url + ")" : ""));
    }

    private void OnRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Str(root, "requestId") is not { } id || !root.TryGetProperty("request", out var request)) return;
        lock (requests) requests[id] = (Str(request, "method") ?? "GET", Str(request, "url") ?? "", Str(root, "type") ?? "Other", nowMs());
        LastNetworkAt = nowMs();
    }

    private readonly Dictionary<string, int> statuses = new(StringComparer.Ordinal);

    private void OnResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Str(root, "requestId") is not { } id || !root.TryGetProperty("response", out var response)) return;
        if (response.TryGetProperty("status", out var status) && status.TryGetInt32(out var code))
            lock (requests) statuses[id] = code;
    }

    private void OnFinished(string json, bool failed)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Str(root, "requestId") is not { } id) return;
        (string Method, string Url, string Type, long Started) request;
        int status;
        lock (requests)
        {
            if (!requests.Remove(id, out request)) return;
            statuses.Remove(id, out status);
        }
        LastNetworkAt = nowMs();
        var size = root.TryGetProperty("encodedDataLength", out var bytes) && bytes.TryGetDouble(out var b) ? " " + Size(b) : "";
        var outcome = failed ? "failed " + (Str(root, "errorText") ?? "") : status == 0 ? "-" : status.ToString(CultureInfo.InvariantCulture);
        var ms = (nowMs() - request.Started).ToString(CultureInfo.InvariantCulture);
        Add(network, request.Method + " " + outcome.Trim() + " " + request.Url + " (" + request.Type.ToLowerInvariant() + size + ", " + ms + " ms)");
    }

    private static string Size(double bytes) => bytes < 1024 ? ((int)bytes).ToString(CultureInfo.InvariantCulture) + " B"
        : bytes < 1024 * 1024 ? (bytes / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : (bytes / (1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    private void DisposeSubscriptions()
    {
        foreach (var subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
