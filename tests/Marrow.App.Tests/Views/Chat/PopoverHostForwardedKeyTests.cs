using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PR-CHAT-110 (and every popover with `.keyboardShortcut(.defaultAction)`): a popover is a key window of its own on the Mac, so Escape closes it and Return presses its default
// button. A popup on Windows is not activated and the keys arrive at the window it hangs over (the native runs TimelineRowsNativePopoverTests prove the real keys); PopoverHost
// hands them on. Headless cannot open a Popup, so the key rule itself is read here on a popup that is registered over a window as an open one.
[Collection("Headless")]
public sealed class PopoverHostForwardedKeyTests(HeadlessSession headless)
{
    private static (AvaloniaWindow Window, Popup Popup, Button Default, Button Other, int[] Clicks, int[] Dismissed) Open(bool defaultEnabled = true)
    {
        var anchor = new Button { Content = "anchor" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var clicks = new int[2];
        var other = new Button { Content = "Cancel" };
        other.Click += (_, _) => clicks[1]++;
        var go = new Button { Content = "Revert", IsDefault = true, IsEnabled = defaultEnabled };
        go.Click += (_, _) => clicks[0]++;
        var popup = new Popup { PlacementTarget = anchor, Child = new PopoverCard { Child = new StackPanel { Children = { other, go } } } };
        var dismissed = new int[1];
        WindowTransients.Show(popup, anchor, () => dismissed[0]++);
        return (window, popup, go, other, clicks, dismissed);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2791 `.keyboardShortcut(.defaultAction)` on Revert
    public void Return_over_the_window_presses_the_popovers_default_button_and_only_it() => headless.OnUiThread(() =>
    {
        var (window, popup, _, _, clicks, dismissed) = Open();
        try
        {
            Assert.True(PopoverHost.ForwardKey(popup, window, Key.Enter, KeyModifiers.None));
            Assert.Equal([1, 0], clicks); // the default button, not Cancel
            Assert.Equal(0, dismissed[0]);
        }
        finally { window.Close(); WindowTransients.Hide(popup); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2777-2798 "Dismissing the popover cancels"
    public void Escape_over_the_window_closes_the_newest_popover() => headless.OnUiThread(() =>
    {
        var (window, popup, _, _, clicks, dismissed) = Open();
        try
        {
            Assert.True(PopoverHost.ForwardKey(popup, window, Key.Escape, KeyModifiers.None));
            Assert.Equal(1, dismissed[0]);
            Assert.Equal([0, 0], clicks);
        }
        finally { window.Close(); WindowTransients.Hide(popup); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void A_chord_a_disabled_default_and_other_keys_are_left_to_the_window() => headless.OnUiThread(() =>
    {
        var (window, popup, _, _, clicks, dismissed) = Open();
        var (disabledWindow, disabled, _, _, disabledClicks, _) = Open(defaultEnabled: false);
        try
        {
            Assert.False(PopoverHost.ForwardKey(popup, window, Key.Enter, KeyModifiers.Control));
            Assert.False(PopoverHost.ForwardKey(popup, window, Key.Escape, KeyModifiers.Shift));
            Assert.False(PopoverHost.ForwardKey(popup, window, Key.A, KeyModifiers.None));
            Assert.False(PopoverHost.ForwardKey(disabled, disabledWindow, Key.Enter, KeyModifiers.None));
            Assert.Equal([0, 0], clicks);
            Assert.Equal([0, 0], disabledClicks);
            Assert.Equal(0, dismissed[0]);
        }
        finally { window.Close(); disabledWindow.Close(); WindowTransients.Hide(popup); WindowTransients.Hide(disabled); }
    });
}
