using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.1 tree line format and 2.4 pruning rules, caps, query and truncation.
[Trait("Decision", "ED-NEW-pcctl-1")]
public sealed class TreeFormatterTests
{
    private static readonly PcRect Box = new(10, 10, 100, 20);

    private static PcUiNode N(string id, string role, string name = "", PcStates states = PcStates.None, PcActions actions = PcActions.None, params PcUiNode[] children) =>
        new(id, role, name) { States = states, Actions = actions, Bounds = Box, Children = children };

    private static PcUiNode Window(params PcUiNode[] children) => N("w", "window", "Untitled - Notepad", PcStates.None, PcActions.Window, children);

    [Fact]
    public void Lines_use_number_indent_role_quoted_name_automation_id_states_value_and_actions()
    {
        var save = N("1", "button", "Save", PcStates.Focused, PcActions.Invoke | PcActions.ExpandCollapse | PcActions.Focus) with { AutomationId = "SaveButton" };
        var field = N("2", "edit", "File name", PcStates.None, PcActions.Value) with { Value = "report.txt" };
        var tree = TreeFormatter.Format(Window(save, field));
        Assert.Equal(
            "[1] window \"Untitled - Notepad\" actions: minimize, maximize, restore, close\n"
            + "[2]   button \"Save\" #SaveButton {focused} actions: expand, collapse\n"
            + "[3]   edit \"File name\" value=\"report.txt\" actions: set_value",
            tree.Render());
        Assert.Equal(2, tree.Focused);
        Assert.Same(field, tree.Elements[3]);
    }

    [Fact]
    public void Off_screen_elements_and_their_children_are_skipped()
    {
        var hidden = N("1", "list item", "Hidden", PcStates.Offscreen, PcActions.None, N("1a", "text", "inside"));
        var tree = TreeFormatter.Format(Window(hidden, N("2", "button", "OK")));
        Assert.DoesNotContain(tree.Lines, l => l.Body.Contains("Hidden", StringComparison.Ordinal) || l.Body.Contains("inside", StringComparison.Ordinal));
        Assert.Equal(2, tree.Lines.Count);
    }

    [Fact]
    public void A_zero_size_element_is_skipped_but_its_children_stay()
    {
        var empty = N("1", "group", "Toolbar") with { Bounds = new PcRect(0, 0, 0, 0), Children = [N("1a", "button", "Bold"), N("1b", "button", "Italic")] };
        var tree = TreeFormatter.Format(Window(empty));
        Assert.Equal(new[] { "window \"Untitled - Notepad\"", "button \"Bold\"", "button \"Italic\"" }, tree.Lines.Select(l => l.Body.Split(" actions:")[0]));
        Assert.Equal(1, tree.Lines[1].Depth);
    }

    [Fact]
    public void A_single_child_wrapper_pane_is_folded_into_its_child()
    {
        var wrapped = N("1", "pane", "", PcStates.None, PcActions.None, N("2", "group", "", PcStates.None, PcActions.None, N("3", "button", "Deep")));
        var tree = TreeFormatter.Format(Window(wrapped));
        Assert.Equal(2, tree.Lines.Count);
        Assert.Equal("button \"Deep\"", tree.Lines[1].Body);
        Assert.Equal(1, tree.Lines[1].Depth);
    }

    [Fact]
    public void A_named_pane_or_one_with_two_children_is_kept()
    {
        var named = N("1", "pane", "Sidebar", PcStates.None, PcActions.None, N("1a", "button", "A"));
        var two = N("2", "pane", "", PcStates.None, PcActions.None, N("2a", "button", "B"), N("2b", "button", "C"));
        var tree = TreeFormatter.Format(Window(named, two));
        Assert.Contains(tree.Lines, l => l.Body == "pane \"Sidebar\"");
        Assert.Contains(tree.Lines, l => l.Body == "pane");
    }

    [Fact]
    public void Unnamed_decorative_images_and_separators_are_dropped_but_named_images_stay()
    {
        var tree = TreeFormatter.Format(Window(N("1", "image"), N("2", "separator"), N("3", "image", "Profile photo"), N("4", "button", "Go")));
        Assert.Equal(new[] { "window \"Untitled - Notepad\"", "image \"Profile photo\"", "button \"Go\"" }, tree.Lines.Select(l => l.Body.Split(" actions:")[0]));
    }

