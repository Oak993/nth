using System.ComponentModel;
using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Install;

/// <summary>What one install or update runs with.</summary>
/// <param name="Update">An update of an older nth (Copying the new version; no Adding step).</param>
/// <param name="Root">Where Setup.exe installs: the existing root for an update, else the chosen one.</param>
/// <param name="VersionBefore">The version installed in <paramref name="Root"/> before the run, or null.</param>
/// <param name="Options">What the install hook is asked to do (install-options.json).</param>
/// <param name="StagingFolder"><c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>: the engine, the options, the result and the log.</param>
/// <param name="Sizes">T_pkg, T_app, T_disk and Velopack's own threshold.</param>
public sealed record InstallRequest(bool Update, string Root, string? VersionBefore, InstallOptions Options, string StagingFolder, InstallSizes Sizes)
{
    public const string LogFileName = "nth Setup Engine.log";

    public string EnginePath => Path.Combine(StagingFolder, EngineStaging.EngineFileName);

    public string OptionsPath => Path.Combine(StagingFolder, InstallOptions.OptionsFileName);

    public string ResultPath => InstallOptions.ResultPathFor(OptionsPath);

    public string LogPath => Path.Combine(StagingFolder, LogFileName);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, steps 7 to 9, and 4; unit U3, ED-NEW-inst-u3-1). One run of Velopack's
/// <c>Setup.exe --silent --log &lt;file&gt; --installto &lt;root&gt;</c> through <see cref="IVelopackRunner"/>, the only thing that
/// starts it, watched until it has exited and the setup's own work is done. The window drives it: <see cref="CheckSpace"/>
/// before anything runs, <see cref="Start"/> once the engine is staged and nth has agreed to close, then <see cref="Tick"/>
/// every 100 ms off the window's thread (it walks the root and tails the log), and <see cref="Finish"/> on the window's
/// thread when <see cref="NeedsFinish"/> says Setup.exe exited 0. Nothing here kills Setup.exe: Velopack rolls back only if
/// it finishes, so a run is always waited for.
/// </summary>
public sealed class InstallRun
{
    private readonly IVelopackRunner runner;
    private readonly IDiskSpace disk;
    private readonly IAfterSetupExit after;
    private readonly TimeProvider time;
    private readonly Func<DateTime> fileClock;
    private readonly long origin;
    private readonly Lock gate = new();
    private InstallObserver? observer;
    private IReadOnlyList<string> rollbackBefore = [];

    public InstallRun(InstallRequest request, IVelopackRunner runner, IDiskSpace disk, IAfterSetupExit after, TimeProvider? time = null, Func<DateTime>? fileClock = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.disk = disk ?? throw new ArgumentNullException(nameof(disk));
        this.after = after ?? throw new ArgumentNullException(nameof(after));
        this.time = time ?? TimeProvider.System;
        this.fileClock = fileClock ?? (() => DateTime.UtcNow);
        origin = this.time.GetTimestamp();
        Progress = new InstallProgress(request.Update, request.Sizes, request.Options.DesktopShortcut);
        Frame = Progress.Advance(Evidence, TimeSpan.Zero);
    }

    public InstallRequest Request { get; }

    public InstallProgress Progress { get; }

    /// <summary>What has been seen so far (the window sets the first step's fields before Setup.exe starts).</summary>
    public ProgressEvidence Evidence { get; } = new();

    /// <summary>The latest frame.</summary>
    public ProgressFrame Frame { get; private set; }

    /// <summary>The free-space check's answer, once asked.</summary>
    public FreeSpaceVerdict? Space { get; private set; }

    /// <summary>The started Setup.exe, or null before <see cref="Start"/>.</summary>
    public VelopackRun? Process { get; private set; }

    /// <summary>The command Setup.exe was started with.</summary>
    public VelopackCommand? Command { get; private set; }

    /// <summary>Why the run failed, once it has.</summary>
    public InstallFailure? Failure { get; private set; }

    /// <summary>What the install hook reported, read once the setup's own work is done.</summary>
    public InstallResult? Result { get; private set; }

    /// <summary>Setup.exe exited 0 and the setup's own work is still to do (on the window's thread).</summary>
    public bool NeedsFinish
    {
        get { lock (gate) return Evidence.ExitedZero && !Evidence.Finished && Failure is null; }
    }

    /// <summary>The run is over, either way.</summary>
    public bool IsOver
    {
        get { lock (gate) return Evidence.Finished || Failure is not null; }
    }

    /// <summary>The first step's own share before Setup.exe starts (the checks and the staging), and whether it reads "Preparing files".</summary>
    public void SetChecking(double fraction, bool staging)
    {
        lock (gate)
        {
            if (Evidence.SetupStarted) return;
            Evidence.CheckingFraction = Math.Max(Evidence.CheckingFraction, Math.Clamp(fraction, 0, 1));
            Evidence.Staging = staging;
        }
    }

    /// <summary>
    /// The first step waits on something outside the setup (unit U4: nth's threads), so it neither creeps nor stalls, and
    /// may carry its own words ("Saving and closing nth", "Waiting for 2 threads to finish").
    /// </summary>
    public void SetWaiting(bool waiting, string? label)
    {
        lock (gate)
        {
            if (Evidence.SetupStarted) return;
            Evidence.Waiting = waiting;
            Evidence.FirstStepLabel = label;
        }
    }

    /// <summary>Free space on the target drive (PLAN.md 4): not enough fails the run before anything has started.</summary>
    public FreeSpaceVerdict CheckSpace()
    {
        lock (gate)
        {
            var verdict = FreeSpaceCheck.Check(Request.Sizes, Request.Update, Request.Root, disk);
            Space = verdict;
            if (!verdict.Enough) Fail(new InstallFailure(FailureReason.NoSpace, NothingChanged: true, ExitCode: null, InstallStep.Checking, verdict, []));
            return verdict;
        }
    }

