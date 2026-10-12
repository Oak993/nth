using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 0 (2), 2.3, 2.5, 2.7, 3.7 (ED-NEW-web-w7-2 to -6): the one service that owns every web view in the process,
/// across every <c>ThreadWindow</c>. It picks each view's profile, keeps at most <see cref="WebSurfaceOptions.MaxLiveViews"/>
/// views live (the least recently used beyond that are suspended and shown as snapshots; model-driven views never),
/// runs hidden views at low memory, discards tabs hidden for 10 minutes to a snapshot plus URL, captures a fresh frame at
/// every gesture start (with the intent cache as the budget fallback), forwards reserved chords from inside pages, turns
/// process failures into the cards of 2.7, checks the runtime, and pre-warms one environment only when it is likely to
/// be used. It runs on the UI thread; <see cref="Tick"/> is driven by a timer there.
/// </summary>
public sealed class WebSurface : IDisposable
{
    public const string StepFailedCrashed = "page crashed";
    public const string StepFailedNotResponding = "page not responding";
    public const string StepFailedVisusStopped = "Visus stopped";

    /// <summary>DESIGN 2.7: what a guarded-action card says when the page crashes before the action ran.</summary>
    public const string GuardedActionCrashed = "The page crashed before this ran. Nothing was sent.";

    private readonly List<WebSurfaceView> views = [];
    private readonly Dictionary<string, bool> windowFocus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DateTimeOffset>> crashes = new(StringComparer.Ordinal);
    private readonly TimeProvider time;
    private readonly Action<Action> post;
    private readonly Logger log = new(Logger.Subsystem, "web");
    private int sequence;
    private bool prewarmTried;
    private DateTimeOffset? prewarmDueAt;
    private DateTimeOffset? prewarmedAt;
    private bool enforcing;
    private bool disposed;

    /// <param name="engine">The engine (WebView2 in the app, the fake in tests).</param>
    /// <param name="profiles">Profile naming and the shared environment options.</param>
    /// <param name="time">The clock (timers, the capture budget, discards).</param>
    /// <param name="post">Runs work after the current input event (reserved chords; DESIGN 3.7: the accelerator event is
    /// synchronous). Defaults to the constructing thread's synchronization context, or inline.</param>
    public WebSurface(IWebEngine engine, WebProfiles profiles, TimeProvider? time = null, Action<Action>? post = null)
    {
        Engine = engine;
        Profiles = profiles;
        this.time = time ?? TimeProvider.System;
        var context = SynchronizationContext.Current;
        this.post = post ?? (context is null ? static work => work() : work => context.Post(static w => ((Action)w!)(), work));
        Cache = new SnapshotCache(this.time);
        Runtime = WebRuntimeStatus.Evaluate(engine);
    }

    public IWebEngine Engine { get; }

    public WebProfiles Profiles { get; }

    public WebSurfaceOptions Options { get; } = new();

    public SnapshotCache Cache { get; }

    /// <summary>The runtime check (DESIGN 2.1): evaluated at start and by <see cref="RefreshRuntime"/>.</summary>
    public WebRuntimeStatus Runtime { get; private set; }

    /// <summary>Every open view, in open order.</summary>
    public IReadOnlyList<WebSurfaceView> Views => views;

    /// <summary>Views that are live pages now (the cap counts these, across every window).</summary>
    public int LiveCount => views.Count(v => v.Liveness == WebLiveness.Live);

    /// <summary>
    /// The reserved chords that reach nth from inside a page (DESIGN 3.7). The shortcut layer (W3b) sets this from the
    /// user's bindings; the default is the design's list.
    /// </summary>
    public Func<KeyChord, bool> IsReservedChord { get; set; } = WebKeys.DefaultReserved.Contains;

    /// <summary>A reserved chord was pressed inside a page; raised after the key event, through the post seam.</summary>
    public event Action<WebSurfaceView, KeyChord>? ReservedChordPressed;

    /// <summary>A running model step failed because of the page (DESIGN 2.7: "page crashed", "page not responding", "Visus stopped").</summary>
    public event Action<WebSurfaceView, string>? StepFailed;

    /// <summary>The failure card's "Close tab" was pressed.</summary>
    public event Action<WebSurfaceView>? CloseRequested;

    /// <summary>
    /// A runtime card's button was pressed: Install, Update (the runtime flow, W23: <c>WebRuntimeInstaller</c>, which acts only
    /// on this user gesture) or Open Microsoft's page.
    /// </summary>
    public event Action<WebFailureAction>? RuntimeActionRequested;

