using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.11 (web surfaces W3c; ED-NEW-web-w3c-1): a thread's pane workspace as JSON. The round trip keeps everything the
// solver reads; a field at its default is left out; each pane unit's data is kept by pane id; and what a newer build wrote
// (unknown fields, unknown pane kinds, a higher schema number) survives a trip through this build.
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class StoredWorkspaceTests
{
    private static readonly PaneKind Tasks = PaneKind.BackgroundTasks, Tabula = PaneKind.Artifacts, Visus = PaneKind.Browser;

    private static StoredWorkspace Roundtrip(StoredWorkspace stored)
    {
        var read = PaneWorkspaceJson.Read(PaneWorkspaceJson.Write(stored));
        Assert.NotNull(read.Stored);
        return read.Stored!;
    }

    private static PaneWorkspaceState Rich() => PaneWorkspaceState.Default
        .Open(Visus).Open(Tasks).Open(Tabula).Open(PaneKind.Changes).Open(PaneKind.SideChat).Open(PaneKind.Terminal)
        .JoinAsTab(Tabula, Tasks).WithRatio(PaneGutterKeys.Chat, 0.4).WithRatio(PaneGutterKeys.Cols, 0.5625)
        .Collapse(PaneGutterKeys.Side, 0.3).Expand(Visus).Select(Tasks);

    [Fact]
    public void A_default_workspace_writes_only_its_schema_number()
    {
        Assert.Equal("{\"v\":1}", PaneWorkspaceJson.Write(new StoredWorkspace()));
        Assert.True(new StoredWorkspace().IsEmpty);
        // Opening a pane writes what changed and nothing else.
        var one = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(Visus) };
        Assert.Equal("{\"v\":1,\"order\":[[\"browser\"]],\"recent\":[\"browser\"],\"narrow\":\"browser\"}", PaneWorkspaceJson.Write(one));
        Assert.False(one.IsEmpty);
    }

    [Fact]
    public void A_workspace_with_everything_set_reads_back_equal()
    {
        var state = Rich();
        Assert.Contains(state.Order, slot => slot.IsGroup);
        Assert.True(state.IsSideChatOpen && state.IsTerminalInDrawer && state.Collapsed.Count > 0 && state.Expanded is not null);
        var stored = new StoredWorkspace { State = state }.WithPaneData("browser", "{ \"tabs\": [ {\"url\":\"https://example.test/\"} ] }");
        var back = Roundtrip(stored);
        Assert.Equal(stored.State, back.State);
        Assert.Equal(stored, back);
        // Every part of the layout the solver reads comes back: the same layout at several sizes.
        foreach (var (w, h) in new[] { (600.0, 700.0), (900.0, 700.0), (1400.0, 900.0), (2560.0, 1400.0) })
            Assert.Equal(PaneLayout.Solve(stored.State, w, h), PaneLayout.Solve(back.State, w, h));
    }

    [Fact]
    public void Two_thousand_random_workspaces_round_trip_to_their_normalised_selves()
    {
        var random = new Random(3_011);
        var problems = new List<string>();
        for (var i = 0; i < 2000; i++)
        {
            var state = PaneLayoutChecks.RandomState(random);
            var back = Roundtrip(new StoredWorkspace { State = state }).State;
            if (!back.Equals(state.Normalized())) problems.Add($"case {i}: {PaneWorkspaceJson.Write(new StoredWorkspace { State = state })}");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void The_text_does_not_depend_on_the_order_things_were_added_in()
    {
        var a = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(Visus).WithRatio("cols", 0.4).WithRatio("chat", 0.3).WithRatio("side", 0.2) };
        var b = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(Visus).WithRatio("side", 0.2).WithRatio("chat", 0.3).WithRatio("cols", 0.4) };
        Assert.Equal(PaneWorkspaceJson.Write(a), PaneWorkspaceJson.Write(b));
        Assert.Contains("\"ratios\":{\"chat\":0.3,\"cols\":0.4,\"side\":0.2}", PaneWorkspaceJson.Write(a), StringComparison.Ordinal);
        var withData = new StoredWorkspace().WithPaneData("browser", "{}").WithPaneData("artifact", "{\"v\":2}");
        Assert.Equal("{\"v\":1,\"panes\":{\"artifact\":{\"v\":2},\"browser\":{}}}", PaneWorkspaceJson.Write(withData));
    }

    [Fact]
    public void A_panes_data_is_kept_as_compact_JSON_and_text_that_is_not_JSON_forgets_it()
    {
        var stored = new StoredWorkspace().WithPaneData("browser", "{ \"zoom\" : 1.25 ,\n \"tabs\": [] }");
        // The library's form: no white space, keys sorted (the same text comes back after a trip through library.json).
        Assert.Equal("{\"tabs\":[],\"zoom\":1.25}", stored.PaneData["browser"]);
        Assert.Same(stored, stored.WithPaneData("browser", "{\"tabs\":[],\"zoom\":1.25}"));
        Assert.Same(stored, stored.WithPaneData("browser", "{\"zoom\":1.25,\"tabs\":[]}"));
        Assert.Same(stored, stored.WithPaneData("", "{}"));
        var forgotten = stored.WithPaneData("browser", "{ nope");
        Assert.Empty(forgotten.PaneData);
        Assert.Empty(stored.WithPaneData("browser", null).PaneData);
        Assert.True(forgotten.IsEmpty);
        // A scalar or an array is JSON too: a unit may keep either.
        Assert.Equal("[1,2]", new StoredWorkspace().WithPaneData("artifact", " [ 1, 2 ] ").PaneData["artifact"]);
    }

    [Fact]
    public void A_newer_schema_keeps_its_number_and_the_fields_this_build_does_not_know()
    {
        const string text = "{\"v\":7,\"order\":[[\"tasks\"]],\"future\":{\"a\":[1,2,{\"b\":null}]},\"theme\":\"night\",\"ratios\":{\"chat\":0.4}}";
        var read = PaneWorkspaceJson.Read(text);
        Assert.Equal(WorkspaceReadOutcome.Newer, read.Outcome);
        var stored = read.Stored!;
        Assert.Equal(7, stored.Version);
        Assert.Equal(new[] { Tasks }, stored.State.GridPanes);
        Assert.Equal(0.4, stored.State.Ratios["chat"]);
        Assert.Equal("{\"a\":[1,2,{\"b\":null}]}", stored.UnknownFields["future"]);
        // This build changes the layout and saves: the number and the unknown fields stay.
        var saved = PaneWorkspaceJson.Write(stored.WithState(stored.State.Open(Visus)));
        var again = PaneWorkspaceJson.Read(saved);
        Assert.Equal(WorkspaceReadOutcome.Newer, again.Outcome);
        Assert.Equal(7, again.Stored!.Version);
        Assert.Equal(stored.UnknownFields, again.Stored.UnknownFields);
        Assert.Equal(new[] { Tasks, Visus }, again.Stored.State.GridPanes);
        // A file of this build or older is plain Ok.
        Assert.Equal(WorkspaceReadOutcome.Ok, PaneWorkspaceJson.Read("{\"v\":1}").Outcome);
        Assert.Equal(WorkspaceReadOutcome.Ok, PaneWorkspaceJson.Read("{}").Outcome);
        Assert.Equal(StoredWorkspace.CurrentVersion, PaneWorkspaceJson.Read("{}").Stored!.Version);
        // Unknown fields alone are worth keeping: the workspace is not empty.
        Assert.False(stored.WithState(PaneWorkspaceState.Default).IsEmpty);
    }

    [Fact]
    public void Unknown_pane_kinds_leave_the_view_and_stay_on_disk()
    {
        // "notes" and "calendar" are panes a newer build has: one alone in a slot, one tabbed with Visus, one in the recent list.
        const string text = "{\"v\":2,\"order\":[[\"tasks\"],[\"notes\"],[\"browser\",\"calendar\"]],\"recent\":[\"notes\",\"browser\"],\"expanded\":\"notes\",\"narrow\":\"notes\"}";
        var read = PaneWorkspaceJson.Read(text);
        var stored = read.Stored!;
        Assert.Equal(new[] { Tasks, Visus }, stored.State.GridPanes);
        Assert.Equal(2, stored.State.Order.Count);
        Assert.Equal(new[] { Visus }, stored.State.RecentTabs);
        Assert.Null(stored.State.Expanded);
        Assert.Equal(PaneKind.Chat, stored.State.NarrowSelection);
        Assert.Equal(2, stored.UnknownSlots.Count);
        Assert.Equal(new[] { "notes" }, stored.UnknownRecent);
        // Saved again unchanged, the slot of only "notes" is back at its place and "calendar" is still tabbed with Visus.
        var written = PaneWorkspaceJson.Read(PaneWorkspaceJson.Write(stored)).Stored!;
        Assert.Equal(stored, written);
        var order = System.Text.Json.JsonDocument.Parse(PaneWorkspaceJson.Write(stored)).RootElement.GetProperty("order").GetRawText();
        Assert.Equal("[[\"tasks\"],[\"notes\"],[\"browser\",\"calendar\"]]", order);
        // The user closes Tabula-less Visus: its tabbed unknown pane has no known neighbour left, so it keeps a slot of its own at the end.
        var closed = stored.WithState(stored.State.Close(Visus));
        var after = System.Text.Json.JsonDocument.Parse(PaneWorkspaceJson.Write(closed)).RootElement.GetProperty("order").GetRawText();
        Assert.Equal("[[\"tasks\"],[\"notes\"],[\"calendar\"]]", after);
        // The user opens Tabula: the unknown slot keeps its index and Tabula takes the next place by rank.
        var opened = stored.WithState(stored.State.Open(Tabula));
        Assert.Contains("\"notes\"", PaneWorkspaceJson.Write(opened), StringComparison.Ordinal);
        Assert.Contains("\"calendar\"", PaneWorkspaceJson.Write(opened), StringComparison.Ordinal);
    }

    [Fact]
    public void The_chat_and_side_chat_are_not_panes_of_the_grid_and_repeats_collapse_to_one()
    {
        var stored = PaneWorkspaceJson.Read("{\"order\":[[\"chat\",\"tasks\"],[\"side\"],[\"tasks\",\"browser\"],[],[\"browser\"]]}").Stored!;
        Assert.Equal(new[] { Tasks, Visus }, stored.State.GridPanes);
        Assert.Equal(2, stored.State.Order.Count);
        Assert.Empty(stored.UnknownSlots);
        // A slot may be written as a bare id.
        Assert.Equal(new[] { Tasks, Visus }, PaneWorkspaceJson.Read("{\"order\":[\"tasks\",\"browser\"]}").Stored!.State.GridPanes);
    }

    [Fact]
    public void An_expanded_or_selected_pane_that_is_not_open_gives_way_to_the_chat()
    {
        var stored = PaneWorkspaceJson.Read("{\"order\":[[\"tasks\"]],\"expanded\":\"browser\",\"narrow\":\"artifact\",\"recent\":[\"browser\",\"tasks\",\"tasks\"]}").Stored!;
        Assert.Null(stored.State.Expanded);
        Assert.Equal(PaneKind.Chat, stored.State.NarrowSelection);
        Assert.Equal(new[] { Visus, Tasks }, stored.State.RecentTabs);
        // The terminal both in the drawer and in the grid: the grid wins (the state normalises it).
        var both = PaneWorkspaceJson.Read("{\"order\":[[\"terminal\"]],\"drawer\":true}").Stored!;
        Assert.False(both.State.IsTerminalInDrawer);
        Assert.True(both.State.IsOpen(PaneKind.Terminal));
    }

    [Fact]
    public void A_layout_saved_on_a_larger_monitor_is_kept_whole_and_a_bigger_window_brings_it_back()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(Tabula).Open(Visus).Open(PaneKind.SideChat)
            .WithRatio(PaneGutterKeys.Chat, 0.25).WithRatio(PaneGutterKeys.Side, 0.6).WithRatio(PaneGutterKeys.Cols, 0.7);
        var big = PaneLayout.Solve(state, 2560, 1400);
        var loaded = Roundtrip(new StoredWorkspace { State = state }).State;
        // On the small screen the solver clamps and, narrower than the chat and a pane need, switches to tabs.
        foreach (var width in new[] { 1280.0, 900.0, 700.0, 500.0 })
        {
            var small = PaneLayout.Solve(loaded, width, 760);
            Assert.True(small.Chat.Width >= Math.Min(PaneKinds.ChatMinWidth, width));
            Assert.Empty(PaneLayoutChecks.Problems(loaded, small));
            // Nothing was re-clamped into the state: the saved ratios are the ones the user set.
            Assert.Equal(state.Ratios, loaded.Ratios);
            loaded = Roundtrip(new StoredWorkspace { State = loaded }).State;
        }
        Assert.Equal(big, PaneLayout.Solve(loaded, 2560, 1400));
        // No pane was dropped on the way: the panes that did not fit were tabs or narrow tabs, not removed.
        Assert.Equal(state.GridPanes, loaded.GridPanes);
        Assert.True(loaded.IsSideChatOpen);
    }
}
