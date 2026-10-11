// PORT-ONLY: first-release feature unit SK-15 (DL-17). The logic of one library page (Plugins or Skills): its rows, the selection, the single downloads, the Download all dialog's phases, the
// two-step removal and reset, the terms step and the switches. The page's views draw what this holds and send it the user's presses; nothing here touches Avalonia.
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Pages;

/// <summary>
/// ENGINEERING DECISIONS (ED-354): (1) the page keeps its own run state beside the pipeline's: an item that is queued, downloading, checking, failed or just installed by this page shows
/// that state until the pipeline's own state says the item is installed (the first-install path is SK-16's, ED-326 (6)(a)); (2) downloads of the page run one at a time, a second one waits
/// as Queued; (3) Pause cancels the transfer in flight and starts it again at Resume (the installer keeps what it fetched, 3.2); Cancel stops the rest and keeps what finished; (4) an item
/// under terms the user has not accepted, or one the catalogue holds no size for, is never part of a run; (5) a switch the backend cannot write is left as it was.
/// </summary>
public sealed class LibraryController : IDisposable
{
    private readonly LibrarySection section;
    private readonly ISkillsLibraryBackend backend;
    private readonly Action<Action> post;
    private readonly IDisposable? watching;
    private readonly object gate = new();
    private readonly SemaphoreSlim installing = new(1, 1);
    private readonly Dictionary<string, ItemRecord> overlay = [];
    private readonly Dictionary<string, double?> progress = [];
    private readonly Dictionary<string, CancellationTokenSource> transfers = [];
    private readonly HashSet<string> removeAsked = [];
    private Run? run;
    private DownloadAllPlan? confirmation;
    private string? selected;
    private bool resetAsked;

    /// <param name="post">Hands a progress report to the thread the page draws on; inline when null (a test).</param>
    public LibraryController(LibrarySection section, ISkillsLibraryBackend backend, Action<Action>? post = null)
    {
        this.section = section;
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.post = post ?? (action => action());
        // SK-16 (DL-19): a download another surface started (an in-chat offer) is shown here with its progress, and a change of it redraws the page.
        watching = backend.WatchFetching(() => this.post(() => { lock (gate) Notify(); }));
    }

    private sealed class Run(DownloadAllPlan plan)
    {
        public DownloadAllPlan Plan { get; } = plan;
        public RunPhase Phase { get; set; } = RunPhase.Downloading;
        public Dictionary<string, RunLineState> Results { get; } = [];
        public CancellationTokenSource Cancel { get; set; } = new();
        public bool PauseRequested { get; set; }
        public TaskCompletionSource<bool>? Resume { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
    }

    /// <summary>Raised after every change the views draw; the revision grows with it.</summary>
    public event Action? Changed;

    public int Revision { get; private set; }

    public LibrarySection Section => section;

    /// <summary>SK-16: whether the first-install seam can fetch at all (the seam that fails closed says no); the page keeps its Download controls and an unavailable item fails with the one unavailable message.</summary>
    public bool FetchingAvailable => backend.CanFetch;

    private void Notify()
    {
        Revision++;
        Changed?.Invoke();
    }

    // ---- rows

    public IReadOnlyList<LibraryRow> Rows
    {
        get
        {
            lock (gate)
            {
                return LibraryRows.Build(section, backend.Catalogue, backend.State, Effective, id => progress.GetValueOrDefault(id) ?? backend.Fetching(id)?.Progress, backend.Switches);
            }
        }
    }

    private ItemRecord Effective(string id)
    {
        var real = backend.State.Get(id);
        if (!overlay.TryGetValue(id, out var mine))
        {
            // SK-16: a download that is running for the item and that this page did not start (an offer's) shows as downloading, with the one progress both surfaces share.
            if (real.InstalledPin is null && real.State == ItemState.Available && backend.Fetching(id) is not null) return new ItemRecord(id, ItemState.Downloading, null, PinOf(Item(id)), null);
            return real;
        }
        if (mine.State == ItemState.Installed && real.InstalledPin is not null)
        {
            overlay.Remove(id);
            progress.Remove(id);
            return real;
        }
        return mine;
    }

