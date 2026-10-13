using System.Text;
using Marrow.Setup.Install;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 ("The --log file is tailed as a second signal and for error reasons")
// and VELOPACK_CHECK.md check 5; unit U3 (ED-NEW-inst-u3-1). The tail reads only this run's whole lines while Velopack
// still writes the file, and starts again after a rotation; the signals are read from Velopack 1.2.161's own phrases
// (install.rs, windows/util.rs, setup.rs, setup_errors.rs); the error lines give the reason.
public sealed class VelopackLogTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-u3-log-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    private string LogPath => Path.Combine(folder, "nth Setup Engine.log");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_tail_returns_whole_lines_only_while_the_writer_keeps_the_file_open()
    {
        using var writer = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var tail = new VelopackLogTail(LogPath);
        Write(writer, "[setup:1] [12:00:00] INFO first\r\n[setup:1] [12:00:00] INFO sec");
        Assert.Equal(["[setup:1] [12:00:00] INFO first"], tail.ReadNew());
        Assert.Empty(tail.ReadNew());
        Write(writer, "ond\r\nthird ü\n");
        Assert.Equal(["[setup:1] [12:00:00] INFO second", "third ü"], tail.ReadNew());
        Assert.Equal(["[setup:1] [12:00:00] INFO first", "[setup:1] [12:00:00] INFO second", "third ü"], tail.Recent);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_earlier_run_s_lines_are_skipped_and_a_rotated_file_is_read_from_its_start()
    {
        File.WriteAllText(LogPath, "old run\r\n");
        var tail = new VelopackLogTail(LogPath, VelopackLogTail.LengthOf(LogPath));
        File.AppendAllText(LogPath, "new run\r\n");
        Assert.Equal(["new run"], tail.ReadNew());
        File.WriteAllText(LogPath, "rotated\r\n");
        Assert.Equal(["rotated"], tail.ReadNew());
        Assert.Equal(0, VelopackLogTail.LengthOf(Path.Combine(folder, "missing.log")));
        Assert.Empty(new VelopackLogTail(Path.Combine(folder, "missing.log")).ReadNew());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Copy_details_keeps_the_last_40_lines()
    {
        File.WriteAllLines(LogPath, Enumerable.Range(1, 100).Select(i => "line " + i));
        var tail = new VelopackLogTail(LogPath);
        Assert.Equal(100, tail.ReadNew().Count);
        Assert.Equal(VelopackLogTail.KeptLines, tail.Recent.Count);
        Assert.Equal("line 61", tail.Recent[0]);
        Assert.Equal("line 100", tail.Recent[^1]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_good_run_s_lines_turn_on_each_signal_in_order()
    {
        var signals = new VelopackLogSignals();
        var seen = new List<string>();
        foreach (var line in GoodRun)
        {
            signals.Read(line);
            seen.Add($"{signals.RenamedForRollback}{signals.Writing}{signals.CreatingShortcuts}{signals.HookStarted}{signals.HookEnded}{signals.Completed}");
        }
        Assert.True(signals.RenamedForRollback && signals.Writing && signals.CreatingShortcuts && signals.HookStarted && signals.HookEnded && signals.Completed);
        Assert.False(signals.Failed || signals.RollingBack || signals.HookTimedOut);
        Assert.Empty(signals.Errors);
        Assert.Equal(FailureReason.Other, signals.Reason());
        Assert.Equal("FalseFalseFalseFalseFalseFalse", seen[0]);
        Assert.Equal("TrueTrueTrueTrueFalseFalse", seen[GoodRun.IndexOf(GoodRun.First(l => l.Contains("Running --veloapp-install", StringComparison.Ordinal)))]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_killed_hook_ends_the_hook_without_failing_the_install()
    {
        var signals = new VelopackLogSignals();
        signals.Read(["[setup:9] [12:00:01] INFO [velopack::install] Starting process install hook", "[setup:9] [12:00:31] ERROR [velopack::windows::util] Process timed out after 30s and was killed."]);
        Assert.True(signals.HookStarted && signals.HookEnded && signals.HookTimedOut);
        Assert.Empty(signals.Errors);
    }

    [Theory]
    [InlineData("An error has occurred: nth requires at least 470.00 MB disk space to be installed. There is only 112.00 MB available.", FailureReason.NoSpace)]
    [InlineData("An error has occurred: There is not enough space on the disk. (os error 112)", FailureReason.NoSpace)]
    [InlineData("An error has occurred: The process cannot access the file because it is being used by another process. (os error 32)", FailureReason.InUse)]
    [InlineData("An error has occurred: Failed to stop application (Access is denied.), please close the application and try running the installer again.", FailureReason.InUse)]
    [InlineData("An error has occurred: Failed to remove existing application directory, please close the application and try running the installer again.", FailureReason.InUse)]
    [InlineData("An error has occurred: Access is denied. (os error 5)", FailureReason.Blocked)]
    [InlineData("An error has occurred: invalid Zip archive: unexpected end of file", FailureReason.Other)]
    [Trait("Decision", "ED-247")]
    public void The_error_lines_give_the_reason(string error, FailureReason reason)
    {
        var signals = new VelopackLogSignals();
        signals.Read("[setup:9] [12:00:01] WARN [velopack::install] Failed to apply channel override (non-fatal): Access is denied. (os error 5)");
        signals.Read("[setup:9] [12:00:02] ERROR [velopack::install] Installation failed!");
        signals.Read("[setup:9] [12:00:02] ERROR [setup] " + error);
        Assert.True(signals.Failed);
        Assert.Equal(reason, signals.Reason());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_error_s_continuation_lines_are_part_of_it()
    {
        var signals = new VelopackLogSignals();
        signals.Read(["[setup:9] [12:00:02] ERROR [setup] An error has occurred: Failed to extract", "", "Caused by:", "    The process cannot access the file because it is being used by another process. (os error 32)", "[setup:9] [12:00:03] INFO next"]);
        Assert.Equal(3, signals.Errors.Count);
        Assert.Equal(FailureReason.InUse, signals.Reason());
    }

    private static void Write(FileStream writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes);
        writer.Flush();
    }

    private static readonly List<string> GoodRun =
    [
        "[setup:9] [12:00:00] INFO [setup] Starting Velopack Setup (1.2.161)",
        "[setup:9] [12:00:00] INFO [velopack::install] User chose to overwrite existing installation.",
        "[setup:9] [12:00:00] INFO [velopack::install] Renaming existing directory to '\"C:\\Users\\ana\\AppData\\Local\\Programs\\nth.A1b2C3d4E5f6G7h8\"' to allow rollback...",
        "[setup:9] [12:00:00] INFO [velopack::install] Preparing and cleaning installation directory...",
        "[setup:9] [12:00:00] INFO [velopack::install] Acquiring lock...",
        "[setup:9] [12:00:00] INFO [velopack::install] Starting installation!",
        "[setup:9] [12:00:00] INFO [velopack::install] Extracting Update.exe...",
        "[setup:9] [12:00:01] INFO [velopack::install] Copying nupkg to packages directory...",
        "[setup:9] [12:00:09] INFO [velopack::install] Creating shortcuts...",
        "[setup:9] [12:00:09] INFO [velopack::install] Starting process install hook",
        "[setup:9] [12:00:09] INFO [velopack::windows::util] Running --veloapp-install hook...",
        "[setup:9] [12:00:10] INFO [velopack::windows::util] Hook executed successfully (took 912ms)",
        "[setup:9] [12:00:10] INFO [velopack::install] Installation completed successfully!",
        "[setup:9] [12:00:10] INFO [velopack::install] Removing rollback directory...",
    ];
}
