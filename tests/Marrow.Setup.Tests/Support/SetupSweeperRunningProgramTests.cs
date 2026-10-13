using System.Diagnostics;
using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers": a running process cannot delete the DLLs it has loaded) and 3
// (Velopack's engine runs from the staging folder), unit U9 (ED-NEW-inst-u9-1). The other sweep tests stand in for "in use" with a file
// held open; this one proves the same answer for a program that really is running from the folder: a copy of cmd.exe named like the
// engine, running a short ping. No installer, Setup.exe or Update.exe is started, and the program is ended by the test.
public sealed class SetupSweeperRunningProgramTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_program_really_running_from_the_staging_folder_keeps_it_whole_until_it_has_ended()
    {
        var engine = Path.Combine(area.SetupRoot, "1.0.0", SetupFolders.EngineFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(engine)!);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), engine);
        var options = area.PutInSetup("1.0.0", InstallOptions.OptionsFileName);
        var start = new ProcessStartInfo(engine) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(engine)! };
        foreach (var argument in new[] { "/c", "ping", "-n", "30", "127.0.0.1" }) start.ArgumentList.Add(argument);

        using (var process = Process.Start(start)!)
        {
            try
            {
                var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

                Assert.Equal(SweepKeptReason.InUse, Assert.Single(result.Kept).Reason);
                Assert.True(File.Exists(engine));
                Assert.True(File.Exists(options), "the options file of a running install must survive");
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        var swept = SweepResult.Nothing;
        for (var attempt = 0; attempt < 50 && !(swept = SetupSweeper.Sweep(area.LocalAppData, area.Temp)).Clean; attempt++) Thread.Sleep(100); // Windows lets go of the image a moment after the exit
        Assert.True(swept.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }
}
