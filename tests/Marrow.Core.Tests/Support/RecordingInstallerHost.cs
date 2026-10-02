using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

/// <summary>
/// ED-247: stands for the Marrow Setup bootstrapper's Win32 side (src/Marrow.Setup/Program.cs) in InstallerGateTests.
/// It records every message the gate shows and every hand-over to Velopack's Setup.exe, and never installs anything.
/// </summary>
internal sealed class RecordingInstallerHost(int setupExitCode = 0) : IInstallerGateHost
{
    public List<(string Caption, string Text, bool Warning)> Messages { get; } = [];

    public List<IReadOnlyList<string>> SetupRuns { get; } = [];

    public void ShowMessage(string caption, string text, bool warning) => Messages.Add((caption, text, warning));

    public int RunVelopackSetup(IReadOnlyList<string> arguments)
    {
        SetupRuns.Add([.. arguments]);
        return setupExitCode;
    }
}
