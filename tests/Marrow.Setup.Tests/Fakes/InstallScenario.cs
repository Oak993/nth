using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Motion;
using Marrow.Setup.Ui;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>One frame of a played scenario, with what the fake had really done by then.</summary>
/// <param name="Seconds">Seconds since Setup.exe started (negative while checking).</param>
/// <param name="Frame">The engine's frame.</param>
/// <param name="Visible">The furthest step the fake's own actions make visible.</param>
/// <param name="Repointed">The test hive's entry was re-pointed by then.</param>
/// <param name="Bar">The bar's drawn value after this frame's 100 ms of 60 Hz frames.</param>
/// <param name="Copied">The bytes the engine had counted by then.</param>
public sealed record PlayedFrame(double Seconds, ProgressFrame Frame, int Visible, bool Repointed, double Bar, long Copied);

/// <summary>
/// Plays a <see cref="VelopackTimeline"/> through a real <see cref="InstallRun"/> with the fake runner, in a temporary
/// folder under a long user name (C:\Users\Ana-María Østergård-Fernández\… inside the test's temp folder), on a hand-driven
/// clock: 100 ms per look, as the window polls, and the bar's spring at 60 Hz between looks.
/// </summary>
public sealed class InstallScenario : IDisposable
{
    public const string OldVersion = "1.0.0";

    private readonly ManualFrameSource frames = new();
    private readonly List<PlayedFrame> played = [];

    public InstallScenario(VelopackTimeline timeline, bool update, long? free = 100_000_000_000, bool desktopShortcut = true, InstallSizes? sizes = null, string? rootUnderLocal = null)
    {
        Base = Path.Combine(Path.GetTempPath(), "nth-u3-" + Guid.NewGuid().ToString("N")[..8]);
        var local = Path.Combine(Base, "Users", "Ana-María Østergård-Fernández", "AppData", "Local");
        Local = local;
        Root = rootUnderLocal is null ? Path.Combine(local, "Programs", "nth") : Path.Combine(local, rootUnderLocal); // unit U10: "NthApp" is where nth 1.0.0 lives and is updated in place
        Staging = InstallOptions.StagingFolder(local, "1.1.0");
        Directory.CreateDirectory(Path.Combine(local, "Programs"));
        if (update) WriteOldInstall(Root);
        Hive = new FakeUninstallHive();
        After = new FakeAfterSetupExit(Hive);
        Disk = new FakeDiskSpace(free);
        Runner = new FakeVelopackRunner(timeline, Hive, Path.Combine(Base, "Start Menu", "Programs"));
        var options = update ? InstallOptions.ForUpdate : InstallOptions.ForInstall(desktopShortcut);
        Request = new InstallRequest(update, Root, update ? OldVersion : null, options, Staging, sizes ?? VelopackScenarios.Sizes);
        // ED-NEW-inst-merge-13: as in the window, the run's folder is claimed (unit U9) before anything is staged, and the
        // finished run hands it back to the scope's guarded cleanup
        Scope = StagingScope.Create(local, Path.Combine(Base, "Temp"), "1.1.0", lastChance: false);
        Scope.Ensure();
        Run = new InstallRun(Request, Runner, Disk, After, Time, releaseStaging: Scope.Release);
        Bar = new ProgressMotion(() => false);
        Bar.Attach(new Animator(frames));
    }

    public string Base { get; }

    /// <summary>The fake %LOCALAPPDATA% the scenario lives under.</summary>
    public string Local { get; }

    public string Root { get; }

    public string Staging { get; }

    /// <summary>The run's claimed staging folder and its guarded cleanup (unit U9).</summary>
    public StagingScope Scope { get; }

    public ManualTimeProvider Time { get; } = new();

    public FakeUninstallHive Hive { get; }

    public FakeAfterSetupExit After { get; }

    public FakeDiskSpace Disk { get; }

    public FakeVelopackRunner Runner { get; }

    public InstallRequest Request { get; }

    public InstallRun Run { get; }

    public ProgressMotion Bar { get; }

    public IReadOnlyList<PlayedFrame> Frames => played;

    /// <summary>
    /// Checks space, looks three times while "checking" (the engine staged), starts Setup.exe, then looks every 100 ms
    /// until it needs finishing (and finishes, unless <paramref name="finish"/> is false: the window was killed), fails, or
    /// <paramref name="maxSeconds"/> pass. Afterwards the bar is drawn for two more seconds.
    /// </summary>
    public InstallScenario Play(double maxSeconds = 150, bool finish = true)
    {
        if (!Run.CheckSpace().Enough)
        {
            Record(-1, Run.Frame);
            return this;
        }
        for (var i = 0; i < 3; i++)
        {
            Run.SetChecking(0.2 + 0.8 * (i + 1) / 3, staging: i < 2);
            Look(-0.3 + 0.1 * i);
        }
        Run.Start();
        while (Runner.Elapsed < maxSeconds)
        {
            Runner.Advance(0.1);
            Look(Runner.Elapsed);
            if (Run.Failure is not null) break;
            if (!Run.NeedsFinish) continue;
            if (finish) Record(Runner.Elapsed, Run.Finish());
            break;
        }
        for (var i = 0; i < 20; i++) Record(Runner.Elapsed, Run.Frame);
        return this;
    }

    /// <summary>The furthest step the fake's own actions have made visible, by the plan's table.</summary>
    public int VisibleStep()
    {
        var seen = Runner.Seen;
        var exitedZero = Runner.HasExited && Runner.Command is not null && seen.Contains(FakeMilestone.Exited) && ExitCode() == 0;
        if (!seen.Contains(FakeMilestone.Started)) return 0;
        var hooked = seen.Contains(FakeMilestone.Shortcuts) || seen.Contains(FakeMilestone.HookStarted) || seen.Contains(FakeMilestone.ResultWritten);
        var ended = seen.Contains(FakeMilestone.HookEnded) || seen.Contains(FakeMilestone.Completed) || exitedZero;
        if (Request.Update) return hooked || ended ? 2 : 1;
        return ended ? 3 : hooked ? 2 : 1;
    }

    public int? ExitCode() => Run.Process?.Exited is { IsCompletedSuccessfully: true } exited ? exited.Result : null;

    public void Dispose()
    {
        Scope.Dispose();
        try { Directory.Delete(Base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Look(double seconds)
    {
        Time.Advance(TimeSpan.FromMilliseconds(100));
        Record(seconds, Run.Tick());
    }

    private void Record(double seconds, ProgressFrame frame)
    {
        Bar.SetTarget(frame.Target);
        frames.Run(0.1);
        played.Add(new PlayedFrame(seconds, frame, VisibleStep(), Hive.IsRepointed(Root), Bar.Value, Run.Evidence.CopiedBytes));
    }

    /// <summary>nth 1.0.0 in the root, its files made an hour ago (an earlier day's install).</summary>
    public static void WriteOldInstall(string root)
    {
        var old = DateTime.UtcNow.AddHours(-1);
        void Write(string relative, int bytes, string? text = null)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (text is not null) File.WriteAllText(path, text);
            else File.WriteAllBytes(path, new byte[bytes]);
            File.SetCreationTimeUtc(path, old);
            File.SetLastWriteTimeUtc(path, old);
        }
        Write("Update.exe", 40_000);
        Write(@"packages\NthApp-1.0.0-full.nupkg", 1_150_000);
        Write(@"current\nth.exe", 2_900_000);
        Write(@"current\sq.version", 0, "<?xml version=\"1.0\"?><package><metadata><id>NthApp</id><version>" + OldVersion + "</version></metadata></package>");
    }
}
