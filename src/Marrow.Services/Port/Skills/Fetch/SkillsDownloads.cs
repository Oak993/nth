// PORT-ONLY: first-release feature unit SK-16 (DL-19, skills-ONDEMAND-PLAN.md section 7: "Settings and offers share one download per item"). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// The one place a first download is started from (the host owns one). ENGINEERING DECISIONS (ED-368): (1) one download per item: an item asked for while its download is running joins it,
/// whoever asks (a Settings page or an in-chat offer), is told where it is at once and sees every later report; (2) an asker that leaves (its token is cancelled) stops waiting and leaves the
/// download running for the others, and when the last asker has left the transfer is cancelled (what it fetched stays for a later try, ONDEMAND 3.2); (3) an item that needs items not installed
/// fetches them first, each its own shared download, and its one progress bar runs across the need and the item weighted by their pinned bytes; (4) a failure reaches every asker and frees the item
/// so the next ask is a new download (Retry); (5) nothing starts on its own: a download is a call, never a restart's doing (the pipeline writes nothing of a download in flight to state.json).
/// </summary>
public sealed class SkillsDownloads(SkillsPipeline pipeline, SkillsCatalogue catalogue)
{
    private readonly object gate = new();
    private readonly Dictionary<string, FetchFlight> flights = new(StringComparer.Ordinal);
    private readonly List<Action> watchers = [];

    /// <summary>
    /// ENGINEERING DECISION (review): downloads begin in the order they were asked for. The pipeline runs one operation at a time and its wait is first come first served, so the order in which flights reach it is the
    /// order they are served in; starting each on the pool left that to chance (an item asked second could be served first, and a failing first item then waited behind a long one). This scheduler runs only the
    /// short part of a flight that ends at its first real wait, one flight at a time in the order queued; the transfer itself runs wherever it runs.
    /// </summary>
    private readonly TaskScheduler starts = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

    /// <summary>True when the download and binding seams are bound, so a download can be tried at all (see <see cref="CanFetch"/> for one item).</summary>
    public bool Available => pipeline.FetchingBound;

    /// <summary>True when the seams are bound and the item and every item it still needs have a complete pin (a pinned file is what is fetched; nothing is guessed).</summary>
    public bool CanFetch(CatalogueItem item) => Available && Chain(item).All(i => i.PinState == PinState.Pinned && CatalogueGate.Missing(i).Count == 0);

    /// <summary>The needs not installed, dependencies first, and the item itself.</summary>
    private List<CatalogueItem> Chain(CatalogueItem item)
    {
        var order = new List<CatalogueItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Visit(CatalogueItem next)
        {
            if (!seen.Add(next.Id)) return;
            foreach (var need in next.Needs) Visit(catalogue.Items.Single(i => i.Id == need));
            if (next == item || pipeline.InstalledFolder(next.Id) is null) order.Add(next);
        }
        Visit(item);
        return order;
    }

    public bool IsRunning(string id) { lock (gate) return flights.ContainsKey(id); }

    /// <summary>The persisted facts plus live downloads for offer detection only; this value is never written to state.json.</summary>
    public AddonsState StateForDetection
    {
        get
        {
            // Read the pipeline before taking our gate: download commits can notify this host.
            var state = pipeline.State;
            string[] live;
            lock (gate) live = flights.Keys.ToArray();
            if (live.Length == 0) return state;
            var records = state.Items.ToDictionary(record => record.Id, StringComparer.Ordinal);
            foreach (var id in live)
            {
                var record = state.Get(id);
                if (record.State is ItemState.Installed or ItemState.UpdateAvailable or ItemState.Removing) continue;
                var item = catalogue.Items.Single(item => item.Id == id);
                records[id] = new ItemRecord(id, ItemState.Downloading, record.InstalledPin, UpdateCheck.CataloguePin(item), null);
            }
            return AddonsState.Restore(records.Values, state.AcceptedTerms, state.Declined, state.Found);
        }
    }

    /// <summary>What a surface shows of a download running for the item, or null when none is.</summary>
    public FetchView? Running(string id)
    {
        lock (gate) return flights.TryGetValue(id, out var flight) ? new FetchView(flight.Last) : null;
    }

    /// <summary>Stops the download running for the item, for every asker (each is cancelled); what it fetched stays for a later try. Nothing when none is running.</summary>
    public void Stop(string id)
    {
        FetchFlight? flight;
        lock (gate)
        {
            if (!flights.Remove(id, out flight)) return;
        }
        flight.Cancel.Cancel();
        Notify();
    }

    /// <summary>Calls <paramref name="changed"/> (on whatever thread) when a download starts, reports or ends. Dispose to stop.</summary>
    public IDisposable Watch(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        lock (gate) watchers.Add(changed);
        return new Watcher(this, changed);
    }

    private void Notify()
    {
        Action[] copy;
        lock (gate) copy = watchers.ToArray();
        foreach (var watcher in copy)
        {
            try { watcher(); }
            catch (Exception) { /* a watcher cannot stop a download */ }
        }
    }

