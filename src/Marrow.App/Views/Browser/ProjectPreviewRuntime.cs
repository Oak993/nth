using System.ComponentModel;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.6-5.9 and 8.1; ED-NEW-web-w9-3. A preview tab owns its diagnostics and state, independent of pane visibility.
public sealed class ProjectPreviewRuntime : IDisposable
{
    private readonly VisusTab tab;
    private readonly ThreadRuntime? runtime;
    private CdpBroker? bound;
    private IWebEngineView? engine;
    private bool started;
    private bool disposed;
    private long editedBefore;
    private bool inTurn;
    private IDisposable? hotSubscription;
    public ProjectPreviewRuntime(VisusTab tab, ThreadRuntime? runtime)
    {
        this.tab = tab; this.runtime = runtime;
        Reload = new PreviewReload(tab.View!, VisusHost.Time, MainActor.Post);
        tab.View!.Changed += Bind;
        tab.View.NavigationStarting += Navigation;
        if (runtime is not null) runtime.PropertyChanged += TurnChanged;
        Bind();
    }
    public PreviewDiagnostics? Diagnostics { get; private set; }
    public PreviewEmulation? Emulation { get; private set; }
    public PreviewElementPicker? Picker { get; private set; }
    public PreviewReload Reload { get; }
    public StaticPreviewSite? Site { get; private set; }
    public event Action? Changed;
    public event Action? DiagnosticsChanged;
    public void Mount(StaticPreviewSite site)
    {
        Site = site;
        tab.View?.Engine?.SetResourceHandler(site);
        Reload.Watch(site.Root);
    }
    private void Bind()
    {
        if (disposed || tab.View is not { } view) return;
        if (!ReferenceEquals(bound, view.Cdp))
        {
            Diagnostics?.Dispose(); Emulation?.Dispose(); Picker?.Dispose(); hotSubscription?.Dispose();
            bound = view.Cdp; engine = view.Engine; started = false;
            if (Site is not null) engine?.SetResourceHandler(Site);
            Diagnostics = bound is null ? null : new PreviewDiagnostics(bound);
            Emulation = bound is null ? null : new PreviewEmulation(bound);
            Picker = bound is null ? null : new PreviewElementPicker(bound);
            if (Diagnostics is not null) Diagnostics.Changed += () => DiagnosticsChanged?.Invoke();
            if (Picker is not null) Picker.Picked += Picked;
            hotSubscription = bound?.Subscribe("Network.webSocketFrameReceived", e =>
            {
                // Vite update/full-reload and webpack/Next built/hot-update messages witness successful HMR, not pings.
                try
                {
                    using var packet = System.Text.Json.JsonDocument.Parse(e.ParamsJson);
                    var payload = packet.RootElement.GetProperty("response").GetProperty("payloadData").GetString();
                    using var message = System.Text.Json.JsonDocument.Parse(payload ?? "{}");
                    var value = message.RootElement;
                    var type = value.TryGetProperty("type", out var t) ? t.GetString() : value.TryGetProperty("action", out var a) ? a.GetString() : null;
                    if (type is "update" or "full-reload" or "built" or "sync" or "hot-update") Reload.HotReloaded();
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { }
            });
            Raise();
        }
        if (Site is not null && !ReferenceEquals(engine, view.Engine)) { engine = view.Engine; engine?.SetResourceHandler(Site); }
        if (!started && view.Engine is { IsAttached: true } && Diagnostics is not null) { started = true; _ = Start(); }
        Reload.Enabled = tab.PreviewState.ReloadOnSave;
    }
    private async Task Start()
    {
        try { await Diagnostics!.StartAsync(); Raise(); }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException) { }
    }
    private void Navigation(object? sender, WebNavigationStarting e) { if (!e.Cancel) Diagnostics?.Navigation(); }
    private void TurnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThreadRuntime.Phase) || runtime is null) return;
        if (runtime.Phase != RuntimePhase.Idle && !inTurn) { inTurn = true; editedBefore = Edited(); Reload.BeginTurn(); }
        else if (runtime.Phase == RuntimePhase.Idle && inTurn) { inTurn = false; Reload.EndTurn(Edited() > editedBefore); }
    }
    private long Edited() => runtime?.Turns.Sum(turn => turn.TouchedPaths?.Count ?? 0) ?? 0;
    private void Picked(PreviewElementContext context) => AddDraft("[Untrusted preview element]\n" + context.Selector + " · " + context.Width + " × " + context.Height + "\n" + context.Html + "\n" + context.Styles, context.Screenshot);
    public void AddDraft(string text, byte[]? screenshot = null)
    {
        if (runtime is null || disposed) return;
        var draft = runtime.Draft with { Quotes = runtime.Draft.Quotes.Appending(new ReplyQuote { Text = text }) };
        if (screenshot is { Length: > 0 } && runtime.VisusThreadFolder is { } folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "preview-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(path, screenshot);
                draft = draft with { Attachments = draft.Attachments.Appending(new Attachment { Name = "Preview element.png", Path = path, MimeType = "image/png" }) };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        runtime.Draft = draft;
    }
    private void Raise() => Changed?.Invoke();
    public void Dispose()
    {
        disposed = true;
        if (tab.View is { } view) { view.Changed -= Bind; view.NavigationStarting -= Navigation; }
        if (runtime is not null) runtime.PropertyChanged -= TurnChanged;
        hotSubscription?.Dispose(); Diagnostics?.Dispose(); Emulation?.Dispose(); Picker?.Dispose(); Reload.Dispose();
    }
}
