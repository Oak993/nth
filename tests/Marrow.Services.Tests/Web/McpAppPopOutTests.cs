using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review, finding M6 (DESIGN.md 7.6 "Display modes", 8.2 "content cannot impersonate nth").
// An MCP App's ui/request-display-mode to pip opened a pinned, activated, borderless window with no user action, one that
// could pose as a system dialog. The app's own request for a window of its own now waits for the user's Pop out on a card
// (Not now leaves it where it is); inline and the App pane (the spec's fullscreen) stay inside nth's window and move at once.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-1")]
public sealed class McpAppPopOutTests
{
    private static JsonObject Mode(string mode) => new() { ["mode"] = mode };

    [Fact]
    public void An_apps_own_pip_waits_for_the_users_Pop_out_on_a_card()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        var (mount, _) = rig.Open();
        var moved = new List<McpAppDisplayMode>();
        mount.DisplayModeHandler = mode =>
        {
            moved.Add(mode);
            mount.UpdateContext(c => c with { DisplayMode = mode });
            return mode;
        };
        rig.Handshake("inline", "pip", "fullscreen");
        var held = new TaskCompletionSource<bool>();
        rig.Ui.HeldDisplay = held;

        rig.Send(new JsonObject { ["id"] = "p1", ["method"] = McpAppProtocol.RequestDisplayMode, ["params"] = Mode("pip") });
        Assert.False(rig.Replied("p1"));
        Assert.Empty(moved); // nothing moves before the user answers
        Assert.Equal([McpAppDisplayMode.PopOut], rig.Ui.DisplayCards);

        held.SetResult(false);
        Assert.Equal("inline", rig.Reply("p1")["result"]!["mode"]!.GetValue<string>());
        Assert.Empty(moved);
        Assert.Equal(McpAppDisplayMode.Inline, mount.DisplayMode);

        rig.Ui.HeldDisplay = null;
        rig.Ui.DisplayAnswer = true;
        Assert.Equal("pip", rig.Call("p2", McpAppProtocol.RequestDisplayMode, Mode("pip"))["result"]!["mode"]!.GetValue<string>());
        Assert.Equal([McpAppDisplayMode.PopOut], moved);
        Assert.Equal(2, rig.Ui.DisplayCards.Count);

        // Back inline, and into the App pane: inside nth's window, no card.
        Assert.Equal("inline", rig.Call("i", McpAppProtocol.RequestDisplayMode, Mode("inline"))["result"]!["mode"]!.GetValue<string>());
        Assert.Equal("fullscreen", rig.Call("f", McpAppProtocol.RequestDisplayMode, Mode("fullscreen"))["result"]!["mode"]!.GetValue<string>());
        Assert.Equal([McpAppDisplayMode.PopOut, McpAppDisplayMode.Inline, McpAppDisplayMode.Pane], moved);
        Assert.Equal(2, rig.Ui.DisplayCards.Count);
    }

    [Fact]
    public void An_app_that_never_said_it_supports_pip_gets_no_card_and_stays()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        var (mount, _) = rig.Open();
        var moved = new List<McpAppDisplayMode>();
        mount.DisplayModeHandler = mode =>
        {
            moved.Add(mode);
            return mode;
        };
        rig.Handshake("inline");

        Assert.Equal("inline", rig.Call("p", McpAppProtocol.RequestDisplayMode, Mode("pip"))["result"]!["mode"]!.GetValue<string>());
        Assert.Empty(rig.Ui.DisplayCards);
        Assert.Empty(moved);
    }
}
