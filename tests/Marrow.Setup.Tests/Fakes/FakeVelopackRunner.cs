using System.Globalization;
using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>What a fake run has done so far, in the words the progress oracle uses.</summary>
public enum FakeMilestone
{
    Started,
    RootRenamed,
    Shortcuts,
    HookStarted,
    ResultWritten,
    HookEnded,
    KeyWritten,
    Completed,
    Exited,
}

/// <summary>One thing a fake Setup.exe does at a time, or over a time (a file that grows).</summary>
public sealed class TimelineStep(double at, double seconds, Action<FakeVelopackRunner, double> apply, FakeMilestone? milestone = null)
{
    public double At { get; } = at;

    public double Seconds { get; } = seconds;

    public FakeMilestone? Milestone { get; } = milestone;

    /// <summary>Called with the share done (0 to 1); an instant step is called once with 1.</summary>
    public Action<FakeVelopackRunner, double> Apply { get; } = apply;

    public double Ends => At + Seconds;
}

/// <summary>
/// A timeline a fake Setup.exe acts out (nth installer PLAN.md 10, "Fake Velopack runner"; unit U3): log lines in Velopack's
/// simplelog form, files under the root written at chosen speeds, the root renamed away and back, the Start shortcut,
/// the install hook's start, result and end, the uninstall key in a test hive, and the exit code. Times are seconds after
/// Setup.exe starts.
/// </summary>
public sealed class VelopackTimeline(string name)
{
    private readonly List<TimelineStep> steps = [];

    public string Name { get; } = name;

    public IReadOnlyList<TimelineStep> Steps => steps;

    /// <summary>When the last step is done.</summary>
    public double End => steps.Count == 0 ? 0 : steps.Max(s => s.Ends);

    public override string ToString() => Name;

    public VelopackTimeline Add(TimelineStep step)
    {
        steps.Add(step);
        return this;
    }

    public VelopackTimeline Log(double at, string text, string level = "INFO") => Add(new TimelineStep(at, 0, (r, _) => r.WriteLog(text, level)));

    /// <summary>A file under the root that grows to <paramref name="bytes"/> from <paramref name="at"/> over <paramref name="seconds"/>.</summary>
    public VelopackTimeline Grow(double at, double seconds, string relative, long bytes) =>
        Add(new TimelineStep(at, seconds, (r, share) => r.SetLength(relative, (long)Math.Round(bytes * share))));

    /// <summary>A whole file under the root at once.</summary>
    public VelopackTimeline File(double at, string relative, long bytes) => Add(new TimelineStep(at, 0, (r, _) => r.SetLength(relative, bytes)));

    /// <summary>Velopack's rollback rename: the root becomes <c>&lt;root&gt;.&lt;16 random&gt;</c> and an empty root is made.</summary>
    public VelopackTimeline RenameRootAway(double at) =>
        Log(at, "Renaming existing directory to '\"" + "<root>.rollback" + "\"' to allow rollback...").Add(new TimelineStep(at, 0, (r, _) => r.RenameRootAway(), FakeMilestone.RootRenamed));

    /// <summary>Velopack's restore: the new root goes and the rollback copy is renamed back.</summary>
    public VelopackTimeline RestoreRoot(double at) => Log(at, "Rolling back installation...").Add(new TimelineStep(at, 0, (r, _) => r.RestoreRoot()));

    /// <summary>Velopack's restore failing silently (its errors are ignored): the rollback copy stays beside a half-written root.</summary>
    public VelopackTimeline RestoreFails(double at) => Log(at, "Rolling back installation...");

    /// <summary>The rollback copy is deleted after a successful upgrade.</summary>
    public VelopackTimeline DeleteRollback(double at) => Log(at, "Removing rollback directory...").Add(new TimelineStep(at, 0, (r, _) => r.DeleteRollback()));

    /// <summary>A folder under the root moved to another name under it (extract to a temporary folder, then rename).</summary>
    public VelopackTimeline MoveFolder(double at, string from, string to) => Add(new TimelineStep(at, 0, (r, _) => r.MoveFolder(from, to)));

    public VelopackTimeline Shortcuts(double at) =>
        Log(at, "Creating shortcuts...").Add(new TimelineStep(at, 0, (r, _) => r.WriteStartLink(), FakeMilestone.Shortcuts));

    public VelopackTimeline HookStart(double at) =>
        Log(at, "Starting process install hook").Log(at, "Running --veloapp-install hook...").Add(new TimelineStep(at, 0, (_, _) => { }, FakeMilestone.HookStarted));

    public VelopackTimeline Result(double at, string json = "{\"version\":1,\"desktopShortcut\":\"created\",\"iconsHidden\":false,\"scanCapped\":false,\"startAppIdRestamped\":false}") =>
        Add(new TimelineStep(at, 0, (r, _) => r.WriteResult(json), FakeMilestone.ResultWritten));

