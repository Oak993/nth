// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the state of the Activity view, apart from how it is drawn.
using System.Globalization;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

public enum ActivityLoadPhase
{
    /// <summary>Not opened yet.</summary>
    Idle,

    /// <summary>The chain is being checked and the lines read, off the UI thread.</summary>
    Checking,

    Ready,

    /// <summary>The log could not be read at all (a locked file, a missing disk); <see cref="ActivityLogModel.Failure"/> says why.</summary>
    Unavailable,
}

/// <summary>What changed, so a view redraws only that part (a filter change never redraws the status strip).</summary>
[Flags]
public enum ActivityChange
{
    None = 0,
    Phase = 1,
    Snapshot = 2,
    Items = 4,
    Filter = 8,
    Selection = 16,
    Export = 32,
    Mode = 64,
}

public enum ActivityExportPhase
{
    Idle,
    Writing,
    Done,
    Failed,
}

/// <summary>Where an export stands. <see cref="Rows"/> and <see cref="Path"/> are set once it is done; <see cref="Message"/> says why it failed.</summary>
public sealed record ActivityExportState(ActivityExportPhase Phase, ActivityExportFormat Format = ActivityExportFormat.Json, string? Path = null, int Rows = 0, string? Message = null)
{
    public static ActivityExportState Idle { get; } = new(ActivityExportPhase.Idle);
}

/// <summary>
/// Names to show in place of ids. The audit log keeps ids (a mission's id, an agent's thread id, an assistant's id); the
/// owner knows the names. A function returns null for an id it does not know, which is then shown as it is (AS-8 and AS-16
/// bind these to the board and the profiles).
/// </summary>
public sealed record ActivityLabels(Func<string, string?>? Assistant = null, Func<string, string?>? Mission = null, Func<string, string?>? Agent = null)
{
    public static ActivityLabels None { get; } = new();

    public string ForAssistant(string id) => Assistant?.Invoke(id) is { Length: > 0 } name ? name : id;

    public string ForMission(string id) => Mission?.Invoke(id) is { Length: > 0 } name ? name : id;

    public string ForAgent(string id) => Agent?.Invoke(id) is { Length: > 0 } name ? name : id;
}

/// <summary>
/// Where the model's slow work runs. The model awaits it from the UI thread, so everything after an await is on the UI
/// thread again (the UI's own synchronization context); a test runs the work inline.
/// </summary>
public interface IActivityWork
{
    Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellation);
}

/// <summary>The app's own: the thread pool.</summary>
public sealed class ThreadPoolActivityWork : IActivityWork
{
    public static ThreadPoolActivityWork Shared { get; } = new();

    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellation) => Task.Run(() => work(cancellation), cancellation);
}

/// <summary>
/// The Activity view's state (12.5): the log opened and its chain checked off the UI thread, the filter, the two ways of
/// listing (the log, newest first; the lineage, mission → agent → action → grant), the folds, the selected line, and the
/// exports. Everything the view shows comes from here and every change is told once, by <see cref="Changed"/>, with what
/// changed. All members are for the UI thread.
/// <para>A filter change, a mode change or a fold builds the list again off the UI thread; an answer that a newer change has
/// overtaken is dropped, so typing never shows a list that does not match the box.</para>
/// </summary>
public sealed class ActivityLogModel : IDisposable
{
    private readonly ActivityLogSource source;
    private readonly IActivityWork work;
    private readonly TimeProvider time;
    private readonly HashSet<string> folded = new(StringComparer.Ordinal);
    private CancellationTokenSource? loading;
    private CancellationTokenSource? listing;
    private CancellationTokenSource? exporting;
    private long listGeneration;
    private bool disposed;

    public ActivityLogModel(ActivityLogSource source, IActivityWork? work = null, TimeProvider? time = null, ActivityLabels? labels = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.work = work ?? ThreadPoolActivityWork.Shared;
        this.time = time ?? TimeProvider.System;
        Labels = labels ?? ActivityLabels.None;
    }

    /// <summary>Told once per change, on the UI thread, with what changed.</summary>
    public event Action<ActivityChange>? Changed;

    /// <summary>Told when the view should scroll a row into sight (a failing line was asked for).</summary>
    public event Action<ActivityRow>? RevealRequested;

    public ActivityLabels Labels { get; }

    public ActivityLoadPhase Phase { get; private set; }

    /// <summary>Why the log could not be read, when <see cref="Phase"/> is <see cref="ActivityLoadPhase.Unavailable"/>.</summary>
    public string? Failure { get; private set; }

    public ActivitySnapshot? Snapshot { get; private set; }

