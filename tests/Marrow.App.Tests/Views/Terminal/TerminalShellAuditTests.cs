using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalStore.swift (B3 52bc792), :28, :36-40; ED-222 / OD-0008 C5.
// File-only PATH stand-ins and the argument-building seam: no TerminalSession or shell process is created.
public sealed class TerminalShellAuditTests
{
    [Fact]
    [Trait("Req", "PR-TERM-039")] // DroppyCode/App/Views/Terminal/TerminalStore.swift:28; ED-222
    public void Resolution_prefers_pwsh_then_powershell_then_the_System32_absolute_path()
    {
        using var home = new ScratchHome();
        var first = Path.Combine(home.Path, "first");
        var second = Path.Combine(home.Path, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var powershell = Path.Combine(first, "powershell.exe");
        var pwsh = Path.Combine(second, "pwsh.exe");
        File.WriteAllText(powershell, "This is a lookup fixture, not an executable.");
        File.WriteAllText(pwsh, "This is a lookup fixture, not an executable.");
        var environment = new Dictionary<string, string> { ["PATH"] = first + ";" + second, ["PATHEXT"] = ".EXE" };
        Assert.Equal(pwsh, WindowsShell.Resolve(environment)); // pwsh wins even later on PATH.
        File.Delete(pwsh);
        Assert.Equal(powershell, WindowsShell.Resolve(environment));
        File.Delete(powershell);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"), WindowsShell.Resolve(environment));
    }

    [Theory]
    [InlineData("Write-Output 'a b'; cmd /c exit 7")]
    [InlineData("")]
    [InlineData("Write-Output \"a;b\"\nWrite-Output '$literal'")]
    [Trait("Req", "PR-TERM-123")] // DroppyCode/App/Views/Terminal/TerminalStore.swift:36-40; ED-222
    public void A_script_uses_exactly_three_arguments_and_preserves_the_command(string command)
    {
        Assert.Equal(new[] { "-NoExit", "-Command", command }, TerminalSession.ArgumentsForCommand(command));
    }

    [Fact]
    [Trait("Req", "PR-TERM-123")] // DroppyCode/App/Views/Terminal/TerminalStore.swift:36-40; ED-222
    public void A_terminal_without_a_script_has_no_command_arguments()
    {
        Assert.Empty(TerminalSession.ArgumentsForCommand(null));
    }
}
