using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: security review of the web feature, S2 (ED-NEW-web-sec-rev-2). A tool the server declares app-only (its
// _meta.ui.visibility leaves out "model") is never run for the model, also while the hub's list of that server is not cached:
// after the server said its tools changed, or when the listing that saw the tool was overtaken by a change. The hub keeps what
// each listing said about who may call a tool until a newer listing says otherwise. Driven end to end against the scripted
// stand-in server (Fixtures/mcp-stdio-rich-fixture.ps1, a system binary; no real AI CLI, no network).
[Trait("Decision", "ED-NEW-web-sec-rev-2")]
public sealed class MCPHubModelVisibilityTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static string FixtureScript([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-rich-fixture.ps1");

    private static async Task<MCPHub> Hub(IReadOnlyDictionary<string, string>? env = null)
    {
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["s"] = new MCPServerSpec.Stdio("powershell",
                new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FixtureScript()]),
                new ValueMap<string, string>(env ?? new Dictionary<string, string>(), SwiftStringComparer.Instance)),
        });
        return hub;
    }

    [Fact]
    public async Task After_the_server_says_its_tools_changed_a_model_call_to_an_app_only_tool_is_still_refused()
    {
        var hub = await Hub();
        try
        {
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);
            Assert.DoesNotContain(await hub.Tools().WaitAsync(Bound), t => t.Name == "refresh");
            await hub.Call("mcp__s__change-tools", new JSONValue.Null()).WaitAsync(Bound);
            await changed.Task.WaitAsync(Bound); // the hub dropped its cached list of "s"

            var refused = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__refresh", new JSONValue.Null()).WaitAsync(Bound));
            Assert.Equal("Unknown tool: mcp__s__refresh.", refused.ErrorDescription);

            // The app view still reaches it, and the model still reaches what it may call.
            Assert.Equal("ran:refresh", (await hub.Call("mcp__s__refresh", new JSONValue.Null(), MCPCaller.App).WaitAsync(Bound)).Text);
            Assert.Equal("ran:board", (await hub.Call("mcp__s__board", new JSONValue.Null()).WaitAsync(Bound)).Text);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_model_call_to_an_app_only_tool_seen_by_a_listing_overtaken_by_a_change_is_refused()
    {
        var hub = await Hub(new Dictionary<string, string> { ["MCP_RICH_NOTIFY_ON_LIST"] = "once" });
        try
        {
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);
            Assert.DoesNotContain(await hub.Tools().WaitAsync(Bound), t => t.Name == "refresh"); // returned, not cached
            await changed.Task.WaitAsync(Bound);

            var refused = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__refresh", new JSONValue.Null()).WaitAsync(Bound));
            Assert.Equal("Unknown tool: mcp__s__refresh.", refused.ErrorDescription);
        }
        finally
        {
            await hub.Reload();
        }
    }
}
