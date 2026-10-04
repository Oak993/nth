using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (rec-tlr, second round): the edges of PopoverHost.ForwardKey that PopoverHostForwardedKeyTests does not read.
[Collection("Headless")]
public sealed class PopoverHostReviewTlrForwardedKeyTests(HeadlessSession headless)
{
    private static (Popup Popup, int[] Clicks, int[] Dismissed) Registered(Avalonia.Controls.Control anchor, Control content)
    {
        var clicks = new int[1];
        var dismissed = new int[1];
        foreach (var button in content.GetSelfAndLogicalDescendants().OfType<Button>().Where(b => b.IsDefault)) button.Click += (_, _) => clicks[0]++;
        var popup = new Popup { PlacementTarget = anchor, Child = new PopoverCard { Child = content } };
        WindowTransients.Show(popup, anchor, () => dismissed[0]++);
        return (popup, clicks, dismissed);
    }

    // The newest popover owns Return, whichever popover's handler the window offers the key to; Escape closes one transient per key.
    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void REVIEW_with_two_popovers_stacked_Return_presses_only_the_newest_ones_default_and_Escape_closes_one() => headless.OnUiThread(() =>
    {
        var anchor = new Button { Content = "anchor" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var (older, olderClicks, olderDismissed) = Registered(anchor, new StackPanel { Children = { new Button { Content = "Older", IsDefault = true } } });
        var (newer, newerClicks, newerDismissed) = Registered(anchor, new StackPanel { Children = { new Button { Content = "Newer", IsDefault = true } } });
        try
        {
            Assert.False(PopoverHost.ForwardKey(older, window, Key.Enter, KeyModifiers.None));
            Assert.True(PopoverHost.ForwardKey(newer, window, Key.Enter, KeyModifiers.None));
            Assert.Equal([0, 1], new[] { olderClicks[0], newerClicks[0] });
            Assert.True(PopoverHost.ForwardKey(older, window, Key.Escape, KeyModifiers.None)); // offered by the older popover's handler
            Assert.Equal([0, 1], new[] { olderDismissed[0], newerDismissed[0] }); // the newest closes
        }
        finally { window.Close(); WindowTransients.Hide(older); WindowTransients.Hide(newer); }
    });

    // A default button the popover itself hides (a page of a pager, a collapsed section) is not a button to press: the Button's own IsVisible says true while its parent is collapsed.
    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void REVIEW_Return_does_not_press_a_default_button_inside_a_collapsed_part_of_the_popover() => headless.OnUiThread(() =>
    {
        var anchor = new Button { Content = "anchor" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var hidden = new Border { IsVisible = false, Child = new Button { Content = "Send", IsDefault = true } };
        var (popup, clicks, _) = Registered(anchor, new StackPanel { Children = { hidden, new Button { Content = "Cancel" } } });
        try
        {
            Assert.False(PopoverHost.ForwardKey(popup, window, Key.Enter, KeyModifiers.None));
            Assert.Equal(0, clicks[0]);
        }
        finally { window.Close(); WindowTransients.Hide(popup); }
    });

    // A popup nobody registered as a transient over its window (the badge popovers: BadgePopoverCoordinator presents through the presenter directly) is never the newest, so its default
    // button never gets Return from ForwardKey: the QuestionSheet's Send, the merge popover's Open.
    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void REVIEW_a_popup_that_is_not_a_registered_transient_is_given_no_Return() => headless.OnUiThread(() =>
    {
        var anchor = new Button { Content = "anchor" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var clicks = 0;
        var send = new Button { Content = "Send", IsDefault = true };
        send.Click += (_, _) => clicks++;
        var popup = new Popup { PlacementTarget = anchor, Child = new PopoverCard { Child = send } };
        try
        {
            Assert.False(PopoverHost.ForwardKey(popup, window, Key.Enter, KeyModifiers.None));
            Assert.Equal(0, clicks);
        }
        finally { window.Close(); }
    });
}
