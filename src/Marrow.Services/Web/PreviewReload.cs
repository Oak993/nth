using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Port;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.6; ED-NEW-web-w9-2/5. Every batch is debounced; dirtiness survives non-live tabs and earlier HMR.
public sealed class PreviewReload : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);
    private readonly WebSurfaceView surface;
    private readonly TimeProvider time;
    private readonly ITimer timer;
    private readonly Action<Action> post;
    private readonly SerialQueue watchQueue = new("nth.preview.watch");
    private FileWatcher? watcher;
    private string? root;
    private double? scroll;
    private bool disposed;
    private bool enabled = true;
    private bool turn;
    private bool reloading;
    private bool hotInTurn;
    private bool nonLive;
    private long turnVersion;
    private long changeVersion;
    private long lastReloadVersion;
    private long? batchAt;
    private long reloadAttempt;
    public PreviewReload(WebSurfaceView surface, TimeProvider? time = null, Action<Action>? post = null)
    {
        this.surface = surface; this.time = time ?? TimeProvider.System; this.post = post ?? (work => work()); nonLive = surface.Liveness != WebLiveness.Live;
        timer = this.time.CreateTimer(_ => this.post(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        surface.NavigationCompleted += OnNavigation; surface.Changed += OnSurfaceChanged;
    }
    public bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Schedule(); }
    }
    public Task WhenIdle { get; private set; } = Task.CompletedTask;
    public int WatchBufferSize => watcher?.BufferSize ?? 0;
    public long WatchGeneration { get; private set; }
    public void Watch(string root)
    {
        if (disposed) return;
        this.root = root; RecreateWatch();
    }
    private void RecreateWatch()
    {
        watcher?.Cancel(); watcher = null;
        var generation = ++WatchGeneration;
        if (disposed || root is null) return;
        watcher = FileWatcher.WatchTree([root], watchQueue, batch => post(() =>
        {
            if (!disposed && WatchGeneration == generation) OnBatch(batch);
        }));
    }
    public void OnBatch(FileWatcherBatch batch)
    {
        if (disposed) return;
        // ED-114: every Error is Everything, including an overflow with no paths. Recover the watcher too.
        if (batch.Everything) RecreateWatch();
        FileChanged();
    }
    public void FileChanged()
    {
        if (disposed) return;
        changeVersion++; batchAt = time.GetTimestamp();
        timer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }
    public void BeginTurn() { turn = true; hotInTurn = false; turnVersion = changeVersion; }
    public void HotReloaded()
    {
        if (disposed) return;
        lastReloadVersion = changeVersion;
        if (turn) hotInTurn = true;
    }
    public void EndTurn(bool filesEdited)
    {
        if (disposed) return;
        turn = false;
        // The touched-path witness is the fallback for server previews without a static watch.
        if (filesEdited && changeVersion == turnVersion && !hotInTurn) changeVersion++;
        Schedule();
    }
    private TimeSpan Remaining => batchAt is { } at ? Debounce - time.GetElapsedTime(at) : TimeSpan.Zero;
    private void Schedule()
    {
        if (disposed || !enabled || turn || reloading || changeVersion == lastReloadVersion) return;
        var remaining = Remaining;
        if (remaining > TimeSpan.Zero) timer.Change(remaining, Timeout.InfiniteTimeSpan);
        else WhenIdle = Reload();
    }
    private void OnTimer()
    {
        if (disposed) return;
        // A root removed during Error may reappear. Do not leave the static preview without a watch.
        if (root is not null && watcher is null) { RecreateWatch(); if (watcher is null) timer.Change(Debounce, Timeout.InfiniteTimeSpan); }
        Schedule();
    }
    private void OnSurfaceChanged()
    {
        var nowNonLive = surface.Liveness != WebLiveness.Live;
        if (nowNonLive && !nonLive) { changeVersion++; reloadAttempt++; reloading = false; scroll = null; }
        nonLive = nowNonLive;
        // Give the tab's resume handler a turn to restore its resource handler before a new engine reloads.
        post(Schedule);
    }
    private async Task Reload()
    {
        if (disposed || !enabled || turn || reloading || changeVersion == lastReloadVersion || surface.Liveness != WebLiveness.Live || surface.Cdp is not { } cdp || surface.Engine is not { IsAttached: true } engine) return;
        reloading = true;
        var version = changeVersion;
        var attempt = ++reloadAttempt;
        try
        {
            var result = await cdp.CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = "scrollY", ["returnByValue"] = true });
            if (attempt != reloadAttempt) return;
            if (disposed || !enabled || turn || surface.Liveness != WebLiveness.Live || !ReferenceEquals(surface.Engine, engine)) { reloading = false; Schedule(); return; }
            if (version != changeVersion && Remaining > TimeSpan.Zero) { reloading = false; Schedule(); return; }
            scroll = PreviewJson.Number(PreviewJson.Field(result, "result"), "value");
            lastReloadVersion = changeVersion;
            engine.Reload(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or OperationCanceledException) { if (attempt == reloadAttempt) { scroll = null; reloading = false; } }
    }
    private void OnNavigation(object? sender, WebNavigationCompleted e)
    {
        if (disposed) return;
        if (reloading)
        {
            reloading = false;
            if (!e.IsSuccess) changeVersion++;
            if (scroll is { } y && e.IsSuccess && surface.Cdp is { } cdp)
                WhenIdle = cdp.CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = "scrollTo(0," + y.ToString(CultureInfo.InvariantCulture) + ")" });
            scroll = null;
            if (e.IsSuccess) Schedule();
        }
        else if (e.IsSuccess) HotReloaded();
    }
    public void Dispose()
    {
        disposed = true; WatchGeneration++; reloadAttempt++; timer.Dispose(); watcher?.Cancel(); watcher = null;
        surface.NavigationCompleted -= OnNavigation; surface.Changed -= OnSurfaceChanged;
    }
}