    /// <summary>
    /// W23: the card the runtime flow is showing in place of <c>Runtime.Card</c> (installing, or why it failed), or null
    /// when the flow is idle. Views opened meanwhile start with it.
    /// </summary>
    public WebFailureCard? RuntimeFlowCard { get; private set; }

    /// <summary>
    /// W23: shows <paramref name="card"/> (or, for null, the plain offer for the runtime found) on every view that is waiting
    /// on the runtime. Sandboxed surfaces keep their static fallback and never get a card.
    /// </summary>
    public void ShowRuntimeFlowCard(WebFailureCard? card)
    {
        RuntimeFlowCard = card;
        foreach (var view in views.Where(v => v.Failure is { } failure && failure.Kind.IsRuntime()).ToList())
            view.SetFailure(RuntimeCardFor(view));
    }

    private WebFailureCard? RuntimeCardFor(WebSurfaceView view)
    {
        var showsCard = !view.IsSandboxed;
        return RuntimeFlowCard is { } flow ? flow with { ShowsCard = showsCard } : Runtime.Card(showsCard);
    }

    /// <summary>Re-reads the runtime; views waiting on a missing or old runtime get an engine view when it is ready.</summary>
    public WebRuntimeStatus RefreshRuntime()
    {
        Runtime = WebRuntimeStatus.Evaluate(Engine);
        if (Runtime.State == WebRuntimeState.Ready)
        {
            RuntimeFlowCard = null;
            foreach (var view in views.Where(v => v.Failure is { } failure && failure.Kind.IsRuntime()).ToList())
            {
                view.SetFailure(null);
                view.CreateEngine();
            }
        }

        return Runtime;
    }

    /// <summary>
    /// Opens a view for a surface. Its profile and creation options come from <see cref="WebProfiles"/>; it becomes live
    /// when the host attaches the platform view. With no usable runtime the view gets the install or update card
    /// (DESIGN 2.7) and no engine view; sandboxed surfaces keep their static fallback (no card).
    /// </summary>
    public WebSurfaceView Open(WebSurfaceKind kind, string windowId, string? url = null, string? threadFolder = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(windowId);
        var view = new WebSurfaceView(this, ++sequence, kind, windowId, Profiles.For(kind, threadFolder), url)
        {
            LastUsedAt = time.GetUtcNow(),
        };
        views.Add(view);
        // W23: a runtime that was missing may have been installed since (by the flow, or by the user from Microsoft's page);
        // the check is a registry read, so a view opened without one looks again before it shows a card.
        if (Runtime.State != WebRuntimeState.Ready) RefreshRuntime();
        if (Runtime.State != WebRuntimeState.Ready)
        {
            view.SetFailure(RuntimeCardFor(view));
            view.SetLiveness(WebLiveness.Closed);
            return view;
        }

        view.CreateEngine();
        return view;
    }

    /// <summary>Closes a view for good.</summary>
    public void Close(WebSurfaceView view)
    {
        if (!views.Remove(view)) return;
        view.ReleaseEngine();
        view.SetLiveness(WebLiveness.Closed);
        Cache.Remove(view);
    }

    /// <summary>Tracks window focus (the intent cache keeps frames fresh only in focused windows; hang probes run there).</summary>
    public void SetWindowFocused(string windowId, bool focused) => windowFocus[windowId] = focused;

    /// <summary>DESIGN 2.3 pre-warm: the first hover of any Visus button or link-open control in a session.</summary>
    public void NoteVisusControlHover() => StartPrewarm();

    /// <summary>DESIGN 2.3 pre-warm: first paint; people who opened Visus in the last 7 days get a pre-warm 3 s later.</summary>
    public void NoteFirstPaint(DateTimeOffset? lastVisusOpen)
    {
        if (lastVisusOpen is { } last && time.GetUtcNow() - last <= Options.PrewarmRecentUse)
            prewarmDueAt = time.GetUtcNow() + Options.PrewarmAfterFirstPaint;
    }