    public VelopackTimeline HookEnd(double at, int tookMs = 900) =>
        Log(at, $"Hook executed successfully (took {tookMs}ms)").Add(new TimelineStep(at, 0, (_, _) => { }, FakeMilestone.HookEnded));

    public VelopackTimeline HookTimeout(double at) =>
        Log(at, "Process timed out after 30s and was killed.", "ERROR").Add(new TimelineStep(at, 0, (_, _) => { }, FakeMilestone.HookEnded));

    /// <summary>Velopack's own uninstall key (to the test hive), written after the hook.</summary>
    public VelopackTimeline Key(double at) => Add(new TimelineStep(at, 0, (r, _) => r.Hive.WriteVelopack(r.Root), FakeMilestone.KeyWritten));

    public VelopackTimeline Completed(double at) =>
        Log(at, "Installation completed successfully!").Add(new TimelineStep(at, 0, (_, _) => { }, FakeMilestone.Completed));

    public VelopackTimeline Exit(double at, int code) => Add(new TimelineStep(at, 0, (r, _) => r.Exit(code), FakeMilestone.Exited));
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 10, "Fake Velopack runner"; unit U3, ED-NEW-inst-u3-1). The progress engine's stand-in
/// for Velopack's Setup.exe: it records the command it was given and acts out a <see cref="VelopackTimeline"/> in the
/// test's temporary folders as the test advances its clock, then completes its exit code. It starts no process.
/// </summary>
public sealed class FakeVelopackRunner(VelopackTimeline timeline, FakeUninstallHive hive, string startMenu) : IVelopackRunner
{
    private readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<TimelineStep> done = [];
    private readonly HashSet<FakeMilestone> seen = [];
    private string? rollback;

    public VelopackTimeline Timeline { get; } = timeline;

    public FakeUninstallHive Hive { get; } = hive;

    public string StartMenu { get; } = startMenu;

    public VelopackCommand? Command { get; private set; }

    public int Starts { get; private set; }

    /// <summary>Seconds since Start.</summary>
    public double Elapsed { get; private set; }

    public string Root => Command?.InstallRoot ?? throw new InvalidOperationException("Not started.");

    public IReadOnlySet<FakeMilestone> Seen => seen;

    public string? RollbackFolder => rollback;

    public bool HasExited => exit.Task.IsCompleted;

    public VelopackRun Start(VelopackCommand command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Starts++;
        seen.Add(FakeMilestone.Started);
        Advance(0);
        return new VelopackRun(4242, exit.Task);
    }

    /// <summary>Moves the fake's clock on by <paramref name="seconds"/> and does what is due.</summary>
    public void Advance(double seconds)
    {
        if (Command is null) return;
        Elapsed += seconds;
        foreach (var step in Timeline.Steps)
        {
            if (done.Contains(step) || Elapsed + 1e-9 < step.At) continue;
            var share = step.Seconds <= 0 ? 1 : Math.Clamp((Elapsed - step.At) / step.Seconds, 0, 1);
            step.Apply(this, share);
            if (share >= 1)
            {
                done.Add(step);
                if (step.Milestone is { } milestone) seen.Add(milestone);
            }
        }
    }

    public void WriteLog(string text, string level)
    {
        var path = Command?.LogPath ?? throw new InvalidOperationException("No log.");
        var time = TimeSpan.FromSeconds(Elapsed);
        var prefix = string.Create(CultureInfo.InvariantCulture, $"[setup:4242] [{12 + time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}] {level} [velopack::install] ");
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(file);
        writer.Write(prefix + text.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n");
    }

    public void SetLength(string relative, long bytes)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length < bytes) file.SetLength(bytes);
    }

    public void RenameRootAway()
    {
        rollback = Root + ".A1b2C3d4E5f6G7h8";
        Directory.Move(Root, rollback);
        Directory.CreateDirectory(Root);
    }

    public void RestoreRoot()
    {
        if (rollback is null) return;
        Directory.Delete(Root, recursive: true);
        Directory.Move(rollback, Root);
        rollback = null;
    }

    public void DeleteRollback()
    {
        if (rollback is null) return;
        Directory.Delete(rollback, recursive: true);
        rollback = null;
    }

    public void MoveFolder(string from, string to) => Directory.Move(Path.Combine(Root, from), Path.Combine(Root, to));

    public void WriteStartLink()
    {
        Directory.CreateDirectory(StartMenu);
        System.IO.File.WriteAllText(Path.Combine(StartMenu, "nth.lnk"), "fake link to " + Path.Combine(Root, "current", "nth.exe"));
    }

    public void WriteResult(string json)
    {
        var options = Command!.Environment[InstallOptions.EnvironmentVariable];
        System.IO.File.WriteAllText(InstallOptions.ResultPathFor(options), json);
    }

    public void Exit(int code) => exit.TrySetResult(code);
}
