using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ACPSession.swift (B3 52bc7926), probeModels :228-247.
//
// Regression proof for the catalog probe's working directory: the probe session starts the agent in its throwaway
// catalog folder under the system temp folder (:229-234). On Windows the folder reached CreateProcessW as
// "/C:/Users/.../marrow-catalog", which is rejected ("The directory name is invalid"), so no probe ever started an
// agent. The agent here is the compiled Claude stand-in (R-TEST-8), told to exit at once: the launch is what is proved.
public sealed class ACPSessionProbeLaunchTests
{
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static string Normalised(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant();

    [Fact]
    [Trait("Req", "PR-PROVACP-136")] // ACPSession.swift:229-234 (the probe session runs in the catalog folder under the temporary directory)
    public async Task the_catalog_probe_starts_its_agent_in_the_catalog_folder_under_the_system_temp_folder()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("acp-probe-launch");
        File.WriteAllText(Path.Combine(script.ControlDirectory, "exit-now.txt"), "1"); // the stand-in exits as soon as it has recorded its launch
        var environment = new ValueMap<string, string>(new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value });

        // The agent exits without answering, so the probe fails: the failure is the try? the registry swallows.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Task.Run(() => runtime.Loop.Run(() => ACPSession.ProbeModels(ProviderKind.Opencode, FileUrl(script.ExecutablePath), environment), TimeSpan.FromSeconds(30))));

        var launch = Assert.Single(script.Calls());
        Assert.Equal(["acp"], launch.Arguments);
        Assert.Equal(Normalised(Path.Combine(Path.GetTempPath(), "marrow-catalog")), Normalised(script.WorkingDirectory(0)));
    }
}