    [Fact]
    public void Names_are_capped_at_80_characters_and_values_at_120_on_one_line()
    {
        var longName = new string('n', 100);
        var longValue = "line one\nline two " + new string('v', 200);
        var tree = TreeFormatter.Format(Window(N("1", "edit", longName, PcStates.None, PcActions.Value) with { Value = longValue }));
        var body = tree.Lines[1].Body;
        Assert.Contains("\"" + new string('n', 80) + "...\"", body, StringComparison.Ordinal);
        Assert.Contains("value=\"line one line two " + new string('v', 120 - 18) + "...\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', body);
    }

    [Fact]
    public void The_cap_never_splits_a_surrogate_pair()
    {
        var name = new string('a', 79) + "\U0001F600" + "tail";
        var clean = TreeFormatter.Clean(name, 80);
        Assert.Equal(new string('a', 79) + "...", clean);
    }

    [Fact]
    public void A_password_field_shows_its_state_and_never_its_value()
    {
        var tree = TreeFormatter.Format(Window(N("1", "edit", "Password", PcStates.Password, PcActions.Value) with { Value = "hunter2" }));
        Assert.Equal("edit \"Password\" {password}", tree.Lines[1].Body);
    }

    [Fact]
    public void Guid_numeric_and_long_automation_ids_are_left_out()
    {
        var tree = TreeFormatter.Format(Window(
            N("1", "button", "A") with { AutomationId = "{3F2504E0-4F89-11D3-9A0C-0305E82C3301}" },
            N("2", "button", "B") with { AutomationId = "1001" },
            N("3", "button", "C") with { AutomationId = new string('x', 41) },
            N("4", "button", "D") with { AutomationId = "has space" },
            N("5", "button", "E") with { AutomationId = "OkButton" }));
        Assert.Equal(new[] { "button \"A\"", "button \"B\"", "button \"C\"", "button \"D\"", "button \"E\" #OkButton" }, tree.Lines.Skip(1).Select(l => l.Body));
    }

    [Fact]
    public void A_list_over_50_items_shows_50_and_says_how_many_there_are()
    {
        var items = Enumerable.Range(1, 80).Select(i => N("i" + i, "list item", "Item " + i)).ToArray();
        var tree = TreeFormatter.Format(Window(N("L", "list", "Results", PcStates.Scrollable, PcActions.Scroll, items)));
        Assert.Equal("list \"Results\" {scrollable} actions: scroll (80 items, 50 shown)", tree.Lines[1].Body);
        Assert.Equal(52, tree.Lines.Count);
    }

    [Fact]
    public void A_list_with_off_screen_items_reports_items_and_shown()
    {
        var items = Enumerable.Range(1, 37).Select(i => N("i" + i, "list item", "Item " + i, i > 8 ? PcStates.Offscreen : PcStates.None)).ToArray();
        var tree = TreeFormatter.Format(Window(N("L", "list", "Results", PcStates.Scrollable, PcActions.None, items)));
        Assert.Equal("list \"Results\" {scrollable} (37 items, 8 shown)", tree.Lines[1].Body);
    }

    [Fact]
    public void Query_keeps_matching_lines_with_their_parents_and_their_numbers()
    {
        var tree = TreeFormatter.Format(Window(
            N("p", "pane", "Left", PcStates.None, PcActions.None, N("a", "button", "Open"), N("b", "button", "Save as")),
            N("q", "pane", "Right", PcStates.None, PcActions.None, N("c", "button", "Close"))));
        Assert.Equal(
            "[1] window \"Untitled - Notepad\" actions: minimize, maximize, restore, close\n"
            + "[2]   pane \"Left\"\n"
            + "[4]     button \"Save as\"",
            tree.Render("save AS"));
        Assert.Equal("no element matches \"zzz\"", tree.Render("zzz"));
    }

    [Fact]
    public void The_line_cap_ends_with_a_tail_that_points_at_query()
    {
        var buttons = Enumerable.Range(1, 30).Select(i => N("b" + i, "button", "B" + i)).ToArray();
        var text = TreeFormatter.Format(Window(buttons)).Render(maxLines: 20);
        var lines = text.Split('\n');
        Assert.Equal(21, lines.Length);
        Assert.Equal("... 11 more lines not shown. Use query to narrow.", lines[^1]);
    }

    [Fact]
    public void Actions_list_only_what_the_element_supports_given_its_state()
    {
        var expanded = N("1", "tree item", "Docs", PcStates.Expanded, PcActions.ExpandCollapse | PcActions.Select);
        var check = N("2", "check box", "Wrap", PcStates.Checked, PcActions.Toggle);
        var selected = N("3", "list item", "One", PcStates.Selected, PcActions.Select);
        var readOnly = N("4", "edit", "Path", PcStates.ReadOnly, PcActions.Value | PcActions.Text) with { Value = "C:\\" };
        Assert.Equal(new[] { "select", "collapse" }, TreeFormatter.ActionNames(expanded));
        Assert.Equal(new[] { "toggle" }, TreeFormatter.ActionNames(check));
        Assert.Empty(TreeFormatter.ActionNames(selected));
        Assert.Equal(new[] { "read" }, TreeFormatter.ActionNames(readOnly));
        Assert.Equal("edit \"Path\" {readonly} value=\"C:\\\\\" actions: read", TreeFormatter.Body(readOnly));
    }

    [Fact]
    public void A_shortcut_the_element_shows_is_written_as_keys()
    {
        var save = N("1", "menu item", "Save", PcStates.None, PcActions.Invoke) with { Shortcut = "Ctrl+S" };
        Assert.Equal("menu item \"Save\" keys=\"Ctrl+S\"", TreeFormatter.Body(save));
    }

    [Fact]
    public void Repeated_or_missing_runtime_ids_still_get_distinct_keys()
    {
        var tree = TreeFormatter.Format(Window(N("", "button", "A"), N("", "button", "B"), N("x", "button", "C"), N("x", "button", "D")));
        Assert.Equal(tree.Lines.Count, tree.Lines.Select(l => l.RuntimeId).Distinct().Count());
    }
}
