using Marrow.Services.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.3 and canvas find handlers; ED-NEW-web-w8c-1. One document owns a search.
public sealed class VisusFindController : IDisposable
{
    private readonly VisusSession session;
    private readonly SemaphoreSlim serial = new(1);
    private VisusTab? tab;
    private IWebEngineView? engine;
    private CancellationTokenSource? pending;
    private int version;
    private bool searching;
    private bool disposed;
    private bool listening;
    public VisusFindController(VisusSession session) { this.session = session; Resume(); }
    internal void Resume()
    {
        if (disposed || listening) return;
        listening = true; session.Changed += OnSelection;
    }
    internal void Suspend()
    {
        Close();
        if (!listening) return;
        listening = false; session.Changed -= OnSelection;
    }
    public bool IsOpen { get; private set; }
    public string CountText { get; private set; } = "";
    public WebFindResult Result { get; private set; } = new(0, -1);
    public event Action? Changed;
    public bool Open()
    {
        if (disposed) return false;
        if (IsOpen && ReferenceEquals(tab, session.Selected)) return true;
        Close();
        if (session.Selected is not { IsBlank: false } selected || selected.ReadyEngine() is not { IsAttached: true } page) return false;
        tab = selected; engine = page; IsOpen = true;
        page.FindResultChanged += OnResult;
        selected.Changed += OnTabChanged;
        selected.View!.NavigationStarting += OnNavigation;
        Changed?.Invoke();
        return true;
    }
    public async Task SearchAsync(string query, bool matchCase)
    {
        if (!IsOpen || engine is not { } page) return;
        var current = ++version;
        pending?.Cancel(); pending?.Dispose();
        var cancellation = pending = new CancellationTokenSource();
        var entered = false;
        try
        {
            await serial.WaitAsync(cancellation.Token);
            entered = true;
            if (!IsOpen || current != version) return;
            searching = true;
            page.StopFind();
            // Await issued engine work to completion: a cancelled managed await cannot cancel Chromium's search.
            var result = query.Length == 0 ? new WebFindResult(0, -1) : await page.FindAsync(query, matchCase, CancellationToken.None);
            if (current != version || !IsOpen || !ReferenceEquals(engine, page)) { page.StopFind(); return; }
            Result = result;
            CountText = query.Length == 0 ? "" : Format(result);
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (current == version && IsOpen) { CountText = "Find unavailable"; Result = new(-1, -1); Changed?.Invoke(); }
        }
        finally { if (entered) { searching = false; serial.Release(); } }
    }
    public void Move(bool previous)
    {
        if (!IsOpen || searching || Result.MatchCount <= 0 || engine is not { } page) return;
        try { if (previous) page.FindPrevious(); else page.FindNext(); }
        catch (Exception) { CountText = "Find unavailable"; Changed?.Invoke(); }
    }
    private static string Format(WebFindResult result) => result.MatchCount < 0 ? "Find unavailable" : result.MatchCount == 0 ? "No matches" : $"{result.ActiveMatchIndex + 1} of {result.MatchCount}";
    private void OnResult(object? sender, WebFindResult result)
    {
        if (!IsOpen || searching || !ReferenceEquals(sender, engine)) return;
        Result = result; CountText = Format(result); Changed?.Invoke();
    }
    private void OnSelection() { if (!ReferenceEquals(tab, session.Selected)) Close(); }
    private void OnTabChanged() { if (tab is { } current && (!ReferenceEquals(engine, current.View?.Engine) || engine?.IsAttached != true)) Close(); }
    private void OnNavigation(object? sender, WebNavigationStarting e) { if (e.IsMainFrame && !e.Cancel) Close(); }
    public void Close()
    {
        ++version; pending?.Cancel();
        if (engine is { } page)
        {
            page.FindResultChanged -= OnResult;
            try { page.StopFind(); } catch (Exception) { }
        }
        if (tab is { } old) { old.Changed -= OnTabChanged; if (old.View is { } view) view.NavigationStarting -= OnNavigation; }
        var wasOpen = IsOpen;
        tab = null; engine = null; IsOpen = false; CountText = ""; Result = new(0, -1);
        if (wasOpen) Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        Suspend(); disposed = true;
        pending?.Dispose(); pending = null;
    }
}
