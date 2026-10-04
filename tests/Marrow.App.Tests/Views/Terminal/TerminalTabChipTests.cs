using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), the tab chip :133-186 inside the strip :20-34,
// driven through a real panel in a real (headless) window so the layout values are the ones a user sees.
[Collection("Headless")]
public sealed class TerminalTabChipTests(HeadlessSession headless)
{
    private static TerminalTab[] Tabs(Control host) => LogicalQuery.SelfAndDescendants<TerminalTab>(host).ToArray();

    private static StackPanel Row(TerminalTab tab) => Assert.IsType<StackPanel>(tab.Child);

    private static Button TabButton(TerminalTab tab, string tooltip) =>
        LogicalQuery.SelfAndDescendants<Button>(tab).Single(button => Equals(ToolTip.GetTip(button), tooltip));

    private static Color SolidColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Rect InTab(TerminalTab tab, Control child) =>
        new(child.TranslatePoint(new Point(), tab)!.Value, child.Bounds.Size);

    private static Point CentreIn(Control control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    private static void Click(TerminalPanelRig rig, AvaloniaWindow window, Point point)
    {
        rig.Step(() => window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(point, MouseButton.Left, RawInputModifiers.None));
    }

    // Opens `count` shells in one folder, shows the panel, and returns its parts.
    private static (Border Host, AvaloniaWindow Window, TerminalSession[] Sessions) ShowWithTabs(TerminalPanelRig rig, string label, int count, double width = 600)
    {
        var directory = TerminalPanelRig.Folder(label);
        rig.Step(() =>
        {
            for (var i = 0; i < count; i++) rig.App.Terminals.Open(rig.Thread.Id, directory);
        });
        var (host, _, window) = rig.Show(directory, width, 300);
        return (host, window, rig.App.Terminals.Sessions(rig.Thread.Id).ToArray());
    }

    [Fact]
    [Trait("Req", "UI-TERM-003")] // App/Views/Terminal/TerminalPanel.swift:143-146,172-180
    public void The_chip_lays_out_glyph_title_and_cross_six_points_apart_inside_ten_by_five_padding_four_from_its_neighbour() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, _, sessions) = ShowWithTabs(rig, "chip layout", 2);
        var tabs = Tabs(host);
        Assert.Equal(2, tabs.Length);
        var tab = tabs[0];
        var row = Row(tab);
        var glyph = Assert.IsType<SwiftImage>(row.Children[0]);
        var title = Assert.IsType<TextBlock>(row.Children[1]);
        var close = TabButton(tab, "Close terminal");
        Assert.Same(close, row.Children[2]); // glyph, title, cross: no restart button while the shell runs

        Assert.Equal(10, InTab(tab, glyph).Left, 3); // :173 .padding(.horizontal, 10)
        Assert.Equal(6, InTab(tab, title).Left - InTab(tab, glyph).Right, 3); // :143 HStack(spacing: 6)
        Assert.Equal(6, InTab(tab, close).Left - InTab(tab, title).Right, 3);
        Assert.Equal(10, tab.Bounds.Width - InTab(tab, close).Right, 3); // :173 trailing padding
        Assert.Equal(tab.Bounds.Height - 10, row.Bounds.Height, 3); // :174 .padding(.vertical, 5)
        Assert.Equal(5, row.TranslatePoint(new Point(), tab)!.Value.Y, 3);
        Assert.True(tab.CornerRadius.TopLeft >= tab.Bounds.Height / 2); // :176-177 capsule
        Assert.Equal(4, tabs[1].Bounds.Left - tabs[0].Bounds.Right, 3); // :22 HStack(spacing: 4) between chips

