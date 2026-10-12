using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3: ui_show, ui_update and html_widget; ED-NEW-web-w24-1, -4). What each tool
// checks, records in the host and tells the model; a view nth could not read at all is an error the model can fix, a view with a few
// unreadable lines is shown and the lines are named. No model provider is called; the arguments are written here.
[Trait("Decision", "ED-NEW-web-w24-1")]
public sealed class LiveAnswerToolsTests
{
    private static readonly Guid Chat = Guid.Parse("5c1e7a30-9d52-4e4b-8f60-3a1c0b9d7e24");

    private const string Hosting =
        "card \"Hosting estimate\" icon=server\n"
        + "  state instances = 3\n"
        + "  stepper instances label=\"Instances\" min=1 max=24\n"
        + "  text \"About three instances.\" id=summary\n"
        + "  table cols=\"Item,Monthly\" id=costs\n"
        + "    row \"Compute\" = fmt(instances * 33.6, \"$0\")\n";

    private static JsonNode Args(object value) => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!;

    private static Task<ArtifactToolResult> Run(LiveAnswerHost host, string tool, object arguments, bool lite = false) =>
        LiveAnswerTools.Run(host, Chat, lite, tool, Args(arguments));

    [Fact]
    public async Task ui_show_records_the_view_names_it_and_tells_the_model_how_to_change_it()
    {
        var host = new LiveAnswerHost();
        var changes = new List<LiveViewChange>();
        using var _ = host.Subscribe(Chat, changes.Add);

        var result = await Run(host, "ui_show", new { ui = Hosting, id = "Hosting Plan" });
        Assert.False(result.Failed, result.Text);
        Assert.StartsWith("Showed the live view \"hosting-plan\" in the chat: 6 components.", result.Text, StringComparison.Ordinal);
        Assert.Contains("ui_update", result.Text, StringComparison.Ordinal);
        Assert.Equal(("hosting-plan", "ui", 1, "Hosting estimate"), (result.Structured!["id"]!.GetValue<string>(), result.Structured["kind"]!.GetValue<string>(), result.Structured["version"]!.GetValue<int>(), result.Structured["title"]!.GetValue<string>()));

        var change = Assert.Single(changes);
        Assert.Equal((LiveChangeKind.Shown, false, "hosting-plan", LiveViewKind.Ui, 1), (change.Kind, change.Replaced, change.View.Id, change.View.Kind, change.View.Version));
        Assert.Equal(Hosting, change.View.Text); // as written: the text of the view is kept whole
        Assert.Equal("Hosting estimate", change.View.Title);

        // The same id again replaces it, at the next version.
        var again = await Run(host, "ui_show", new { ui = "card \"Smaller\"\n  text \"a\"", id = "hosting-plan" });
        Assert.StartsWith("Replaced the live view \"hosting-plan\" (now version 2)", again.Text, StringComparison.Ordinal);
        Assert.True(changes[1].Replaced);
        Assert.Equal(2, (await host.Find(Chat, "HOSTING-PLAN"))!.Version);
    }

    [Fact]
    public async Task ui_show_makes_an_id_when_none_is_given_and_reads_the_view_from_its_other_names_and_through_fences()
    {
        var host = new LiveAnswerHost();
        var result = await Run(host, "ui_show", new { view = "```nth-ui\ntext \"Hello\"\n```" });
        Assert.False(result.Failed, result.Text);
        var id = result.Structured!["id"]!.GetValue<string>();
        Assert.Matches("^view-[0-9a-f]{4}$", id);
        Assert.Equal("text \"Hello\"", (await host.Find(Chat, id))!.Text);
        Assert.False((await Run(host, "ui_show", new { content = "text \"b\"" })).Failed);
        Assert.NotEqual(id, host.Ids(Chat)[^1]);
    }

