using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Headless;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Panes;

namespace Marrow.UI.Tests.Panes;

// nth web surfaces, unit W3c (work/design/web-surfaces/DESIGN.md 3.8, 13.3 "narrow tabs"; canvas .ntabs): the narrow window's glass
// segmented strip. Built only when its tabs change, one tab stop, the chosen tab filled and in the primary text colour, the pill the
// width its tabs need or the width it is given, and the tabs scroll when they do not fit.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3c-2")]
public sealed class NarrowTabStripTests(HeadlessSession headless)
{
    private static readonly PaneKind[] Five = [PaneKind.Chat, PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser, PaneKind.SideChat];

    private static string Label(Button tab) => ((TextBlock)((StackPanel)((Border)tab.Content!).Child!).Children[1]).Text!;

    private static IBrush? Fill(Button tab) => ((Border)tab.Content!).Background;

    private static IBrush? Ink(Button tab) => ((TextBlock)((StackPanel)((Border)tab.Content!).Child!).Children[1]).Foreground;

    [Fact]
    public void The_buttons_are_built_when_the_tabs_change_and_only_restyled_when_the_choice_does() => headless.OnUiThread(() =>
    {
        using var rig = new NarrowTabStripRig(800);
        rig.Strip.SetTabs(Five, PaneKind.Chat);
        rig.Layout();
        var buttons = rig.Strip.TabButtons.ToList();
        Assert.Equal(new[] { "Chat", "Tasks", "Tabula", "Visus", "Side chat" }, buttons.Select(Label).ToArray());
        rig.Strip.SetTabs(Five, PaneKind.Browser);
        rig.Layout();
        Assert.True(buttons.SequenceEqual(rig.Strip.TabButtons), "the same buttons");
        Assert.Equal(PaneKind.Browser, rig.Strip.Selected);
        rig.Strip.SetTabs(Five.Take(4).ToArray(), PaneKind.Browser);
        rig.Layout();
        Assert.Equal(4, rig.Strip.TabButtons.Count);
        Assert.False(buttons.Take(4).SequenceEqual(rig.Strip.TabButtons), "new tabs, new buttons");
        rig.Strip.SetTabs(null!, PaneKind.Chat);
        rig.Layout();
        Assert.Empty(rig.Strip.TabButtons);
        Assert.Equal(0, rig.Strip.Pill.Bounds.Width - 2 * NarrowTabStrip.Inset, 3);
    });

    [Fact]
    public void The_chosen_tab_is_the_one_tab_stop_filled_and_in_the_primary_text_colour() => headless.OnUiThread(() =>
    {
        using var rig = new NarrowTabStripRig(800);
        rig.Strip.SetTabs(Five, PaneKind.Artifacts);
        rig.Layout();
        var tabs = rig.Strip.TabButtons;
        Assert.Equal(new[] { false, false, true, false, false }, tabs.Select(tab => tab.IsTabStop).ToArray());
        Assert.True(ColourOf(Fill(tabs[2])).A > 0, "the chosen tab is filled");
        Assert.All(tabs.Where(tab => tab != tabs[2]), tab => Assert.Equal(0, ColourOf(Fill(tab)).A));
        // The first and the last are the same grey; the chosen one is lighter (dark theme) than they are.
        Assert.Equal(ColourOf(Ink(tabs[0])), ColourOf(Ink(tabs[4])));
        Assert.NotEqual(ColourOf(Ink(tabs[0])), ColourOf(Ink(tabs[2])));
        rig.Strip.SetTabs(Five, PaneKind.Chat);
        rig.Layout();
        Assert.Equal(new[] { true, false, false, false, false }, tabs.Select(tab => tab.IsTabStop).ToArray());
        Assert.Equal(ColourOf(Ink(tabs[0])), ColourOf(PaneTokens.Text1.Brush(tabs[0])));
        Assert.Equal(ColourOf(Ink(tabs[1])), ColourOf(PaneTokens.Text2.Brush(tabs[1])));
    });

    private static Color ColourOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : default;

