using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.12; critique C26, C31). Every action is checked on a
// fresh tree before its call returns: typed text reads back (NFC and line endings normalised, Urdu and Hindi too), a toggle
// flipped, a value shows, a scroll moved, a window closed. An Invoke passes only for a change tied to its target (its
// subtree, its parent's state, a new window, a title or focus change); unrelated changes, and changes that happen on their
// own, give unverified; no change at all is a mismatch. The model's expect is checked on the same tree.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcVerifyTests
{
    private static PcUiNode Node(string id, string role, string name, PcActions actions = PcActions.None) =>
        new(id, role, name) { Actions = actions, Bounds = new PcRect(10, 10, 100, 20) };

    private static PcTree Tree(params PcUiNode[] children) =>
        TreeFormatter.Format(new PcUiNode("1", "window", "Form") { Actions = PcActions.Window, Bounds = new PcRect(0, 0, 800, 600), Children = children });

    private static PcUiNode In(PcTree tree, string id) => tree.Elements[tree.IndexOf(id)!.Value];

    private static PcCheck Invoke(PcTree before, PcTree after, string? target, IReadOnlySet<string>? noise = null) => new()
    {
        Command = target is null ? null : new PcElementCommand(PcElementAction.Invoke),
        Kind = PcInputKind.Click,
        Before = before,
        After = after,
        Target = target is null ? null : In(before, target),
        TargetAfter = target is null || after.IndexOf(target) is null ? null : In(after, target),
        TitleBefore = "Form",
        TitleAfter = "Form",
        Noise = noise ?? new HashSet<string>(StringComparer.Ordinal),
    };

    [Theory]
    [InlineData("a\r\nb", "a\nb", false)]
    [InlineData("a\rb", "a\nb", false)]
    [InlineData("\u06C1\u0654\u06CC", "\u06C2\u06CC", false)]
    [InlineData("\u0915\u093C\u093E", "\u0958\u093E", false)]
    [InlineData("caf\u00E9 \u0645\u062D\u0645\u062F", "cafe\u0301 \u0645\u062D\u0645\u062F", false)]
    [InlineData("Hello world", "world", true)]
    public void Typed_text_reads_back_after_normalising_line_endings_and_unicode(string shown, string typed, bool insert)
    {
        var field = Node("1.2", "edit", "Message", PcActions.Value);
        Assert.True(PcVerify.Typed(field, field with { Value = shown }, typed, insert).Passed);
    }

    [Fact]
    public void Typed_text_that_does_not_read_back_is_a_mismatch_saying_what_the_field_shows()
    {
        var field = Node("1.2", "edit", "Message", PcActions.Value);
        var replaced = PcVerify.Typed(field, field with { Value = "HELLO" }, "hello", insert: false);
        Assert.Equal(PcVerdictKind.Mismatch, replaced.Kind);
        Assert.Equal("edit \"Message\" shows \"HELLO\", not \"hello\"", replaced.Saw);
        var inserted = PcVerify.Typed(field, field with { Value = "Hello" }, " world", insert: true);
        Assert.Contains("does not hold", inserted.Saw, StringComparison.Ordinal);
        Assert.Equal("edit \"Message\" is gone", PcVerify.Typed(field, null, "x", false).Saw);
    }

    [Fact]
    public void A_password_field_passes_without_reading_back_and_a_field_that_hides_its_text_is_unverified()
    {
        var password = Node("1.2", "edit", "Password", PcActions.Value) with { States = PcStates.Password };
        Assert.True(PcVerify.Typed(password, password, "secret", false).Passed);
        var canvas = Node("1.3", "document", "Canvas");
        Assert.Equal(PcVerdictKind.Unverified, PcVerify.Typed(canvas, canvas, "text", false).Kind);
    }

    [Fact]
    public void A_toggle_must_flip_and_a_set_value_must_show()
    {
        var box = Node("1.2", "check box", "Word wrap", PcActions.Toggle);
        var toggle = new PcCheck { Command = new PcElementCommand(PcElementAction.Toggle), Target = box, TargetAfter = box with { States = PcStates.Checked } };
        Assert.True(PcVerify.Implicit(toggle).Passed);
        Assert.Equal("check box \"Word wrap\" is still off", PcVerify.Implicit(toggle with { TargetAfter = box }).Saw);
        var slider = Node("1.3", "slider", "Volume", PcActions.RangeValue) with { Value = "10" };
        var set = new PcCheck { Command = new PcElementCommand(PcElementAction.SetValue) { Value = "50" }, Target = slider, TargetAfter = slider with { Value = "50.0" } };
        Assert.True(PcVerify.Implicit(set).Passed);
        Assert.Equal(PcVerdictKind.Mismatch, PcVerify.Implicit(set with { TargetAfter = slider with { Value = "40" } }).Kind);
        Assert.Contains("is gone", PcVerify.Implicit(set with { TargetAfter = null }).Saw, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scroll_must_move_unless_it_was_already_at_its_end()
    {
        var list = Node("1.2", "list", "Messages", PcActions.Scroll) with { ScrollPercent = 30 };
        var down = new PcCheck { Command = new PcElementCommand(PcElementAction.Scroll) { Direction = PcScrollDirection.Down }, Target = list, TargetAfter = list with { ScrollPercent = 45 } };
        Assert.True(PcVerify.Implicit(down).Passed);
        Assert.Contains("did not move (still at 30%)", PcVerify.Implicit(down with { TargetAfter = list }).Saw, StringComparison.Ordinal);
        var bottom = list with { ScrollPercent = 100 };
        Assert.True(PcVerify.Implicit(down with { Target = bottom, TargetAfter = bottom }).Passed);
    }

    [Fact]
    public void A_close_must_close_the_window_and_only_a_click_or_key_may_close_it_otherwise()
    {
        var root = Node("1", "window", "Form", PcActions.Window);
        var close = new PcCheck { Command = new PcElementCommand(PcElementAction.Close), Target = root, WindowClosed = true };
        Assert.True(PcVerify.Implicit(close).Passed);
        Assert.Equal("the window is still open", PcVerify.Implicit(close with { WindowClosed = false }).Saw);
        var ok = Node("1.3", "button", "OK", PcActions.Invoke);
        Assert.True(PcVerify.Implicit(new PcCheck { Command = new PcElementCommand(PcElementAction.Invoke), Target = ok, WindowClosed = true }).Passed);
        var field = Node("1.2", "edit", "Name", PcActions.Value);
        Assert.Equal("the window closed", PcVerify.Implicit(new PcCheck { Command = new PcElementCommand(PcElementAction.SetValue) { Value = "x" }, Target = field, WindowClosed = true }).Saw);
    }

    [Fact]
    public void An_invoke_passes_for_a_change_in_its_own_subtree_or_its_parents_state()
    {
        var before = Tree(Node("1.2", "tree item", "Inbox", PcActions.Invoke | PcActions.ExpandCollapse) with { Children = [Node("1.2.1", "tree item", "Today")] });
        var after = Tree(Node("1.2", "tree item", "Inbox", PcActions.Invoke | PcActions.ExpandCollapse) with { Children = [Node("1.2.1", "tree item", "Today"), Node("1.2.2", "tree item", "Older")] });
        Assert.True(PcVerify.Attributed(Invoke(before, after, "1.2")).Passed);
        var group = Node("1.4", "group", "Options") with { States = PcStates.Collapsed, Children = [Node("1.4.1", "button", "More", PcActions.Invoke)] };
        var opened = group with { States = PcStates.Expanded };
        Assert.True(PcVerify.Attributed(Invoke(Tree(group), Tree(opened), "1.4.1")).Passed);
    }

    [Fact]
    public void An_invoke_with_only_unrelated_changes_is_unverified_and_with_no_change_is_a_mismatch()
    {
        var send = Node("1.2", "button", "Send", PcActions.Invoke);
        var before = Tree(send, Node("1.3", "text", "12:01"));
        var after = Tree(send, Node("1.3", "text", "12:02"));
        var unrelated = PcVerify.Attributed(Invoke(before, after, "1.2"));
        Assert.Equal(PcVerdictKind.Unverified, unrelated.Kind);
        Assert.Equal("only unrelated parts of the window changed", unrelated.Saw);
        // A clock that ticks on its own never makes an Invoke pass, not even a point click with no target.
        var noise = new HashSet<string>(StringComparer.Ordinal) { "1.3" };
        Assert.Equal(PcVerdictKind.Unverified, PcVerify.Attributed(Invoke(before, after, null, noise)).Kind);
        Assert.True(PcVerify.Attributed(Invoke(before, after, null)).Passed);
        var nothing = PcVerify.Attributed(Invoke(before, before, "1.2"));
        Assert.Equal(PcVerdictKind.Mismatch, nothing.Kind);
        Assert.Equal("nothing changed", nothing.Saw);
    }

    [Fact]
    public void A_new_window_a_title_change_or_a_focus_change_counts_for_an_invoke_but_the_users_own_focus_does_not()
    {
        var send = Node("1.2", "button", "Send", PcActions.Invoke);
        var field = Node("1.3", "edit", "To", PcActions.Value);
        var tree = Tree(send, field);
        var check = Invoke(tree, tree, "1.2");
        Assert.True(PcVerify.Attributed(check with { NewWindows = [new PcWindowInfo(0x5e, "Sent", "#32770", 101, "C:\\a\\mail.exe")] }).Passed);
        Assert.True(PcVerify.Attributed(check with { TitleAfter = "Form - sent" }).Passed);
        var focusedBefore = Tree(send, field with { States = PcStates.Focused });
        var focusedSend = Tree(send with { States = PcStates.Focused }, field);
        Assert.True(PcVerify.Attributed(Invoke(focusedBefore, focusedSend, "1.2")).Passed);
        // The user was typing in "To": the focus leaving it is theirs, not the action's.
        var typing = new HashSet<string>(StringComparer.Ordinal) { "1.3" };
        Assert.Equal("nothing changed", PcVerify.Attributed(Invoke(focusedBefore, focusedSend, "1.2", typing)).Saw);
    }

    [Fact]
    public void An_invoke_still_running_or_without_an_earlier_look_is_unverified()
    {
        var send = Node("1.2", "button", "Send", PcActions.Invoke);
        var tree = Tree(send);
        var running = PcVerify.Attributed(Invoke(tree, tree, "1.2") with { StillRunning = true });
        Assert.Equal(PcVerdictKind.Unverified, running.Kind);
        Assert.Contains("has not finished", running.Saw, StringComparison.Ordinal);
        Assert.Equal(PcVerdictKind.Unverified, PcVerify.Attributed(Invoke(tree, tree, "1.2") with { Before = null }).Kind);
    }

    [Fact]
    public void The_models_expect_is_checked_on_the_fresh_tree()
    {
        var field = Node("1.2", "edit", "Amount", PcActions.Value) with { Value = "3.0" };
        var box = Node("1.3", "check box", "Remember", PcActions.Toggle) with { States = PcStates.Checked };
        var tree = Tree(field, box, Node("1.4", "text", "Saved \u0645\u062D\u0641\u0648\u0638"));
        var check = new PcCheck { After = tree, Target = In(tree, "1.2"), TargetAfter = In(tree, "1.2") };
        Assert.True(PcVerify.Expected(new PcExpect { Value = "3" }, check).Passed);
        Assert.True(PcVerify.Expected(new PcExpect { Name = "amount", TextInWindow = "saved", Appears = "\u0645\u062D\u0641\u0648\u0638" }, check).Passed);
        Assert.Equal("edit \"Amount\" shows \"3.0\", not \"4\"", PcVerify.Expected(new PcExpect { Value = "4" }, check).Saw);
        Assert.Equal("\"Saved\" is still in the window", PcVerify.Expected(new PcExpect { Gone = "Saved" }, check).Saw);
        Assert.Equal("no \"Error\" in the window", PcVerify.Expected(new PcExpect { TextInWindow = "Error" }, check).Saw);
        var boxCheck = check with { Target = In(tree, "1.3"), TargetAfter = In(tree, "1.3") };
        Assert.True(PcVerify.Expected(new PcExpect { Checked = true }, boxCheck).Passed);
        Assert.Contains("is still checked", PcVerify.Expected(new PcExpect { Checked = false }, boxCheck).Saw, StringComparison.Ordinal);
        Assert.Contains("does not have the app's focus", PcVerify.Expected(new PcExpect { FocusedInApp = true }, boxCheck).Saw, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_opens_matches_title_and_class_and_a_closed_window_passes_only_gone_or_window_opens()
    {
        var dialog = new PcWindowInfo(0x5e, "Save As", "#32770", 101, "C:\\a\\notepad.exe");
        var check = new PcCheck { After = Tree(Node("1.2", "button", "Save", PcActions.Invoke)), NewWindows = [dialog] };
        Assert.True(PcVerify.Expected(new PcExpect { WindowOpens = new PcWindowMatch("save as", "#32770") }, check).Passed);
        Assert.Equal("the window that opened is \"Save As\"", PcVerify.Expected(new PcExpect { WindowOpens = new PcWindowMatch("Print", null) }, check).Saw);
        Assert.Equal("no window opened", PcVerify.Expected(new PcExpect { WindowOpens = new PcWindowMatch("Save", null) }, check with { NewWindows = [] }).Saw);
        var closed = check with { After = null, WindowClosed = true };
        Assert.True(PcVerify.Expected(new PcExpect { Gone = "Save" }, closed).Passed);
        Assert.True(PcVerify.Expected(new PcExpect { WindowOpens = new PcWindowMatch("Save As", null) }, closed).Passed);
        Assert.Equal("the window closed", PcVerify.Expected(new PcExpect { Value = "x" }, closed).Saw);
        Assert.True(PcVerify.Fits(dialog, new PcWindowMatch(null, "#32770")));
        Assert.False(PcVerify.Fits(dialog, new PcWindowMatch("Save", "Notepad")));
    }
}
