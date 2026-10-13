using System.Text.RegularExpressions;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 ("The temp copy deletes itself"); ED-NEW-inst-picks-1, which settles the pick
// of ED-NEW-inst-fix-2 as the helper option. The uninstaller's copy cannot delete its own file, so as it ends it starts one
// short-lived hidden cmd.exe that waits for its process id and then removes exactly the copy's folder. Through a fake process
// launcher (nothing is started): what would start, in which folder, with which command line, and the many cases in which nothing
// does. The command line itself was run once by hand against a scratch folder and a short-lived process (ED-NEW-inst-picks-1).
public sealed class UninstallCopyCleanupTests
{
    private const int ProcessId = 48384;

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_copy_in_its_own_marked_folder_starts_one_hidden_shell_that_waits_for_its_process_id_and_removes_that_folder()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        area.Sweep.Put(Path.Combine(area.Sweep.UninstallTemp, "libSkiaSharp.dll"));
        area.Sweep.Put(Path.Combine(area.Sweep.UninstallTemp, "setup.log"));
        var system = SystemFolder(area);
        var launcher = new FakeHelperLauncher();

        Assert.True(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start));

        var start = Assert.Single(launcher.Starts);
        Assert.Equal(Path.Combine(system, "cmd.exe"), start.File);
        Assert.Equal(system, start.WorkingDirectory); // never in the folder it removes
        var folder = area.Sweep.UninstallTemp;
        Assert.StartsWith("/d /v:off /s /c \"for /l %n in (1,1,90) do @(", start.Arguments, StringComparison.Ordinal);
        Assert.EndsWith(")\"", start.Arguments, StringComparison.Ordinal);
        Assert.Contains("\"PID eq " + ProcessId + "\"", start.Arguments, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(start.Arguments, "rd /s /q \"" + folder + "\""));
        Assert.Equal(1, Occurrences(start.Arguments, "rd /s /q"));
        Assert.Equal(1, Occurrences(start.Arguments, "%")); // only the loop's own variable: nothing for the shell to expand
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_command_line_names_one_folder_in_full_and_every_program_by_its_full_path_and_no_wildcard_or_delete()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        var system = SystemFolder(area);

        var helper = UninstallCopyCleanup.Plan(copy, area.Temp, ProcessId, system)!;

        Assert.Equal(area.Sweep.UninstallTemp, helper.Folder);
        var inner = helper.Arguments["/d /v:off /s /c \"".Length..^1]; // inside the shell's own outer quotes
        var quoted = Regex.Matches(inner, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).Where(text => text.Contains('\\', StringComparison.Ordinal)).Distinct().Order().ToArray();
        string[] expected = [area.Sweep.UninstallTemp, Path.Combine(system, "find.exe"), Path.Combine(system, "ping.exe"), Path.Combine(system, "tasklist.exe")];
        Assert.Equal(expected.Order().ToArray(), quoted);
        Assert.DoesNotContain("*", helper.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("?", helper.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("del ", helper.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" /f ", helper.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("copy ", helper.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("start ", helper.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("schtasks", helper.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RunOnce", helper.Arguments, StringComparison.OrdinalIgnoreCase);
        // the removal happens only once the process id is no longer listed, and only the removing round can end the shell
        var removal = helper.Arguments.IndexOf("if errorlevel 1 (rd /s /q", StringComparison.Ordinal);
        Assert.True(removal > helper.Arguments.IndexOf("tasklist.exe", StringComparison.Ordinal));
        Assert.Equal(1, Occurrences(helper.Arguments, " exit)"));
        Assert.True(helper.Arguments.IndexOf(" exit)", StringComparison.Ordinal) > removal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_helper_is_bounded_and_every_round_waits_about_a_second_so_it_always_ends()
    {
        Assert.Equal(90, UninstallCopyCleanup.Rounds);
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        var helper = UninstallCopyCleanup.Plan(copy, area.Temp, ProcessId, SystemFolder(area))!;

        Assert.Contains("for /l %n in (1,1,90) do", helper.Arguments, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(helper.Arguments, "ping.exe\" -n 2 127.0.0.1 >nul")); // both branches of a round that is not done wait
        Assert.DoesNotContain("goto", helper.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(":loop", helper.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_copy_under_a_temp_folder_with_parentheses_and_an_ampersand_is_quoted_whole()
    {
        using var area = new UninstallArea();
        var temp = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "Ana (Work) & Co", "temp")).FullName;
        var folder = Path.Combine(temp, "nth Uninstall");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var copy = Path.Combine(folder, "nth Uninstall.exe");
        File.WriteAllText(copy, "copy");

        var helper = UninstallCopyCleanup.Plan(copy, temp, ProcessId, SystemFolder(area))!;

        Assert.Contains("rd /s /q \"" + folder + "\"", helper.Arguments, StringComparison.Ordinal);
        Assert.Contains("if not exist \"" + folder + "\" exit", helper.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_starts_unless_the_running_program_is_the_copy_in_the_folder_nth_made()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        var system = SystemFolder(area);
        var launcher = new FakeHelperLauncher();
        var folder = area.Sweep.UninstallTemp;

        // the facts that must hold, one at a time
        Assert.False(UninstallCopyCleanup.Begin(Path.Combine(area.Root, "nth Uninstall.exe"), area.Temp, ProcessId, system, launcher.Start)); // a run from the root
        Assert.False(UninstallCopyCleanup.Begin(Path.Combine(folder, "deeper", "nth Uninstall.exe"), area.Temp, ProcessId, system, launcher.Start)); // not directly in the folder
        Assert.False(UninstallCopyCleanup.Begin(Path.Combine(area.Temp, "nth Uninstall 2", "nth Uninstall.exe"), area.Temp, ProcessId, system, launcher.Start)); // a sibling that only starts like it
        Assert.False(UninstallCopyCleanup.Begin(folder, area.Temp, ProcessId, system, launcher.Start)); // the folder itself
        Assert.False(UninstallCopyCleanup.Begin(null, area.Temp, ProcessId, system, launcher.Start));
        Assert.False(UninstallCopyCleanup.Begin("nth Uninstall.exe", area.Temp, ProcessId, system, launcher.Start)); // not a full path
        Assert.False(UninstallCopyCleanup.Begin(copy, "temp", ProcessId, system, launcher.Start));
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, 0, system, launcher.Start));
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, -4, system, launcher.Start));
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, "system32", launcher.Start));
        Assert.False(UninstallCopyCleanup.Begin(copy, Path.Combine(area.Sweep.Base, "elsewhere"), ProcessId, system, launcher.Start)); // that temp folder has no such copy

        Assert.Empty(launcher.Starts);
        Assert.True(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start)); // and the same call with every fact true starts
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_folder_nth_did_not_make_or_cannot_prove_is_never_handed_to_the_helper()
    {
        using var area = new UninstallArea();
        var system = SystemFolder(area);
        var launcher = new FakeHelperLauncher();
        var folder = area.Sweep.UninstallTemp;
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "nth Uninstall.exe");
        File.WriteAllText(copy, "copy");
        var marker = Path.Combine(folder, ".nth-setup-owner");

        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start)); // no marker: unmarked content is never adopted
        File.WriteAllText(marker, "somebody else's folder\n");
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start)); // a marker with other words
        File.WriteAllText(marker, "nth Setup owned unit v1\n");
        Assert.True(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start));
        launcher.Starts.Clear();

        Directory.CreateDirectory(Path.Combine(folder, "logs")); // a folder inside: not what nth makes here
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start));
        Directory.Delete(Path.Combine(folder, "logs"));

        var target = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "somewhere else")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "mine");
        SweepArea.MakeJunction(Path.Combine(folder, "link"), target); // a link inside: never followed, never handed on
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start));
        Directory.Delete(Path.Combine(folder, "link"));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));

        for (var i = 0; i < UninstallCopyCleanup.MaxEntries; i++) File.WriteAllText(Path.Combine(folder, "many " + i + ".log"), "x"); // more than nth ever writes there
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start));

        Assert.Empty(launcher.Starts);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_folder_itself_as_a_link_is_refused()
    {
        using var area = new UninstallArea();
        var system = SystemFolder(area);
        var launcher = new FakeHelperLauncher();
        var target = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "target folder")).FullName;
        File.WriteAllText(Path.Combine(target, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        File.WriteAllText(Path.Combine(target, "nth Uninstall.exe"), "copy");
        SweepArea.MakeJunction(area.Sweep.UninstallTemp, target);

        Assert.False(UninstallCopyCleanup.Begin(Path.Combine(area.Sweep.UninstallTemp, "nth Uninstall.exe"), area.Temp, ProcessId, system, launcher.Start));

        Assert.Empty(launcher.Starts);
        Assert.True(File.Exists(Path.Combine(target, "nth Uninstall.exe")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_path_that_cannot_sit_between_quotes_as_it_is_starts_nothing()
    {
        using var area = new UninstallArea();
        var launcher = new FakeHelperLauncher();
        var percent = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "100%done", "temp")).FullName;
        var folder = Path.Combine(percent, "nth Uninstall");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var copy = Path.Combine(folder, "nth Uninstall.exe");
        File.WriteAllText(copy, "copy");

        Assert.False(UninstallCopyCleanup.Begin(copy, percent, ProcessId, SystemFolder(area), launcher.Start)); // % would be expanded by the shell

        var system = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "50%sys")).FullName;
        foreach (var tool in new[] { "cmd.exe", "tasklist.exe", "find.exe", "ping.exe" }) File.WriteAllText(Path.Combine(system, tool), "tool");
        var marked = area.Sweep.PutInUninstall("nth Uninstall.exe");
        Assert.False(UninstallCopyCleanup.Begin(marked, area.Temp, ProcessId, system, launcher.Start));

        Assert.Empty(launcher.Starts);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_system_folder_without_one_of_the_four_programs_starts_nothing()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        var launcher = new FakeHelperLauncher();
        foreach (var missing in new[] { "cmd.exe", "tasklist.exe", "find.exe", "ping.exe" })
        {
            var system = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "system without " + missing)).FullName;
            foreach (var tool in new[] { "cmd.exe", "tasklist.exe", "find.exe", "ping.exe" }.Where(tool => tool != missing)) File.WriteAllText(Path.Combine(system, tool), "tool");
            Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, launcher.Start), missing);
        }
        Assert.Empty(launcher.Starts);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_helper_that_cannot_start_is_reported_so_the_restart_removal_and_the_sweeps_remain()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.PutInUninstall("nth Uninstall.exe");
        var system = SystemFolder(area);

        var refused = new FakeHelperLauncher { Result = false };
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, refused.Start));
        Assert.Single(refused.Starts);

        var blocked = new FakeHelperLauncher { Throws = true }; // a security product that refuses the start
        Assert.False(UninstallCopyCleanup.Begin(copy, area.Temp, ProcessId, system, blocked.Start));
        Assert.Single(blocked.Starts);

        Assert.True(Directory.Exists(area.Sweep.UninstallTemp)); // planning removes nothing
        Assert.True(File.Exists(copy));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_program_starts_the_helper_first_and_keeps_the_restart_removal_only_for_a_helper_that_did_not_start()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));
        var begin = program.IndexOf("UninstallCopyCleanup.Begin(process, temp, Environment.ProcessId, Environment.SystemDirectory, StartCleanupHelper)", StringComparison.Ordinal);
        var restart = program.IndexOf("UninstallLaunch.ScheduleCopyRemoval(process, temp, Restart" + "Removal.DeleteAtRestart)", StringComparison.Ordinal); // spelled apart: no test file may name the real call
        Assert.True(begin > 0 && restart > begin, "the helper is tried first");
        Assert.Contains("if (copy && !UninstallCopyCleanup.Begin(", program, StringComparison.Ordinal);
        Assert.Contains("CreateNoWindow = true", program[program.IndexOf("private static bool StartCleanupHelper", StringComparison.Ordinal)..], StringComparison.Ordinal);
        // nothing else in the setup starts the shell for this: the helper is the one place that names it
        var setup = Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup");
        var named = Directory.EnumerateFiles(setup, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.ReadAllText(file).Contains("\"cmd.exe\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(setup, file).Replace('\\', '/'))
            .Order()
            .ToArray();
        Assert.Equal(["Flow/UninstallCopyCleanup.cs", "Smoke/UiSmoke.cs"], named); // the smoke run only names it as a stand-in link target
    }

    private static string SystemFolder(UninstallArea area)
    {
        var system = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "system")).FullName;
        foreach (var tool in new[] { "cmd.exe", "tasklist.exe", "find.exe", "ping.exe" }) File.WriteAllText(Path.Combine(system, tool), "tool");
        return system;
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
