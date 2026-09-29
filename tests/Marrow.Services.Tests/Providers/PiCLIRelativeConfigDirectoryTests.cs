using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): PiCLI.swift:21-23 returns URL(fileURLWithPath: directory), which resolves a relative PI_CODING_AGENT_DIR against the
// current working directory rather than the filesystem root.
public sealed class PiCLIRelativeConfigDirectoryTests
{
    [Trait("Req", "PR-PROVCLI-308")] // Services/Providers/PiCLI.swift:20-27
    [Fact]
    public void A_relative_PI_CODING_AGENT_DIR_is_resolved_against_the_current_directory()
    {
        var environment = new Dictionary<string, string> { ["PI_CODING_AGENT_DIR"] = "pi-agent" };
        var expected = SwiftPath.AppendingPathComponent(Environment.CurrentDirectory, "pi-agent");

        Assert.Equal(expected, PiCLI.ConfigDirectoryPath(environment));
    }
}