    public string? SelectedId
    {
        get
        {
            var rows = Rows;
            return selected is not null && rows.Any(r => r.Id == selected) ? selected : rows.FirstOrDefault()?.Id;
        }
    }

    public LibraryRow? Selected => Rows.FirstOrDefault(r => r.Id == SelectedId);

    public void Select(string id)
    {
        if (selected == id || Rows.All(r => r.Id != id)) return;
        selected = id;
        Notify();
    }

    /// <summary>"Listed in every session: N skills": what the switches of the installed items put in every session.</summary>
    public string Listing => LibraryWords.ListedHeading(Rows.Sum(r => r.ListedOn));

    public IReadOnlyList<ProviderSkillGroup> Providers => backend.Providers().Where(g => g.Skills.Count > 0).ToList();

    // ---- single items

    private CatalogueItem Item(string id) => backend.Catalogue.Items.First(i => i.Id == id);

    private static string PinOf(CatalogueItem item) => UpdateCheck.CataloguePin(item) ?? "unpinned";

    private void Set(string id, ItemState state, string? failure = null)
    {
        var item = Item(id);
        var pin = PinOf(item);
        var installed = backend.State.Get(id).InstalledPin;
        overlay[id] = state switch
        {
            ItemState.Installed => new ItemRecord(id, ItemState.Installed, pin, null, null),
            ItemState.Failed => new ItemRecord(id, ItemState.Failed, installed, pin, failure ?? LibraryWords.DidNotDownload),
            _ => new ItemRecord(id, state, installed, pin, null),
        };
    }

    private void Clear(string id)
    {
        overlay.Remove(id);
        progress.Remove(id);
    }

    private sealed class Reporter(LibraryController owner, string id) : IProgress<double?>
    {
        public void Report(double? value) => owner.post(() =>
        {
            lock (owner.gate)
            {
                if (!owner.overlay.TryGetValue(id, out var record) || record.State is ItemState.Failed or ItemState.Installed) return;
                owner.progress[id] = value;
                if (value >= 1 && record.State == ItemState.Downloading) owner.Set(id, ItemState.Checking);
                owner.Notify();
            }
        });
    }

    /// <summary>
    /// Downloads one item (its Download button, or Retry). Ignored while the item is here, being fetched, under terms still to accept, or on a page whose Download all is running. The returned task ends when
    /// the item is installed, failed or cancelled.
    /// </summary>
    public Task Download(string id) => Download(id, CancellationToken.None);

    private Task Download(string id, CancellationToken cancellation)
    {
        lock (gate)
        {
            if (!backend.CanFetch) return Task.CompletedTask;
            if (!backend.CanFetchItem(id)) return Task.CompletedTask; // ED-NEW-dlhonest-1: an item that cannot be fetched is not tried (it would only fail)
            if (run is { Phase: RunPhase.Downloading or RunPhase.Paused }) return Task.CompletedTask;
            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row is null || row.TermsOwed || row.OnThisPc || row.Busy) return Task.CompletedTask;
            if (backend.State.Get(id) is { InstalledPin: not null } here) return here.State == ItemState.Damaged ? Repair(id) : Update(id); // a failed update or repair tries again as itself
            Set(id, ItemState.Queued);
            progress[id] = 0;
            Notify();
        }
        return Task.Run(() => InstallAsync(null, id, cancellation));
    }

    /// <summary>The cancel of one running or waiting item: its transfer stops (what was fetched stays for a resume) and the row goes back to what it was.</summary>
    public void CancelItem(string id)
    {
        lock (gate)
        {
            if (transfers.TryGetValue(id, out var transfer)) transfer.Cancel();
            else if (overlay.TryGetValue(id, out var record) && record.State == ItemState.Queued) { Clear(id); Notify(); }
            backend.CancelFetching(id); // SK-16: Cancel on the page stops the download for everyone who asked (an offer's card returns to asked)
        }
    }