    [Fact]
    public async Task ui_show_fails_when_nothing_could_be_read_and_names_the_lines_that_could_not_when_some_could()
    {
        var host = new LiveAnswerHost();
        var json = await Run(host, "ui_show", new { ui = "{\"title\": \"not a view\"}" });
        Assert.True(json.Failed);
        Assert.StartsWith("Error: No line of the view could be read as a component, so nothing was shown.", json.Text, StringComparison.Ordinal);
        Assert.Contains("ui_reference", json.Text, StringComparison.Ordinal);
        Assert.Empty(host.Ids(Chat));

        Assert.Contains("ui is required", (await Run(host, "ui_show", new { })).Text, StringComparison.Ordinal);
        Assert.Contains("ui is required", (await Run(host, "ui_show", new { ui = "   \n  " })).Text, StringComparison.Ordinal);
        Assert.Contains("longer than", (await Run(host, "ui_show", new { ui = "text \"" + new string('x', ToolLiveView.MaxTextLength) + "\"" })).Text, StringComparison.Ordinal);

        var mixed = await Run(host, "ui_show", new { ui = "card \"Plan\"\n  gauge cpu = 0.4\n  text \"fine\"\n" });
        Assert.False(mixed.Failed);
        Assert.Contains("Some lines could not be read and are shown as plain text: line 2:", mixed.Text, StringComparison.Ordinal);
        Assert.Contains("gauge", mixed.Text, StringComparison.Ordinal);
        Assert.Single(host.Ids(Chat));
        Assert.Single(mixed.Structured!["unread"]!.AsArray());
    }

    [Fact]
    public async Task A_small_local_model_gets_the_lite_subset_and_no_html_widget()
    {
        var host = new LiveAnswerHost();
        Assert.Equal(["ui_show", "ui_update"], LiveAnswerTools.Offered(lite: true));
        Assert.Equal(["ui_show", "ui_update", "html_widget"], LiveAnswerTools.Offered(lite: false));
        const string view = "card \"Release\"\n  timeline \"Steps\"\n    event \"Code freeze\"\n";
        var result = await Run(host, "ui_show", new { ui = view, id = "rooms" }, lite: true);
        Assert.False(result.Failed);
        Assert.Contains("timeline is not in the lite subset", result.Text, StringComparison.Ordinal);
        Assert.True((await host.Find(Chat, "rooms"))!.Lite);
        var full = await Run(host, "ui_show", new { ui = view, id = "full" });
        Assert.DoesNotContain("not in the lite subset", full.Text, StringComparison.Ordinal);
        Assert.False((await host.Find(Chat, "full"))!.Lite);
    }

    [Fact]
    public async Task ui_update_changes_a_node_sets_a_state_and_appends_and_records_the_next_version()
    {
        var host = new LiveAnswerHost();
        await Run(host, "ui_show", new { ui = Hosting, id = "hosting" });
        var changes = new List<LiveViewChange>();
        using var _ = host.Subscribe(Chat, changes.Add);

        var result = await Run(host, "ui_update", new
        {
            id = "hosting",
            patch = new { state = new { instances = 5 }, nodes = new Dictionary<string, string> { ["summary"] = "text \"Now five.\" id=summary" }, append = new Dictionary<string, string> { ["costs"] = "row \"Backup\" = 4" } },
        });
        Assert.False(result.Failed, result.Text);
        Assert.StartsWith("Updated the live view \"hosting\" (now version 2): set instances; replaced summary; added to costs.", result.Text, StringComparison.Ordinal);
        var change = Assert.Single(changes);
        Assert.Equal((LiveChangeKind.Updated, 2, "{\"instances\":5}"), (change.Kind, change.View.Version, change.StateSets));
        Assert.Contains("Now five.", change.View.Text, StringComparison.Ordinal);
        Assert.Contains("row \"Backup\" = 4", change.View.Text, StringComparison.Ordinal);
        Assert.Equal(change.View, await host.Find(Chat, "hosting"));
    }

