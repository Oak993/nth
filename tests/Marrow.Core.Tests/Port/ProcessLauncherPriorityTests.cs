using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-18 (lean unit L18a): ProcessLauncher's priority groups. A child launched while a background group is
// current runs below normal priority through its own job (JOB_OBJECT_LIMIT_PRIORITY_CLASS, so its tree follows) with
// EcoQoS on the child; the group coming to the foreground puts a live child back at normal priority and lifts the
// limit, so the job is again the limitless job of ED-194. A child launched outside any group is launched as before.
// "No limit" is a job whose limit flags are 0 (Windows then reports the normal class as the job's
// priority class, unused). The children are harmless system tools (sort.exe waiting on its input, powershell.exe reading its own priority),
// never an AI CLI (R-TEST-10). Waits are bounded and condition-driven, never fixed sleeps (R-TEST-5).
public sealed class ProcessLauncherPriorityTests
{
    private const uint Normal = 0x20;
    private const uint BelowNormal = 0x4000;
    private const uint PriorityLimit = 0x20;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_child_launched_in_the_background_group_carries_the_job_priority_limit_and_EcoQoS()
    {
        ProcessLauncher launcher;
        using (ProcessPriorityGroup.Background.Enter()) launcher = Waiting();
        Assert.Same(ProcessPriorityGroup.Background, launcher.PriorityGroup);
        launcher.Run();
        try
        {
            Assert.Equal((PriorityLimit, BelowNormal), launcher.JobPriorityForTesting());
            Assert.Equal(BelowNormal, launcher.PriorityClassForTesting());
            Assert.True(launcher.EcoQoSAppliedForTesting);
        }
        finally
        {
            End(launcher);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_child_launched_outside_any_group_is_launched_exactly_as_before()
    {
        Assert.Null(ProcessPriorityGroup.Current);
        var launcher = Waiting();
        Assert.Null(launcher.PriorityGroup);
        launcher.Run();
        try
        {
            Assert.Equal((uint)0, launcher.JobPriorityForTesting()!.Value.LimitFlags);
            Assert.Null(launcher.EcoQoSAppliedForTesting);
        }
        finally
        {
            End(launcher);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_group_that_comes_to_the_foreground_puts_its_live_child_back_at_normal_and_lifts_the_limit()
    {
        var group = new ProcessPriorityGroup(background: true);
        ProcessLauncher launcher;
        using (group.Enter()) launcher = Waiting();
        launcher.Run();
        try
        {
            Assert.Equal(1, group.Count);
            Assert.Equal(BelowNormal, launcher.PriorityClassForTesting());

            group.IsBackground = false;
            Assert.Equal((uint)0, launcher.JobPriorityForTesting()!.Value.LimitFlags);
            Assert.Equal(Normal, launcher.PriorityClassForTesting());
            Assert.True(launcher.EcoQoSAppliedForTesting);

            group.IsBackground = true;
            Assert.Equal((PriorityLimit, BelowNormal), launcher.JobPriorityForTesting());
            Assert.Equal(BelowNormal, launcher.PriorityClassForTesting());
        }
        finally
        {
            End(launcher);
        }
        Assert.Equal(0, group.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_group_for_one_folder_takes_only_children_launched_in_that_folder()
    {
        var home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var system = Environment.SystemDirectory;
        var group = new ProcessPriorityGroup(background: true, system.ToUpperInvariant() + "\\");
        ProcessLauncher elsewhere;
        ProcessLauncher inside;
        using (group.Enter())
        {
            elsewhere = Waiting(home);
            inside = Waiting(system.ToLowerInvariant());
        }
        elsewhere.Run();
        inside.Run();
        try
        {
            Assert.Equal(1, group.Count);
            Assert.Equal((uint)0, elsewhere.JobPriorityForTesting()!.Value.LimitFlags);
            Assert.Null(elsewhere.EcoQoSAppliedForTesting);
            Assert.Equal((PriorityLimit, BelowNormal), inside.JobPriorityForTesting());
        }
        finally
        {
            End(elsewhere);
            End(inside);
        }
        Assert.Equal(0, group.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public async Task A_Shell_run_inside_the_background_group_reads_its_own_priority_as_below_normal()
    {
        // The path git takes (Git -> Shell.Run -> ProcessLauncher): the group flows from the caller into Shell's own
        // task, and the child sees the priority itself.
        const string script = "[Console]::Out.Write((Get-Process -Id $PID).PriorityClass)";
        ShellResult lowered;
        using (ProcessPriorityGroup.Background.Enter())
            lowered = await Shell.Run(SystemTools.PowerShell, SystemTools.PowerShellScript(script)).WaitAsync(TimeSpan.FromSeconds(60));
        var normal = await Shell.Run(SystemTools.PowerShell, SystemTools.PowerShellScript(script)).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal("BelowNormal", lowered.Output.Trim());
        Assert.Equal("Normal", normal.Output.Trim());
    }

    private static ProcessLauncher Waiting(string? directory = null) => new()
    {
        ExecutableURL = SystemTools.Sort,
        StandardInput = new ProcessPipe(),
        CurrentDirectoryURL = directory,
    };

    private static void End(ProcessLauncher launcher)
    {
        launcher.StandardInput!.CloseWriting();
        if (!launcher.IsRunning) return;
        var waited = Task.Run(launcher.WaitUntilExit).Wait(TimeSpan.FromSeconds(30));
        if (!waited) launcher.KillTree();
        Assert.True(waited, "sort.exe did not end when its input closed");
    }
}
