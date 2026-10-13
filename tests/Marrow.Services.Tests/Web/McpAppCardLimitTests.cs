using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L7 (DESIGN.md 7.6, 10.1). An MCP App could stack any number of cards:
// a tool card per call, a message card per message, a permission card per request. Now an app has one card of its own
// showing at a time (a tool, its messages, a permission, a link, its own window), and anything it asks for meanwhile is
// refused at once; once the user answers, the next one may ask. nth's own domain card, and the broad entries it names, are
// asked once per page.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-4")]
public sealed class McpAppCardLimitTests
{
    private const string Waiting = "Another request from shipyard is waiting for the user.";

    private static JsonObject Params(params (string Key, JsonNode? Value)[] items)
    {
        var o = new JsonObject();
        foreach (var (key, value) in items) o[key] = value;
        return o;
    }

    private static JsonObject Text(string text) => Params(("role", "user"), ("content", new JsonObject { ["type"] = "text", ["text"] = text }));

    private static string Error(JsonObject reply) => reply["error"]!["message"]!.GetValue<string>();

    [Fact]
    public void While_a_tool_card_shows_everything_else_the_app_asks_for_is_refused_at_once()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None, permissions: new McpAppPermissions(Camera: true, Microphone: false, Geolocation: false, ClipboardWrite: false));
        var (mount, view) = rig.Open();
        mount.DisplayModeHandler = mode => mode;
        rig.Handshake("inline", "pip");
        rig.Ui.MessageAnswer = true;
        rig.Ui.LinkAnswer = true;
        rig.Ui.PermissionAnswer = true;
        rig.Ui.DisplayAnswer = true;
        var held = new TaskCompletionSource<McpAppToolDecision>();
        rig.Ui.HeldTool = held;

        rig.Send(new JsonObject { ["id"] = "t1", ["method"] = McpAppProtocol.ToolsCall, ["params"] = Params(("name", "promote_release")) });
        Assert.False(rig.Replied("t1"));

        Assert.Equal(Waiting, Error(rig.Call("t2", McpAppProtocol.ToolsCall, Params(("name", "promote_release")))));
        Assert.Equal(Waiting, Error(rig.Call("t3", McpAppProtocol.ToolsCall, Params(("name", "list_releases")))));
        Assert.Equal(Waiting, Error(rig.Call("m", McpAppProtocol.Message, Text("Approve it."))));
        Assert.Equal(Waiting, Error(rig.Call("l", McpAppProtocol.OpenLink, Params(("url", "https://shipyard.dev/")))));
        Assert.Equal(Waiting, Error(rig.Call("p", McpAppProtocol.RequestDisplayMode, Params(("mode", "pip")))));
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(view.RaisePermission(rig.Origin, WebPermissionKind.Camera)));

        Assert.Equal(["promote_release"], rig.Ui.ToolCards);
        Assert.Equal(0, rig.Ui.MessageCards);
        Assert.Empty(rig.Ui.LinkCards);
        Assert.Empty(rig.Ui.PermissionCards);
        Assert.Empty(rig.Ui.DisplayCards);
        Assert.Empty(rig.Servers.Calls);

        // The user answers: the call runs, and the next request may ask.
        held.SetResult(McpAppToolDecision.AllowOnce);
        Assert.NotNull(rig.Reply("t1")["result"]);
        rig.Ui.HeldTool = null;
        Assert.NotNull(rig.Call("t4", McpAppProtocol.ToolsCall, Params(("name", "list_releases")))["error"]); // asked, and denied
        Assert.Equal(["promote_release", "list_releases"], rig.Ui.ToolCards);
        Assert.Equal(WebPermissionDecision.AllowOnce, WebRig.Decided(view.RaisePermission(rig.Origin, WebPermissionKind.Camera)));
        Assert.NotNull(rig.Call("m2", McpAppProtocol.Message, Text("Done."))["result"]);
        Assert.Equal(1, rig.Ui.MessageCards);
    }

    [Fact]
    public void A_waiting_message_permission_or_window_card_holds_the_next_request_too()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None, permissions: new McpAppPermissions(Camera: true, Microphone: true, Geolocation: false, ClipboardWrite: false));
        var (mount, view) = rig.Open();
        mount.DisplayModeHandler = mode => mode;
        rig.Handshake("inline", "pip");

        var message = new TaskCompletionSource<bool>();
        rig.Ui.HeldMessage = message;
        rig.Send(new JsonObject { ["id"] = "m1", ["method"] = McpAppProtocol.Message, ["params"] = Text("First.") });
        Assert.Equal(Waiting, Error(rig.Call("m2", McpAppProtocol.Message, Text("Second."))));
        Assert.Equal(Waiting, Error(rig.Call("t", McpAppProtocol.ToolsCall, Params(("name", "promote_release")))));
        Assert.Equal(1, rig.Ui.MessageCards);
        message.SetResult(false);
        Assert.NotNull(rig.Reply("m1")["error"]);

        var permission = new TaskCompletionSource<bool>();
        rig.Ui.HeldPermission = permission;
        var camera = view.RaisePermission(rig.Origin, WebPermissionKind.Camera);
        Assert.False(camera.Decision.IsCompleted);
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(view.RaisePermission(rig.Origin, WebPermissionKind.Microphone)));
        Assert.Equal(Waiting, Error(rig.Call("l", McpAppProtocol.OpenLink, Params(("url", "https://shipyard.dev/")))));
        Assert.Equal([WebPermissionKind.Camera], rig.Ui.PermissionCards);
        permission.SetResult(true);
        Assert.Equal(WebPermissionDecision.AllowOnce, WebRig.Decided(camera));

        var window = new TaskCompletionSource<bool>();
        rig.Ui.HeldDisplay = window;
        rig.Send(new JsonObject { ["id"] = "p1", ["method"] = McpAppProtocol.RequestDisplayMode, ["params"] = Params(("mode", "pip")) });
        Assert.Equal(Waiting, Error(rig.Call("p2", McpAppProtocol.RequestDisplayMode, Params(("mode", "pip")))));
        Assert.Equal(Waiting, Error(rig.Call("t2", McpAppProtocol.ToolsCall, Params(("name", "promote_release")))));
        window.SetResult(false);
        Assert.Equal("inline", rig.Reply("p1")["result"]!["mode"]!.GetValue<string>());
        Assert.Single(rig.Ui.DisplayCards);
        Assert.Empty(rig.Ui.ToolCards);
    }

    [Fact]
    public void The_domain_card_is_asked_once_per_page_and_names_its_broad_entries()
    {
        using var rig = new McpAppsRig();
        var (mount, _) = rig.Open();
        rig.Handshake();
        mount.Reload();
        mount.Reload();
        var card = Assert.Single(rig.Ui.DomainCards);
        Assert.Equal("Allow broad access also allows: *.shipyard-assets.dev", card.BroadText);
    }
}