    private async Task InstallAsync(Run? r, string id, CancellationToken runToken)
    {
        var item = Item(id);
        while (true)
        {
            if (r is { PauseRequested: true } && !await WaitForResume(r, runToken).ConfigureAwait(false)) { lock (gate) { Clear(id); Notify(); } return; }
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            try
            {
                await installing.WaitAsync(transfer.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    if (r is { PauseRequested: true } && !runToken.IsCancellationRequested) continue;
                    Clear(id);
                    Notify();
                    return;
                }
            }
            try
            {
                lock (gate)
                {
                    transfers[id] = transfer;
                    Set(id, ItemState.Downloading);
                    progress.TryAdd(id, 0);
                    Notify();
                }
                await backend.Installs.InstallAsync(item, new Reporter(this, id), transfer.Token).ConfigureAwait(false);
                lock (gate)
                {
                    Set(id, ItemState.Installed);
                    progress.Remove(id);
                    r?.Results[id] = RunLineState.Downloaded;
                    Notify();
                }
                return;
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    if (r is { PauseRequested: true } && !runToken.IsCancellationRequested)
                    {
                        Set(id, ItemState.Queued);
                        Notify();
                        continue;
                    }
                    Clear(id);
                    Notify();
                    return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                lock (gate)
                {
                    Set(id, ItemState.Failed, error.Message);
                    progress.Remove(id);
                    r?.Results[id] = RunLineState.Failed;
                    Notify();
                }
                return;
            }
            finally
            {
                lock (gate) transfers.Remove(id);
                installing.Release();
            }
        }
    }

    // ---- update, repair, remove

    public Task Update(string id) => Maintain(id, (progressReport, token) => backend.UpdateAsync(id, progressReport, token));

    public Task Repair(string id) => Maintain(id, (progressReport, token) => backend.RepairAsync(id, progressReport, token));

