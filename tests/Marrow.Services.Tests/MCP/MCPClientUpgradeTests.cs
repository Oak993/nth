using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1 and ED-NEW-web-w18-2). The upgraded MCP client driven
// end to end through MCPHub against a scripted stand-in server over stdio (Fixtures/mcp-stdio-rich-fixture.ps1, a
// system binary, powershell.exe; no real AI CLI and no network): tool _meta kept, who may call a tool, structured
// results and images, resources, list changes, and the server's own requests answered.
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class MCPClientUpgradeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static string FixtureScript([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-rich-fixture.ps1");

    private static MCPServerSpec.Stdio Rich(IReadOnlyDictionary<string, string>? env = null) =>
        new("powershell",
            new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FixtureScript()]),
            new ValueMap<string, string>(env ?? new Dictionary<string, string>(), SwiftStringComparer.Instance));

    private static async Task<MCPHub> Hub(IReadOnlyDictionary<string, string>? env = null)
    {
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { ["s"] = Rich(env) });
        return hub;
    }

    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    // ---- the handshake ----

    [Fact]
    public async Task The_hub_tells_the_server_it_hosts_mcp_apps_and_reads_resources()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__client-caps", new JSONValue.Null()).WaitAsync(Bound);
            var sent = Json(result.Text);
            Assert.Equal("2025-06-18", sent["protocolVersion"]?.JSONValueString);
            Assert.Equal("text/html;profile=mcp-app", sent["capabilities"]?["extensions"]?["io.modelcontextprotocol/ui"]?["mimeTypes"]?[0]?.JSONValueString);
            Assert.NotNull(sent["capabilities"]?["resources"]);
            Assert.Equal("nth", sent["clientInfo"]?["name"]?.JSONValueString);
        }
        finally
        {
            await hub.Reload();
        }
    }

    // ---- tool _meta and who may call a tool ----

    [Fact]
    public async Task Tools_keep_their_meta_and_read_the_ui_resource_from_all_three_keys()
    {
        var hub = await Hub();
        try
        {
            var all = await hub.AllTools().WaitAsync(Bound);
            Assert.Equal("ui://board/view.html", all.Single(t => t.Name == "board").UiResourceUri);
            Assert.Equal("ui://flat/view.html", all.Single(t => t.Name == "flat").UiResourceUri);
            Assert.Equal("ui://openai/view.html", all.Single(t => t.Name == "openai").UiResourceUri);
            Assert.Null(all.Single(t => t.Name == "bad-uri").UiResourceUri); // https:// is not a view address
            Assert.Null(all.Single(t => t.Name == "plain").UiResourceUri);
            Assert.Equal("ui://board/view.html", all.Single(t => t.Name == "board").Meta!["ui"]!["resourceUri"]!.JSONValueString);
            Assert.Null(all.Single(t => t.Name == "plain").Meta);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task The_model_is_not_offered_an_app_only_tool_but_the_app_host_still_lists_it()
    {
        var hub = await Hub();
        try
        {
            var model = await hub.Tools().WaitAsync(Bound);
            var all = await hub.AllTools().WaitAsync(Bound);
            Assert.DoesNotContain(model, t => t.Name == "refresh");
            Assert.Contains(model, t => t.Name == "board");
            Assert.Contains(model, t => t.Name == "modelonly");
            Assert.Contains(all, t => t.Name == "refresh");
            Assert.Equal(MCPToolVisibility.App, all.Single(t => t.Name == "refresh").Visibility);
            Assert.Equal(MCPToolVisibility.Model, all.Single(t => t.Name == "modelonly").Visibility);
            Assert.Equal(MCPToolVisibility.Both, all.Single(t => t.Name == "board").Visibility);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_model_call_to_an_app_only_tool_is_refused_and_an_app_call_to_it_goes_through()
    {
        var hub = await Hub();
        try
        {
            // The session lists the tools before the model can call one; the hub knows who may call what from then on.
            Assert.DoesNotContain(await hub.Tools().WaitAsync(Bound), t => t.Name == "refresh");
            var refused = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__refresh", new JSONValue.Null()));
            Assert.Equal("Unknown tool: mcp__s__refresh.", refused.ErrorDescription);

            var allowed = await hub.Call("mcp__s__refresh", new JSONValue.Null(), MCPCaller.App).WaitAsync(Bound);
            Assert.Equal("ran:refresh", allowed.Text);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task An_app_call_lists_the_server_first_and_is_refused_a_model_only_tool()
    {
        var hub = await Hub();
        try
        {
            // Nothing has listed the tools yet: the app caller makes the list known so the visibility can be enforced.
            var refused = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__modelonly", new JSONValue.Null(), MCPCaller.App));
            Assert.Equal("Unknown tool: mcp__s__modelonly.", refused.ErrorDescription);
            var unknown = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__nonexistent", new JSONValue.Null(), MCPCaller.App));
            Assert.Equal("Unknown tool: mcp__s__nonexistent.", unknown.ErrorDescription);
            var model = await hub.Call("mcp__s__modelonly", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("ran:modelonly", model.Text);
        }
        finally
        {
            await hub.Reload();
        }
    }

    // ---- structured results, images and links ----

    [Fact]
    public async Task A_result_keeps_its_structured_content_meta_server_tool_and_ui_resource()
    {
        var hub = await Hub();
        try
        {
            await hub.Tools().WaitAsync(Bound); // the hub learns each tool's view from the list
            var result = await hub.Call("mcp__s__board", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("ran:board", result.Text);
            Assert.Equal(3, result.StructuredContent!["count"]!.JSONValueInt);
            Assert.Equal("abc", result.Meta!["viewState"]!.JSONValueString);
            Assert.Equal(("s", "board", "ui://board/view.html"), (result.Server, result.ToolName, result.UiResourceUri));
            var payload = result.ToPayload()!;
            Assert.Equal("ui://board/view.html", payload.UiResourceUri);
            Assert.Contains("\"count\":3", payload.StructuredContent, StringComparison.Ordinal);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_plain_tool_has_no_payload()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__plain", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("plain-ok", result.Text);
            Assert.Null(result.ToPayload());
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task An_image_comes_through_as_an_image_and_the_text_still_holds_the_placeholder()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__picture", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("here is the chart\n[image]", result.Text);
            Assert.Equal(new MCPResultImage("image/png", "iVBORw0KGgo="), Assert.Single(result.Images));
            Assert.Equal("iVBORw0KGgo=", Assert.Single(result.ToPayload()!.Images).Data);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task Resource_links_and_embedded_resources_are_listed_and_an_app_page_is_kept_from_the_model()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__links", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("[resource: file:///report.pdf]\n[resource: file:///inline.txt]\ninline words\n[resource: ui://board/view.html]", result.Text);
            Assert.Equal(3, result.Resources.Count);
            Assert.True(result.Resources[1].Embedded);
            Assert.DoesNotContain("do not show", result.Text, StringComparison.Ordinal);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_result_with_only_structured_content_reads_as_that_content()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__structured-only", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal("""{"answer":42}""", result.Text);
        }
        finally
        {
            await hub.Reload();
        }
    }

    // ---- resources ----

    [Fact]
    public async Task Resources_are_listed_across_pages_with_their_fields_and_templates_are_listed()
    {
        var hub = await Hub();
        try
        {
            var resources = await hub.Resources("s").WaitAsync(Bound);
            Assert.Equal(["file:///notes.txt", "ui://board/view.html"], resources.Select(r => r.Uri).ToArray());
            var notes = resources[0];
            Assert.Equal(("notes", "Notes", "plain notes", "text/plain", 12L), (notes.Name, notes.Title, notes.Description, notes.MimeType, notes.Size));
            Assert.True(resources[1].Meta!["ui"]!["prefersBorder"]!.JSONValueBool);

            var templates = await hub.ResourceTemplates("s").WaitAsync(Bound);
            var logs = Assert.Single(templates);
            Assert.Equal(("file:///logs/{day}.txt", "logs", "s"), (logs.UriTemplate, logs.Name, logs.Server));
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_server_that_named_its_capabilities_without_resources_is_not_asked_for_them()
    {
        var hub = await Hub(new Dictionary<string, string> { ["MCP_RICH_CAPS"] = "none-resources" });
        try
        {
            Assert.Empty(await hub.Resources("s").WaitAsync(Bound)); // the stand-in would have answered if asked
            Assert.Empty(await hub.ResourceTemplates("s").WaitAsync(Bound));
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task ReadResource_returns_the_items_and_a_missing_resource_is_the_servers_error()
    {
        var hub = await Hub();
        try
        {
            var item = Assert.Single(await hub.ReadResource("s", "file:///notes.txt").WaitAsync(Bound));
            Assert.Equal(("file:///notes.txt", "text/plain", "hello notes"), (item.Uri, item.MimeType, item.Text));

            var missing = await Assert.ThrowsAsync<MCPHubError>(() => hub.ReadResource("s", "file:///nope.txt"));
            Assert.Equal("Resource not found", missing.ErrorDescription);

            var unknownServer = await Assert.ThrowsAsync<MCPHubError>(() => hub.ReadResource("ghost", "file:///x"));
            Assert.Equal("No MCP server named ghost.", unknownServer.ErrorDescription);
            await Assert.ThrowsAsync<MCPHubError>(() => hub.Resources("ghost"));
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_ui_resource_is_read_with_its_domains_permissions_and_border()
    {
        var hub = await Hub();
        try
        {
            var page = await hub.ReadUiResource("s", "ui://board/view.html").WaitAsync(Bound);
            Assert.Equal("<html>board</html>", page.Html);
            Assert.Equal(["api.example.com"], page.Ui.Csp.ConnectDomains.ToArray());
            Assert.Equal(["cdn.example.com", "*.example.org"], page.Ui.Csp.ResourceDomains.ToArray());
            Assert.Equal(new MCPUiPermissions(Camera: true, Microphone: false, Geolocation: true, ClipboardWrite: false), page.Ui.Permissions);
            Assert.Equal("board.example.com", page.Ui.Domain);
            Assert.True(page.Ui.PrefersBorder);
            Assert.Equal("s", page.Server);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_ui_resource_sent_as_a_blob_is_decoded_and_one_that_is_not_an_app_page_is_refused()
    {
        var hub = await Hub();
        try
        {
            var blob = await hub.ReadUiResource("s", "ui://blob/view.html").WaitAsync(Bound);
            Assert.Equal("<html>blob</html>", blob.Html);
            Assert.Equal(MCPUiMeta.Empty, blob.Ui);

            var wrongMime = await Assert.ThrowsAsync<MCPHubError>(() => hub.ReadUiResource("s", "ui://wrong/mime"));
            Assert.Equal("The server's resource isn't an MCP App page.", wrongMime.ErrorDescription);

            var notUi = await Assert.ThrowsAsync<MCPHubError>(() => hub.ReadUiResource("s", "file:///notes.txt"));
            Assert.Equal("That isn't an app page address.", notUi.ErrorDescription);
        }
        finally
        {
            await hub.Reload();
        }
    }

    // ---- list changes ----

    [Fact]
    public async Task A_tools_list_changed_notification_drops_the_cache_raises_the_event_and_moves_the_revision()
    {
        var hub = await Hub();
        try
        {
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);
            Assert.DoesNotContain(await hub.Tools().WaitAsync(Bound), t => t.Name == "fresh-tool");
            var revision = hub.ToolsRevision;

            await hub.Call("mcp__s__change-tools", new JSONValue.Null()).WaitAsync(Bound);
            var change = await changed.Task.WaitAsync(Bound);

            Assert.Equal(new MCPListChange("s", MCPWire.ListKind.Tools), change);
            Assert.True(hub.ToolsRevision > revision);
            Assert.Contains(await hub.Tools().WaitAsync(Bound), t => t.Name == "fresh-tool");
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_resources_list_changed_notification_drops_the_resource_cache()
    {
        var hub = await Hub();
        try
        {
            var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.ListChanged += change => changed.TrySetResult(change);
            Assert.Equal(2, (await hub.Resources("s").WaitAsync(Bound)).Count);
            var revision = hub.ToolsRevision;

            await hub.Call("mcp__s__change-resources", new JSONValue.Null()).WaitAsync(Bound);
            Assert.Equal(new MCPListChange("s", MCPWire.ListKind.Resources), await changed.Task.WaitAsync(Bound));

            Assert.Equal(3, (await hub.Resources("s").WaitAsync(Bound)).Count);
            Assert.Equal(revision, hub.ToolsRevision); // the tools did not change
        }
        finally
        {
            await hub.Reload();
        }
    }

    // ---- the server's own requests ----

    [Fact]
    public async Task A_ping_from_the_server_is_answered_with_an_empty_result()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__inbound-ping", new JSONValue.Null()).WaitAsync(Bound);
            Assert.StartsWith("answer:", result.Text, StringComparison.Ordinal);
            var answer = Json(result.Text["answer:".Length..]);
            Assert.Equal(900, answer["id"]?.JSONValueInt);
            Assert.Empty(answer["result"]!.JSONValueObject!);
            Assert.Null(answer["error"]);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_request_for_something_nth_never_advertised_is_answered_method_not_found()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__inbound-unknown", new JSONValue.Null()).WaitAsync(Bound);
            var answer = Json(result.Text["answer:".Length..]);
            Assert.Equal(901, answer["id"]?.JSONValueInt);
            Assert.Equal(-32601, answer["error"]?["code"]?.JSONValueInt);
        }
        finally
        {
            await hub.Reload();
        }
    }

    [Fact]
    public async Task A_server_request_that_reuses_our_request_number_is_not_taken_for_the_reply()
    {
        var hub = await Hub();
        try
        {
            var result = await hub.Call("mcp__s__id-collision", new JSONValue.Null()).WaitAsync(Bound);
            Assert.StartsWith("real-reply-after:", result.Text, StringComparison.Ordinal);
            Assert.Contains("\"result\":{}", result.Text, StringComparison.Ordinal);
        }
        finally
        {
            await hub.Reload();
        }
    }
}