        // :143-146 the title is the session's title, in the callout style (12 points, ED-059).
        Assert.Equal(sessions[0].Title, title.Text);
        Assert.Equal(12, title.FontSize);
        Assert.Equal(1, title.MaxLines);
    });

    [Fact]
    [Trait("Req", "UI-TERM-003")] // App/Views/Terminal/TerminalPanel.swift:175-176,180
    public void The_selected_chip_is_primary_on_an_eight_percent_capsule_the_other_is_secondary_on_clear_and_an_exited_one_dims_to_55_percent() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, _, sessions) = ShowWithTabs(rig, "chip states", 2);
        var tabs = Tabs(host);
        var theme = tabs[0].ActualThemeVariant;
        Assert.Equal(SwiftColor.Primary.Opacity(0.08).Resolve(theme), SolidColor(tabs[1].Background)); // the last opened is selected
        Assert.Equal(SwiftColor.Primary.Resolve(theme), SolidColor(((TextBlock)Row(tabs[1]).Children[1]).Foreground));
        Assert.Equal(Colors.Transparent, SolidColor(tabs[0].Background));
        Assert.Equal(SwiftColor.Secondary.Resolve(theme), SolidColor(((TextBlock)Row(tabs[0]).Children[1]).Foreground));
        Assert.Equal(1, tabs[0].Opacity);
        Assert.DoesNotContain(Row(tabs[0]).Children, child => child is Button button && Equals(ToolTip.GetTip(button), "Start a new shell in this tab"));

        // An exited shell: the whole chip at 0.55, the restart button present (running: absent), even while selected.
        rig.Step(() => sessions[1].Terminate());
        tabs = Tabs(host);
        Assert.Equal(0.55, tabs[1].Opacity);
        Assert.Equal(1, tabs[0].Opacity);
        var restart = TabButton(tabs[1], "Start a new shell in this tab");
        Assert.Contains(restart, Row(tabs[1]).Children);
        Assert.Equal(SwiftColor.Primary.Opacity(0.08).Resolve(theme), SolidColor(tabs[1].Background)); // selection look is kept
    });

    [Fact]
    [Trait("Req", "UI-TERM-003")] // App/Views/Terminal/TerminalPanel.swift:178-179
    public void Clicking_the_chip_selects_it_and_hovering_it_reveals_its_close_cross_without_changing_its_width() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "chip click", 2);
        var first = Tabs(host)[0];
        var close = TabButton(first, "Close terminal");
        var width = first.Bounds.Width;
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal(0, close.Opacity);

        rig.Step(() => window.MouseMove(CentreIn(first, window) + new Vector(0, 0), RawInputModifiers.None));
        Assert.Equal(1, close.Opacity); // hovered: the cross shows
        Assert.Equal(width, first.Bounds.Width, 3);

        // The capsule's own padding is part of the tap target (.contentShape(.capsule)).
        Click(rig, window, first.TranslatePoint(new Point(4, first.Bounds.Height / 2), window)!.Value);
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        var after = Tabs(host);
        Assert.Equal(SwiftColor.Primary.Opacity(0.08).Resolve(after[0].ActualThemeVariant), SolidColor(after[0].Background));
        Assert.Equal(Colors.Transparent, SolidColor(after[1].Background));
    });

    // PR-TERM-024: SwiftUI's .onTapGesture(perform: select) (TerminalPanel.swift:178) completes when the pointer is lifted
    // inside the shape (.contentShape(.capsule), :177). ENGINEERING DECISION (framework semantics, OD-0098: no video of a
    // tab click): a tap is a press and a release that both land in the capsule; the port's own HydraPanel capsule ports a
    // tap the same way. The tests below drive real pointer events through the window.
    [Fact]
    [Trait("Req", "PR-TERM-024")] // App/Views/Terminal/TerminalPanel.swift:177-179
    public void Pressing_a_tab_does_not_select_it_until_the_pointer_is_released_inside_the_capsule() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "tap release", 2);
        var first = Tabs(host)[0];
        var inside = first.TranslatePoint(new Point(4, first.Bounds.Height / 2), window)!.Value; // the padding, not the text
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);

        rig.Step(() => window.MouseDown(inside, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // still the old selection while held
        rig.Step(() => window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // :27 selection[threadID] = session.id
    });

    [Fact]
    [Trait("Req", "PR-TERM-024")] // App/Views/Terminal/TerminalPanel.swift:178: a tap whose pointer left before release is no tap
    public void Releasing_outside_the_pressed_capsule_does_not_select() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "tap drag off", 3);
        var tabs = Tabs(host);
        var pressAt = tabs[0].TranslatePoint(new Point(4, tabs[0].Bounds.Height / 2), window)!.Value;
        var releaseAt = CentreIn(tabs[1], window); // another chip
        Assert.Equal(sessions[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);

        rig.Step(() => window.MouseDown(pressAt, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(releaseAt, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(releaseAt, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(sessions[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // neither chip was tapped
    });

    [Fact]
    [Trait("Req", "PR-TERM-024")] // App/Views/Terminal/TerminalPanel.swift:176-177 .background(in: .capsule), .contentShape(.capsule)
    public void A_press_at_the_bounding_box_corner_outside_the_capsule_does_not_select() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "tap corner", 2);
        var first = Tabs(host)[0];
        // The rectangle's corner pixel lies outside the capsule (corner radius = half the height): it is not hit.
        var corner = first.TranslatePoint(new Point(0.5, first.Bounds.Height - 0.5), window)!.Value; // bottom-left: the top 8 points of the panel belong to the resize strip
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        var hit = window.InputHitTest(corner) as Visual;
        Assert.NotSame(first, hit?.FindAncestorOfType<TerminalTab>(includeSelf: true)); // the corner pixel is not over the chip
        rig.Step(() => window.MouseDown(corner, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);

        // The padding at the vertical middle of the left edge is inside the capsule and selects.
        var edge = first.TranslatePoint(new Point(2, first.Bounds.Height / 2), window)!.Value;
        Click(rig, window, edge);
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
    });

    [Fact]
    [Trait("Req", "UI-TERM-004")] // App/Views/Terminal/TerminalPanel.swift:144-145,175,180
    public void The_leading_glyph_is_the_terminal_symbol_in_caption_inheriting_the_chip_colour_and_dimming_and_clicks_fall_through_to_the_chip() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "chip glyph", 2);
        var tabs = Tabs(host);
        var theme = tabs[0].ActualThemeVariant;
        foreach (var (tab, expected) in new[] { (tabs[0], SwiftColor.Secondary), (tabs[1], SwiftColor.Primary) })
        {
            var glyph = Assert.IsType<SwiftImage>(Row(tab).Children[0]); // leading item
            Assert.Equal("terminal", glyph.SymbolName); // :144
            Assert.Equal(SwiftFont.Caption, SwiftImage.GetFont(glyph)); // :145 .font(.caption)
            Assert.Equal(expected.Resolve(theme), SolidColor(glyph.Foreground)); // inherits the chip's foreground style
            Assert.Equal(10, SwiftFont.Caption.PointSize, 3); // ED-059: .caption is 10 points
        }

        // Exited: the glyph takes the chip's 0.55 and has no dimming of its own.
        rig.Step(() => sessions[0].Terminate());
        tabs = Tabs(host);
        Assert.Equal(0.55, tabs[0].Opacity);
        Assert.Equal(1, Row(tabs[0]).Children[0].Opacity);

        // :178 a click on the glyph selects the chip.
        Click(rig, window, CentreIn(Row(tabs[0]).Children[0], window));
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
    });

    [Fact]
    [Trait("Req", "UI-TERM-005")] // App/Views/Terminal/TerminalPanel.swift:146,172; TerminalStore.swift:93
    public void The_title_label_is_one_line_callout_text_that_inherits_the_chip_colour_and_clicks_fall_through_and_the_shell_never_renames_it() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("chip title");
        rig.Step(() =>
        {
            rig.App.Terminals.Open(rig.Thread.Id, directory, title: "Short");
            rig.App.Terminals.Open(rig.Thread.Id, directory, title: "A title that is far too long to fit in any chip a reader would want to see in a row of tabs without ever wrapping onto a second line");
        });
        var (host, _, window) = rig.Show(directory, 600, 300);
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id);
        var tabs = Tabs(host);
        var shortTitle = Assert.IsType<TextBlock>(Row(tabs[0]).Children[1]);
        var longTitle = Assert.IsType<TextBlock>(Row(tabs[1]).Children[1]);
        Assert.Equal("Short", shortTitle.Text);
        Assert.Equal(1, longTitle.MaxLines); // :146 .lineLimit(1)
        Assert.Equal(TextTrimming.CharacterEllipsis, longTitle.TextTrimming);
        Assert.Equal(12, longTitle.FontSize); // :172 .callout (12 points, ED-059)
        Assert.Equal(shortTitle.Bounds.Height, longTitle.Bounds.Height, 3); // a long title is still one line: never wraps or grows taller
        Assert.Equal(SwiftColor.Secondary.Resolve(tabs[0].ActualThemeVariant), SolidColor(shortTitle.Foreground));
        Assert.Equal(SwiftColor.Primary.Resolve(tabs[1].ActualThemeVariant), SolidColor(longTitle.Foreground));
        // Between the glyph and the buttons, six points from each.
        Assert.Equal(6, InTab(tabs[0], shortTitle).Left - InTab(tabs[0], (Control)Row(tabs[0]).Children[0]).Right, 3);

        // :178 clicking the title selects the chip.
        Click(rig, window, CentreIn(shortTitle, window));
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);

        // TerminalStore.swift:93 setTerminalTitle is an empty body: whatever the shell announces, the title stays.
        var announced = Assert.IsAssignableFrom<Marrow.App.Views.Terminal.Port.ITerminalProcessDelegate>(sessions[0].View.ProcessDelegate);
        rig.Step(() => announced.SetTerminalTitle("renamed by the shell"));
        Assert.Equal("Short", sessions[0].Title);
        Assert.Equal("Short", Assert.IsType<TextBlock>(Row(Tabs(host)[0]).Children[1]).Text);
    });

    [Fact]
    [Trait("Req", "UI-TERM-006")] // App/Views/Terminal/TerminalPanel.swift:148-158; TerminalStore.swift:139-155
    public void The_restart_button_exists_only_for_an_exited_shell_between_title_and_cross_and_its_click_starts_a_fresh_shell_that_does_not_rerun_the_script() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("chip restart");
        TerminalSession script = null!;
        rig.Step(() => script = rig.App.Terminals.Open(rig.Thread.Id, directory, title: "Build", command: "Write-Output ('SCRIPT' + 'RAN')"));
        Assert.True(UiPump.Until(() => script.View.ScrollbackText.Contains("SCRIPTRAN", StringComparison.Ordinal), TimeSpan.FromSeconds(30)),
            "the script tab did not run its command");
        var (host, _, window) = rig.Show(directory, 600, 300);
        var tab = Tabs(host).Single();
        Assert.DoesNotContain(Row(tab).Children, child => child is Button button && Equals(ToolTip.GetTip(button), "Start a new shell in this tab"));

        rig.Step(() => script.Terminate()); // exited
        tab = Tabs(host).Single();
        var row = Row(tab);
        var restart = TabButton(tab, "Start a new shell in this tab");
        var title = (Control)row.Children[1];
        var close = TabButton(tab, "Close terminal");
        Assert.Equal([title, restart, close], [row.Children[1], row.Children[2], row.Children[3]]); // between title and cross
        Assert.Equal(6, InTab(tab, restart).Left - InTab(tab, title).Right, 3);
        Assert.Equal(6, InTab(tab, close).Left - InTab(tab, restart).Right, 3);
        Assert.Equal("Restart Build", AutomationProperties.GetName(restart));
        var icon = Assert.IsType<SwiftImage>(restart.Content);
        Assert.Equal("arrow.clockwise", icon.SymbolName);
        Assert.Equal(SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold), SwiftImage.GetFont(icon));
        Assert.Equal(Chrome.SecondaryText.Resolve(icon.ActualThemeVariant), SolidColor(icon.Foreground));

        Click(rig, window, CentreIn(restart, window));

        // The old session is gone from its place, a fresh one stands there with the same title and directory.
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id);
        var fresh = Assert.Single(sessions);
        Assert.NotSame(script, fresh);
        Assert.False(script.IsRunning);
        Assert.True(fresh.IsRunning);
        Assert.Equal("Build", fresh.Title);
        Assert.Equal(directory, fresh.Directory);
        Assert.Equal(fresh.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.DoesNotContain(Row(Tabs(host).Single()).Children, child => child is Button button && Equals(ToolTip.GetTip(button), "Start a new shell in this tab"));

        // A restarted script tab is a plain shell: the script's command is dropped, so it never ran again. The
        // script would have printed before the shell reached the line typed here.
        rig.Step(() => fresh.View.SendBytes(System.Text.Encoding.UTF8.GetBytes("Write-Output ('ALIVE' + 'MARK')\r")));
        Assert.True(UiPump.Until(() => fresh.View.ScrollbackText.Contains("ALIVEMARK", StringComparison.Ordinal), TimeSpan.FromSeconds(30)),
            "the fresh shell did not answer");
        Assert.DoesNotContain("SCRIPTRAN", fresh.View.ScrollbackText, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "UI-TERM-007")] // App/Views/Terminal/TerminalPanel.swift:159-170; TerminalStore.swift:157-163
    public void The_close_cross_is_the_trailing_item_and_closing_the_selected_tab_selects_the_last_remaining_one_without_asking() => TerminalPanelRig.Run(headless, rig =>
    {
        rig.Step(() => rig.App.Settings.ConfirmBeforeDeleting = true); // not consulted by the cross
        var (host, window, sessions) = ShowWithTabs(rig, "chip close", 3);
        var tabs = Tabs(host);
        // Select the FIRST tab, so the survivors after closing it are the second and third.
        Click(rig, window, tabs[0].TranslatePoint(new Point(4, tabs[0].Bounds.Height / 2), window)!.Value);
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        tabs = Tabs(host);
        var close = TabButton(tabs[0], "Close terminal");
        Assert.Equal("Close " + sessions[0].Title, AutomationProperties.GetName(close));
        var icon = Assert.IsType<SwiftImage>(close.Content);
        Assert.Equal("xmark", icon.SymbolName);
        Assert.Equal(SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold), SwiftImage.GetFont(icon));
        Assert.Equal(Chrome.SecondaryText.Resolve(icon.ActualThemeVariant), SolidColor(icon.Foreground));
        Assert.Same(close, Row(tabs[0]).Children[^1]); // trailing item
        Assert.Equal(6, InTab(tabs[0], close).Left - InTab(tabs[0], (Control)Row(tabs[0]).Children[1]).Right, 3);
        Assert.Equal(1, close.Opacity);

        Click(rig, window, CentreIn(close, window));

        var remaining = rig.App.Terminals.Sessions(rig.Thread.Id);
        Assert.Equal([sessions[1], sessions[2]], remaining);
        Assert.False(sessions[0].IsRunning); // the shell is terminated
        Assert.Equal(sessions[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // the LAST remaining, not the neighbour
        Assert.Equal(2, Tabs(host).Length);

        // Closing a tab that is not selected leaves the selection where it is.
        var other = Tabs(host)[0];
        rig.Step(() => window.MouseMove(CentreIn(other, window), RawInputModifiers.None));
        var otherClose = TabButton(other, "Close terminal");
        Assert.Equal(1, otherClose.Opacity);
        Click(rig, window, CentreIn(otherClose, window));
        Assert.Equal([sessions[2]], rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal(sessions[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.False(sessions[1].IsRunning);
    });

    [Fact]
    [Trait("Req", "UI-TERM-007")] // App/Views/Terminal/TerminalPanel.swift:165,170
    public void A_cross_that_is_hidden_takes_no_click_so_the_tab_is_selected_instead_and_it_keeps_its_slot() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "chip hidden cross", 2);
        var tab = Tabs(host)[0]; // not selected
        var close = TabButton(tab, "Close terminal");
        var width = tab.Bounds.Width;
        Assert.Equal(0, close.Opacity);
        Assert.False(close.IsHitTestVisible);
        Assert.True(close.Bounds.Width > 0 && close.IsVisible); // the slot is kept: opacity, not removal

        // Press and release exactly where the cross is while the chip is not hovered (the same exit event the
        // tab's own pointer tracking reacts to, as PR-TERM-024's test raises it: a real pointer would have
        // entered the chip, and so revealed the cross, before it ever pressed).
        rig.Step(() => window.MouseMove(tab.TranslatePoint(new Point(4, tab.Bounds.Height / 2), window)!.Value, RawInputModifiers.None));
        rig.Step(() => tab.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, tab, new Pointer(1, PointerType.Mouse, true), tab, new Point(), 0, new PointerPointProperties(), KeyModifiers.None)));
        Assert.Equal(0, close.Opacity);
        Assert.False(close.IsHitTestVisible);
        var where = CentreIn(close, window);
        rig.Step(() => window.MouseDown(where, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(where, MouseButton.Left, RawInputModifiers.None));

        Assert.Equal(2, rig.App.Terminals.Sessions(rig.Thread.Id).Count); // not closed
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // selected instead
        Assert.Equal(width, Tabs(host)[0].Bounds.Width, 3);
    });
}
