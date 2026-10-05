using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-10.1: the Working dots swatches are two radio groups (Classic, Signature) of which at most one holds the pick; the other holds none. CZ-1's group
// required one; a group may now be built with none (ED-NEW-cloud-cz-5): no button is picked, the first is the tab stop, an arrow key picks, and the pick can be taken away again.
[Collection("Headless")]
public sealed class CzRadioGroupNoPickTests(HeadlessSession headless)
{
    private static CzRadioGroup Group(string? selected) => new("Signature dot colours", [("a", "A"), ("b", "B"), ("c", "C")], selected);

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.1: a group without a pick has no button picked and only its first button in the tab order
    public void A_group_built_with_no_pick_has_none_picked_and_its_first_button_is_the_tab_stop() => headless.OnUiThread(() =>
    {
        var group = Group(null);
        Assert.Equal("", group.SelectedId);
        Assert.All(group.Buttons, b => Assert.False(b.Picked));
        Assert.Equal([true, false, false], group.Buttons.Select(b => b.Focusable));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.1: picking in it picks that button and moves the tab stop to it; the arrow keys step from nothing to the first or the last
    public void A_pick_in_it_is_a_pick_and_the_arrows_start_from_the_ends() => headless.OnUiThread(() =>
    {
        var group = Group(null);
        var picked = new List<string>();
        group.SelectionChanged += picked.Add;
        group.Select("b");
        Assert.Equal(["b"], picked);
        Assert.Equal([false, true, false], group.Buttons.Select(b => b.Focusable));
        group.ShowNone();
        Assert.Equal("", group.SelectedId);
        Assert.Equal(["b"], picked); // taking a pick away is not a pick
        var right = new KeyEventArgs { Key = Key.Right, RoutedEvent = InputElement.KeyDownEvent };
        group.RaiseEvent(right);
        Assert.Equal("a", group.SelectedId);
        group.ShowNone();
        group.RaiseEvent(new KeyEventArgs { Key = Key.Left, RoutedEvent = InputElement.KeyDownEvent });
        Assert.Equal("c", group.SelectedId);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-1 stays as it was: a group built with a pick that is not one of its options is still refused
    public void A_pick_that_is_not_an_option_is_still_refused() => headless.OnUiThread(() =>
    {
        Assert.Throws<ArgumentException>(() => Group("z"));
        Assert.Equal("b", Group("b").SelectedId);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.1: the group's automation peer does not claim a required selection while nothing is picked
    public void The_peer_requires_a_selection_only_while_one_is_held() => headless.OnUiThread(() =>
    {
        var peer = new CzRadioGroupPeer(Group(null));
        Assert.False(peer.IsSelectionRequired);
        Assert.Empty(peer.GetSelection());
        Assert.True(new CzRadioGroupPeer(Group("a")).IsSelectionRequired);
    });
}
