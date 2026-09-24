using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (ED-188 ruling point 3: "every orchestration record against a fake
// IUpdateInstallBoundary (records the calls, can fail each step)"). Records the boundary calls AppUpdater's
// install orchestration makes, in order, and lets a test make any one of them fail or return a chosen value.
public sealed class FakeUpdateInstallBoundary : IUpdateInstallBoundary
{
    public List<string> Calls { get; } = [];

    // A real, existing directory (not a fixed literal): AppUpdater.HandOff writes install.ps1 and install.log
    // beside the staged path with plain File.WriteAllText, so a fake path with no directory behind it would
    // throw a real DirectoryNotFoundException instead of exercising the record under test.
    public string StagedPathToReturn { get; set; } = Path.Combine(Directory.CreateTempSubdirectory("aut-staged-").FullName, "App");
    public string? VersionToReturn { get; set; } = "1.3.0";
    public Exception? StageThrows { get; set; }
    public Exception? VerifyThrows { get; set; }
    public Exception? LaunchThrows { get; set; }

    public Task<string> Stage(string downloadedArtifactPath, string expectedVersion)
    {
        Calls.Add($"Stage({downloadedArtifactPath}, {expectedVersion})");
        if (StageThrows is { } error) throw error;
        return Task.FromResult(StagedPathToReturn);
    }

    public void Verify(string stagedPath)
    {
        Calls.Add($"Verify({stagedPath})");
        if (VerifyThrows is { } error) throw error;
    }

    public string? ReadVersion(string stagedPath)
    {
        Calls.Add($"ReadVersion({stagedPath})");
        return VersionToReturn;
    }

    public void LaunchDetachedInstaller(int processId, string stagedPath, string destinationPath, string scriptPath, string logPath)
    {
        Calls.Add($"LaunchDetachedInstaller({processId}, {stagedPath}, {destinationPath}, {scriptPath}, {logPath})");
        if (LaunchThrows is { } error) throw error;
    }
}
