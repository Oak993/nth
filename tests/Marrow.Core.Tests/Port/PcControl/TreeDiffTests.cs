using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.4: an action's diff by UIA RuntimeId (added, removed, changed, reordered),
// and no diff when it would be longer than the cap (the caller then sends the full tree).
[Trait("Decision", "ED-NEW-pcctl-1")]
public sealed class TreeDiffTests
{
    private static readonly PcRect Box = new(0, 0, 50, 20);

    private static PcUiNode B(string id, string name, PcStates states = PcStates.None) => new(id, "button", name) { Bounds = Box, States = states };

    private static PcTree T(params PcUiNode[] children) => TreeFormatter.Format(new PcUiNode("w", "window", "App") { Bounds = Box, Children = children });

    [Fact]
    public void An_unchanged_tree_says_so()
    {
        Assert.Equal("no visible change since o:3", TreeDiff.Compute(T(B("1", "A"), B("2", "B")), T(B("1", "A"), B("2", "B")), "o:3"));
    }

    [Fact]
    public void Added_removed_and_changed_lines_are_marked_with_new_numbers()
    {
        var before = T(B("1", "A"), B("2", "B"), B("3", "C"));
        var after = T(B("1", "A", PcStates.Focused), B("3", "C"), B("4", "D"));
        Assert.Equal(
            "changes since o:7: 1 added, 1 removed, 1 changed\n"
            + "-   button \"B\"\n"
            + "~ [2]   button \"A\" {focused}\n"
            + "+ [4]   button \"D\"",
            TreeDiff.Compute(before, after, "o:7"));
    }

    [Fact]
    public void A_reordered_element_is_reported_as_moved()
    {
        var before = T(B("1", "A"), B("2", "B"), B("3", "C"));
        var after = T(B("3", "C"), B("1", "A"), B("2", "B"));
        Assert.Equal(
            "changes since o:1: 0 added, 0 removed, 1 changed\n"
            + "~ [2]   button \"C\" (moved)",
            TreeDiff.Compute(before, after, "o:1"));
    }

    [Fact]
    public void An_element_that_moved_to_another_parent_is_reported()
    {
        var group = new PcUiNode("g", "group", "Box") { Bounds = Box, Children = [B("1", "A"), B("9", "Z")] };
        var before = T(group, B("2", "B"));
        var after = T(group with { Children = [B("1", "A"), B("9", "Z"), B("2", "B")] });
        var diff = TreeDiff.Compute(before, after, "o:2");
        Assert.Equal("changes since o:2: 0 added, 0 removed, 1 changed\n~ [5]     button \"B\" (moved)", diff);
    }

    [Fact]
    public void Insertions_do_not_mark_the_unmoved_elements_after_them()
    {
        var before = T(B("1", "A"), B("2", "B"));
        var after = T(B("0", "New"), B("1", "A"), B("2", "B"));
        Assert.Equal("changes since o:4: 1 added, 0 removed, 0 changed\n+ [2]   button \"New\"", TreeDiff.Compute(before, after, "o:4"));
    }

    [Fact]
    public void A_diff_over_the_cap_is_null_so_the_full_tree_is_sent()
    {
        var before = T(Enumerable.Range(0, 70).Select(i => B("a" + i, "A" + i)).ToArray());
        var after = T(Enumerable.Range(0, 70).Select(i => B("b" + i, "B" + i)).ToArray());
        Assert.Null(TreeDiff.Compute(before, after, "o:5"));
        Assert.NotNull(TreeDiff.Compute(before, after, "o:5", maxLines: 200));
    }
}