    /// <summary>"Verified" or the first line that fails; null until the check has run.</summary>
    public AuditVerification? Verification => Snapshot?.Verification;

    public ActivityFilter Filter { get; private set; } = new();

    public ActivityViewMode Mode { get; private set; } = ActivityViewMode.Log;

    /// <summary>The lines the list shows.</summary>
    public IReadOnlyList<ActivityListItem> Items { get; private set; } = [];

    /// <summary>How many of the listed rows pass the filter.</summary>
    public int MatchCount { get; private set; }

    public ActivityRow? Selected { get; private set; }

    public ActivityExportState Export { get; private set; } = ActivityExportState.Idle;

    /// <summary>The headers the owner folded in the lineage.</summary>
    public IReadOnlySet<string> Folded => folded;

    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    // ---- opening ----------------------------------------------------------------------------------------------------

    /// <summary>Checks the chain and reads the log (12.5: "verifies the chain on open"). Opening again is a refresh.</summary>
    public Task OpenAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        loading?.Cancel();
        loading?.Dispose();
        var cancel = loading = new CancellationTokenSource();
        Phase = ActivityLoadPhase.Checking;
        Failure = null;
        Raise(ActivityChange.Phase);
        ActivitySnapshot snapshot;
        try
        {
            snapshot = await work.RunAsync(token => ActivityLogReader.Load(source, token), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return; // a newer refresh, or the view closed
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (cancel.IsCancellationRequested || disposed) return;
            Phase = ActivityLoadPhase.Unavailable;
            Failure = error.Message;
            Raise(ActivityChange.Phase);
            return;
        }
        if (cancel.IsCancellationRequested || disposed) return;
        Snapshot = snapshot;
        Phase = ActivityLoadPhase.Ready;
        // The line that was selected is kept when the fresh read still holds it, else let go.
        if (Selected is { } before) Selected = snapshot.Rows.FirstOrDefault(r => r.At == before.At && r.Hmac == before.Hmac);
        Raise(ActivityChange.Phase | ActivityChange.Snapshot | ActivityChange.Selection);
        await RebuildAsync();
    }

    /// <summary>
    /// Lets go of the log: a hidden view holds none of it (a long log is a hundred megabytes of lines). The filter, the mode
    /// and the folds stay, so the view comes back as it was left; opening again reads and checks the log again.
    /// </summary>
    public void Release()
    {
        loading?.Cancel();
        listing?.Cancel();
        listGeneration++;
        Snapshot = null;
        Items = [];
        MatchCount = 0;
        Selected = null;
        Failure = null;
        Phase = ActivityLoadPhase.Idle;
        Raise(ActivityChange.Phase | ActivityChange.Snapshot | ActivityChange.Items | ActivityChange.Selection);
    }

    // ---- filter, mode, folds ----------------------------------------------------------------------------------------