    [Fact]
    public void The_pill_is_as_wide_as_its_tabs_need_or_as_wide_as_it_is_given_and_centred() => headless.OnUiThread(() =>
    {
        using var rig = new NarrowTabStripRig(900);
        rig.Strip.SetTabs(Five, PaneKind.Chat);
        rig.Layout();
        var natural = rig.Strip.NaturalWidth;
        Assert.InRange(natural, 300, 600);
        var pill = rig.Strip.Pill.Bounds;
        Assert.Equal(natural, pill.Width, 1);
        Assert.Equal(NarrowTabStrip.PillHeight, pill.Height, 3);
        Assert.InRange(pill.X + pill.Width / 2, 900 / 2.0 - 1, 900 / 2.0 + 1);
        Assert.InRange(pill.Y + pill.Height / 2, 44 / 2.0 - 1, 44 / 2.0 + 1);
        Assert.Equal(0, rig.Strip.Scroller.Extent.Width - rig.Strip.Scroller.Viewport.Width, 1);

        rig.Strip.Width = 240;
        rig.Layout();
        Assert.Equal(240, rig.Strip.Pill.Bounds.Width, 1);
        Assert.True(rig.Strip.Scroller.Extent.Width > rig.Strip.Scroller.Viewport.Width + 1, "the tabs scroll");
        Assert.Equal(natural, rig.Strip.NaturalWidth, 1); // the natural width does not depend on the room
        Assert.Equal(PaneKind.Chat, rig.Strip.Selected);
    });

    [Fact]
    public void The_arrow_keys_home_and_end_move_the_choice_and_mirror_in_a_right_to_left_window() => headless.OnUiThread(() =>
    {
        foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            using var rig = new NarrowTabStripRig(800, direction: direction);
            rig.Choose();
            rig.Strip.SetTabs(Five, PaneKind.Artifacts);
            rig.Layout();
            rig.Strip.TabButtons[2].Focus();
            rig.Layout();
            var forward = direction == FlowDirection.LeftToRight ? Key.Right : Key.Left;
            var back = direction == FlowDirection.LeftToRight ? Key.Left : Key.Right;
            rig.Key(forward);
            Assert.Equal(new[] { PaneKind.Browser }, rig.Chosen);
            rig.Key(back);
            Assert.Equal(PaneKind.Artifacts, rig.Chosen[^1]);
            rig.Key(Key.Home);
            Assert.Equal(PaneKind.Chat, rig.Chosen[^1]);
            rig.Key(back); // before the first: nothing
            Assert.Equal(PaneKind.Chat, rig.Chosen[^1]);
            var count = rig.Chosen.Count;
            rig.Key(Key.End);
            Assert.Equal(PaneKind.SideChat, rig.Chosen[^1]);
            rig.Key(forward);
            Assert.Equal(count + 1, rig.Chosen.Count);
            Assert.Same(rig.Strip.TabButtons[4], rig.Window.FocusManager!.GetFocusedElement());
        }
    });

    [Fact]
    public void A_click_chooses_the_tab_and_a_tab_that_is_not_in_the_strip_is_not_chosen() => headless.OnUiThread(() =>
    {
        using var rig = new NarrowTabStripRig(800);
        rig.Choose();
        rig.Strip.SetTabs(Five, PaneKind.Chat);
        rig.Layout();
        var tab = rig.Strip.TabButtons[3];
        var at = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        rig.Layout();
        Assert.Equal(new[] { PaneKind.Browser }, rig.Chosen);
    });

    [Fact]
    public void The_glass_follows_the_theme_with_a_rim_and_a_shadow_and_no_second_blur() => headless.OnUiThread(() =>
    {
        using var dark = new NarrowTabStripRig(800, ThemeVariant.Dark);
        using var light = new NarrowTabStripRig(800, ThemeVariant.Light);
        foreach (var rig in new[] { dark, light })
        {
            rig.Strip.SetTabs(Five, PaneKind.Chat);
            rig.Layout();
        }
        Assert.IsType<LinearGradientBrush>(dark.Strip.Pill.Background);
        Assert.IsType<LinearGradientBrush>(dark.Strip.Pill.BorderBrush);
        Assert.NotEqual(dark.Strip.Pill.BoxShadow.ToString(), light.Strip.Pill.BoxShadow.ToString());
        var darkTop = ((LinearGradientBrush)dark.Strip.Pill.Background!).GradientStops[0].Color;
        var lightTop = ((LinearGradientBrush)light.Strip.Pill.Background!).GradientStops[0].Color;
        Assert.True(darkTop.R < lightTop.R, "dark glass is darker than light glass");
        Assert.Equal(PillRadius, dark.Strip.Pill.CornerRadius.TopLeft);
        // The text colour follows the theme as well, and the chosen fill is the strip's own token.
        Assert.NotEqual(ColourOf(Ink(dark.Strip.TabButtons[1])), ColourOf(Ink(light.Strip.TabButtons[1])));
        Assert.Equal(ColourOf(PaneTokens.NarrowTabFill.Brush(dark.Strip)), ColourOf(Fill(dark.Strip.TabButtons[0])));
    });

    private const double PillRadius = NarrowTabStrip.PillHeight / 2;
}
