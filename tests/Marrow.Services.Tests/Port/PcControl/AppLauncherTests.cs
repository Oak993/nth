using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-3). What pc_launch_app may hand to ShellExecuteEx: an existing .exe by its
// full path or an AppsFolder id, never a command line, a URL, another shell: location or a script. Nothing is launched.
[Trait("Decision", "ED-NEW-pcctl-3")]
public sealed class AppLauncherTests
{
    [Fact]
    public void An_existing_exe_by_full_path_is_opened_as_itself()
    {
        using var folder = new PcTestFolder("pcctl-launch-");
        {
            var exe = Path.Combine(folder.Path, "tool.exe");
            File.WriteAllBytes(exe, []);
            Assert.Equal(exe, AppLauncher.Target(exe));
            Assert.Null(AppLauncher.Target(Path.Combine(folder.Path, "missing.exe")));
            var script = Path.Combine(folder.Path, "run.bat");
            File.WriteAllBytes(script, []);
            Assert.Null(AppLauncher.Target(script));
        }
    }

    [Theory]
    [InlineData(@"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe", @"shell:AppsFolder\{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe")]
    public void An_apps_folder_id_is_opened_through_shell_apps_folder(string id, string target)
    {
        Assert.Equal(target, AppLauncher.Target(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notepad.exe & calc.exe")]
    [InlineData("cmd.exe /c del x")]
    [InlineData(@"""C:\Windows\notepad.exe"" x")]
    [InlineData("https://example.test/x")]
    [InlineData("shell:Startup")]
    [InlineData(@"\\server\share\app.exe")]
    [InlineData("a|b")]
    public void Command_lines_urls_shell_locations_and_shares_are_refused(string? text)
    {
        Assert.Null(AppLauncher.Target(text));
    }
}
