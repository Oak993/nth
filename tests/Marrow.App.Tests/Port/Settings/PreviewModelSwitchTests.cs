using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-4.5 (the preview model switch and its menu) and CZ-15 (Esc closes the menu only). The menu is a popover with the Mac's tip (OD-0095, Q-CZ-5).
[Collection("Headless")]
public sealed class PreviewModelSwitchTests(HeadlessSession headless)
{
    private static (PreviewSession Session, PreviewModelSwitch Switch, Border Host) Make(ProviderKind provider = ProviderKind.Claude)
    {
        var session = new PreviewSession(provider);
        var control = new PreviewModelSwitch(session) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 0, 24) };
        return (session, control, new Border { Child = control });
    }

    private static void Press(HostWindow window, Key key) => window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T32, CZ-4.5.1
    [Trait("Spec", "CZ-T32")]
    public void The_steppers_are_named_wrap_around_the_list_and_the_chips_name_follows_the_pick() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        PreviewHost.Show(host, (window, _) =>
        {
            Assert.Equal("Previous model", AutomationProperties.GetName(control.Previous));
            Assert.Equal("Next model", AutomationProperties.GetName(control.Next));
            Assert.Equal("Preview model: Claude (claude-fable-5.1)", AutomationProperties.GetName(control.Chip));
            Assert.Equal((22, 22), (control.Previous.Bounds.Width, control.Previous.Bounds.Height));
            Assert.Equal((22, 22), (control.Next.Bounds.Width, control.Next.Bounds.Height));
            Assert.Equal(28, control.Chip.Bounds.Height);
            control.Previous.Activate();
            Assert.Equal("Arcee", session.Maker);
            Assert.Equal("Preview model: Arcee", AutomationProperties.GetName(control.Chip));
            control.Next.Activate();
            Assert.Equal("Claude", session.Maker);
            control.Next.Activate();
            Assert.Equal("OpenAI", session.Maker);
            Assert.Equal("Preview model: OpenAI (gpt-6-astra)", AutomationProperties.GetName(control.Chip));
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T32, CZ-4.5.1, CZ-14.1: the chip's width eases to the new name over 0.3 s while the name crossfades; reduce motion lands at once
    [Trait("Spec", "CZ-T32")]
    public void The_chips_width_eases_to_the_new_name_and_reduce_motion_lands_at_once() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        PreviewHost.Show(host, (window, _) =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
            var from = control.Chip.ValueLabel.Width;
            session.Pick("NousResearch");
            Assert.Equal(from, control.Chip.ValueLabel.Width);
            Assert.Equal(0, control.Chip.ValueLabel.IncomingOpacity);
            runtime.AdvanceMilliseconds(120); frames.Advance(TimeSpan.FromMilliseconds(120));
            Assert.InRange(control.Chip.ValueLabel.Width, from + .01, 1000);
            var middle = control.Chip.ValueLabel.Width;
            Assert.InRange(control.Chip.ValueLabel.IncomingOpacity, .01, 1);
            runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
            Assert.True(control.Chip.ValueLabel.Width >= middle);
            Assert.Equal(1, control.Chip.ValueLabel.IncomingOpacity);
            var settled = control.Chip.ValueLabel.Width;
            EnvironmentValues.SetAccessibilityReduceMotion(control.Chip, true);
            session.Pick("Claude");
            Assert.True(control.Chip.ValueLabel.Width < settled);
            Assert.Equal(1, control.Chip.ValueLabel.IncomingOpacity);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T33, CZ-4.5.2: the clamp of the list's height
    [Trait("Spec", "CZ-T33")]
    public void The_lists_height_is_the_room_above_the_chip_clamped_between_112_and_236()
    {
        Assert.Equal(112, PreviewModelSwitch.ListMaxHeight(chipTop: 10));
        Assert.Equal(112, PreviewModelSwitch.ListMaxHeight(chipTop: 200)); // 200 - 46 - 44 = 110
        Assert.Equal(160, PreviewModelSwitch.ListMaxHeight(chipTop: 250));
        Assert.Equal(236, PreviewModelSwitch.ListMaxHeight(chipTop: 326));
        Assert.Equal(236, PreviewModelSwitch.ListMaxHeight(chipTop: 900));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T33, CZ-4.5.2, OD-0095
    [Trait("Spec", "CZ-T33")]
    public void The_menu_opens_above_the_chip_as_a_popover_with_the_tip_down_and_lists_the_makers() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        PreviewHost.Show(host, (window, overlay) =>
        {
            Assert.False(control.IsOpen);
            control.Chip.Activate();
            Assert.True(control.IsOpen);
            var popup = Assert.Single(overlay.Popups);
            Assert.Equal(PlacementMode.Top, popup.Placement);
            Assert.Same(control.Chip, popup.PlacementTarget);
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Equal(PopoverTipEdge.Bottom, card.TipEdge);
            Assert.Equal(270, control.MenuWidth);
            Assert.Equal("Preview with", control.Heading.Text);
            Assert.Equal(30, control.Items.Count);
            Assert.Equal("Preview model", AutomationProperties.GetName(control.Menu));
            Assert.Equal(AutomationControlType.Menu, ControlAutomationPeer.CreatePeerForElement(control.Menu).GetAutomationControlType());
            var current = Assert.Single(control.Items, i => i.Checked);
            Assert.Equal("Claude", current.Maker.Id);
            Assert.Equal("claude-fable-5.1", current.ModelLabel.Text);
            Assert.True(ControlAutomationPeer.CreatePeerForElement(current) is ISelectionItemProvider { IsSelected: true });
            Assert.Equal(ExpandCollapseState.Expanded, ((IExpandCollapseProvider)ControlAutomationPeer.CreatePeerForElement(control.Chip)).ExpandCollapseState);
            control.CloseMenu();
            Assert.False(control.IsOpen);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T33, CZ-4.5.2: the current item is scrolled to the middle and takes focus
    [Trait("Spec", "CZ-T33")]
    public void Opening_centres_the_current_item_in_the_list_and_focuses_it() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        session.Pick("NousResearch");
        PreviewHost.Show(host, (window, overlay) =>
        {
            control.OpenMenu();
            window.UpdateLayout();
            var current = Assert.Single(control.Items, i => i.Checked);
            Assert.Equal("NousResearch", current.Maker.Id);
            Assert.True(current.IsFocused);
            var viewport = control.List.Viewport.Height;
            Assert.Equal(28, current.Bounds.Height);
            var top = control.Items.ToList().IndexOf(current) * 28.0;
            Assert.Equal(Math.Max(0, top - (viewport - 28) / 2), control.List.Offset.Y, 1);
            Assert.True(control.List.Offset.Y > 0);
            Assert.InRange(control.List.MaxHeight, 112, 236);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T33, CZ-15
    [Trait("Spec", "CZ-T33")]
    public void The_keys_move_pick_and_close_and_Escape_closes_the_menu_only_and_returns_focus_to_the_chip() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        PreviewHost.Show(host, (window, overlay) =>
        {
            var windowEscapes = 0;
            window.KeyDown += (_, e) => { if (e.Key == Key.Escape) windowEscapes++; };
            control.OpenMenu();
            Assert.True(control.Items[0].IsFocused);
            Press(window, Key.Up);
            Assert.True(control.Items[29].IsFocused); // wraps
            Press(window, Key.Down);
            Assert.True(control.Items[0].IsFocused);
            Press(window, Key.End);
            Assert.True(control.Items[29].IsFocused);
            Press(window, Key.Home);
            Press(window, Key.Down);
            Press(window, Key.Enter);
            Assert.Equal("OpenAI", session.Maker);
            Assert.False(control.IsOpen);
            Assert.True(control.Chip.IsFocused);
            control.OpenMenu();
            Press(window, Key.Down);
            Press(window, Key.Space);
            Assert.Equal("Gemini", session.Maker);
            Assert.False(control.IsOpen);
            control.OpenMenu();
            Press(window, Key.Down);
            Press(window, Key.Escape);
            Assert.False(control.IsOpen);
            Assert.True(control.Chip.IsFocused);
            Assert.Equal("Gemini", session.Maker); // Esc picks nothing
            Assert.True(window.IsVisible); // the window stays
            Assert.Equal(0, windowEscapes);   // the window's own Escape was never offered the key
            control.OpenMenu();
            control.LightDismiss(); // a click outside
            Assert.False(control.IsOpen);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T33: a pick by the pointer closes the menu and focuses the chip
    [Trait("Spec", "CZ-T33")]
    public void Picking_an_item_closes_the_menu_and_focuses_the_chip() => headless.OnUiThread(() =>
    {
        var (session, control, host) = Make();
        PreviewHost.Show(host, (window, overlay) =>
        {
            control.OpenMenu();
            control.Items[4].Activate();
            Assert.Equal("Grok", session.Maker);
            Assert.False(control.IsOpen);
            Assert.True(control.Chip.IsFocused);
        });
    });
}
