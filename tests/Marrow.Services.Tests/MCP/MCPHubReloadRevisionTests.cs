using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: security review of the web feature, follow-up (ED-NEW-web-sec-rev-5). A hub Reload forgets every server and every list,
// so it moves the tools revision as a list-changed notification does: a session that read the tools before reads them again.
[Trait("Decision", "ED-NEW-web-sec-rev-5")]
public sealed class MCPHubReloadRevisionTests
{
    [Fact]
    public async Task A_reload_moves_the_tools_revision_and_a_second_one_moves_it_again()
    {
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["s"] = new MCPServerSpec.Stdio("marrow-sec-rev-tests-no-such-binary", ValueList<string>.Empty, ValueMap<string, string>.Empty),
        });
        var before = hub.ToolsRevision;

        await hub.Reload();
        var once = hub.ToolsRevision;
        await hub.Reload();

        Assert.True(once > before, $"{once} > {before}");
        Assert.True(hub.ToolsRevision > once, $"{hub.ToolsRevision} > {once}");
    }
}
