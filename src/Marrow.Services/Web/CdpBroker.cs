using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Services.Web;

/// <summary>An iframe or worker target attached in flatten mode (DESIGN 5.10).</summary>
public sealed record CdpSession(string SessionId, string? ParentSessionId, string TargetId, string Type, string Url);

/// <summary>
/// DESIGN 5.8, 5.10 (ED-NEW-web-w7-4): the one way nth talks the DevTools Protocol to a view. Calls run strictly in
/// order (each starts after the previous one finished, failed or was cancelled), so a model step's clicks, typing and
/// reads never interleave. iframes are reached with <c>Target.setAutoAttach {flatten: true}</c> and per-session
/// calls; the broker tracks attached sessions. Every result and event is redacted (<see cref="CdpRedactor"/>) before
/// anyone sees it, with no raw channel. CDP is in-process only (no <c>--remote-debugging-port</c>, DESIGN 8.1).
/// Snapshot captures do not queue here: they go straight to the engine so a gesture never waits behind a model step.
/// </summary>
public sealed class CdpBroker : IDisposable
{
    private readonly IWebEngineView view;
    private readonly Dictionary<string, CdpSession> sessions = new(StringComparer.Ordinal);
    private readonly List<IDisposable> own = [];
    private readonly CancellationTokenSource lifetime = new();
    private Task tail = Task.CompletedTask;
    private bool disposed;

    public CdpBroker(IWebEngineView view)
    {
        this.view = view;
        own.Add(view.SubscribeCdp("Target.attachedToTarget", OnAttached));
        own.Add(view.SubscribeCdp("Target.detachedFromTarget", OnDetached));
    }

    /// <summary>The iframe and worker sessions attached now.</summary>
    public IReadOnlyCollection<CdpSession> Sessions => sessions.Values;

    /// <summary>
    /// Queues one call; it runs after every earlier call settled. <paramref name="sessionId"/> targets an attached
    /// session (null: the page). Returns the redacted result.
    /// </summary>
    public Task<JsonElement> CallAsync(string method, JsonObject? parameters = null, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(method);
        var json = parameters?.ToJsonString(CdpRedactor.Wire) ?? "{}";
        var run = RunAfter(tail, method, json, sessionId, cancellationToken);
        tail = run.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        // Cancellation settles the caller immediately. The serialization tail still waits for
        // the predecessor: cancelling a queued caller must not let its successor overtake it.
        return run.WaitAsync(cancellationToken).WaitAsync(lifetime.Token);
    }

    /// <summary>
    /// W8b (DESIGN 5.4, 5.5, 8.4; ED-NEW-web-w8b-3): a call a model step makes. Downloads, permissions, file choosers, file drops,
    /// passkeys and nth's own guard switches stay the user's and nth's: such a call is refused before it reaches the page
    /// (<see cref="Marrow.Core.Web.WebFeaturePolicy.ModelMayCall"/>), with <see cref="CdpModelCallRefusedException"/>.
    /// </summary>
    public Task<JsonElement> CallForModelAsync(string method, JsonObject? parameters = null, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(method);
        var json = parameters?.ToJsonString(CdpRedactor.Wire) ?? "{}";
        if (!Marrow.Core.Web.WebFeaturePolicy.ModelMayCall(method, json)) return Task.FromException<JsonElement>(new CdpModelCallRefusedException(method));
        return CallAsync(method, parameters, sessionId, cancellationToken);
    }

    /// <summary>Turns on flatten-mode auto-attach so iframes get their own sessions (DESIGN 5.10).</summary>
    public Task<JsonElement> AttachToFramesAsync(CancellationToken cancellationToken = default) =>
        CallAsync("Target.setAutoAttach", new JsonObject
        {
            ["autoAttach"] = true,
            ["waitForDebuggerOnStart"] = false,
            ["flatten"] = true,
        }, cancellationToken: cancellationToken);

    /// <summary>Subscribes to a CDP event across the page and its sessions; the handler sees redacted params.</summary>
    public IDisposable Subscribe(string eventName, Action<WebCdpEvent> handler)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return view.SubscribeCdp(eventName, e => handler(e with { ParamsJson = CdpRedactor.Redact(e.ParamsJson) }));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        foreach (var subscription in own) subscription.Dispose();
        own.Clear();
        sessions.Clear();
        lifetime.Dispose();
    }

    // No ConfigureAwait(false): CoreWebView2 calls stay on the caller's (UI) context.
    private async Task<JsonElement> RunAfter(Task previous, string method, string json, string? sessionId, CancellationToken cancellationToken)
    {
        await previous.WaitAsync(lifetime.Token);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await view.CallCdpAsync(method, json, sessionId, cancellationToken);
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(raw) ? "{}" : CdpRedactor.Redact(raw));
        return document.RootElement.Clone();
    }

    private void OnAttached(WebCdpEvent e)
    {
        using var document = JsonDocument.Parse(e.ParamsJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("sessionId", out var id) || id.GetString() is not { } sessionId) return;
        var info = root.TryGetProperty("targetInfo", out var t) ? t : default;
        sessions[sessionId] = new CdpSession(
            sessionId,
            e.SessionId,
            Text(info, "targetId"),
            Text(info, "type"),
            Text(info, "url"));
    }

    private void OnDetached(WebCdpEvent e)
    {
        using var document = JsonDocument.Parse(e.ParamsJson);
        if (document.RootElement.TryGetProperty("sessionId", out var id) && id.GetString() is { } sessionId)
        {
            sessions.Remove(sessionId);
            // Children of a detached session go with it.
            foreach (var child in sessions.Values.Where(s => string.Equals(s.ParentSessionId, sessionId, StringComparison.Ordinal)).ToList())
                sessions.Remove(child.SessionId);
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}

/// <summary>W8b: a model step asked for a DevTools call that stays the user's or nth's (8.4); nothing was sent.</summary>
public sealed class CdpModelCallRefusedException(string method)
    : InvalidOperationException($"{method} is not available to the model: downloads, permissions, files and sign-in stay the user's.")
{
    public string Method { get; } = method;
}