    private async Task Maintain(string id, Func<IProgress<double?>, CancellationToken, Task> work)
    {
        lock (gate)
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row is null || row.Busy || backend.State.Get(id).InstalledPin is null) return;
            Set(id, ItemState.Downloading);
            progress[id] = 0;
            Notify();
        }
        try
        {
            await Task.Run(() => work(new Reporter(this, id), CancellationToken.None)).ConfigureAwait(false);
            lock (gate) { Clear(id); Notify(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (gate) { Set(id, ItemState.Failed, error.Message); progress.Remove(id); Notify(); }
        }
    }

    public bool RemoveAsked(string id)
    {
        lock (gate) return removeAsked.Contains(id);
    }

    /// <summary>The trash icon: the first press asks (Q-V-13 B's pattern), a second press on Remove removes. Nothing is removed by the first.</summary>
    public void AskRemove(string id)
    {
        lock (gate)
        {
            if (Rows.FirstOrDefault(r => r.Id == id) is not { Busy: false } || backend.State.Get(id).InstalledPin is null || !removeAsked.Add(id)) return;
            Notify();
        }
    }

    public void CancelRemove(string id)
    {
        lock (gate)
        {
            if (removeAsked.Remove(id)) Notify();
        }
    }

    public async Task ConfirmRemove(string id)
    {
        lock (gate)
        {
            if (!removeAsked.Remove(id)) return;
            Set(id, ItemState.Removing);
            Notify();
        }
        try
        {
            await Task.Run(() => backend.RemoveAsync(id, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (gate) { Set(id, ItemState.Failed, error.Message); Notify(); }
            return;
        }
        lock (gate) { Clear(id); Notify(); }
    }

    public bool ResetAsked
    {
        get { lock (gate) return resetAsked; }
    }

    /// <summary>"Reset skills and tools": the first press asks, the second (Reset) runs it.</summary>
    public void AskReset()
    {
        lock (gate)
        {
            if (resetAsked) return;
            resetAsked = true;
            Notify();
        }
    }

    public void CancelReset()
    {
        lock (gate)
        {
            if (!resetAsked) return;
            resetAsked = false;
            Notify();
        }
    }

    public async Task ConfirmReset()
    {
        lock (gate)
        {
            if (!resetAsked) return;
            resetAsked = false;
            Notify();
        }
        await Task.Run(() => backend.ResetAsync(CancellationToken.None)).ConfigureAwait(false);
        lock (gate)
        {
            overlay.Clear();
            progress.Clear();
            Notify();
        }
    }

    // ---- terms and switches

    public TermsOffer? Terms(string id) => backend.Terms(id);

    public Task<string> LicenceText(string id, CancellationToken cancellation = default) => backend.LicenceTextAsync(id, cancellation);

    /// <summary>The separate "I have read these terms" control was used: the acceptance is stored with its date and the licence hash.</summary>
    public void AcceptTerms(string id)
    {
        lock (gate)
        {
            backend.AcceptTerms(id);
            Notify();
        }
    }

    /// <summary>A switch of the selected set or any set of the page, by its key; ignored while the set is not downloaded or the file cannot be written. It applies to new chats.</summary>
    public void SetSwitch(string key, bool on)
    {
        lock (gate)
        {
            var line = Rows.SelectMany(r => r.Switches).FirstOrDefault(l => l.Key == key);
            if (line is not { Enabled: true } || line.On == on) return;
            try { backend.SetSwitch(line, on); }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException) { return; }
            Notify();
        }
    }

    // ---- Download all

    /// <summary>
    /// ED-NEW-dlhonest-1, ED-NEW-dl-6: while fetching is bound, an item not here that cannot be fetched (<see cref="CanFetchItem"/>) is not downloaded by Download all (the run would only fail on it), and the
    /// confirmation lists it as skipped with the reason instead of leaving it out silently. Its row says why too.
    /// </summary>
    public DownloadAllPlan Plan => DownloadAllPlan.Build(section, Rows, backend.Catalogue, backend.Free(), backend.CanFetch ? backend.CanFetchItem : null);

    /// <summary>ED-NEW-dlhonest-1: whether the item can be downloaded now (the page's Download, Retry and Check again are on only then; otherwise the row shows that downloading isn't available).</summary>
    public bool CanFetchItem(string id) => backend.CanFetch && backend.CanFetchItem(id);

    /// <summary>The button at the foot of the list: "Download all plugins · 32 MB", or "All plugins are here".</summary>
    public string DownloadAllLabel
    {
        get
        {
            var plan = Plan;
            if (!plan.IsEmpty) return LibraryWords.DownloadAllButton(section, plan.DownloadMb);
            return plan.Lines.Any(l => l.Kind is PlanLineKind.NeedsTerms or PlanLineKind.NoSize or PlanLineKind.CantFetch) ? "Download all " + section.Noun() : LibraryWords.DownloadAllButton(section, null);
        }
    }

    public bool CanFetch => backend.CanFetch;

    // ---- "Download it yourself" (the design's fallback block under a failed item)

    /// <summary>Step 1 of the fallback for the item (its file and where from, the design's words), or null when the design draws none for it.</summary>
    public ManualFallback? ManualFallbackOf(string id) => backend.ManualFallbackOf(id); // the item's own entry (the Tools page shows tools as sets; its fallback is still a tool's)

    /// <summary>The folder step 2 names, as the design writes it.</summary>
    public string ManualFolderText => ManualFallbackWords.Folder;

    /// <summary>
    /// "Check again": the files the user put in the folder are checked against the item's pinned inputs (length and SHA-256; a file that is none of them is left out) and the item is downloaded the normal way, so
    /// what matches is used without a connection and the rest is fetched. Only a failed item has the block, so any other does nothing.
    /// </summary>
    public Task CheckAgain(string id)
    {
        CatalogueItem item;
        ItemRecord? before;
        CancellationTokenSource placement;
        lock (gate)
        {
            if (!backend.CanFetch || !backend.CanFetchItem(id)) return Task.CompletedTask; // unbound, or not fetchable: nothing is placed and nothing is asked of the install
            if (backend.State.Get(id).State != ItemState.Failed && !(overlay.TryGetValue(id, out var mine) && mine.State == ItemState.Failed)) return Task.CompletedTask;
            if (Rows.FirstOrDefault(row => row.Id == id) is not { Busy: false }) return Task.CompletedTask;
            item = backend.Catalogue.Items.First(i => i.Id == id);
            overlay.TryGetValue(id, out before);
            transfers[id] = placement = new CancellationTokenSource();
            Set(id, ItemState.Checking);
            progress.Remove(id);
            Notify();
        }
        void Restore()
        {
            if (before is null) Clear(id);
            else overlay[id] = before;
        }
        return Task.Run(async () =>
        {
            try
            {
                _ = backend.PlaceManualFiles(item);
                Task download;
                lock (gate)
                {
                    placement.Token.ThrowIfCancellationRequested();
                    Restore();
                    // Keep the original guards and update/repair routing. Cancellation also covers the queued handoff.
                    download = Download(id, placement.Token);
                    Notify();
                }
                await download.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (gate) { Restore(); Notify(); }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                lock (gate) { Set(id, ItemState.Failed, error.Message); progress.Remove(id); Notify(); }
            }
            finally
            {
                lock (gate)
                    if (transfers.TryGetValue(id, out var current) && current == placement) transfers.Remove(id);
                placement.Dispose();
            }
        });
    }

    public void OpenManualPage(string id)
    {
        if (ManualFallbackOf(id) is { } fallback) backend.OpenManualPage(fallback.Page);
    }

    public void OpenManualFolder() => backend.OpenManualFolder();

    public bool CanDownloadAll => backend.CanFetch && (run is { Phase: RunPhase.Downloading or RunPhase.Paused or RunPhase.Done or RunPhase.Partial } || !Plan.IsEmpty);

    /// <summary>ED-NEW-dl-6: why Download all is off when items are not here but none of them can be fetched by this build (null otherwise), so the off button is never silent.</summary>
    public string? DownloadAllNote
    {
        get
        {
            if (CanDownloadAll) return null;
            var plan = Plan;
            return plan.Lines.Any(l => l.Kind == PlanLineKind.CantFetch) ? LibraryWords.DownloadsUnavailable : null;
        }
    }

    /// <summary>Opens the confirmation. Nothing downloads until <see cref="Continue"/>. While a run goes on, its dialog is what opens again.</summary>
    public void OpenDownloadAll()
    {
        lock (gate)
        {
            if (run is not null) { Notify(); return; }
            var plan = Plan;
            if (plan.IsEmpty) return;
            confirmation = plan;
            Notify();
        }
    }

    /// <summary>Cancel in the confirmation: the dialog closes and nothing was started.</summary>
    public void CancelConfirmation()
    {
        lock (gate)
        {
            if (confirmation is null) return;
            confirmation = null;
            Notify();
        }
    }

    /// <summary>Continue: the items of the plan download one at a time. The task ends when the run has ended (finished, partly failed or cancelled).</summary>
    public Task Continue()
    {
        lock (gate)
        {
            if (!backend.CanFetch) return Task.CompletedTask;
            if (confirmation is not { CanContinue: true } plan) return Task.CompletedTask;
            confirmation = null;
            run = new Run(plan);
            foreach (var id in plan.ToDownload)
            {
                Set(id, ItemState.Queued);
                progress[id] = 0;
            }
            var started = run;
            started.Task = Task.Run(() => RunAsync(started, plan.ToDownload));
            Notify();
            return started.Task;
        }
    }

    private async Task RunAsync(Run r, IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            if (r.Cancel.IsCancellationRequested) break;
            await InstallAsync(r, id, r.Cancel.Token).ConfigureAwait(false);
        }
        lock (gate)
        {
            if (r.Cancel.IsCancellationRequested)
            {
                foreach (var id in ids.Where(i => overlay.TryGetValue(i, out var o) && o.State is ItemState.Queued or ItemState.Downloading or ItemState.Checking)) Clear(id);
                if (ReferenceEquals(run, r)) run = null;
            }
            else r.Phase = r.Results.Values.Any(s => s == RunLineState.Failed) ? RunPhase.Partial : RunPhase.Done;
            Notify();
        }
    }

    private async Task<bool> WaitForResume(Run r, CancellationToken token)
    {
        Task<bool> wait;
        lock (gate)
        {
            r.Phase = RunPhase.Paused;
            r.Resume ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wait = r.Resume.Task;
            Notify();
        }
        using var registration = token.Register(() => r.Resume!.TrySetResult(false));
        var resumed = await wait.ConfigureAwait(false);
        lock (gate)
        {
            r.Resume = null;
            if (resumed) { r.Phase = RunPhase.Downloading; Notify(); }
        }
        return resumed;
    }

    /// <summary>Pause: the transfer in flight stops where it is (the installer keeps what it fetched) and nothing else starts until Resume.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (run is not { Phase: RunPhase.Downloading } r || r.PauseRequested) return;
            r.PauseRequested = true;
            foreach (var transfer in transfers.Values) transfer.Cancel();
            if (transfers.Count == 0) { r.Phase = RunPhase.Paused; Notify(); }
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (run is not { PauseRequested: true } r) return;
            r.PauseRequested = false;
            r.Resume?.TrySetResult(true);
            if (r.Resume is null) { r.Phase = RunPhase.Downloading; Notify(); }
        }
    }

    /// <summary>Cancel in a running dialog: the rest is not fetched and what already downloaded stays. The dialog closes.</summary>
    public void StopRun()
    {
        lock (gate)
        {
            if (run is not { Phase: RunPhase.Downloading or RunPhase.Paused } r) return;
            r.Cancel.Cancel();
            r.Resume?.TrySetResult(false);
        }
    }

    /// <summary>The task of the run that is going (or went last), for the one who waits for its end.</summary>
    public Task RunCompletion => run?.Task ?? Task.CompletedTask;

    /// <summary>Retry in the partly failed dialog: only the failed items are fetched again.</summary>
    public Task RetryFailed()
    {
        lock (gate)
        {
            if (!backend.CanFetch) return Task.CompletedTask;
            if (run is not { Phase: RunPhase.Partial } r) return Task.CompletedTask;
            var failed = r.Plan.ToDownload.Where(id => r.Results.TryGetValue(id, out var s) && s == RunLineState.Failed).ToList();
            foreach (var id in failed)
            {
                r.Results.Remove(id);
                Set(id, ItemState.Queued);
                progress[id] = 0;
            }
            r.Phase = RunPhase.Downloading;
            r.Cancel = new CancellationTokenSource();
            var again = r;
            again.Task = Task.Run(() => RunAsync(again, failed));
            Notify();
            return again.Task;
        }
    }

    /// <summary>Close, in the finished dialogs.</summary>
    public void CloseRun()
    {
        lock (gate)
        {
            if (run is not { Phase: RunPhase.Done or RunPhase.Partial }) return;
            run = null;
            Notify();
        }
    }

    // ---- the dialog

    public RunView Dialog
    {
        get
        {
            lock (gate)
            {
                if (confirmation is { } plan) return Confirm(plan);
                return run is { } r ? Running(r) : RunView.Closed;
            }
        }
    }

    private RunView Confirm(DownloadAllPlan plan)
    {
        var lines = plan.Lines.Select(l => l.Kind switch
        {
            PlanLineKind.AlreadyHere => new RunLine(l.Id, l.Title, RunLineState.AlreadyHere, 0, LibraryWords.AlreadyHere),
            PlanLineKind.NeedsTerms => new RunLine(l.Id, l.Title, RunLineState.NeedsTerms, 0, l.TermsLabel ?? ""),
            PlanLineKind.NoSize => new RunLine(l.Id, l.Title, RunLineState.NoSize, 0, LibraryWords.SizeUnknown),
            PlanLineKind.CantFetch => new RunLine(l.Id, l.Title, RunLineState.CantFetch, 0, LibraryWords.NotAvailable),
            _ => new RunLine(l.Id, l.Title, RunLineState.Waiting, 0, SizeText.Short(l.SizeMb ?? 0)),
        }).ToList();
        return new RunView(RunPhase.Confirm, plan.Title, plan.Sub, plan.Statement, plan.Large && !plan.IsEmpty, LibraryWords.ConfirmFoot, null, 0, lines, plan.IncludedHeading, plan.DownloadFact,
            plan.DiskFact, plan.Free?.Label, plan.FreeFact, plan.HostsFact, plan.RoomNote, plan.CanContinue && backend.CanFetch, 0);
    }

    private RunView Running(Run r)
    {
        var plan = r.Plan;
        var failed = r.Results.Count(p => p.Value == RunLineState.Failed);
        var done = 0;
        double doneMb = 0;
        var lines = new List<RunLine>();
        foreach (var l in plan.Lines)
        {
            if (l.Kind != PlanLineKind.Download)
            {
                lines.Add(l.Kind switch
                {
                    PlanLineKind.AlreadyHere => new RunLine(l.Id, l.Title, RunLineState.AlreadyHere, 0, LibraryWords.AlreadyHere),
                    PlanLineKind.NeedsTerms => new RunLine(l.Id, l.Title, RunLineState.NeedsTerms, 0, l.TermsLabel ?? ""),
                    PlanLineKind.CantFetch => new RunLine(l.Id, l.Title, RunLineState.CantFetch, 0, LibraryWords.NotAvailable),
                    _ => new RunLine(l.Id, l.Title, RunLineState.NoSize, 0, LibraryWords.SizeUnknown),
                });
                continue;
            }
            var mb = l.SizeMb ?? 0;
            var fraction = progress.GetValueOrDefault(l.Id) ?? 0;
            overlay.TryGetValue(l.Id, out var record);
            if (r.Results.TryGetValue(l.Id, out var result) && result == RunLineState.Downloaded)
            {
                done++;
                doneMb += mb;
                lines.Add(new RunLine(l.Id, l.Title, RunLineState.Downloaded, 1, LibraryWords.Downloaded));
            }
            else if (result == RunLineState.Failed)
            {
                doneMb += fraction * mb;
                lines.Add(new RunLine(l.Id, l.Title, RunLineState.Failed, fraction, LibraryWords.DidNotDownload));
            }
            else if (record?.State == ItemState.Checking)
            {
                done++;
                doneMb += mb;
                lines.Add(new RunLine(l.Id, l.Title, RunLineState.Checking, 1, LibraryWords.Checking));
            }
            else if (record?.State == ItemState.Downloading)
            {
                doneMb += fraction * mb;
                lines.Add(new RunLine(l.Id, l.Title, RunLineState.Downloading, fraction, (int)Math.Floor(fraction * 100) + "%"));
            }
            else lines.Add(new RunLine(l.Id, l.Title, RunLineState.Waiting, 0, LibraryWords.WaitingWith(l.SizeMb)));
        }
        var total = plan.DownloadMb;
        var overall = total > 0 ? Math.Clamp(doneMb / total, 0, 1) : 0;
        var paused = r.Phase == RunPhase.Paused;
        var items = plan.ToDownload.Count;
        var (title, sub, foot) = r.Phase switch
        {
            RunPhase.Paused => (LibraryWords.PausedTitle, LibraryWords.PausedSub, LibraryWords.RunningFoot),
            RunPhase.Done => (LibraryWords.DoneTitle, LibraryWords.DoneSub(items, plan.DiskFact), LibraryWords.DoneFoot),
            RunPhase.Partial => (LibraryWords.PartialTitle(items - failed, items), LibraryWords.PartialSub(failed), LibraryWords.PartialFoot),
            _ => (LibraryWords.RunningTitle(section), LibraryWords.RunningSub, LibraryWords.RunningFoot),
        };
        return new RunView(r.Phase, title, sub, null, false, foot, LibraryWords.Progress(paused, (int)Math.Floor(overall * 100), doneMb, total, done, items), overall, lines, plan.IncludedHeading,
            plan.DownloadFact, plan.DiskFact, plan.Free?.Label, plan.FreeFact, plan.HostsFact, null, false, failed);
    }

    public void Dispose()
    {
        watching?.Dispose();
        lock (gate)
        {
            run?.Cancel.Cancel();
            run?.Resume?.TrySetResult(false);
            foreach (var transfer in transfers.Values) transfer.Cancel();
        }
    }
}
