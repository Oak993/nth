// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "rendered to SVG once per completed statement by the vendored Mermaid library inside a
// hidden, sandboxed artifacts-profile view (no network, no bridge) ... the hidden renderer is shared and suspended when idle";
// 2.3 live-view cap and low memory; 6.5; ED-NEW-web-w16c-1). No Swift counterpart. The hidden renderer: one web view for the whole
// process, opened on first use on its own sandbox site (MermaidRenderSite), kept while diagrams keep coming, and closed after
// 30 seconds with nothing to draw so it costs no memory idle. Drawings run one at a time, in order. A drawing is one DevTools
// Runtime.evaluate of nthRenderDiagram(source, options) in the page, answered with a plain object; the SVG in it is handed to
// the caller unread (the live view sanitises it). Nothing in the page can reach nth: there is no bridge, and the site refuses every
// other address. A page that stops, crashes or does not answer in 10 seconds ends the drawing with a failure and is replaced on
// the next request. The view needs a platform host: the UI puts the surface in <see cref="Hosted"/> into a window (hidden, 1x1)
// when <see cref="HostedChanged"/> fires, as it does for any web view; with no host within 6 seconds the drawing is unavailable.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public sealed class MermaidRenderEngine : IDiagramEngine, IDiagramEngineHost, IDisposable
{
    /// <summary>The window the renderer's view belongs to (a name no real window has).</summary>
    public const string WindowId = "nth-diagram-renderer";

    /// <summary>With nothing to draw this long the view is closed.</summary>
    public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(30);

    /// <summary>How long a request waits for the UI to host the view and the page to load.</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(6);

    /// <summary>How long one drawing may take.</summary>
    public static readonly TimeSpan RenderWait = TimeSpan.FromSeconds(10);

    private readonly WebSurface surface;
    private readonly MermaidRenderSite site;
    private readonly TimeProvider time;
    private readonly Action<Action> post;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Logger log = new(Logger.Subsystem, "web");
    private WebSurfaceView? view;
    private ITimer? idle;
    private bool disposed;
    private bool wasAvailable;

    /// <param name="post">Runs work on the UI thread (the idle timer fires elsewhere); the constructing thread's synchronization
    /// context when null, else inline.</param>
    public MermaidRenderEngine(WebSurface surface, ArtifactLibraryFiles libraries, TimeProvider? time = null, Action<Action>? post = null)
    {
        this.surface = surface;
        site = new MermaidRenderSite(libraries);
        this.time = time ?? TimeProvider.System;
        var context = SynchronizationContext.Current;
        this.post = post ?? (context is null ? static work => work() : work => context.Post(static w => ((Action)w!)(), work));
        wasAvailable = IsAvailable;
    }

    public MermaidRenderSite Site => site;

    /// <summary>The renderer's view, for the UI to host in a window; null while none is open.</summary>
    public IWebViewSurface? Hosted => view;

    /// <summary>The renderer's view (the engine layer's own type).</summary>
    public WebSurfaceView? View => view;

    /// <summary>Raised when <see cref="Hosted"/> changes: a view opened, or the idle one closed.</summary>
    public event Action? HostedChanged;

    public event Action? AvailabilityChanged;

    /// <summary>Whether a drawing can be made at all: the web runtime is there and the build carries Mermaid.</summary>
    public bool IsAvailable => !disposed && surface.Runtime.State == WebRuntimeState.Ready && site.MermaidPath is not null;

    /// <summary>Call after the runtime flow installed or updated the runtime: tells the live views a drawing is possible now.</summary>
    public void RefreshAvailability()
    {
        var now = IsAvailable;
        if (now == wasAvailable) return;
        wasAvailable = now;
        AvailabilityChanged?.Invoke();
    }

    public void Prepare()
    {
        if (!IsAvailable || view is not null || gate.CurrentCount == 0) return;
        _ = PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        try
        {
            await gate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (disposed) return;
            await EnsureViewAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is DiagramRenderException or OperationCanceledException or InvalidOperationException)
        {
            log.Info("web: diagram renderer could not be prepared: " + error.Message);
        }
        finally
        {
            ArmIdle();
            gate.Release();
        }
    }

    public async Task<DiagramRenderResult> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) throw new DiagramRenderException(DiagramFailure.Syntax, "The diagram is empty.");
        if (request.Source.Length > DiagramLimits.MaxSource) throw new DiagramRenderException(DiagramFailure.TooLarge, "The diagram is larger than nth draws.");
        if (!IsAvailable) throw new DiagramRenderException(DiagramFailure.Unavailable, "No diagram renderer is available.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (disposed) throw new DiagramRenderException(DiagramFailure.Stopped, "The diagram renderer was shut down.");
            idle?.Dispose();
            idle = null;
            var page = await EnsureViewAsync(cancellationToken);
            try
            {
                return await DrawAsync(page, request, cancellationToken);
            }
            catch (DiagramRenderException error) when (error.Failure is DiagramFailure.Timeout or DiagramFailure.Stopped)
            {
                // The page is in an unknown state: the next drawing gets a new one.
                CloseView();
                throw;
            }
        }
        finally
        {
            ArmIdle();
            gate.Release();
        }
    }

    // ---- the view ------------------------------------------------------------------------------------------------------

    private async Task<WebSurfaceView> EnsureViewAsync(CancellationToken cancellationToken)
    {
        if (view is { } current && current.Liveness == WebLiveness.Live && current.Failure is null && current.Engine is { IsAttached: true, IsSuspended: false } && ready)
            return current;
        CloseView();
        var opened = surface.Open(WebSurfaceKind.InlineWidget, WindowId);
        view = opened;
        ready = false;
        opened.SetSandbox(new WebSandbox(MermaidRenderSite.Origin, site, [SandboxScripts.Hardening]));
        if (opened.Engine is null)
        {
            CloseView();
            throw new DiagramRenderException(DiagramFailure.Unavailable, "The web runtime is not available.");
        }

        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var live = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (opened.Liveness == WebLiveness.Live) live.TrySetResult(true);
            if (opened.Failure is not null || opened.Liveness == WebLiveness.Closed) live.TrySetResult(false);
        }

        void OnCompleted(object? sender, WebNavigationCompleted e)
        {
            if (e.Url.StartsWith(MermaidRenderSite.Origin, StringComparison.OrdinalIgnoreCase)) loaded.TrySetResult(e.IsSuccess);
        }

        opened.Changed += OnChanged;
        opened.NavigationCompleted += OnCompleted;
        try
        {
            opened.Navigate(MermaidRenderSite.Address);
            HostedChanged?.Invoke();
            OnChanged();
            await WaitAsync(live.Task, StartWait, cancellationToken, "No window hosted the diagram renderer.");
            if (!live.Task.Result) throw new DiagramRenderException(DiagramFailure.Unavailable, "The diagram renderer could not start.");
            await WaitAsync(loaded.Task, StartWait, cancellationToken, "The diagram renderer's page did not load.");
            if (!loaded.Task.Result) throw new DiagramRenderException(DiagramFailure.Unavailable, "The diagram renderer's page did not load.");
            var value = await EvaluateAsync(opened, "window.nthDiagramReady === true", StartWait, cancellationToken);
            if (value is not { ValueKind: JsonValueKind.True }) throw new DiagramRenderException(DiagramFailure.Unavailable, "Mermaid is not part of this build.");
            ready = true;
            return opened;
        }
        catch
        {
            if (ReferenceEquals(view, opened)) CloseView();
            throw;
        }
        finally
        {
            opened.Changed -= OnChanged;
            opened.NavigationCompleted -= OnCompleted;
        }
    }

    private bool ready;

    private void CloseView()
    {
        if (view is not { } closing) return;
        view = null;
        ready = false;
        surface.Close(closing);
        HostedChanged?.Invoke();
    }

    private void ArmIdle()
    {
        idle?.Dispose();
        idle = null;
        if (disposed || view is null) return;
        idle = time.CreateTimer(static state => ((MermaidRenderEngine)state!).OnIdle(), this, IdleClose, Timeout.InfiniteTimeSpan);
    }

    private void OnIdle() => post(() =>
    {
        if (disposed || gate.CurrentCount == 0) return; // a drawing started meanwhile
        idle?.Dispose();
        idle = null;
        if (view is not null) log.Info("web: diagram renderer idle, closed");
        CloseView();
    });

    // ---- one drawing -----------------------------------------------------------------------------------------------------

    private async Task<DiagramRenderResult> DrawAsync(WebSurfaceView page, DiagramRenderRequest request, CancellationToken cancellationToken)
    {
        var options = new JsonObject
        {
            ["dark"] = request.Theme.Dark,
            ["fontFamily"] = request.Theme.FontFamily,
            ["fontSize"] = request.Theme.FontSize,
            ["variables"] = new JsonObject(request.Theme.Variables.Select(v => KeyValuePair.Create(v.Key, (JsonNode?)JsonValue.Create(v.Value)))),
        };
        var expression = "nthRenderDiagram(" + JsonSerializer.Serialize(request.Source) + "," + options.ToJsonString() + ")";
        using var step = page.BeginModelStep(); // never suspended or discarded while a drawing is being made (DESIGN 2.3)
        var value = await EvaluateAsync(page, expression, RenderWait, cancellationToken);
        if (value is not { ValueKind: JsonValueKind.Object } answer) throw new DiagramRenderException(DiagramFailure.Stopped, "The renderer gave no answer.");
        if (answer.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True && answer.TryGetProperty("svg", out var svg) && svg.GetString() is { Length: > 0 } text)
        {
            if (text.Length > DiagramLimits.MaxSvg) throw new DiagramRenderException(DiagramFailure.TooLarge, "The drawing is larger than nth draws.");
            return new DiagramRenderResult(text);
        }

        var message = answer.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : "The diagram could not be drawn.";
        var kind = answer.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : "syntax";
        int? line = answer.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var n) ? n : null;
        throw new DiagramRenderException(kind switch
        {
            "toolarge" => DiagramFailure.TooLarge,
            "unavailable" => DiagramFailure.Unavailable,
            _ => DiagramFailure.Syntax,
        }, message, line);
    }

    /// <summary>Runtime.evaluate of an expression whose value is returned by value (promises awaited); the value, or a failure.</summary>
    private async Task<JsonElement?> EvaluateAsync(WebSurfaceView page, string expression, TimeSpan limit, CancellationToken cancellationToken)
    {
        var engine = page.Engine ?? throw new DiagramRenderException(DiagramFailure.Stopped, "The renderer's view is gone.");
        var parameters = new JsonObject
        {
            ["expression"] = expression,
            ["awaitPromise"] = true,
            ["returnByValue"] = true,
            ["timeout"] = (int)limit.TotalMilliseconds,
        };
        var died = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (page.Failure is not null || page.Liveness is WebLiveness.Closed or WebLiveness.Discarded) died.TrySetResult(true);
        }

        page.Changed += OnChanged;
        try
        {
            var call = engine.CallCdpAsync("Runtime.evaluate", parameters.ToJsonString(), null, cancellationToken);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timer = Task.Delay(limit + TimeSpan.FromSeconds(2), time, stop.Token);
            var first = await Task.WhenAny(call, died.Task, timer);
            stop.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (first == died.Task) throw new DiagramRenderException(DiagramFailure.Stopped, "The renderer stopped.");
            if (first == timer) throw new DiagramRenderException(DiagramFailure.Timeout, "The renderer did not answer in time.");
            var raw = await call;
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(raw) ? "{}" : raw);
            var root = document.RootElement;
            if (root.TryGetProperty("exceptionDetails", out var details))
            {
                var text = details.TryGetProperty("exception", out var exception) && exception.TryGetProperty("description", out var description) ? description.GetString() : null;
                throw new DiagramRenderException(DiagramFailure.Stopped, text ?? "The renderer's page raised an error.");
            }

            if (!root.TryGetProperty("result", out var result)) return null;
            return result.TryGetProperty("value", out var value) ? value.Clone() : null;
        }
        finally
        {
            page.Changed -= OnChanged;
        }
    }

    private async Task WaitAsync(Task task, TimeSpan limit, CancellationToken cancellationToken, string timeout)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = Task.Delay(limit, time, stop.Token);
        var first = await Task.WhenAny(task, timer);
        stop.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        if (first == timer) throw new DiagramRenderException(DiagramFailure.Unavailable, timeout);
        await task;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idle?.Dispose();
        idle = null;
        CloseView();
        AvailabilityChanged?.Invoke();
    }
}