    /// <summary>
    /// Downloads and installs the item (and the needs it lacks) once, or joins the download already running for it. The task ends with the installed folder, with the failure, or cancelled when
    /// <paramref name="cancellation"/> is. An item that is here is not downloaded again.
    /// </summary>
    public Task<string> Install(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => Install(item, progress, cancellation, null);

    /// <summary>
    /// As <see cref="Install(CatalogueItem, IProgress{double?}, CancellationToken)"/>, and <paramref name="part"/> is told the own progress (0 to 1) of each item of the chain (a need and the item), so a card of several items
    /// can count a dependency they share once.
    /// </summary>
    public Task<string> Install(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation, Action<string, double>? part)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progress);
        cancellation.ThrowIfCancellationRequested();
        if (pipeline.InstalledFolder(item.Id) is { } here) return Task.FromResult(here);
        if (!CanFetch(item)) return Task.FromException<string>(new FirstInstallUnavailableException());
        return Join(item, progress, cancellation, part);
    }

    /// <summary>Joins only a flight captured under the gate; losing it never starts a replacement download.</summary>
    public Task<string> Attach(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation, Action<string, double>? part)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progress);
        cancellation.ThrowIfCancellationRequested();
        return Join(item, progress, cancellation, part, existingOnly: true);
    }

    /// <summary>The items a download of this one would fetch: the needs not installed (dependencies first) and the item; none when it is installed.</summary>
    public IReadOnlyList<CatalogueItem> Pending(CatalogueItem item) => pipeline.InstalledFolder(item.Id) is null ? Chain(item) : [];

    private async Task<string> Join(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation, Action<string, double>? part = null, bool existingOnly = false)
    {
        FetchFlight flight;
        var start = false;
        lock (gate)
        {
            if (!flights.TryGetValue(item.Id, out flight!))
            {
                if (existingOnly) throw new InvalidOperationException("No existing download is bound.");
                flights[item.Id] = flight = new FetchFlight(item.Id);
                start = true;
            }
            flight.Askers++;
            // RR-44: reserve the start in the same critical section as creation.
            // Observers/listeners outside this gate may block or re-enter; they cannot reorder starts.
            if (start) _ = Task.Factory.StartNew(() => Run(flight, item), CancellationToken.None, TaskCreationOptions.DenyChildAttach, starts).Unwrap();
        }
        using var listening = flight.Listen(progress, part);
        var left = 0;
        void Leave()
        {
            if (Interlocked.Exchange(ref left, 1) == 1) return;
            var stop = false;
            lock (gate)
            {
                if (--flight.Askers == 0 && !flight.Result.Task.IsCompleted)
                {
                    stop = true;
                    if (flights.TryGetValue(item.Id, out var current) && current == flight) flights.Remove(item.Id);
                }
            }
            if (!stop) return;
            flight.Cancel.Cancel();
            Notify();
        }
        using var registration = cancellation.Register(Leave);
        if (start)
        {
            Notify();
        }
        try { return await flight.Result.Task.WaitAsync(cancellation).ConfigureAwait(false); }
        finally { Leave(); }
    }

    private async Task Run(FetchFlight flight, CatalogueItem item)
    {
        string? path = null;
        Exception? failure = null;
        try
        {
            var token = flight.Cancel.Token;
            var chain = Chain(item);
            var weights = chain.Select(Bytes).ToArray();
            var total = (double)weights.Sum();
            double before = 0;
            for (var step = 0; step < chain.Count; step++)
            {
                var offset = before;
                var weight = weights[step];
                var scaled = new Scaled(value =>
                {
                    flight.Report(value is null ? null : (offset + value.Value * weight) / total);
                    Notify();
                });
                if (chain[step] == item) path = await pipeline.InstallAsync(item.Id, new Scaled(value => { if (value is { } own) flight.ReportPart(item.Id, own); scaled.Report(value); }), token).ConfigureAwait(false);
                else await Join(chain[step], scaled, token, flight.ReportPart).ConfigureAwait(false);
                before += weight;
            }
        }
        catch (Exception error) { failure = error; }
        lock (gate) { if (flights.TryGetValue(item.Id, out var current) && current == flight) flights.Remove(item.Id); }
        Notify();
        if (failure is OperationCanceledException) flight.Result.TrySetCanceled();
        else if (failure is not null) flight.Result.TrySetException(failure);
        else flight.Result.TrySetResult(path!);
    }

    /// <summary>The item's pinned bytes: what its progress is weighted by (at least one, so an item with no size still moves the bar).</summary>
    public static long Bytes(CatalogueItem item) => Math.Max(1, item.Files.Sum(f => f.Size) + item.Artifacts.Sum(a => a.Size ?? 0));

    private sealed class Scaled(Action<double?> report) : IProgress<double?>
    {
        public void Report(double? value) => report(value);
    }

    private sealed class Watcher(SkillsDownloads owner, Action changed) : IDisposable
    {
        public void Dispose() { lock (owner.gate) owner.watchers.Remove(changed); }
    }
}