    /// <summary>
    /// The periodic pass (about every 250 ms on the UI thread): pre-warm due and idle close, discards, hang probes, the
    /// step-hang rule and intent-cache refreshes.
    /// </summary>
    public void Tick()
    {
        if (disposed) return;
        var now = time.GetUtcNow();
        if (prewarmDueAt is { } due && now >= due)
        {
            prewarmDueAt = null;
            StartPrewarm();
        }

        if (prewarmedAt is { } warmed && Engine.IsPrewarmed && now - warmed >= Options.PrewarmIdleClose)
        {
            prewarmedAt = null;
            Engine.ReleasePrewarm();
            log.Info("web: pre-warmed environment unused for 10 minutes, closed");
        }

        foreach (var view in views.ToList())
        {
            if (ShouldDiscard(view, now)) Discard(view);
        }

        foreach (var view in views.ToList())
        {
            if (view.Liveness == WebLiveness.Live && view.IsShown && view.IsLiveVisible && IsFocused(view.WindowId)
                && view.Engine is { IsAttached: true, IsSuspended: false } && !view.IsProbing && view.Failure is null
                && (view.LastProbeAt is not { } probed || now - probed >= Options.ProbeInterval))
            {
                view.LastProbeAt = now;
                _ = ProbeAsync(view);
            }

            if (view.HungSince is { } hung && view.IsModelStepRunning && !view.StepHangReported && now - hung >= Options.StepHangTimeout)
            {
                view.StepHangReported = true;
                StepFailed?.Invoke(view, StepFailedNotResponding);
            }

            if (Cache.Enabled && view.Liveness == WebLiveness.Live && view.Engine is { IsAttached: true }
                && Cache.ShouldRefresh(view, intent: false, visibleAndFocused: view.IsShown && view.IsLiveVisible && IsFocused(view.WindowId)))
                _ = RefreshAsync(view);
        }
    }

    /// <summary>"Restart Visus" (DESIGN 2.7): a new environment, and every view recreated and restored by URL.</summary>
    public void Restart()
    {
        Engine.ResetEnvironment();
        foreach (var view in views.Where(v => v.Liveness != WebLiveness.Closed || v.Engine is not null).ToList())
        {
            if (view.Failure is { } waiting && waiting.Kind.IsRuntime()) continue;
            view.ReleaseEngine();
            view.HungSince = null;
            view.SetFailure(null);
            view.CreateEngine();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var view in views) view.ReleaseEngine();
        views.Clear();
        if (Engine.IsPrewarmed) Engine.ReleasePrewarm();
    }

    // Called by WebSurfaceView.

    internal void Touch(WebSurfaceView view)
    {
        view.LastUsedAt = time.GetUtcNow();
        view.ActivityVersion++;
    }

    internal void SetShown(WebSurfaceView view, bool shown)
    {
        view.IsShown = shown;
        if (shown)
        {
            view.HiddenSince = null;
            Activate(view);
            return;
        }

        view.HiddenSince = time.GetUtcNow();
        if (view.Engine is not { } engine) return;
        engine.MemoryLevel = WebMemoryLevel.Low;
        // A hidden tab stops painting; a model-driven tab keeps rendering (DESIGN 5.10, spike e picks the hidden host).
        if (!view.IsModelStepRunning) view.SetLiveVisible(false);
    }

    internal void Activate(WebSurfaceView view)
    {
        Touch(view);
        switch (view.Liveness)
        {
            case WebLiveness.Discarded:
                view.CreateEngine(); // the host sees Pending and creates the platform view; the URL is restored on attach
                return;
            case WebLiveness.Suspended:
                view.Engine?.Resume();
                view.SetLiveness(WebLiveness.Live);
                view.RaiseBecameLive();
                break;
        }

        if (view.Engine is { } engine) engine.MemoryLevel = view.IsShown ? WebMemoryLevel.Normal : WebMemoryLevel.Low;
        EnforceCapSoon(except: view);
    }

    internal void OnViewLive(WebSurfaceView view)
    {
        Touch(view);
        if (view.Engine is { } engine) engine.MemoryLevel = view.IsShown ? WebMemoryLevel.Normal : WebMemoryLevel.Low;
        // The first view keeps the browser process group alive; the pre-warm hold is no longer needed.
        if (Engine.IsPrewarmed)
        {
            prewarmedAt = null;
            Engine.ReleasePrewarm();
        }

        view.RaiseBecameLive();
        EnforceCapSoon(except: view);
    }

    internal void OnNavigated(WebSurfaceView view) => Cache.NoteNavigation(view);

    internal void NoteIntent(WebSurfaceView view)
    {
        if (view.Liveness == WebLiveness.Live && view.Engine is { IsAttached: true } && Cache.ShouldRefresh(view, intent: true, visibleAndFocused: true))
            _ = RefreshAsync(view);
    }

