using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: a launcher that records what it was asked to start and with which arguments (unit P18, ED-NEW-pcws-9). It
// starts nothing.
public sealed class RecordingLauncher : IAppLauncher
{
    public List<(string Target, string? Arguments)> Started { get; } = [];

    public IReadOnlyList<PcInstalledApp> Installed() => [];

    public PcLaunch Launch(string appIdOrPath) => Launch(appIdOrPath, null);

    public PcLaunch Launch(string appIdOrPath, string? arguments)
    {
        Started.Add((appIdOrPath, arguments));
        return new PcLaunch(true, null, null);
    }
}
