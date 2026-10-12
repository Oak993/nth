using Marrow.Core.Support;
using Marrow.Setup.Screens;

namespace Marrow.Setup.Flow;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 step 4 and 5, "Leftovers"; unit U9, ED-NEW-inst-u9-1). This run's staging folder
/// (<c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>: the engine, <c>install-options.json</c>, <c>install-result.json</c>, the log)
/// and everything that must happen to it on every exit path the setup controls. The folder is made and claimed on first
/// use (<see cref="Ensure"/>), so a sweep by anything else (a second user session, nth) leaves it alone while this run uses it.
/// It is deleted by <see cref="Release"/>, which the flow calls when Ready or Problem shows, and again when the window closes
/// on any screen (<see cref="Dispose"/>), and by a last-chance handler when an unhandled exception or <c>Environment.Exit</c>
/// ends the process. If a program in it is still running (Velopack's engine after a crash) the folder is left whole and the
/// next sweep takes it. Nothing here throws for a file-system failure.
/// </summary>
public sealed class StagingScope : IDisposable
{
    private readonly string localAppData;
    private readonly string tempPath;
    private readonly bool lastChance;
    private static readonly object SweepGate = new(); // one sweep or one claim at a time in this process: a sweep never meets a folder half made
    private readonly object gate = new();
    private FolderClaim? claim;
    private bool disposed;

    private StagingScope(string localAppData, string tempPath, string? folder, bool lastChance)
    {
        this.localAppData = localAppData;
        this.tempPath = tempPath;
        this.lastChance = lastChance;
        Folder = folder;
        if (lastChance)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
    }

    /// <summary>
    /// A scope for this setup's <paramref name="version"/>. <paramref name="lastChance"/> hooks the process-wide handlers for an
    /// unhandled exception and for exit; only the real setup asks for it. A version that cannot name a folder (a build made
    /// without one) leaves <see cref="Folder"/> null: nothing is staged and there is nothing to release.
    /// </summary>
    public static StagingScope Create(string localAppData, string tempPath, string version, bool lastChance = true)
    {
        SetupFolders.RequireFull(localAppData, nameof(localAppData));
        SetupFolders.RequireFull(tempPath, nameof(tempPath));
        var folder = SetupFolders.IsSegment(version) ? InstallOptions.StagingFolder(localAppData, version) : null;
        return new StagingScope(localAppData, tempPath, folder, lastChance);
    }

    /// <summary><c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>, or null when this build has no version to stage under.</summary>
    public string? Folder { get; }

    /// <summary>Whether the folder is claimed by this run right now.</summary>
    public bool IsHeld
    {
        get
        {
            lock (gate) return claim is not null;
        }
    }

    /// <summary>
    /// Makes the folder and claims it, once per use: staging, the options file and the engine go in only after this. A folder
    /// an earlier run left is emptied first, so a stale <c>install-result.json</c> is never read as this run's. After
    /// <see cref="Release"/> it makes the folder again (Retry). Throws <see cref="IOException"/> if another setup of this user
    /// already holds the folder, and <see cref="InvalidOperationException"/> when there is no version to stage under.
    /// </summary>
    public string Ensure()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var folder = Folder ?? throw new InvalidOperationException("This setup was built without a version, so there is nowhere to stage.");
            if (claim is not null) return folder;
            lock (SweepGate)
            {
                var cleanup = SetupSweeper.RemoveFolder(localAppData, folder);
                if (cleanup.Kept.Any(kept => string.Equals(kept.Path, folder, StringComparison.OrdinalIgnoreCase)
                    || kept.Path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("The staging folder could not be safely cleared: '" + folder + "'.");
                claim = FolderClaim.Take(folder);
            }
            return folder;
        }
    }

    /// <summary>
    /// Deletes the folder (the claim is let go first). Safe to call again, from any thread, before or after <see cref="Ensure"/>.
    /// What is still in use stays, and is reported in <see cref="SweepResult.Kept"/>.
    /// </summary>
    public SweepResult Release()
    {
        lock (gate) return ReleaseCore();
    }

    /// <summary>
    /// Binds this scope to the window's session: <see cref="SetupSession.Staging"/> returns it (everything the install stages goes
    /// through <see cref="Ensure"/>, so that this scope can delete it), and the folder is released whenever the session shows Ready
    /// (step 8 of PLAN.md 3: the result is read, then the folder goes) or Problem (nothing more is staged until Retry). The screen
    /// is built before it is shown, so what it needs from the folder (the result file, the log tail) must already be read.
    /// </summary>
    public void Attach(SetupSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Staging = this;
        session.ScreenShown += screen =>
        {
            if (screen is SetupScreen.Ready or SetupScreen.Problem) _ = Release();
        };
    }

    /// <summary>
    /// Step 4 of PLAN.md 3: sweeps stale folders from earlier runs (<see cref="SetupSweeper.Sweep"/>) on a pool thread. This
    /// run's own folder is claimed and its native libraries are loaded, so they are kept. It and <see cref="Ensure"/> take turns
    /// (a sweep never meets a folder that is half made), so the order between them does not matter.
    /// </summary>
    public Task<SweepResult> SweepStaleAsync() => Task.Run(() =>
    {
        try
        {
            lock (SweepGate) return SetupSweeper.Sweep(localAppData, tempPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return SweepResult.Nothing; }
    });

    public void Dispose()
    {
        if (lastChance)
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            _ = ReleaseCore();
        }
    }

    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => ReleaseLastChance();

    internal void OnProcessExit(object? sender, EventArgs e) => ReleaseLastChance();

    /// <summary>The process is ending: clean up, but never hang its end behind a lock another thread holds.</summary>
    private void ReleaseLastChance()
    {
        if (!Monitor.TryEnter(gate, TimeSpan.FromSeconds(1))) return;
        try { _ = ReleaseCore(); }
        finally { Monitor.Exit(gate); }
    }

    private SweepResult ReleaseCore()
    {
        claim?.Dispose();
        claim = null;
        if (Folder is null) return SweepResult.Nothing;
        try { return SetupSweeper.RemoveFolder(localAppData, Folder); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return SweepResult.Nothing; }
    }
}
