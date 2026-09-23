using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// LoginEnvironment (Shell.swift:327-462) where the process's own state is read or changed: the process's PATHEXT that
// ED-171 falls back to. These run in the "Login environment" collection, alone, and put every variable back.
[Collection("Login environment")]
public sealed class ShellLoginEnvironmentProcessTests
{
    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:415-427
    [Trait("Decision", "ED-171")]
    public void Without_PATHEXT_in_the_environment_which_takes_the_processs_own_and_without_that_the_four_default_extensions()
    {
        var folder = ProcessTestFolders.Create("login which process pathext");
        ProcessTestFolders.Touch(folder, "tool.cmd");
        ProcessTestFolders.Touch(folder, "tool.exe");
        var absent = LoginEnvironmentSamples.Environment(("PATH", folder));
        var empty = LoginEnvironmentSamples.Environment(("PATH", folder), ("PATHEXT", ""));
        using (new ProcessVariable("PATHEXT", ".CMD"))
        {
            Assert.Equal(Path.Join(folder, "tool.cmd"), LoginEnvironment.Which("tool", absent));
            Assert.Equal(Path.Join(folder, "tool.cmd"), LoginEnvironment.Which("tool", empty));
            // The environment's own PATHEXT still comes first.
            Assert.Equal(Path.Join(folder, "tool.exe"),
                LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", folder), ("PATHEXT", ".EXE"))));
        }
        using (new ProcessVariable("PATHEXT", null))
        {
            // .COM;.EXE;.BAT;.CMD: .EXE before .CMD.
            Assert.Equal(Path.Join(folder, "tool.exe"), LoginEnvironment.Which("tool", absent));
            Assert.Equal(Path.Join(folder, "tool.exe"), LoginEnvironment.Which("tool", empty));
            ProcessTestFolders.Touch(folder, "only.bat");
            ProcessTestFolders.Touch(folder, "only.com");
            Assert.Equal(Path.Join(folder, "only.com"), LoginEnvironment.Which("only", absent));
            ProcessTestFolders.Touch(folder, "script.vbs");
            Assert.Null(LoginEnvironment.Which("script", absent));
        }
    }
}
