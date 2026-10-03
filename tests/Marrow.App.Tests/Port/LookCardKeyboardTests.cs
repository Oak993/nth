using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088 (ED-250): the Look card's keyboard, SPEC 7: the tab order, the Accent group's roving focus (selection follows
// focus, every arrow picks), More, segment 4 and the theme menu, Escape and focus that never scrolls the page.
[Collection("Headless")]
public sealed class LookCardKeyboardTests(HeadlessSession headless) : LookCardFixture(headless)
{
    private static IInputElement? Focused(HostWindow w) => w.FocusManager?.GetFocusedElement();

    private static void Tab(HostWindow w) => Press(w, Key.Tab);

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Tab_visits_each_segment_then_the_accent_group_once_at_its_pick_then_more() => Card((card, model, h, w) =>
    {
        Assert.True(card.Segments.Buttons[0].Focus(NavigationMethod.Tab));
        Tab(w);
        Assert.Same(card.Segments.Buttons[1], Focused(w));
        Tab(w);
        Assert.Same(card.Segments.Buttons[2], Focused(w));
        Tab(w);
        Assert.Same(card.Segments.Buttons[3], Focused(w));
        Tab(w);
        Assert.Same(card.Classic.Swatches[0], Focused(w)); // the group's one stop: the picked swatch
        Tab(w);
        Assert.Same(card.More, Focused(w));
        // With Teal picked the stop is Teal; shut over a Signature pick it is the first swatch, and the line is out of reach.
        model.Settings.Accent = "teal";
        Settle(h, w);
        Assert.Equal([8], card.Classic.Swatches.Where(s => s.IsTabStop).Select(s => s.Index));
        model.Settings.Accent = "orchid";
        Settle(h, w);
        Assert.False(card.Open);
        Assert.Equal([0], card.Classic.Swatches.Where(s => s.IsTabStop).Select(s => s.Index));
        Assert.All(card.Signature.Swatches, s => Assert.False(s.Focusable));
        Assert.True(card.Segments.Buttons[3].Focus(NavigationMethod.Tab));
        Tab(w);
        Assert.Same(card.Classic.Swatches[0], Focused(w));
        // The keyboard focus outline (2 px --pac) fades in over .12 s.
        Settle(h, w);
        Assert.Equal(1, card.Classic.Swatches[0].Outline.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_arrows_pick_and_focus_the_next_swatch_wrapping_at_both_ends_and_down_and_up_change_line_at_the_column() => Card((card, model, h, w) =>
    {
        var scrolls = 0;
        w.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => scrolls++, RoutingStrategies.Bubble);
        Assert.True(card.Classic.Swatches[0].Focus(NavigationMethod.Tab));
        void Key(Key key, string id, int index)
        {
            Press(w, key);
            Assert.Equal(id, model.Settings.Accent);
            var swatch = index < 10 ? card.Classic.Swatches[index] : card.Signature.Swatches[index - 10];
            Assert.Same(swatch, Focused(w));
        }
        Key(Avalonia.Input.Key.Right, "blue", 1);
        Key(Avalonia.Input.Key.Left, "windows", 0);
        Assert.False(card.Open);
        Key(Avalonia.Input.Key.Left, "slate", 21); // wraps to the last of all 22, and opens the line
        Assert.True(card.Open);
        Key(Avalonia.Input.Key.Right, "windows", 0); // and wraps back
        Key(Avalonia.Input.Key.End, "slate", 21);
        Key(Avalonia.Input.Key.Home, "windows", 0);
        for (var i = 1; i <= 8; i++) Press(w, Avalonia.Input.Key.Right);
        Assert.Equal("teal", model.Settings.Accent);
        Key(Avalonia.Input.Key.Down, "sage", 18); // Classic 8 -> Signature 8
        Key(Avalonia.Input.Key.Up, "teal", 8); // and back
        Key(Avalonia.Input.Key.Up, "sage", 18); // Up from Classic goes to the other line too
        Key(Avalonia.Input.Key.Right, "alpine", 19);
        Key(Avalonia.Input.Key.Right, "mint", 20);
        Key(Avalonia.Input.Key.Down, "graphite", 9); // Signature 10 -> Classic min(10, 9)
        Key(Avalonia.Input.Key.End, "slate", 21);
        Key(Avalonia.Input.Key.Up, "graphite", 9); // Signature 11 -> Classic 9
        Assert.Equal(0, scrolls); // focus moved without scrolling the page
        Assert.Equal("graphite", h.Defaults.Object("accent"));
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Moving_onto_a_shut_signature_line_opens_it_and_its_ring_fades_in_there_as_the_line_arrives() => Card((card, model, h, w) =>
    {
        model.Settings.Accent = "teal";
        Settle(h, w);
        Assert.True(card.Classic.Swatches[8].Focus(NavigationMethod.Tab));
        Press(w, Key.Down);
        Assert.True(card.Open);
        Assert.Equal("sage", model.Settings.Accent);
        Assert.Equal(8 * 26, card.Signature.Ring.X.Value); // placed, not slid
        Frame(h);
        Assert.InRange(card.Signature.Ring.Fade.Value, 0.05, 0.12); // fading in (scenario 27)
        Assert.InRange(card.Fold.FoldHeight.Value, 0.5, 3); // with the fold
        Settle(h, w);
        Assert.Equal(1, card.Signature.Ring.Fade.Value);
        Assert.Equal(36, card.Fold.FoldHeight.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Space_or_enter_on_more_folds_the_line_and_keys_on_more_are_not_the_groups() => Card((card, model, h, w) =>
    {
        Assert.True(card.More.Focus(NavigationMethod.Tab));
        Press(w, Key.Space);
        Assert.True(card.Open);
        Press(w, Key.Enter);
        Assert.False(card.Open);
        Press(w, Key.Right);
        Assert.Equal("windows", model.Settings.Accent);
        Assert.Same(card.More, Focused(w));
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Segment_keys_pick_and_the_menu_opened_by_keyboard_focuses_the_current_row_moves_clamped_and_picks_or_escapes() => Card((card, model, h, w) =>
    {
        Assert.True(card.Segments.Buttons[1].Focus(NavigationMethod.Tab));
        Press(w, Key.Space);
        Assert.Equal(AppTheme.Light, model.Settings.Theme);
        Assert.True(card.Segments.Buttons[2].Focus(NavigationMethod.Tab));
        Press(w, Key.Enter);
        Assert.Equal(AppTheme.Dark, model.Settings.Theme);
        // Enter on segment 4: the menu, focus on its first row (Dark is not in it).
        Assert.True(card.Segments.Buttons[3].Focus(NavigationMethod.Tab));
        Press(w, Key.Enter);
        Assert.True(card.Menu.IsOpen);
        var rows = card.Menu.Rows;
        Assert.Same(rows[0], Focused(w));
        Press(w, Key.Up); // clamped
        Assert.Same(rows[0], Focused(w));
        Press(w, Key.Down);
        Press(w, Key.Down);
        Assert.Same(rows[2], Focused(w));
        Assert.Equal(AppTheme.Dark, model.Settings.Theme); // moving picks nothing
        Press(w, Key.Enter);
        Assert.Equal(AppTheme.TokyoNight, model.Settings.Theme);
        Assert.False(card.Menu.IsOpen);
        // Reopened: focus lands on the current theme's row; the last row clamps; Escape closes and returns to segment 4.
        Assert.True(card.Segments.Buttons[3].Focus(NavigationMethod.Tab));
        Press(w, Key.Space);
        Assert.Same(rows.Single(r => r.RowTheme == AppTheme.TokyoNight), Focused(w));
        for (var i = 0; i < 30; i++) Press(w, Key.Down);
        Assert.Same(rows[^1], Focused(w));
        Settle(h, w);
        Assert.Equal(card.Menu.List.Extent.Height - card.Menu.List.Viewport.Height, card.Menu.List.Offset.Y, 0.5); // brought into view
        Press(w, Key.Escape);
        Assert.False(card.Menu.IsOpen);
        Assert.Same(card.Segments.Buttons[3], Focused(w));
        Assert.Equal(AppTheme.TokyoNight, model.Settings.Theme);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_menu_opened_by_pointer_keeps_focus_on_the_segment_and_escape_closes_it_all_the_same() => Card((card, _, h, w) =>
    {
        Click(w, card.Segments.Buttons[3]);
        Assert.True(card.Menu.IsOpen);
        Assert.DoesNotContain(card.Menu.Rows, r => r.IsFocused);
        Press(w, Key.Escape);
        Assert.False(card.Menu.IsOpen);
        Assert.Same(card.Segments.Buttons[3], Focused(w));
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void An_arrow_that_focuses_a_swatch_out_of_view_does_not_scroll_the_page() => With(() =>
    {
        var page = new AppearanceSettingsPage();
        var scope = new ThemeVariantScope { Width = 520, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = page };
        return new ScrollViewer { Height = 200, Width = 520, Content = scope, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
    }, (view, model, h, w) =>
    {
        var scroll = (ScrollViewer)view;
        var card = Assert.Single(Nodes<LookCard>(view));
        Settle(h, w);
        Assert.True(card.Classic.Swatches[0].Focus(NavigationMethod.Tab));
        Drain(w);
        var before = scroll.Offset;
        Press(w, Key.Down); // onto the Signature line, below the 200 tall view
        Settle(h, w);
        Assert.Equal("ocean", model.Settings.Accent);
        Assert.Same(card.Signature.Swatches[0], Focused(w));
        Assert.Equal(before, scroll.Offset);
    });
}
