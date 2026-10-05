// PORT-ONLY: first-release feature unit UE-2 (BUILD-PLAN.md 2.4 UE-2, OD-0100). The orchestrator behind "Update everything" (places A and D). Pure: items come through IUpdateItem.
namespace Marrow.Core.Port.Updates;

/// <summary>
/// Check now, Update everything and Retry over a list of items. OD-0100 pick: one visible control, items one at a time, Marrow last. A run is refused, not queued, while another is going.
/// Rows are listed in run order (<see cref="UpdateItemKind"/>, then as given). ENGINEERING DECISIONS (ED-308): an item's failure never stops the others (Marrow still runs last);
/// a cancel puts the running and the waiting rows back to the state they had; "up to date" is the plain equality of the version numbers inside the two texts (never an order).
/// </summary>
public sealed class UpdateOrchestrator
{
    private readonly object gate = new();
    private readonly List<IUpdateItem> items;
    private List<UpdateRow> rows;
    private int busy;

    public UpdateOrchestrator(IEnumerable<IUpdateItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var given = items.ToList();
        if (given.GroupBy(i => i.Id, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new ArgumentException("Two update items have the same id.", nameof(items));
        this.items = given.Select((item, index) => (item, index)).OrderBy(t => t.item.Kind).ThenBy(t => t.index).Select(t => t.item).ToList();
        rows = this.items.Select(i => new UpdateRow(i.Id, i.Title, i.Kind, UpdateRowState.Unchecked, null, null, null, null, false)).ToList();
    }

    /// <summary>Raised after every change of a row.</summary>
    public event Action? Changed;

    public IReadOnlyList<UpdateRow> Rows { get { lock (gate) return rows.ToList(); } }

    public bool IsBusy => Volatile.Read(ref busy) != 0;

    /// <summary>True once an item that needs the app restarted (Marrow's own update) has been updated: the page then offers "Restart Marrow".</summary>
    public bool RestartRequired { get { lock (gate) return rows.Any(r => r.RestartRequired); } }

    /// <summary>True when every installed row is up to date or was just updated (the "Up to date" state).</summary>
    public bool IsEverythingCurrent { get { lock (gate) return rows.All(r => r.State is UpdateRowState.NotInstalled or UpdateRowState.UpToDate or UpdateRowState.Updated); } }

    private UpdateRow Get(string id) { lock (gate) return rows.Single(r => r.Id == id); }

    private void Set(string id, Func<UpdateRow, UpdateRow> change)
    {
        lock (gate) rows = rows.Select(r => r.Id == id ? change(r) : r).ToList();
        Changed?.Invoke();
    }

    private void Begin()
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("An update run is already going.");
    }

    private void End() => Volatile.Write(ref busy, 0);

    /// <summary>"Check now": reads every item's installed and latest version. An item with no listing, or whose listing cannot be reached, is "Checks when updated".</summary>
    public async Task CheckNow(CancellationToken cancellation = default)
    {
        Begin();
        try { await Check(cancellation); }
        finally { End(); }
    }

    private async Task Check(CancellationToken cancellation)
    {
        foreach (var item in items)
        {
            cancellation.ThrowIfCancellationRequested();
            string? installed;
            try { installed = await item.InstalledVersion(cancellation); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Set(item.Id, r => r with { State = UpdateRowState.Failed, Installed = null, Latest = null, Progress = null, Message = error.Message });
                continue;
            }
            if (installed is null)
            {
                Set(item.Id, r => r with { State = UpdateRowState.NotInstalled, Installed = null, Latest = null, Progress = null, Message = null });
                continue;
            }
            string? latest = null;
            string? message = null;
            try { latest = await item.LatestVersion(cancellation); }
            catch (Exception error) when (error is not OperationCanceledException) { message = error.Message; }
            var state = latest is null ? UpdateRowState.ChecksWhenUpdated : VersionText.Same(installed, latest) ? UpdateRowState.UpToDate : UpdateRowState.UpdateAvailable;
            Set(item.Id, r => r with { State = state, Installed = installed, Latest = latest, Progress = null, Message = message });
        }
    }

    /// <summary>"Update everything": checks what was never checked, then updates every row that needs it (an update is available, or the version cannot be known beforehand, or an earlier try failed), one at a time, Marrow last.</summary>
    public async Task UpdateEverything(CancellationToken cancellation = default)
    {
        Begin();
        try
        {
            if (Rows.Any(r => r.State == UpdateRowState.Unchecked)) await Check(cancellation);
            var due = Rows.Where(r => r.State is UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated or UpdateRowState.Failed).Select(r => r.Id).ToList();
            await Run(due, cancellation);
        }
        finally { End(); }
    }

    /// <summary>Runs one failed item again, alone.</summary>
    public async Task Retry(string id, CancellationToken cancellation = default)
    {
        if (items.All(i => i.Id != id)) throw new ArgumentException($"No update item has the id '{id}'.", nameof(id));
        Begin();
        try
        {
            if (Get(id).State != UpdateRowState.Failed) throw new InvalidOperationException($"'{id}' did not fail, so there is nothing to retry.");
            await Run([id], cancellation);
        }
        finally { End(); }
    }

    /// <summary>PORT-ONLY (UE-4, the Updates page's per-row Update, ED-NEW-f-ue-4): runs one item alone. It needs an update to be due (available, version unknown until it runs, or failed before); a row that is current or not installed is refused.</summary>
    public async Task UpdateOne(string id, CancellationToken cancellation = default)
    {
        if (items.All(i => i.Id != id)) throw new ArgumentException($"No update item has the id '{id}'.", nameof(id));
        Begin();
        try
        {
            if (Get(id).State is not (UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated or UpdateRowState.Failed)) throw new InvalidOperationException($"'{id}' has no update due.");
            await Run([id], cancellation);
        }
        finally { End(); }
    }

    private async Task Run(IReadOnlyList<string> ids, CancellationToken cancellation)
    {
        var before = ids.ToDictionary(i => i, i => Get(i), StringComparer.Ordinal);
        foreach (var id in ids) Set(id, r => r with { State = UpdateRowState.Queued, Progress = null, Message = null });
        var finished = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in items.Where(i => ids.Contains(i.Id)))
            {
                cancellation.ThrowIfCancellationRequested();
                Set(item.Id, r => r with { State = UpdateRowState.Updating, Progress = null });
                var progress = new InlineProgress(value => Set(item.Id, r => r with { Progress = value }));
                UpdateOutcome outcome;
                try { outcome = await item.Update(progress, cancellation); }
                catch (Exception error) when (error is not OperationCanceledException) { outcome = new UpdateOutcome(false, null, false, false, error.Message); }
                finished.Add(item.Id);
                string? installed = outcome.NewVersion;
                try { installed = await item.InstalledVersion(cancellation) ?? installed; }
                catch (Exception error) when (error is not OperationCanceledException) { }
                var state = !outcome.Succeeded ? UpdateRowState.Failed : outcome.Changed ? UpdateRowState.Updated : UpdateRowState.UpToDate;
                Set(item.Id, r => r with
                {
                    State = state, Installed = installed ?? r.Installed, Latest = outcome.Succeeded && state != UpdateRowState.UpToDate ? installed ?? r.Latest : r.Latest,
                    Progress = null, Message = outcome.Message, RestartRequired = outcome.Succeeded && outcome.RestartRequired,
                });
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var id in ids.Where(i => !finished.Contains(i))) Set(id, _ => before[id]);
            throw;
        }
    }

    private sealed class InlineProgress(Action<double?> report) : IProgress<double?>
    {
        public void Report(double? value) => report(value);
    }
}