    internal async Task<WebGestureFrame> CaptureForGestureAsync(WebSurfaceView view, CancellationToken cancellationToken)
    {
        if (view.Liveness != WebLiveness.Live || view.Engine is not { IsAttached: true, IsSuspended: false } engine)
            return new WebGestureFrame(view.LastSnapshot, null);

        var capture = CaptureAndStoreAsync(view, engine, cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = Task.Delay(Options.CaptureBudget, time, budget.Token);
        var first = await Task.WhenAny(capture, timer);
        if (first == capture)
        {
            budget.Cancel(); // synchronous: CancelAsync would run the callbacks on the pool and resume off this thread
            var fresh = await capture;
            return new WebGestureFrame(fresh ?? Cache.Newest(view), null);
        }

        // DESIGN 2.5: over budget, start with the intent cache's frame and cross-fade to the fresh one when it lands.
        var cached = Cache.Newest(view);
        if (cached is null) return new WebGestureFrame(await capture, null);
        return new WebGestureFrame(cached, capture);
    }

    internal async Task<bool> WaitForFramesAsync(IWebEngineView engine, int frames, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ping = engine.WaitForFramesAsync(frames, Timeout.InfiniteTimeSpan, limit.Token);
        var timer = Task.Delay(timeout, time, limit.Token);
        var first = await Task.WhenAny(ping, timer);
        limit.Cancel();
        return first == ping && ping.IsCompletedSuccessfully && ping.Result;
    }

    internal void OnAccelerator(WebSurfaceView view, WebAcceleratorKey e)
    {
        // DESIGN 3.7: decide at once (the browser waits for this handler), run the chord after the key event.
        if (!e.IsKeyDown || !IsReservedChord(e.Chord)) return;
        e.Handled = true;
        var chord = e.Chord;
        post(() => ReservedChordPressed?.Invoke(view, chord));
    }

    internal void OnProcessFailed(WebSurfaceView view, WebProcessFailed e)
    {
        switch (e.Kind)
        {
            case WebProcessFailureKind.RenderProcessExited:
            case WebProcessFailureKind.FrameRenderProcessExited:
                var stopped = RecordCrash(view.Url);
                view.HungSince = null;
                view.SetFailure(WebFailureCard.For(WebFailureKind.RendererCrashed, showsCard: true, automaticReloadsStopped: stopped));
                if (view.IsModelStepRunning) StepFailed?.Invoke(view, StepFailedCrashed);
                log.Info($"web: renderer exited ({e.Reason}, {e.ExitCode}) in view {view.Sequence}");
                break;
            case WebProcessFailureKind.RenderProcessUnresponsive:
                MarkHung(view);
                break;
            case WebProcessFailureKind.BrowserProcessExited:
                if (view.Failure?.Kind == WebFailureKind.BrowserGone) break; // every view reports it; handle once
                var first = true;
                foreach (var each in views.Where(v => v.Engine is not null).OrderBy(v => v.Sequence))
                {
                    each.HungSince = null;
                    each.SetFailure(WebFailureCard.For(WebFailureKind.BrowserGone, showsCard: first));
                    first = false;
                    if (each.IsModelStepRunning) StepFailed?.Invoke(each, StepFailedVisusStopped);
                }

                log.Error($"web: browser process exited ({e.Reason}, {e.ExitCode})");
                break;
            default:
                // DESIGN 2.7: a GPU process crash shows nothing (Chromium recovers); logged.
                log.Info($"web: {e.Kind} ({e.Reason}, {e.ExitCode}) in view {view.Sequence}");
                break;
        }
    }

    internal void OnFailureAction(WebSurfaceView view, WebFailureAction action)
    {
        switch (action)
        {
            case WebFailureAction.Reload:
                view.HungSince = null;
                view.SetFailure(null);
                if (view.Engine is { IsAttached: true } engine) engine.Reload(hard: false);
                else if (view.Liveness == WebLiveness.Discarded) Activate(view);
                break;
            case WebFailureAction.Wait:
                if (view.Failure?.Kind == WebFailureKind.RendererHung)
                {
                    view.HungSince = null;
                    view.SetFailure(null);
                }

                break;
            case WebFailureAction.CloseTab:
                CloseRequested?.Invoke(view);
                break;
            case WebFailureAction.RestartVisus:
                Restart();
                break;
            case WebFailureAction.InstallRuntime:
            case WebFailureAction.UpdateRuntime:
            case WebFailureAction.OpenRuntimePage:
                RuntimeActionRequested?.Invoke(action);
                break;
        }
    }

    internal bool AllowsAutomaticReload(string? url)
    {
        if (url is null || !crashes.TryGetValue(PageKey(url), out var times)) return true;
        Prune(times);
        return times.Count < Options.CrashLimit;
    }

    internal void EnforceCapSoon(WebSurfaceView? except = null) => _ = EnforceCapAsync(except);

    private async Task EnforceCapAsync(WebSurfaceView? except)
    {
        if (enforcing) return;
        enforcing = true;
        try
        {
            var tried = new HashSet<WebSurfaceView>();
            while (views.Count(v => v.Liveness == WebLiveness.Live && !v.IsSuspending) > Options.MaxLiveViews)
            {
                var shownOverCap = views.Count(v => v.IsShown) > Options.MaxLiveViews;
                var candidate = views
                    .Where(v => v.Liveness == WebLiveness.Live && !v.IsSuspending && !v.IsModelStepRunning && (v != except || !v.IsShown) && !tried.Contains(v)
                                && (!v.IsShown || shownOverCap)
                                && v.Engine is { IsAttached: true })
                    .OrderBy(v => v.IsShown)
                    .ThenBy(v => v.LastUsedAt)
                    .ThenBy(v => v.Sequence)
                    .FirstOrDefault();
                if (candidate is null)
                {
                    log.Info($"web: {LiveCount} live views, none can be suspended now");
                    break;
                }

                tried.Add(candidate);
                await SuspendAsync(candidate);
            }
        }
        finally
        {
            enforcing = false;
        }
    }

    private async Task SuspendAsync(WebSurfaceView view)
    {
        if (view.Engine is not { } engine) return;
        var activity = view.ActivityVersion;
        view.IsSuspending = true;
        try
        {
            // A shown view gets a current frame first, so its snapshot is what the user last saw.
            var shown = view.IsShown && view.IsLiveVisible;
            if (shown) await CaptureAndStoreAsync(view, engine, CancellationToken.None);
            if (view.Engine != engine || view.Liveness != WebLiveness.Live || view.ActivityVersion != activity) return;
            view.SetLiveness(WebLiveness.Suspended);
            if (shown && view.IsLiveVisible)
            {
                // DESIGN 2.5 order: the host puts the snapshot on screen, then hides the live view. Suspending needs the
                // view hidden, so wait for the host (bounded, for a host that is gone or not rendering).
                using var stop = new CancellationTokenSource();
                await Task.WhenAny(view.WhenLiveHiddenAsync(), Task.Delay(Options.HideWait, time, stop.Token));
                stop.Cancel();
                if (view.Engine != engine || view.Liveness != WebLiveness.Suspended) return; // restored or replaced meanwhile
            }

            view.SetLiveVisible(false);
            engine.MemoryLevel = WebMemoryLevel.Low;
            var suspended = await engine.SuspendAsync();
            if (view.Engine != engine) return;
            if (view.ActivityVersion != activity || view.Liveness != WebLiveness.Suspended)
            {
                // Activation can race the native await: its earlier Resume did not cancel TrySuspendAsync.
                if (suspended || engine.IsSuspended) engine.Resume();
                if (view.Liveness == WebLiveness.Suspended)
                {
                    view.SetLiveness(WebLiveness.Live);
                    view.RaiseBecameLive();
                }
                return;
            }
            if (!suspended)
            {
                // The page cannot be suspended now (audio playing, for one): it stays live; the cap tries the next view.
                view.SetLiveness(WebLiveness.Live);
                if (view.IsShown) engine.MemoryLevel = WebMemoryLevel.Normal;
                log.Info($"web: view {view.Sequence} could not be suspended");
            }
        }
        finally
        {
            view.IsSuspending = false;
        }
    }

    internal void OnPlatformViewUnusable(WebSurfaceView view)
    {
        // DESIGN 2.7: the platform gave a view that is not WebView2 (no runtime it could use): the runtime card, no engine.
        log.Error($"web: view {view.Sequence} got a platform view that is not WebView2; treating the runtime as missing");
        Runtime = new WebRuntimeStatus(WebRuntimeState.Missing, null);
        view.ReleaseEngine();
        view.HungSince = null;
        view.SetFailure(Runtime.Card(showsCard: !view.IsSandboxed));
        view.SetLiveness(WebLiveness.Closed);
    }

    private bool ShouldDiscard(WebSurfaceView view, DateTimeOffset now) =>
        view.Liveness is WebLiveness.Live or WebLiveness.Suspended
        && !view.IsShown
        && !view.IsModelStepRunning
        && view.Kind != WebSurfaceKind.VisusPrivateTab // ED-NEW-web-w7-2: discarding would end its InPrivate session
        && view.HiddenSince is { } hidden
        && now - hidden >= Options.DiscardAfter;

    private void Discard(WebSurfaceView view)
    {
        view.ReleaseEngine();
        view.HungSince = null;
        view.SetLiveness(WebLiveness.Discarded);
        log.Info($"web: view {view.Sequence} discarded to its snapshot after 10 minutes hidden");
    }

    private async Task ProbeAsync(WebSurfaceView view)
    {
        if (view.Engine is not { } engine) return;
        view.IsProbing = true;
        try
        {
            using var stop = new CancellationTokenSource();
            var ping = engine.WaitForFramesAsync(1, Timeout.InfiniteTimeSpan, stop.Token);
            var timer = Task.Delay(Options.HangTimeout, time, stop.Token);
            if (await Task.WhenAny(ping, timer) != ping)
            {
                // DESIGN 2.7: no requestAnimationFrame reply in 5 s: the page is not responding.
                if (view.Engine == engine && view.Liveness == WebLiveness.Live && view.IsLiveVisible) MarkHung(view);
            }

            if (await SafeAwait(ping) && view.Engine == engine && view.Failure?.Kind == WebFailureKind.RendererHung)
            {
                view.HungSince = null;
                view.SetFailure(null);
            }

            stop.Cancel();
        }
        finally
        {
            view.IsProbing = false;
        }
    }

    private static async Task<bool> SafeAwait(Task<bool> task)
    {
        try
        {
            return await task;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private void MarkHung(WebSurfaceView view)
    {
        if (view.Failure is not null) return;
        view.HungSince = time.GetUtcNow();
        view.SetFailure(WebFailureCard.For(WebFailureKind.RendererHung, showsCard: true, automaticReloadsStopped: !AllowsAutomaticReload(view.Url)));
    }

    private async Task RefreshAsync(WebSurfaceView view)
    {
        if (view.Engine is not { } engine) return;
        Cache.BeginRefresh(view);
        WebSnapshot? frame = null;
        try
        {
            frame = await engine.CaptureSnapshotAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            log.Info($"web: intent capture failed: {ex.Message}");
        }
        finally
        {
            Cache.EndRefresh(view, frame);
        }

        if (frame is not null) view.RaiseChanged();
    }

    private async Task<WebSnapshot?> CaptureAndStoreAsync(WebSurfaceView view, IWebEngineView engine, CancellationToken cancellationToken)
    {
        try
        {
            var frame = await engine.CaptureSnapshotAsync(cancellationToken);
            if (frame is not null)
            {
                Cache.Put(view, frame);
                view.RaiseChanged();
            }

            return frame;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            log.Info($"web: capture failed: {ex.Message}");
            return null;
        }
    }

    private void StartPrewarm()
    {
        if (prewarmTried || disposed || Runtime.State != WebRuntimeState.Ready || views.Count > 0) return;
        prewarmTried = true;
        prewarmedAt = time.GetUtcNow();
        _ = PrewarmAsync();
    }

    private async Task PrewarmAsync()
    {
        try
        {
            await Engine.PrewarmAsync(Profiles.For(WebSurfaceKind.PreviewTab), CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            prewarmedAt = null;
            log.Error($"web: pre-warm failed: {ex.Message}");
        }
    }

    private bool IsFocused(string windowId) => !windowFocus.TryGetValue(windowId, out var focused) || focused;

    private bool RecordCrash(string? url)
    {
        if (url is null) return false;
        var key = PageKey(url);
        if (!crashes.TryGetValue(key, out var times)) crashes[key] = times = [];
        times.Add(time.GetUtcNow());
        Prune(times);
        return times.Count >= Options.CrashLimit;
    }

    private void Prune(List<DateTimeOffset> times)
    {
        var cutoff = time.GetUtcNow() - Options.CrashWindow;
        times.RemoveAll(t => t < cutoff);
    }

    // "The same page": the address without its fragment.
    private static string PageKey(string url)
    {
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? url : url[..hash];
    }
}
