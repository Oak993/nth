using System.ComponentModel;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A process launcher for the uninstaller copy's cleanup helper (ED-NEW-inst-picks-1): it records what would have been started
/// (file, command line, working folder) and starts nothing. <see cref="Result"/> is what it answers; <see cref="Throws"/> makes
/// it fail the way a refused start does.
/// </summary>
public sealed class FakeHelperLauncher
{
    public bool Result { get; set; } = true;

    public bool Throws { get; set; }

    public List<(string File, string Arguments, string WorkingDirectory)> Starts { get; } = [];

    public bool Start(string file, string arguments, string workingDirectory)
    {
        Starts.Add((file, arguments, workingDirectory));
        return Throws ? throw new Win32Exception(5) : Result;
    }
}
