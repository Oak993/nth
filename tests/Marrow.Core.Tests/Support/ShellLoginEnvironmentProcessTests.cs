using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// LoginEnvironment (Shell.swift:327-462) where the process's own state is read or changed: the process-wide load, and
// the process's PATHEXT that ED-171 falls back to. These run in the "Login environment" collection, alone, and put
// every variable back.
[Collection("Login environment")]
public sealed class ShellLoginEnvironmentProcessTests
{
    [Fact]
    [Trait("Req", "PR-TERM-104")] // Core/Support/Shell.swift:341-351,357-365
    [Trait("Decision", "ED-170")]
    public async Task Load_leaves_the_process_environment_loaded_and_every_caller_returns()
    {
        var before = LoginEnvironment.Current.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(LoginEnvironment.Load))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(LoginEnvironment.IsLoaded);
        await LoginEnvironment.Load().WaitAsync(TimeSpan.FromSeconds(30));
        // With no login shell to read (ED-170 (2)), the loaded environment holds what the fallback held.
        Assert.Equal(before, LoginEnvironment.Current.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

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
            Assert.Equal(Path.Join(folder, "tool.CMD"), LoginEnvironment.Which("tool", absent));
            Assert.Equal(Path.Join(folder, "tool.CMD"), LoginEnvironment.Which("tool", empty));
            // The environment's own PATHEXT still comes first.
            Assert.Equal(Path.Join(folder, "tool.EXE"),
                LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", folder), ("PATHEXT", ".EXE"))));
        }
        using (new ProcessVariable("PATHEXT", null))
        {
            // .COM;.EXE;.BAT;.CMD: .EXE before .CMD, appended as spelled there (ED-129).
            Assert.Equal(Path.Join(folder, "tool.EXE"), LoginEnvironment.Which("tool", absent));
            Assert.Equal(Path.Join(folder, "tool.EXE"), LoginEnvironment.Which("tool", empty));
            ProcessTestFolders.Touch(folder, "only.bat");
            ProcessTestFolders.Touch(folder, "only.com");
            Assert.Equal(Path.Join(folder, "only.COM"), LoginEnvironment.Which("only", absent));
            ProcessTestFolders.Touch(folder, "script.vbs");
            Assert.Null(LoginEnvironment.Which("script", absent));
        }
    }
}