    public Task SetFilterAsync(ActivityFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter == Filter) return Task.CompletedTask;
        Filter = filter;
        Raise(ActivityChange.Filter);
        return RebuildAsync();
    }

    public Task UpdateFilterAsync(Func<ActivityFilter, ActivityFilter> change) => SetFilterAsync(change(Filter));

    public Task ClearFilterAsync() => SetFilterAsync(new ActivityFilter());

    public Task SetModeAsync(ActivityViewMode mode)
    {
        if (mode == Mode) return Task.CompletedTask;
        Mode = mode;
        Raise(ActivityChange.Mode);
        return RebuildAsync();
    }

    /// <summary>Folds an open header of the lineage, or opens a folded one.</summary>
    public Task ToggleFoldAsync(string key)
    {
        if (!folded.Remove(key)) folded.Add(key);
        return RebuildAsync();
    }

    /// <summary>
    /// What the mission card's Lineage button asks for (3.3): the Activity log filtered to the mission, listed as a lineage,
    /// with nothing folded.
    /// </summary>
    public Task ShowMissionAsync(string mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        folded.Clear();
        Mode = ActivityViewMode.Lineage;
        Filter = ActivityFilter.ForMission(mission);
        Raise(ActivityChange.Filter | ActivityChange.Mode);
        return RebuildAsync();
    }

    /// <summary>
    /// Brings the first line that fails into sight: whatever filter or fold hides it is undone, it is selected, and the view is
    /// told to scroll to it. A failure that names a day and no line (the digest's printed final no longer matches) filters to
    /// that day. False when there is no failure, or none that names a line or a day.
    /// </summary>
    public async Task<bool> ShowFailureAsync()
    {
        if (Snapshot is not { } snapshot || snapshot.Verification.FirstFailure is not { } at) return false;
        if (snapshot.FirstFailureRow is { } row)
        {
            if (!Filter.Matches(row)) Filter = new ActivityFilter();
            folded.Remove(ActivityListBuilder.MissionKey(row.Mission ?? ""));
            folded.Remove(ActivityListBuilder.AgentKey(row.Mission ?? "", row.Agent ?? ""));
            Selected = row;
            Raise(ActivityChange.Filter | ActivityChange.Selection);
            await RebuildAsync();
            RevealRequested?.Invoke(row);
            return true;
        }
        if (at.File.Length >= 10 && DateOnly.TryParseExact(at.File[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            await SetFilterAsync(new ActivityFilter { From = day, To = day });
            return true;
        }
        return false;
    }

    public void Select(ActivityRow? row)
    {
        if (ReferenceEquals(row, Selected)) return;
        Selected = row;
        Raise(ActivityChange.Selection);
    }

    /// <summary>Where a row is in the list now, or -1 when it is not listed (a filter or a fold hides it).</summary>
    public int IndexOf(ActivityRow row)
    {
        for (var i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i].Row, row)) return i;
        return -1;
    }

    // ---- exporting --------------------------------------------------------------------------------------------------

    /// <summary>A name to offer in the save dialog.</summary>
    public string SuggestedFileName(ActivityExportFormat format) => ActivityExporter.SuggestedFileName(format, Today);

    /// <summary>
    /// Exports the lines that pass the filter, with the chain check's result as this view shows it, to <paramref name="path"/>
    /// (12.5: "The log exports to JSON or CSV"). It reads the files again, so it covers every line of the log, not only the
    /// lines this view lists. Never throws for a file or a disk: <see cref="Export"/> says how it went.
    /// </summary>
    public async Task<ActivityExportState> ExportAsync(ActivityExportFormat format, string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        exporting?.Cancel();
        exporting?.Dispose();
        var cancel = exporting = new CancellationTokenSource();
        var filter = Filter;
        var verification = Snapshot?.Verification;
        var now = time.GetUtcNow();
        Export = new ActivityExportState(ActivityExportPhase.Writing, format, path);
        Raise(ActivityChange.Export);
        ActivityExportState result;
        try
        {
            var written = await work.RunAsync(token => ActivityExporter.WriteFile(path, format,
                ActivityLogReader.Stream(source.Directory, verification, token).Where(filter.Matches), new ActivityExportContext(filter, verification, now), token), cancel.Token);
            result = new ActivityExportState(ActivityExportPhase.Done, format, written.Path, written.Rows);
        }
        catch (OperationCanceledException)
        {
            return Export; // a newer export, or the view closed
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            result = new ActivityExportState(ActivityExportPhase.Failed, format, path, 0, error.Message);
        }
        if (cancel.IsCancellationRequested || disposed) return Export;
        Export = result;
        Raise(ActivityChange.Export);
        return result;
    }

    // ---- the list ---------------------------------------------------------------------------------------------------

    private async Task RebuildAsync()
    {
        listing?.Cancel();
        listing?.Dispose();
        var cancel = listing = new CancellationTokenSource();
        var generation = ++listGeneration;
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Items = [];
            MatchCount = 0;
            Raise(ActivityChange.Items);
            return;
        }
        var filter = Filter;
        var mode = Mode;
        var keys = folded.ToHashSet(StringComparer.Ordinal);
        (int Matches, IReadOnlyList<ActivityListItem> Items) built;
        try
        {
            built = await work.RunAsync(token =>
            {
                var matching = filter.IsEmpty ? snapshot.Rows : snapshot.Rows.Where(filter.Matches).ToList();
                token.ThrowIfCancellationRequested();
                return (matching.Count, ActivityListBuilder.Build(matching, mode, keys));
            }, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (generation != listGeneration || disposed) return; // a newer change has overtaken this answer
        Items = built.Items;
        MatchCount = built.Matches;
        var change = ActivityChange.Items;
        if (Selected is not null && IndexOf(Selected) < 0 && !Filter.Matches(Selected))
        {
            Selected = null; // a filter that hides the selected line lets it go; a fold only hides it
            change |= ActivityChange.Selection;
        }
        Raise(change);
    }

    private void Raise(ActivityChange change)
    {
        if (change != ActivityChange.None) Changed?.Invoke(change);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        loading?.Cancel();
        listing?.Cancel();
        exporting?.Cancel();
        loading?.Dispose();
        listing?.Dispose();
        exporting?.Dispose();
    }
}
