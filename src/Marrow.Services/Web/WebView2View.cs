using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Microsoft.Web.WebView2.Core;

namespace Marrow.Services.Web;

/// <summary>
/// One real web view (DESIGN 2.1, 2.2): the managed <c>CoreWebView2</c> and <c>CoreWebView2Controller</c> wrapped from the
/// pointers Avalonia's adapter exposes. Avalonia keeps owning the controller (it closes it when the
/// <c>NativeWebView</c> leaves the tree); this object adds nth's policy events beside Avalonia's own handlers. Every
/// member runs on the UI thread, where WebView2 requires it.
/// </summary>
internal sealed class WebView2View : IWebEngineView
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly WebView2Engine engine;
    private readonly Logger log = new(Logger.Subsystem, "web");
    private readonly List<CdpSubscription> cdpSubscriptions = [];
    private CoreWebView2? core;
    private CoreWebView2Controller? controller;
    private double scale = 1;
    private bool disposed;
    private readonly List<string> documentScripts = [];
    private IWebResourceHandler? resourceHandler;
    private bool resourcesHooked;
    private Task scriptsReady = Task.CompletedTask;

    public WebView2View(WebView2Engine engine, WebCreationOptions creation)
    {
        this.engine = engine;
        Creation = creation;
    }

    public WebCreationOptions Creation { get; }

    public bool IsAttached => core is not null;

    public string? Url => Try(() => core?.Source);
    public string? Title => Try(() => core?.DocumentTitle);
    public string? FaviconUrl => Try(() => core?.FaviconUri);
    public bool CanGoBack => Try(() => core?.CanGoBack ?? false);
    public bool CanGoForward => Try(() => core?.CanGoForward ?? false);
    public bool IsSuspended => Try(() => core?.IsSuspended ?? false);
    public bool IsPlayingAudio => Try(() => core?.IsDocumentPlayingAudio ?? false);
    public uint? BackgroundArgb { get; private set; }

    public double Zoom
    {
        get => Try(() => controller?.ZoomFactor ?? 1);
        set
        {
            if (controller is { } c) c.ZoomFactor = value;
        }
    }

    public WebMemoryLevel MemoryLevel
    {
        get => Try(() => core?.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low) ? WebMemoryLevel.Low : WebMemoryLevel.Normal;
        set => Safe(() =>
        {
            if (core is { } c) c.MemoryUsageTargetLevel = value == WebMemoryLevel.Low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
        });
    }

    public event EventHandler? Attached;
    public event EventHandler<WebNavigationStarting>? NavigationStarting;
    public event EventHandler<WebNavigationStarting>? FrameNavigationStarting;
    public event EventHandler<WebNavigationCompleted>? NavigationCompleted;
    public event EventHandler<WebNewWindowRequest>? NewWindowRequested;
    public event EventHandler<WebPermissionRequest>? PermissionRequested;
    public event EventHandler<WebDownloadRequest>? DownloadStarting;
    public event EventHandler? TitleChanged;
    public event EventHandler? FaviconChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler<WebProcessFailed>? ProcessFailed;
    public event EventHandler<WebMessage>? WebMessageReceived;
    public event EventHandler<WebAcceleratorKey>? AcceleratorKeyPressed;
    public event EventHandler<WebMoveFocusRequest>? MoveFocusRequested;
    public event EventHandler<WebExternalUriRequest>? ExternalUriRequested;
    public event EventHandler<WebAuthRequest>? AuthenticationRequested;
    public event EventHandler<WebScriptDialogRequest>? ScriptDialogOpening;
    public event EventHandler<WebCertificateErrorRequest>? CertificateErrorDetected;
    public event EventHandler<bool>? FullScreenChanged;
    public event EventHandler<WebNotification>? NotificationReceived;
    public event EventHandler<WebScreenCaptureRequest>? ScreenCaptureStarting;

    /// <summary>Takes one reference on each pointer (the adapter's getters hand out owned references) and wraps them.</summary>
    public void Attach(nint coreWebView2, nint controllerPointer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            if (core is not null) Detach();
            core = CoreWebView2.CreateFromComICoreWebView2(coreWebView2);
            try
            {
                controller = WebView2Engine.WrapController(controllerPointer);
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or InvalidCastException or COMException or InvalidOperationException)
            {
                controller = null;
            }

            if (controller is null) log.Error("web: controller wrap unavailable; reserved chords from pages and focus exit stay off");
            Apply(WebView2Engine.SettingsFor(Creation));
            Hook();
            RegisterSandbox();
            foreach (var subscription in cdpSubscriptions) subscription.Connect(core);
        }
        finally
        {
            if (coreWebView2 != 0) Marshal.Release(coreWebView2);
            if (controllerPointer != 0) Marshal.Release(controllerPointer);
        }

        Attached?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        if (core is null) return;
        foreach (var subscription in cdpSubscriptions) subscription.Disconnect();
        UnhookResources();
        Unhook();
        core = null;
        controller = null;
    }

    public void Navigate(string url)
    {
        if (core is null) return;
        if (scriptsReady.IsCompletedSuccessfully) core.Navigate(url);
        else NavigateWhenScriptsReady(scriptsReady, url);
    }

    public void AddDocumentCreatedScript(string script)
    {
        ArgumentException.ThrowIfNullOrEmpty(script);
        documentScripts.Add(script);
        if (core is { } c) scriptsReady = Task.WhenAll(scriptsReady, c.AddScriptToExecuteOnDocumentCreatedAsync(script));
    }

    public void SetResourceHandler(IWebResourceHandler? handler)
    {
        resourceHandler = handler;
        if (handler is null) UnhookResources();
        else HookResources();
    }
    public void GoBack() => core?.GoBack();
    public void GoForward() => core?.GoForward();
    public void Stop() => core?.Stop();

    public void Reload(bool hard)
    {
        if (core is null) return;
        if (!hard)
        {
            core.Reload();
            return;
        }

        _ = core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
    }

    public async Task<WebFindResult> FindAsync(string query, bool matchCase, CancellationToken cancellationToken)
    {
        if (core is null) return new WebFindResult(0, -1);
        try
        {
            var options = core.Environment.CreateFindOptions();
            options.FindTerm = query;
            options.IsCaseSensitive = matchCase;
            options.SuppressDefaultFindDialog = true;
            options.ShouldHighlightAllMatches = true;
            await core.Find.StartAsync(options).WaitAsync(cancellationToken);
            return new WebFindResult(core.Find.MatchCount, core.Find.ActiveMatchIndex);
        }
        catch (NotImplementedException)
        {
            // An older runtime without the Find API; the find bar's CDP fallback (W8c) takes over.
            return new WebFindResult(0, -1);
        }
    }

    public void FindNext() => Safe(() => core?.Find.FindNext());
    public void FindPrevious() => Safe(() => core?.Find.FindPrevious());
    public void StopFind() => Safe(() => core?.Find.Stop());
    public void ShowPrintUI() => core?.ShowPrintUI(CoreWebView2PrintDialogKind.System);

    public async Task<bool> PrintToPdfAsync(string path, CancellationToken cancellationToken) =>
        core is not null && await core.PrintToPdfAsync(path).WaitAsync(cancellationToken);

    public async Task<string> CallCdpAsync(string method, string paramsJson, string? sessionId, CancellationToken cancellationToken)
    {
        var view = core ?? throw new InvalidOperationException("The web view is not attached.");
        var call = sessionId is null
            ? view.CallDevToolsProtocolMethodAsync(method, paramsJson)
            : view.CallDevToolsProtocolMethodForSessionAsync(sessionId, method, paramsJson);
        return await call.WaitAsync(cancellationToken);
    }

    public IDisposable SubscribeCdp(string eventName, Action<WebCdpEvent> handler)
    {
        var subscription = new CdpSubscription(eventName, handler, cdpSubscriptions);
        cdpSubscriptions.Add(subscription);
        if (core is not null) subscription.Connect(core);
        return subscription;
    }

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        var view = core ?? throw new InvalidOperationException("The web view is not attached.");
        return await view.ExecuteScriptAsync(script).WaitAsync(cancellationToken);
    }

    public void PostMessage(string json) => core?.PostWebMessageAsJson(json);

    public async Task<byte[]?> GetFaviconAsync(CancellationToken cancellationToken)
    {
        if (core is not { } view || string.IsNullOrEmpty(Try(() => view.FaviconUri))) return null;
        try
        {
            using var stream = await view.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png).WaitAsync(cancellationToken);
            if (stream is null) return null;
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken);
            return copy.Length == 0 ? null : copy.ToArray();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null; // a favicon is decoration: a failed read shows the tab's glyph
        }
    }

    /// <summary>
    /// DESIGN 2.5: CDP <c>Page.captureScreenshot</c> (png at device scale, <c>optimizeForSpeed</c>, the visible
    /// viewport), with <c>CapturePreviewAsync</c> as the fallback.
    /// </summary>
    public async Task<WebSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken)
    {
        if (core is not { } view) return null;
        byte[]? png = null;
        var source = WebSnapshotSource.Cdp;
        try
        {
            var json = await view.CallDevToolsProtocolMethodAsync(
                "Page.captureScreenshot",
                "{\"format\":\"png\",\"optimizeForSpeed\":true,\"captureBeyondViewport\":false,\"fromSurface\":true}").WaitAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out var data)) png = data.GetBytesFromBase64();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or FormatException)
        {
            log.Info($"web: CDP capture failed, using the preview capture: {ex.Message}");
        }

        if (png is null)
        {
            using var stream = new MemoryStream();
            await view.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream).WaitAsync(cancellationToken);
            png = stream.ToArray();
            source = WebSnapshotSource.Preview;
        }

        var (width, height) = WebView2Engine.PngSize(png);
        if (width == 0) return null;
        return new WebSnapshot(png, width, height, scale, DateTimeOffset.UtcNow, BackgroundArgb, source);
    }

    public void SetBounds(double width, double height, double scale) => this.scale = scale > 0 ? scale : 1;

    public void SetVisible(bool visible) => Safe(() =>
    {
        if (controller is { } c && c.IsVisible != visible) c.IsVisible = visible;
    });

    /// <summary>A <c>requestAnimationFrame</c> ping through CDP <c>Runtime.evaluate</c> (awaits the promise).</summary>
    public async Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (core is not { } view) return false;
        var count = Math.Max(1, frames);
        var expression = "new Promise(r=>{let n=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         + ";const f=()=>{if(--n<=0)r(true);else requestAnimationFrame(f)};requestAnimationFrame(f)})";
        var parameters = new JsonObject { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true };
        try
        {
            var call = view.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters.ToJsonString(CdpRedactor.Wire));
            if (timeout == Timeout.InfiniteTimeSpan) await call.WaitAsync(cancellationToken);
            else await call.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<bool> SuspendAsync() => core is not null && await core.TrySuspendAsync();

    public void Resume() => Safe(() => core?.Resume());

    public void Dispose()
    {
        if (disposed) return;
        Detach();
        cdpSubscriptions.Clear();
        disposed = true;
        engine.ViewClosed();
    }

    private void Apply(WebViewSettingsPlan plan)
    {
        if (core is null) return;
        var s = core.Settings;
        Safe(() => s.AreHostObjectsAllowed = plan.AreHostObjectsAllowed);
        Safe(() => s.IsWebMessageEnabled = plan.IsWebMessageEnabled);
        Safe(() => s.AreDevToolsEnabled = plan.AreDevToolsEnabled);
        Safe(() => s.AreBrowserAcceleratorKeysEnabled = plan.AreBrowserAcceleratorKeysEnabled);
        Safe(() => s.AreDefaultScriptDialogsEnabled = plan.AreDefaultScriptDialogsEnabled);
        Safe(() => s.AreDefaultContextMenusEnabled = plan.AreDefaultContextMenusEnabled);
        Safe(() => s.IsPasswordAutosaveEnabled = plan.IsPasswordAutosaveEnabled);
        Safe(() => s.IsGeneralAutofillEnabled = plan.IsGeneralAutofillEnabled);
        Safe(() => s.IsZoomControlEnabled = plan.IsZoomControlEnabled);
        Safe(() => s.IsSwipeNavigationEnabled = plan.IsSwipeNavigationEnabled);
        Safe(() => s.IsStatusBarEnabled = plan.IsStatusBarEnabled);
        Safe(() => s.IsReputationCheckingRequired = plan.IsReputationCheckingRequired);
        Safe(() => s.IsNonClientRegionSupportEnabled = plan.IsNonClientRegionSupportEnabled);
    }

    private void Hook()
    {
        var c = core!;
        c.NavigationStarting += OnNavigationStarting;
        c.FrameNavigationStarting += OnFrameNavigationStarting;
        c.NavigationCompleted += OnNavigationCompleted;
        c.NewWindowRequested += OnNewWindowRequested;
        c.PermissionRequested += OnPermissionRequested;
        c.DownloadStarting += OnDownloadStarting;
        c.DocumentTitleChanged += OnTitleChanged;
        c.FaviconChanged += OnFaviconChanged;
        c.HistoryChanged += OnHistoryChanged;
        c.ProcessFailed += OnProcessFailed;
        c.WebMessageReceived += OnWebMessageReceived;
        c.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
        c.BasicAuthenticationRequested += OnBasicAuthenticationRequested;
        c.ScriptDialogOpening += OnScriptDialogOpening;
        c.ServerCertificateErrorDetected += OnServerCertificateErrorDetected;
        c.ContainsFullScreenElementChanged += OnFullScreenChanged;
        Safe(() => c.NotificationReceived += OnNotificationReceived);
        Safe(() => c.ScreenCaptureStarting += OnScreenCaptureStarting);
        if (controller is { } ctl)
        {
            ctl.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
            ctl.MoveFocusRequested += OnMoveFocusRequested;
        }
    }

    private void Unhook()
    {
        if (core is { } c)
        {
            Safe(() =>
            {
                c.NavigationStarting -= OnNavigationStarting;
                c.FrameNavigationStarting -= OnFrameNavigationStarting;
                c.NavigationCompleted -= OnNavigationCompleted;
                c.NewWindowRequested -= OnNewWindowRequested;
                c.PermissionRequested -= OnPermissionRequested;
                c.DownloadStarting -= OnDownloadStarting;
                c.DocumentTitleChanged -= OnTitleChanged;
                c.FaviconChanged -= OnFaviconChanged;
                c.HistoryChanged -= OnHistoryChanged;
                c.ProcessFailed -= OnProcessFailed;
                c.WebMessageReceived -= OnWebMessageReceived;
                c.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
                c.BasicAuthenticationRequested -= OnBasicAuthenticationRequested;
                c.ScriptDialogOpening -= OnScriptDialogOpening;
                c.ServerCertificateErrorDetected -= OnServerCertificateErrorDetected;
                c.ContainsFullScreenElementChanged -= OnFullScreenChanged;
            });
            Safe(() => c.NotificationReceived -= OnNotificationReceived);
            Safe(() => c.ScreenCaptureStarting -= OnScreenCaptureStarting);
        }

        if (controller is { } ctl)
        {
            Safe(() =>
            {
                ctl.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
                ctl.MoveFocusRequested -= OnMoveFocusRequested;
            });
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var e = new WebNavigationStarting(args.Uri, args.IsUserInitiated, args.IsRedirected, isMainFrame: true);
        NavigationStarting?.Invoke(this, e);
        if (e.Cancel) args.Cancel = true;
    }

    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var e = new WebNavigationStarting(args.Uri, args.IsUserInitiated, args.IsRedirected, isMainFrame: false);
        FrameNavigationStarting?.Invoke(this, e);
        if (e.Cancel) args.Cancel = true;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        var url = Url ?? string.Empty;
        var status = Try(() => args.HttpStatusCode);
        NavigationCompleted?.Invoke(this, new WebNavigationCompleted(url, args.IsSuccess, status, args.WebErrorStatus.ToString()));
        _ = ReadBackgroundAsync();
    }

    // DESIGN 2.5: the page's own background colour, read once per navigation.
    private async Task ReadBackgroundAsync()
    {
        if (core is not { } view) return;
        try
        {
            var json = await view.ExecuteScriptAsync(
                "(()=>{const s=e=>e?getComputedStyle(e).backgroundColor:'';return [s(document.body),s(document.documentElement)]})()");
            using var document = JsonDocument.Parse(json);
            uint? found = null;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    found = WebView2Engine.ParseCssColor(item.GetString());
                    if (found is not null) break;
                }
            }

            BackgroundArgb = found;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException)
        {
            BackgroundArgb = null;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // DESIGN 8.1: always handled; the page never gets a real window. nth opens a tab or blocks it.
        args.Handled = true;
        var e = new WebNewWindowRequest(args.Uri, args.IsUserInitiated);
        NewWindowRequested?.Invoke(this, e);
        e.CompleteIfUnanswered();
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        Safe(() => args.Handled = true); // never the default prompt
        var e = new WebPermissionRequest(Origin(args.Uri), WebView2Engine.Map(args.PermissionKind), args.IsUserInitiated);
        PermissionRequested?.Invoke(this, e);
        Settle(e, args.GetDeferral, decision =>
        {
            args.State = decision == WebPermissionDecision.Deny ? CoreWebView2PermissionState.Deny : CoreWebView2PermissionState.Allow;
            // W8b (ED-NEW-web-w8b-1): nth's PermissionStore is the only memory of an answer, so the profile never keeps one
            // that Settings › Site permissions does not list (and Remove works with no view open).
            Safe(() => args.SavesInProfile = false);
        });
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true; // nth's own prompt, never the default download dialog
        var operation = args.DownloadOperation;
        var total = Try(() => operation.TotalBytesToReceive);
        var e = new WebDownloadRequest(operation.Uri, Path.GetFileName(args.ResultFilePath), total is > 0 ? (long?)total : null, operation.MimeType, Origin(Url));
        DownloadStarting?.Invoke(this, e);
        Settle(e, args.GetDeferral, decision =>
        {
            if (decision.Save && decision.ResultFilePath is { } path)
            {
                args.ResultFilePath = path;
                args.Cancel = false;
                e.ReportStarted(new WebView2Download(operation, path));
            }
            else
            {
                args.Cancel = true;
                e.ReportStarted(null);
            }
        });
    }

    private void OnTitleChanged(object? sender, object e) => TitleChanged?.Invoke(this, EventArgs.Empty);
    private void OnFaviconChanged(object? sender, object e) => FaviconChanged?.Invoke(this, EventArgs.Empty);
    private void OnHistoryChanged(object? sender, object e) => HistoryChanged?.Invoke(this, EventArgs.Empty);

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        ProcessFailed?.Invoke(this, new WebProcessFailed(WebView2Engine.Map(args.ProcessFailedKind), args.Reason.ToString(), args.ExitCode));

    /// <summary>
    /// DESIGN 6.5: the document scripts go in before the first navigation (which waits for them), and every request goes
    /// to the resource handler.
    /// </summary>
    private void RegisterSandbox()
    {
        if (core is not { } c) return;
        var pending = documentScripts.Select(script => (Task)c.AddScriptToExecuteOnDocumentCreatedAsync(script)).ToList();
        scriptsReady = pending.Count == 0 ? Task.CompletedTask : Task.WhenAll(pending);
        resourcesHooked = false;
        if (resourceHandler is not null) HookResources();
    }

    private async void NavigateWhenScriptsReady(Task ready, string url)
    {
        try
        {
            await ready;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // Fail closed: a sandboxed page never loads without its hardening and bridge scripts.
            log.Error($"web: document scripts could not be added; the page stays blank: {error.Message}");
            return;
        }

        core?.Navigate(url);
    }

    private void HookResources()
    {
        if (core is not { } c || resourcesHooked) return;
        c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        c.WebResourceRequested += OnWebResourceRequested;
        resourcesHooked = true;
    }

    private void UnhookResources()
    {
        if (core is not { } c || !resourcesHooked) return;
        resourcesHooked = false;
        Safe(() =>
        {
            c.WebResourceRequested -= OnWebResourceRequested;
            c.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        });
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (resourceHandler is not { } handler || core is not { } c) return;
        WebResourceResponse? answer;
        try
        {
            answer = handler.Answer(new WebResourceRequest(args.Request.Uri, args.Request.Method, Map(args.ResourceContext)));
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // Fail closed: a handler that throws blocks the request.
            log.Error($"web: resource handler failed, request blocked: {error.Message}");
            answer = new WebResourceResponse(403, "Forbidden", [], []);
        }

        if (answer is null) return;
        var headers = string.Join("\r\n", answer.Headers.Select(h => h.Key + ": " + h.Value));
        args.Response = c.Environment.CreateWebResourceResponse(new MemoryStream(answer.Body, writable: false), answer.StatusCode, answer.ReasonPhrase, headers);
    }

    internal static WebResourceKind Map(CoreWebView2WebResourceContext context) => context switch
    {
        CoreWebView2WebResourceContext.Document => WebResourceKind.Document,
        CoreWebView2WebResourceContext.Stylesheet => WebResourceKind.Stylesheet,
        CoreWebView2WebResourceContext.Image => WebResourceKind.Image,
        CoreWebView2WebResourceContext.Media => WebResourceKind.Media,
        CoreWebView2WebResourceContext.Font => WebResourceKind.Font,
        CoreWebView2WebResourceContext.Script => WebResourceKind.Script,
        CoreWebView2WebResourceContext.XmlHttpRequest => WebResourceKind.XmlHttpRequest,
        CoreWebView2WebResourceContext.Fetch => WebResourceKind.Fetch,
        CoreWebView2WebResourceContext.EventSource => WebResourceKind.EventSource,
        CoreWebView2WebResourceContext.Websocket => WebResourceKind.WebSocket,
        CoreWebView2WebResourceContext.Manifest => WebResourceKind.Manifest,
        CoreWebView2WebResourceContext.Ping => WebResourceKind.Ping,
        _ => WebResourceKind.Other,
    };

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args) =>
        WebMessageReceived?.Invoke(this, new WebMessage(Origin(args.Source), args.WebMessageAsJson));

    private void OnLaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args)
    {
        var e = new WebExternalUriRequest(args.Uri, args.InitiatingOrigin, args.IsUserInitiated);
        args.Cancel = true; // DESIGN 8.3: default deny; only an explicit Open lets it through
        ExternalUriRequested?.Invoke(this, e);
        Settle(e, args.GetDeferral, open => args.Cancel = !open);
    }

    private void OnBasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs args)
    {
        var e = new WebAuthRequest(args.Uri, args.Challenge);
        AuthenticationRequested?.Invoke(this, e);
        Settle(e, args.GetDeferral, credentials =>
        {
            if (credentials is null)
            {
                args.Cancel = true;
                return;
            }

            args.Response.UserName = credentials.UserName;
            args.Response.Password = credentials.Password;
        });
    }

    private void OnScriptDialogOpening(object? sender, CoreWebView2ScriptDialogOpeningEventArgs args)
    {
        var kind = args.Kind switch
        {
            CoreWebView2ScriptDialogKind.Confirm => WebScriptDialogKind.Confirm,
            CoreWebView2ScriptDialogKind.Prompt => WebScriptDialogKind.Prompt,
            CoreWebView2ScriptDialogKind.Beforeunload => WebScriptDialogKind.BeforeUnload,
            _ => WebScriptDialogKind.Alert,
        };
        var e = new WebScriptDialogRequest(args.Uri, kind, args.Message, args.DefaultText);
        ScriptDialogOpening?.Invoke(this, e);
        Settle(e, args.GetDeferral, decision =>
        {
            if (!decision.Accept) return;
            if (decision.Text is { } text) args.ResultText = text;
            args.Accept();
        });
    }

    private void OnServerCertificateErrorDetected(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs args)
    {
        args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        var e = new WebCertificateErrorRequest(args.RequestUri, args.ErrorStatus.ToString());
        CertificateErrorDetected?.Invoke(this, e);
        Settle(e, args.GetDeferral, proceed => args.Action = proceed
            ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
            : CoreWebView2ServerCertificateErrorAction.Cancel);
    }

    private void OnNotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs args)
    {
        // W8b (DESIGN 5.5): never Windows' default notification; nth shows its own toast for browse tabs or drops it.
        Safe(() => args.Handled = true);
        var note = Try(() => args.Notification);
        if (note is null) return;
        var e = new WebNotification(Origin(Try(() => args.SenderOrigin)), Try(() => note.Title) ?? "", Try(() => note.Body) ?? "", Try(() => note.Tag), Try(() => note.IsSilent));
        NotificationReceived?.Invoke(this, e);
        if (e.Shown) Safe(note.ReportShown);
    }

    private void OnScreenCaptureStarting(object? sender, CoreWebView2ScreenCaptureStartingEventArgs args)
    {
        Safe(() => args.Handled = true);
        args.Cancel = true; // denied unless the surface lets the browser's own picker show (W8b)
        var e = new WebScreenCaptureRequest(Origin(Try(() => args.OriginalSourceFrameInfo?.Source)));
        ScreenCaptureStarting?.Invoke(this, e);
        Settle(e, args.GetDeferral, allow => args.Cancel = !allow);
    }

    private void OnFullScreenChanged(object? sender, object e) => FullScreenChanged?.Invoke(this, Try(() => core?.ContainsFullScreenElement ?? false));

    private void OnAcceleratorKeyPressed(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs args)
    {
        // Synchronous in windowed mode: decide at once, never call out of process here.
        var status = args.PhysicalKeyStatus;
        var chord = WebKeys.ChordFrom(status.ScanCode, status.IsExtendedKey != 0, Down(VkControl), Down(VkMenu), Down(VkShift));
        var kind = args.KeyEventKind switch
        {
            CoreWebView2KeyEventKind.KeyUp => WebKeyEventKind.KeyUp,
            CoreWebView2KeyEventKind.SystemKeyDown => WebKeyEventKind.SystemKeyDown,
            CoreWebView2KeyEventKind.SystemKeyUp => WebKeyEventKind.SystemKeyUp,
            _ => WebKeyEventKind.KeyDown,
        };
        var e = new WebAcceleratorKey(chord, args.VirtualKey, kind, status.WasKeyDown != 0);
        AcceleratorKeyPressed?.Invoke(this, e);
        if (e.Handled) args.Handled = true;
    }

    private void OnMoveFocusRequested(object? sender, CoreWebView2MoveFocusRequestedEventArgs args)
    {
        if (args.Reason == CoreWebView2MoveFocusReason.Programmatic) return;
        var e = new WebMoveFocusRequest(args.Reason == CoreWebView2MoveFocusReason.Next);
        MoveFocusRequested?.Invoke(this, e);
        if (e.Handled) args.Handled = true;
    }

    // Applies a decided request at once, or holds the platform deferral until the decision arrives (on the UI thread).
    private void Settle<TDecision>(WebDeferredRequest<TDecision> request, Func<CoreWebView2Deferral> deferral, Action<TDecision> apply)
    {
        request.CompleteIfUnanswered();
        if (request.Decision.IsCompleted)
        {
            Safe(() => apply(request.Decision.Result));
            return;
        }

        var held = deferral();
        var context = SynchronizationContext.Current;
        var scheduler = context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
        _ = request.Decision.ContinueWith(task =>
        {
            Safe(() => apply(task.Result));
            Safe(held.Complete);
        }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
    }

    private static string Origin(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : url ?? string.Empty;

    private static bool Down(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    private T Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException)
        {
            return default!;
        }
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException)
        {
            log.Info($"web: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetKeyState(int virtualKey);

    // A CDP event subscription that survives detach and re-attach of the platform view.
    private sealed class CdpSubscription(string eventName, Action<WebCdpEvent> handler, List<CdpSubscription> owner) : IDisposable
    {
        private CoreWebView2DevToolsProtocolEventReceiver? receiver;

        public void Connect(CoreWebView2 view)
        {
            Disconnect();
            receiver = view.GetDevToolsProtocolEventReceiver(eventName);
            receiver.DevToolsProtocolEventReceived += OnEvent;
        }

        public void Disconnect()
        {
            if (receiver is null) return;
            try
            {
                receiver.DevToolsProtocolEventReceived -= OnEvent;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // the view is gone
            }

            receiver = null;
        }

        public void Dispose()
        {
            Disconnect();
            owner.Remove(this);
        }

        private void OnEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
            handler(new WebCdpEvent(eventName, string.IsNullOrEmpty(args.SessionId) ? null : args.SessionId, args.ParameterObjectAsJson));
    }
}

/// <summary>
/// W8b (DESIGN 5.4): a saved WebView2 download as <see cref="IWebDownloadOperation"/>. A pause is the engine's Interrupted state
/// with the UserPaused reason; nth never opens the file (no <c>OpenDefaultDownloadDialog</c>, no shell open).
/// </summary>
internal sealed class WebView2Download : IWebDownloadOperation
{
    private readonly CoreWebView2DownloadOperation operation;
    private string path;

    public WebView2Download(CoreWebView2DownloadOperation operation, string path)
    {
        this.operation = operation;
        this.path = path;
        operation.BytesReceivedChanged += OnChanged;
        operation.StateChanged += OnStateChanged;
    }

    public string ResultFilePath => path;

    public long BytesReceived => Read(() => operation.BytesReceived, 0L);

    public long? TotalBytes => Read(() => operation.TotalBytesToReceive is > 0 and var total ? (long?)total : null, null);

    public WebDownloadState State => Read(() => operation.State switch
    {
        CoreWebView2DownloadState.Completed => WebDownloadState.Completed,
        CoreWebView2DownloadState.Interrupted when operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserPaused => WebDownloadState.Paused,
        CoreWebView2DownloadState.Interrupted => WebDownloadState.Interrupted,
        _ => WebDownloadState.InProgress,
    }, WebDownloadState.Interrupted);

    public string? InterruptReason => Read(() => operation.State == CoreWebView2DownloadState.Interrupted ? operation.InterruptReason.ToString() : null, null);

    public bool CanResume => Read(() => operation.CanResume, false);

    public event EventHandler? Changed;

    public void Cancel() => Run(operation.Cancel);

    public void Pause() => Run(operation.Pause);

    public void Resume() => Run(operation.Resume);

    private void OnChanged(object? sender, object e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnStateChanged(object? sender, object e)
    {
        var final = Read(() => operation.ResultFilePath, null);
        if (!string.IsNullOrEmpty(final)) path = final;
        Changed?.Invoke(this, EventArgs.Empty);
        if (State is WebDownloadState.Completed || State == WebDownloadState.Interrupted && !CanResume)
        {
            Run(() =>
            {
                operation.BytesReceivedChanged -= OnChanged;
                operation.StateChanged -= OnStateChanged;
            });
        }
    }

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException)
        {
            return fallback;
        }
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException)
        {
            // the operation is over or the browser is gone
        }
    }
}
