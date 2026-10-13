using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// Stands for the gate host's Win32 side in the setup's tests (unit U10; the same idea as Marrow.Core.Tests' RecordingInstallerHost,
/// which the setup's tests cannot link). It records every message the gate would show and every hand-over to Velopack's
/// Setup.exe, answers the exit code it was given, and starts nothing.
/// </summary>
public sealed class RecordingGateHost(int setupExitCode = 0) : IInstallerGateHost
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
