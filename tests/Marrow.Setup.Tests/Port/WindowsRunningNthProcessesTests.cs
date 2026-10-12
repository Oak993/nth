using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 10, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The Win32 side of the handshake on real
// processes: finding the nth programs below a root (nth.exe, and Marrow.App.exe of an install made before the rename), telling this process from
// a recycled id, and ending one. The programs are copies of ping.exe in a temporary folder, so nothing here is nth and no real nth is touched;
// each test ends what it started. WINDOWS-ONLY.
public sealed class WindowsRunningNthProcessesTests
{
    private static readonly WindowsRunningNthProcesses Processes = new();

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void Both_names_of_the_app_are_found_below_the_root_with_their_file_and_start_and_only_there()
    {
        using var install = new TempNthInstall();
        using var elsewhere = new TempNthInstall();
        using var current = install.Start(@"current\nth.exe");
        using var old = install.Start(@"current\Marrow.App.exe");
        using var other = elsewhere.Start(@"current\nth.exe");

        var found = Processes.FindInside(install.Root);

        Assert.Equal(new[] { current.Id, old.Id }.Order().ToArray(), found.Select(item => item.ProcessId).Order().ToArray());
        Assert.All(found, item =>
        {
            Assert.True(RunningNthPaths.IsInside(install.Root, item.ExecutablePath));
            Assert.True(item.StartedAtTicks > 0);
        });
        Assert.Equal(Path.Combine(install.Root, "current", "nth.exe"), found.Single(item => item.ProcessId == current.Id).ExecutablePath, ignoreCase: true);
        Assert.Equal([other.Id], Processes.FindInside(elsewhere.Root).Select(item => item.ProcessId));
        Assert.Empty(Processes.FindInside(Path.Combine(Path.GetTempPath(), "nth-process-test-nothing-here")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_program_of_another_name_in_the_root_is_not_nth()
    {
        using var install = new TempNthInstall();
        using var helper = install.Start(@"current\helper.exe");

        Assert.Empty(Processes.FindInside(install.Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_process_is_running_until_it_ends_and_an_id_with_another_start_time_is_not_it()
    {
        using var install = new TempNthInstall();
        using var process = install.Start(@"current\nth.exe");
        var nth = Assert.Single(Processes.FindInside(install.Root));

        Assert.True(Processes.IsRunning(nth));
        Assert.False(Processes.IsRunning(nth with { StartedAtTicks = nth.StartedAtTicks + 1 })); // the id was recycled
        Assert.True(Processes.IsRunning(nth with { StartedAtTicks = 0 })); // start unknown: the id alone

        process.Kill();
        process.WaitForExit(5000);

        Assert.False(Processes.IsRunning(nth));
        Assert.False(Processes.IsRunning(nth with { StartedAtTicks = 0 }));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void Ending_a_process_ends_it_and_waits_and_a_second_end_finds_nothing_to_end()
    {
        using var install = new TempNthInstall();
        using var process = install.Start(@"current\nth.exe");
        var nth = Assert.Single(Processes.FindInside(install.Root));

        Assert.Equal(ProcessEndResult.Ended, Processes.End(nth));

        Assert.True(process.WaitForExit(2000));
        Assert.False(Processes.IsRunning(nth));
        Assert.Equal(ProcessEndResult.Gone, Processes.End(nth));
        Assert.Empty(Processes.FindInside(install.Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void An_id_that_now_belongs_to_another_program_is_never_ended()
    {
        using var install = new TempNthInstall();
        using var process = install.Start(@"current\nth.exe");
        var nth = Assert.Single(Processes.FindInside(install.Root));

        var result = Processes.End(nth with { StartedAtTicks = nth.StartedAtTicks + 1 });

        Assert.Equal(ProcessEndResult.Gone, result);
        Assert.False(process.HasExited);
        Assert.True(Processes.IsRunning(nth));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_programs_that_count_as_the_app_are_nth_and_the_name_it_had_before_the_rename()
    {
        Assert.Equal(["nth", "Marrow.App"], WindowsRunningNthProcesses.AppProgramNames);
        Assert.Equal(TimeSpan.FromSeconds(3), WindowsRunningNthProcesses.EndWait);
    }
}