    /// <summary>
    /// Writes the options file, clears an earlier result, notes where the log ends and which rollback folders already
    /// exist, and starts Setup.exe. A program that cannot be started fails the run with nothing changed.
    /// </summary>
    public void Start()
    {
        lock (gate)
        {
            if (Process is not null || Failure is not null) return;
            try
            {
                Directory.CreateDirectory(Request.StagingFolder);
                Request.Options.Write(Request.OptionsPath);
                if (File.Exists(Request.ResultPath)) File.Delete(Request.ResultPath);
                rollbackBefore = InstallFailure.RollbackFolders(Request.Root);
                var logStart = VelopackLogTail.LengthOf(Request.LogPath);
                var command = VelopackCommand.Install(Request.EnginePath, Request.Root, Request.LogPath, Request.OptionsPath);
                var started = fileClock();
                observer = new InstallObserver(Request.Root, Request.LogPath, logStart, Request.ResultPath, started, fileClock);
                Command = command;
                Process = runner.Start(command);
                Evidence.SetupStarted = true;
                Evidence.Waiting = false;
                Evidence.Staging = false;
                Evidence.CheckingFraction = 1;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException)
            {
                var blocked = error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 or 225 or 1260 };
                Fail(new InstallFailure(blocked ? FailureReason.Blocked : FailureReason.Other, NothingChanged: true, ExitCode: null, InstallStep.Checking, null, []));
            }
        }
    }

    /// <summary>The run cannot start (the engine could not be staged): Problem, with nothing changed.</summary>
    public void FailBeforeStart(FailureReason reason)
    {
        lock (gate)
        {
            if (Process is not null) return;
            Fail(new InstallFailure(reason, NothingChanged: true, ExitCode: null, InstallStep.Checking, Space, []));
        }
    }

    /// <summary>One look (every 100 ms): the root, the log and the hook's result, then whether Setup.exe has ended.</summary>
    public ProgressFrame Tick()
    {
        lock (gate)
        {
            if (Process is { } process && observer is not null && Failure is null && !Evidence.Finished)
            {
                var seen = observer.Observe();
                Evidence.CopiedBytes = Math.Max(Evidence.CopiedBytes, seen.CopiedBytes);
                Evidence.CreatingShortcuts |= seen.Log.CreatingShortcuts;
                Evidence.HookStarted |= seen.Log.HookStarted;
                Evidence.ResultWritten |= seen.ResultWritten;
                Evidence.HookEnded |= seen.Log.HookEnded;
                Evidence.Completed |= seen.Log.Completed;
                if (process.Exited.IsCompleted && !Evidence.ExitedZero)
                {
                    var code = process.Exited.IsCompletedSuccessfully ? process.Exited.Result : -1;
                    if (code == 0) Evidence.ExitedZero = true;
                    else Fail(Failed(code, seen.Log));
                }
            }
            Frame = Progress.Advance(Evidence, Now());
            return Frame;
        }
    }

    /// <summary>
    /// Step 8 of PLAN.md 3, after exit 0: the setup's own work (<see cref="IAfterSetupExit"/>: the uninstall entry, Start),
    /// then <c>install-result.json</c> is read and the staging folder deleted. Only then may the bar show 100.
    /// </summary>
    public ProgressFrame Finish()
    {
        if (!NeedsFinish) return Frame;
        try { after.Run(Request.Root, Request.ResultPath); }
        catch (Exception)
        {
            // The step catches its own errors (unit U5); nth's start-time self-heal repeats it either way.
        }
        lock (gate)
        {
            Result = InstallResult.ReadForReady(Request.ResultPath);
            DeleteStaging();
            Evidence.Finished = true;
            Frame = Progress.Advance(Evidence, Now());
            return Frame;
        }
    }

    /// <summary>Removes the staging folder (the engine, the options, the result and the log), as every exit path the setup controls does.</summary>
    public void DeleteStaging()
    {
        try
        {
            if (Directory.Exists(Request.StagingFolder)) Directory.Delete(Request.StagingFolder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // In use by a scanner this moment: unit U9's sweeps remove it at the next start.
        }
    }

    private InstallFailure Failed(int code, VelopackLogSignals log)
    {
        var reason = log.Reason();
        if (reason == FailureReason.Other)
        {
            // Setup.exe exits only 0 or 1 (setup.rs:59-60). Another code, or 1 with no error logged, means it was ended
            // from outside (security software); an NTSTATUS error code (0xC…) is a crash.
            var crashed = code < 0 && unchecked((uint)code) >= 0xC0000000;
            var silent = log.Errors.Count == 0 && !log.Failed;
            if (!crashed && (code != 1 || silent)) reason = FailureReason.Blocked;
        }
        FreeSpaceVerdict? space = null;
        if (reason == FailureReason.NoSpace) space = Space = FreeSpaceCheck.Check(Request.Sizes, Request.Update, Request.Root, disk);
        var unchanged = InstallFailure.Unchanged(Request.Root, Request.VersionBefore, rollbackBefore, InstallerGate.ReadInstalledVersion);
        return new InstallFailure(reason, unchanged, code, Progress.Ranges[Progress.WorkingIndex(Evidence)].Step, space, observer?.RecentLog ?? []);
    }

    private void Fail(InstallFailure failure)
    {
        Failure ??= failure;
        Evidence.Failed = true;
        Frame = Progress.Advance(Evidence, Now());
    }

    private TimeSpan Now() => time.GetElapsedTime(origin);
}