    [Fact]
    public async Task ui_update_accepts_a_patch_sent_as_text_and_refuses_what_it_cannot_do_with_a_reason_the_model_can_act_on()
    {
        var host = new LiveAnswerHost();
        await Run(host, "ui_show", new { ui = Hosting, id = "hosting" });
        await Run(host, "html_widget", new { html = "<html><body>x</body></html>", id = "pong" });

        var text = await Run(host, "ui_update", new { id = "hosting", patch = "{\"state\":{\"instances\":7}}" });
        Assert.False(text.Failed, text.Text);

        var unknown = await Run(host, "ui_update", new { id = "nope", patch = new { state = new { a = 1 } } });
        Assert.True(unknown.Failed);
        Assert.Contains("There is no live view \"nope\" in this chat; its views are pong, hosting.", unknown.Text, StringComparison.Ordinal);
        Assert.Contains("id is required", (await Run(host, "ui_update", new { patch = new { } })).Text, StringComparison.Ordinal);
        Assert.Contains("patch is required", (await Run(host, "ui_update", new { id = "hosting" })).Text, StringComparison.Ordinal);
        Assert.Contains("patch is required", (await Run(host, "ui_update", new { id = "hosting", patch = "not json" })).Text, StringComparison.Ordinal);
        Assert.Contains("is an HTML widget, which cannot be patched", (await Run(host, "ui_update", new { id = "pong", patch = new { state = new { } } })).Text, StringComparison.Ordinal);
        var missing = await Run(host, "ui_update", new { id = "hosting", patch = new { nodes = new Dictionary<string, string> { ["zzz"] = "text \"a\"" } } });
        Assert.Contains("No node has id=\"zzz\"; the ids are summary, costs.", missing.Text, StringComparison.Ordinal);
        Assert.Equal(2, (await host.Find(Chat, "hosting"))!.Version); // only the one good update counted
    }

