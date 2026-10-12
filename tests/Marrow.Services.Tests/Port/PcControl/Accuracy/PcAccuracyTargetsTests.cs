using System.Globalization;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl.Accuracy;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 5.5 accuracy suite; critique C25, C27, C28). Finding
// the right element: look-alike buttons, lists that reorder, recycled containers, counts in names, virtualised lists,
// Arabic, Urdu and Hindi names and values, tiny targets, canvases and windows on other monitors. Each case runs every
// variant of its table row; the target is 100% right or flagged (99.5% for virtualised lists), and no variant may act on
// the wrong element.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcAccuracyTargetsTests
{
    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static async Task<PcControlEngine> Looked(PcEngineHarness harness, string window)
    {
        var engine = harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"" + window + "\",\"screenshot\":\"no\"}");
        Assert.True(look.Ok, look.Render());
        return engine;
    }

    private static PcFakeNode Item(string id, string name, int row) =>
        new(id, "list item", name) { Actions = PcActions.Select, Bounds = new PcRect(115, 155 + (row * 30), 380, 26) };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Look_alike_buttons_are_told_apart_by_their_pane_or_flagged_never_guessed(bool editorFirst)
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "Draft", "writer.exe", 303);
        var pressed = new List<string>();
        Action<PcFakeDesktop, PcFakeNode> Press(string which) => (_, _) =>
        {
            pressed.Add(which);
            app.Title = "Draft - " + N(pressed.Count);
        };
        var editor = new PcFakeNode("1.61.10", "pane", "Editor") { Bounds = new PcRect(110, 150, 500, 300) };
        editor.Add(new PcFakeNode("1.61.11", "button", "Save") { Actions = PcActions.Invoke, Bounds = new PcRect(120, 160, 60, 24), OnInvoke = Press("editor") });
        var comments = new PcFakeNode("1.61.20", "pane", "Comments") { Bounds = new PcRect(620, 150, 500, 300) };
        comments.Add(new PcFakeNode("1.61.21", "button", "Save") { Actions = PcActions.Invoke, Bounds = new PcRect(630, 160, 60, 24), OnInvoke = Press("comments") });
        app.Root.Add(editorFirst ? editor : comments);
        app.Root.Add(editorFirst ? comments : editor);
        app.Root.Add(new PcFakeNode("1.61.30", "button", "Save as") { Actions = PcActions.Invoke, Bounds = new PcRect(120, 470, 80, 24), OnInvoke = Press("save as") });
        var engine = await Looked(harness, "w:3d");
        var both = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"Save\"}}");
        Assert.Equal(PcErrorCode.AmbiguousTarget, both.Error);
        Assert.Contains("[3]", both.Reason, StringComparison.Ordinal);
        Assert.Contains("[5]", both.Reason, StringComparison.Ordinal);
        Assert.Empty(pressed);
        var inComments = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"Save\",\"within\":\"Comments\"}}");
        Assert.True(inComments.Ok, inComments.Render());
        Assert.Equal(new[] { "comments" }, pressed);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"Save as\"}}")).Ok);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"element\":" + N(editorFirst ? 3 : 5) + "}")).Ok);
        Assert.Equal(new[] { "comments", "save as", "editor" }, pressed);
    }

    [Theory]
    [InlineData("Gamma,Alpha,Delta,Beta")]
    [InlineData("Delta,Gamma,Beta,Alpha")]
    [InlineData("Beta,Alpha,Gamma,Delta")]
    [InlineData("Epsilon,Alpha,Gamma,Beta,Delta")]
    public async Task Items_that_reorder_between_the_look_and_the_act_are_found_by_identity_never_by_position(string order)
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "Mail", "mail.exe", 303);
        var list = app.Root.Add(new PcFakeNode("1.61.2", "list", "Messages") { Bounds = new PcRect(110, 150, 400, 400) });
        var items = new Dictionary<string, PcFakeNode>(StringComparer.Ordinal);
        foreach (var (name, row) in new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" }.Select((n, i) => (n, i)))
            items[name] = Item("1.61.2." + N(row + 1), name, row);
        foreach (var name in new[] { "Alpha", "Beta", "Gamma", "Delta" }) list.Add(items[name]);
        // [3] Alpha, [4] Beta, [5] Gamma, [6] Delta.
        var engine = await Looked(harness, "w:3d");
        list.Children.Clear();
        foreach (var (name, row) in order.Split(',').Select((n, i) => (n, i)))
        {
            items[name].Bounds = new PcRect(115, 155 + (row * 30), 380, 26);
            list.Add(items[name]);
        }
        harness.Desktop.Bump(0x3d, structure: true);
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"element\":4}");
        Assert.True(click.Ok, click.Render());
        Assert.Equal(new[] { "Beta" }, items.Values.Where(i => i.States.HasFlag(PcStates.Selected)).Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task A_recycled_container_showing_another_item_is_never_acted_on_as_the_old_one()
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "Contacts", "contacts.exe", 303);
        var list = app.Root.Add(new PcFakeNode("1.61.2", "list", "People") { Bounds = new PcRect(110, 150, 400, 400) });
        var rows = new[] { "Alice", "Bob", "Carol" }.Select((name, row) => list.Add(Item("1.61.2." + N(row + 1), name, row))).ToArray();
        var engine = await Looked(harness, "w:3d");
        // The list scrolled and reused its three containers for other people: same RuntimeIds, new content.
        rows[0].Name = "Dave";
        rows[1].Name = "Erin";
        rows[2].Name = "Frank";
        harness.Desktop.Bump(0x3d, structure: true);
        var gone = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"element\":4}");
        Assert.Equal(PcErrorCode.StaleElement, gone.Error);
        Assert.Contains("\"Bob\"", gone.Reason, StringComparison.Ordinal);
        Assert.Contains("\"Erin\"", gone.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(rows, r => r.States.HasFlag(PcStates.Selected));
        // Bob is back, in another container: he is found there.
        rows[0].Name = "Bob";
        rows[1].Name = "Carol";
        rows[2].Name = "Dave";
        harness.Desktop.Bump(0x3d, structure: true);
        var found = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"element\":4}");
        Assert.True(found.Ok, found.Render());
        Assert.Equal(new[] { "Bob" }, rows.Where(r => r.States.HasFlag(PcStates.Selected)).Select(r => r.Name).ToArray());
    }

    [Theory]
    [InlineData("Inbox (12)", "Inbox (13)", "Inbox (2)")]
    [InlineData("ان باکس (١٢)", "ان باکس (١٣)", "ان باکس (٢)")]
    [InlineData("इनबॉक्स [4 नए]", "इनबॉक्स [5 नए]", "इनबॉक्स [1 नए]")]
    public async Task A_name_whose_count_changed_still_finds_its_one_element_and_two_such_names_are_flagged(string seen, string now, string other)
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "Mail", "mail.exe", 303);
        var folders = app.Root.Add(new PcFakeNode("1.61.2", "tree", "Folders") { Bounds = new PcRect(110, 150, 300, 500) });
        var inbox = folders.Add(new PcFakeNode("1.61.2.1", "tree item", seen) { Actions = PcActions.Select, Bounds = new PcRect(115, 155, 280, 26) });
        var sent = folders.Add(new PcFakeNode("1.61.2.2", "tree item", "Sent") { Actions = PcActions.Select, Bounds = new PcRect(115, 185, 280, 26) });
        var engine = await Looked(harness, "w:3d");
        inbox.Name = now;
        harness.Desktop.Bump(0x3d);
        var byName = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"" + seen + "\"}}");
        Assert.True(byName.Ok, byName.Render());
        Assert.True(inbox.States.HasFlag(PcStates.Selected));
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"element\":4}")).Ok);
        Assert.True(sent.States.HasFlag(PcStates.Selected));
        // A second account's inbox: the old name now fits two folders and is flagged, never guessed.
        folders.Add(new PcFakeNode("1.61.2.3", "tree item", other) { Actions = PcActions.Select, Bounds = new PcRect(115, 215, 280, 26) });
        harness.Desktop.Bump(0x3d, structure: true);
        var two = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"" + seen + "\"}}");
        Assert.Equal(PcErrorCode.AmbiguousTarget, two.Error);
        Assert.True(sent.States.HasFlag(PcStates.Selected));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(37)]
    [InlineData(57)]
    [InlineData(99)]
    public async Task An_item_of_a_virtualised_list_is_realised_by_scrolling_then_acted_on_and_confirmed(int wanted)
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "Library", "library.exe", 303);
        var list = app.Root.Add(new PcFakeNode("1.61.2", "list", "Results") { Actions = PcActions.Scroll, ScrollPercent = 0, Bounds = new PcRect(110, 150, 400, 620) });
        var page = 0;
        void Realise()
        {
            // Only the 20 items in view exist, as in a virtualised list.
            list.Children.Clear();
            for (var row = 0; row < 20; row++)
            {
                var number = (page * 20) + row + 1;
                list.Add(Item("1.61.2." + N(number), "Item " + N(number), row));
            }
            list.ScrollPercent = page * 25.0;
        }
        Realise();
        list.OnScroll = (_, _, command) =>
        {
            if (command.Direction == PcScrollDirection.Down && page < 4) page++;
            else if (command.Direction == PcScrollDirection.Up && page > 0) page--;
            Realise();
        };
        var engine = await Looked(harness, "w:3d");
        var name = "Item " + N(wanted);
        for (var scrolls = 0; scrolls < 5 && !list.Children.Any(c => c.Name == name); scrolls++)
        {
            var scrolled = await PcEngineHarness.Call(engine, "pc_scroll", "{\"window\":\"w:3d\",\"element\":2,\"direction\":\"down\"}");
            Assert.True(scrolled.Ok, scrolled.Render());
        }
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"" + name + "\"},\"expect\":{\"selected\":true}}");
        Assert.True(click.Ok, click.Render());
        Assert.Equal(new[] { name }, list.Children.Where(c => c.States.HasFlag(PcStates.Selected)).Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Arabic_and_urdu_names_match_in_any_normalisation_and_values_round_trip()
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x3d, "ترتیبات", "app.exe", 303);
        var opened = 0;
        app.Root.Add(new PcFakeNode("1.61.2", "button", "إعدادات") { Actions = PcActions.Invoke, Bounds = new PcRect(120, 160, 90, 24), OnInvoke = (_, _) => app.Title = "إعدادات " + N(++opened) });
        var field = app.Root.Add(new PcFakeNode("1.61.3", "edit", "نام") { Actions = PcActions.Value | PcActions.Text, Value = "", Bounds = new PcRect(120, 200, 300, 24) });
        var engine = await Looked(harness, "w:3d");
        // The model writes the hamza below as a separate mark; the label holds the composed letter.
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:3d\",\"target\":{\"name\":\"إعدادات\"}}");
        Assert.True(click.Ok, click.Render());
        Assert.Equal(1, opened);
        const string value = "عمر احمد ۱۲۳ (Lahore)";
        var set = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:3d\",\"target\":{\"name\":\"نام\"},\"value\":\"" + value + "\"}");
        Assert.True(set.Ok, set.Render());
        Assert.Equal(value, field.Value);
        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:3d\",\"element\":3,\"text\":\" ٹیسٹ\"}");
        Assert.True(typed.Ok, typed.Render());
        Assert.Equal(value + " ٹیسٹ", field.Value);
    }

    [Theory]
    [InlineData("क्षत्रिय")]
    [InlineData("क्‍ष")]
    [InlineData("نستعلیق میں لکھا")]
    [InlineData("آرڈر #۴۵ برائے 2026")]
    [InlineData("مبارک 🎉")]
    public async Task Urdu_and_hindi_text_reads_back_through_ui_automation_and_through_a_classic_edit(string text)
    {
        var harness = new PcEngineHarness();
        var notepad = harness.Notepad();
        var classic = harness.Classic();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}")).Ok);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}")).Ok);
        var json = System.Text.Json.JsonSerializer.Serialize(text);
        var set = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":" + json + "}");
        Assert.True(set.Ok, set.Render());
        Assert.Equal(PcVerify.Text(text), PcVerify.Text(notepad.Root.Children[0].Value));
        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:2c\",\"element\":2,\"text\":" + json + "}");
        Assert.True(typed.Ok, typed.Render());
        Assert.Contains("R2", typed.Body, StringComparison.Ordinal);
        Assert.Equal(PcVerify.Text(text), PcVerify.Text(classic.Root.Children[0].Value));
    }

    [Fact]
    public async Task Tiny_targets_in_a_dense_toolbar_get_a_full_resolution_crop_and_the_marks_match_the_numbers()
    {
        var harness = new PcEngineHarness();
        harness.CaptureSource.Frame = PcFakeCapture.Solid(2400, 1350, 90);
        var app = harness.Desktop.AddApp(0x4e, "Studio", "studio.exe", 404, new PcRect(40, 40, 2400, 1350));
        var bar = app.Root.Add(new PcFakeNode("1.78.2", "tool bar", "Tools") { Bounds = new PcRect(50, 50, 600, 16) });
        for (var i = 0; i < 30; i++)
            bar.Add(new PcFakeNode("1.78.2." + N(i + 1), "button", "Tool " + N(i + 1)) { Actions = PcActions.Invoke, Bounds = new PcRect(52 + (i * 14), 52, 12, 12) });
        var engine = harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:4e\",\"screenshot\":\"yes\"}");
        Assert.True(look.Ok, look.Render());
        Assert.NotNull(look.Image);
        Assert.InRange(look.Details.Count, 1, PcControlEngine.MaxDetails);
        Assert.Contains("small on the screenshot: [3] [4]", look.Body, StringComparison.Ordinal);
        var marks = harness.Encoder.Marks.First(m => m.Count > 0);
        Assert.Equal(Enumerable.Range(3, 30).ToArray(), marks.Select(m => m.Number).Order().ToArray());
        foreach (var mark in marks)
        {
            var tool = bar.Children[mark.Number - 3];
            Assert.Equal(tool.Bounds.Offset(-40, -40), mark.Rect);
        }
        // Each detail crop is at native resolution: never scaled down.
        Assert.All(harness.Encoder.Calls.Skip(1), call => Assert.Equal((call.Crop.Width, call.Crop.Height), (call.Width, call.Height)));
    }

    [Fact]
    public async Task A_point_click_on_a_canvas_returns_a_native_crop_and_a_weak_tree_comes_as_native_tiles()
    {
        var harness = new PcEngineHarness();
        harness.CaptureSource.Frame = PcFakeCapture.Solid(2400, 1350, 90);
        var app = harness.Desktop.AddApp(0x4f, "Sketch", "sketch.exe", 404, new PcRect(40, 40, 2400, 1350));
        app.Root.Framework = "Win32";
        var strokes = 0;
        var canvas = app.Root.Add(new PcFakeNode("1.79.2", "pane", "Canvas") { Framework = "Win32", Bounds = new PcRect(40, 120, 2400, 1270) });
        canvas.OnInvoke = (_, node) => node.Add(new PcFakeNode("1.79.2." + N(++strokes), "text", "Stroke " + N(strokes)) { Bounds = new PcRect(600, 600, 40, 40) });
        var engine = harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:4f\"}");
        Assert.True(look.Ok, look.Render());
        Assert.Contains("tile(s) at full resolution", look.Body, StringComparison.Ordinal);
        Assert.InRange(look.Details.Count, 3, PcControlEngine.MaxDetails);
        Assert.All(look.Details, tile => Assert.True((long)tile.Width * tile.Height <= PcGridProfile.Standard.MaxPixels));
        Assert.All(harness.Encoder.Calls.Skip(1), call => Assert.Equal((call.Crop.Width, call.Crop.Height), (call.Width, call.Height)));
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:4f\",\"point\":[700,450],\"obs\":\"" + look.Window!.Obs + "\"}");
        Assert.True(click.Ok, click.Render());
        Assert.Equal(1, strokes);
        Assert.Contains(harness.Desktop.Log, line => line.StartsWith("post_mouse ", StringComparison.Ordinal));
        var crop = Assert.Single(click.Details);
        Assert.Equal((256, 256), (crop.Width, crop.Height));
        Assert.Equal((256, 256), (harness.Encoder.Calls[^1].Crop.Width, harness.Encoder.Calls[^1].Width));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(-1800, 200)]
    [InlineData(2700, -900)]
    public async Task Points_and_zoom_crops_map_back_exactly_on_every_monitor(int x, int y)
    {
        var harness = new PcEngineHarness();
        harness.Desktop.Screens.Add(new PcMonitor(2, new PcRect(-1920, 0, 1920, 1080), new PcRect(-1920, 0, 1920, 1040), 96, false));
        harness.Desktop.Screens.Add(new PcMonitor(3, new PcRect(2560, -1080, 3840, 2160), new PcRect(2560, -1080, 3840, 2120), 216, false));
        var app = harness.Desktop.AddApp(0x1a, "Untitled - notepad", "notepad.exe", 101, new PcRect(x, y, 1200, 800));
        var box = app.Root.Add(new PcFakeNode("1.26.4", "check box", "Word wrap") { Actions = PcActions.Toggle, Bounds = new PcRect(x + 100, y + 10, 120, 30) });
        app.Root.Add(new PcFakeNode("1.26.2", "edit", "Text editor") { Actions = PcActions.Value, Value = "", Bounds = new PcRect(x + 10, y + 60, 1100, 600) });
        var engine = harness.Engine();
        var shot = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.True(shot.Ok, shot.Render());
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"" + shot.Window!.Obs + "\"}");
        Assert.True(click.Ok, click.Render());
        Assert.True(box.States.HasFlag(PcStates.Checked));
        var zoom = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"region\":[100,0,200,100]}");
        Assert.True(zoom.Ok, zoom.Render());
        Assert.Single(zoom.Details);
        Assert.Equal(new PcRect(100, 0, 200, 100), harness.Encoder.Calls[^1].Crop);
    }
}
