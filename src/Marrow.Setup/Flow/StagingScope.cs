using Marrow.Core.Support;
using Marrow.Setup.Screens;

namespace Marrow.Setup.Flow;

/// <summary>PORT-ONLY (PLAN 3/5; ED-NEW-inst-c7-8/10): claimed, provenance-marked staging and bounded cleanup on every controlled exit.</summary>
public sealed class StagingScope : IDisposable
{
    private readonly string localAppData;
    private readonly string tempPath;
    private readonly bool lastChance;
    private readonly SweepLimits? cleanupLimits;
    private readonly TimeProvider clock;
    private static readonly object SweepGate = new();
    private readonly object gate = new();
    private FolderClaim? claim;
    private bool disposed;
    private SweepResult lastCleanup = SweepResult.Nothing;

    private StagingScope(string localAppData, string tempPath, string? folder, bool lastChance, SweepLimits? cleanupLimits, TimeProvider? clock)
    {
        this.localAppData = localAppData;
        this.tempPath = tempPath;
        this.lastChance = lastChance;
        this.cleanupLimits = cleanupLimits;
        this.clock = clock ?? TimeProvider.System;
        Folder = folder;
        if (lastChance)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
    }

    public static StagingScope Create(string localAppData, string tempPath, string version, bool lastChance = true, SweepLimits? cleanupLimits = null, TimeProvider? clock = null)
    {
        SetupFolders.RequireFull(localAppData, nameof(localAppData));
        SetupFolders.RequireFull(tempPath, nameof(tempPath));
        var folder = SetupFolders.IsSegment(version) ? InstallOptions.StagingFolder(localAppData, version) : null;
        return new(localAppData, tempPath, folder, lastChance, cleanupLimits, clock);
    }

    public string? Folder { get; }
    public bool IsHeld { get { lock (gate) return claim is not null; } }
    /// <summary>Also records failures/limits from void Dispose and last-chance callbacks for current-process diagnostics.</summary>
    public SweepResult LastCleanup => Volatile.Read(ref lastCleanup);

    public string Ensure()
    {
        var work = new SweepBudget(SweepLimits.Normal, clock);
        if (!Monitor.TryEnter(gate, work.Remaining))
        { Record(Kept(SweepKeptReason.LimitReached)); throw new IOException("The staging claim gate did not become available within its budget."); }
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var folder = Folder ?? throw new InvalidOperationException("This setup was built without a version, so there is nowhere to stage.");
            if (claim is not null) return folder;
            if (!Monitor.TryEnter(SweepGate, work.Remaining))
            { Record(Kept(SweepKeptReason.LimitReached)); throw new IOException("The staging sweep gate did not become available within its budget."); }
            try
            {
                var cleanup = SetupSweeper.RemoveFolderWithBudget(localAppData, folder, work);
                Record(cleanup);
                if (cleanup.Kept.Any(kept => kept.Reason == SweepKeptReason.LimitReached
                    || string.Equals(kept.Path, folder, StringComparison.OrdinalIgnoreCase)
                    || kept.Path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("The staging folder could not be safely cleared: '" + folder + "'.");
                claim = FolderClaim.TakeWithBudget(folder, work);
            }
            finally { Monitor.Exit(SweepGate); }
            return folder;
        }
        finally { Monitor.Exit(gate); }
    }

    public SweepResult Release()
    {
        var work = new SweepBudget(cleanupLimits ?? SweepLimits.Release, clock);
        if (!Monitor.TryEnter(gate, work.Remaining)) return Record(Kept(SweepKeptReason.LimitReached));
        try { return Record(ReleaseCore(work)); }
        finally { Monitor.Exit(gate); }
    }

    public void Attach(SetupSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Staging = this;
        session.ScreenShown += screen => { if (screen is SetupScreen.Ready or SetupScreen.Problem) _ = Release(); };
    }

    public Task<SweepResult> SweepStaleAsync() => Task.Run(() =>
    {
        var work = new SweepBudget(SweepLimits.Normal, clock);
        if (!Monitor.TryEnter(SweepGate, work.Remaining)) return Record(Kept(SweepKeptReason.LimitReached));
        try
        {
            try { return Record(SetupSweeper.SweepWithBudget(localAppData, tempPath, work)); }
            catch (SweepLimitException) { return Record(Kept(SweepKeptReason.LimitReached)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return Record(Kept(SweepKeptReason.NotRemoved)); }
        }
        finally { Monitor.Exit(SweepGate); }
    });

    public void Dispose()
    {
        if (lastChance)
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }
        var work = new SweepBudget(cleanupLimits ?? SweepLimits.Release, clock);
        if (!Monitor.TryEnter(gate, work.Remaining)) { Volatile.Write(ref disposed, true); Record(Kept(SweepKeptReason.LimitReached)); return; }
        try
        {
            if (disposed && claim is null) return;
            disposed = true;
            Record(ReleaseCore(work));
        }
        finally { Monitor.Exit(gate); }
    }

    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => ReleaseLastChance();
    internal void OnProcessExit(object? sender, EventArgs e) => ReleaseLastChance();

    private void ReleaseLastChance()
    {
        // One deadline spans the gate wait, claim release, sweep gate and all filesystem traversal.
        var work = new SweepBudget(SweepLimits.LastChance, clock);
        if (!Monitor.TryEnter(gate, work.Remaining)) { Record(Kept(SweepKeptReason.LimitReached)); return; }
        try { Record(ReleaseCore(work)); }
        finally { Monitor.Exit(gate); }
    }

    private SweepResult ReleaseCore(SweepBudget work)
    {
        if (Folder is null) return SweepResult.Nothing;
        if (!Monitor.TryEnter(SweepGate, work.Remaining)) return Kept(SweepKeptReason.LimitReached);
        try
        {
            try
            {
                work.Visit(Folder);
                claim?.Dispose();
                claim = null;
                work.Check(Folder);
                return SetupSweeper.RemoveFolderWithBudget(localAppData, Folder, work);
            }
            catch (SweepLimitException) { return Kept(SweepKeptReason.LimitReached); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return Kept(SweepKeptReason.NotRemoved); }
        }
        finally { Monitor.Exit(SweepGate); }
    }

    private SweepResult Kept(SweepKeptReason reason) => new(0, [new(Folder ?? SetupFolders.Root(localAppData), reason)]);
    private SweepResult Record(SweepResult result) { Volatile.Write(ref lastCleanup, result); return result; }
}