    [Fact]
    public async Task ui_update_says_which_new_lines_could_not_be_read()
    {
        var host = new LiveAnswerHost();
        await Run(host, "ui_show", new { ui = Hosting, id = "hosting" });
        var result = await Run(host, "ui_update", new { id = "hosting", patch = new { nodes = new Dictionary<string, string> { ["summary"] = "gauge x = 1" } } });
        Assert.False(result.Failed);
        Assert.Contains("Some lines could not be read and are shown as plain text: line 4:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task html_widget_records_the_page_with_its_height_and_title_and_warns_about_outside_addresses()
    {
        var host = new LiveAnswerHost();
        const string page = "<!doctype html><html><head><title>Pong &amp; friends</title></head><body><canvas></canvas><script>1</script></body></html>";
        var result = await Run(host, "html_widget", new { html = page, height = 9999 });
        Assert.False(result.Failed, result.Text);
        Assert.Contains("Showed the HTML widget \"pong-friends\" in the chat, 900 pixels high.", result.Text, StringComparison.Ordinal);
        var view = (await host.Find(Chat, "pong-friends"))!;
        Assert.Equal((LiveViewKind.Html, "Pong & friends", 900, page), (view.Kind, view.Title, view.Height, view.Text));
        Assert.DoesNotContain("outside address", result.Text, StringComparison.Ordinal);

        var net = await Run(host, "html_widget", new { html = "<html><script src=\"https://cdn.example/x.js\"></script></html>", title = "Net", height = 10 });
        Assert.Contains("names an outside address", net.Text, StringComparison.Ordinal);
        Assert.Equal(120, (await host.Find(Chat, "net"))!.Height);

        Assert.Contains("html is required", (await Run(host, "html_widget", new { })).Text, StringComparison.Ordinal);
        Assert.Contains("has no tags", (await Run(host, "html_widget", new { html = "just words" })).Text, StringComparison.Ordinal);
        Assert.Contains("longer than", (await Run(host, "html_widget", new { html = "<p>" + new string('x', LiveAnswerTools.MaxHtmlLength) })).Text, StringComparison.Ordinal);
        var again = await Run(host, "html_widget", new { html = "<p>v2</p>", id = "net" });
        Assert.Contains("Replaced the HTML widget \"net\" (now version 2)", again.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_live_answers_turned_off_every_tool_says_so()
    {
        var host = new LiveAnswerHost(enabled: () => false);
        foreach (var tool in LiveAnswerTools.Names)
        {
            var result = await Run(host, tool, new { ui = "text \"a\"", html = "<p>", id = "x", patch = new { } });
            Assert.True(result.Failed);
            Assert.Equal("Error: Live answers are turned off in Settings.", result.Text);
        }
        Assert.Empty(host.Ids(Chat));
    }

    [Fact]
    public async Task A_view_from_an_earlier_session_is_found_through_the_lookup_seam_and_can_be_updated()
    {
        var saved = new LiveViewRecord("hosting", LiveViewKind.Ui, Hosting.TrimEnd('\n'), "Hosting estimate", 3, false, null);
        var asked = new List<(Guid, string)>();
        var host = new LiveAnswerHost(lookup: (thread, id) =>
        {
            asked.Add((thread, id));
            return Task.FromResult<LiveViewRecord?>(id == "hosting" ? saved : null);
        });
        var result = await Run(host, "ui_update", new { id = "hosting", patch = new { state = new { instances = 9 } } });
        Assert.False(result.Failed, result.Text);
        Assert.Equal((Chat, "hosting"), Assert.Single(asked));
        Assert.Equal(4, (await host.Find(Chat, "hosting"))!.Version);
        Assert.Single(asked); // now in the record
        Assert.Null(await host.Find(Chat, "other"));
    }

    [Fact]
    public async Task Changes_reach_only_the_subscribers_of_that_chat_and_stop_when_disposed()
    {
        var host = new LiveAnswerHost();
        var other = Guid.NewGuid();
        var heard = new List<Guid>();
        var subscription = host.Subscribe(Chat, change => heard.Add(change.Thread));
        using var _ = host.Subscribe(other, change => heard.Add(change.Thread));
        await Run(host, "ui_show", new { ui = "text \"a\"", id = "a" });
        await LiveAnswerTools.Run(host, other, false, "ui_show", Args(new { ui = "text \"b\"", id = "b" }));
        Assert.Equal([Chat, other], heard);
        subscription.Dispose();
        await Run(host, "ui_show", new { ui = "text \"c\"", id = "c" });
        Assert.Equal(2, heard.Count);
        host.Forget(Chat);
        Assert.Empty(host.Ids(Chat));
    }

    [Fact]
    public void The_definitions_carry_the_description_and_schema_of_each_tool_and_the_row_is_called_Live_answers()
    {
        Assert.Equal(NthUiTeaching.ShowToolDescription, LiveAnswerTools.Description("ui_show"));
        Assert.Equal(["ui"], LiveAnswerTools.InputSchema("ui_show")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["id", "patch"], LiveAnswerTools.InputSchema("ui_update")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["html"], LiveAnswerTools.InputSchema("html_widget")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        foreach (var name in LiveAnswerTools.Names)
        {
            var definition = LiveAnswerTools.FunctionDefinition(name);
            Assert.Equal(name, definition["function"]!["name"]!.JSONValueString);
            Assert.Equal("function", definition["type"]!.JSONValueString);
        }
        Assert.Equal("Live answers", LiveAnswerTools.RowTitle("ui_show", Args(new { ui = "x" })));
        Assert.Equal("Live answers · Pong", LiveAnswerTools.RowTitle("html_widget", Args(new { html = "x", title = "Pong" })));
        Assert.True(LiveAnswerTools.IsName("ui_update"));
        Assert.False(LiveAnswerTools.IsName("ui_reference"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAnswerTools.Description("nope"));
    }
}
