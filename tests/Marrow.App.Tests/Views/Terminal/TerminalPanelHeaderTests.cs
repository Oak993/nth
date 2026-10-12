using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), the header row :19-56 and the appear hook :109.
// Every test attaches a real panel to a real (headless) window. The shell the store opens for a session is a stand-in
// (no process starts), and the rig terminates every session on every path.
[Collection("Headless")]
public sealed class TerminalPanelHeaderTests(HeadlessSession headless)
{
    private static Button ButtonNamed(Control root, string name) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(button => AutomationProperties.GetName(button) == name);

    private static SwiftImage Glyph(Button button) => LogicalQuery.SelfAndDescendants<SwiftImage>(button).Single();

    private static Color SolidColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static string TabTitles(Control root) =>
        string.Join("|", LogicalQuery.SelfAndDescendants<TerminalTab>(root)
            .Select(tab => LogicalQuery.SelfAndDescendants<TextBlock>(tab).First().Text ?? ""));

    private static string Titles(IEnumerable<TerminalSession> sessions) => string.Join("|", sessions.Select(session => session.Title));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    [Trait("Req", "PR-TERM-004")] // App/Views/Terminal/TerminalPanel.swift:109; TerminalStore.swift:131-135
    public void Showing_the_panel_opens_exactly_one_Terminal_1_in_the_given_directory_and_a_thread_that_has_shells_gets_nothing_new() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("appear opens one");
        Assert.Empty(rig.App.Terminals.Sessions(rig.Thread.Id));
        var (host, _, _) = rig.Show(directory);

        var only = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal("Terminal 1", only.Title);
        Assert.Equal(directory, only.Directory);
        Assert.True(only.IsRunning);
        Assert.Equal(only.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal("Terminal 1", TabTitles(host));

        // :109 appears again for a thread that already has a shell (the drawer shown a second time): nothing new.
        rig.Show(TerminalPanelRig.Folder("appear opens none"));
        Assert.Same(only, Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id)));

        // A thread with several shells gets none either, whatever directory the panel was handed.
        TerminalSession second = null!;
        rig.Step(() => second = rig.App.Terminals.Open(rig.Thread.Id, directory));
        var (secondHost, _, _) = rig.Show(TerminalPanelRig.Folder("appear opens none again"));
        Assert.Equal([only, second], rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal("Terminal 1|Terminal 2", TabTitles(secondHost));
    });

    [Fact]
    [Trait("Req", "PR-TERM-007")] // App/Views/Terminal/TerminalPanel.swift:35-43; TerminalStore.swift:118-125
    [Trait("Req", "UI-TERM-008")] // App/Views/Terminal/TerminalPanel.swift:35-43
    [Trait("Req", "UI-TERM-003")] // App/Views/Terminal/TerminalPanel.swift:146; TerminalStore.swift:120-121: names are not unique and never renumbered
    public void The_plus_button_appends_and_selects_Terminal_N_in_the_same_directory_with_the_New_terminal_tooltip_and_name() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("plus button appends");
        var (host, _, _) = rig.Show(directory);
        var first = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        var plus = ButtonNamed(host, "New terminal");
        Assert.Equal("New terminal", ToolTip.GetTip(plus)); // :42 .help("New terminal")
        Assert.Equal("New terminal", AutomationProperties.GetName(plus)); // :43
        Assert.Equal("plus", Glyph(plus).SymbolName); // :38
        Assert.Equal(Chrome.IconFont, SwiftImage.GetFont(Glyph(plus))); // :39

        // The reader had the first tab selected: the new one takes the selection over, it is not just "the last one".
        rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = first.Id));
        Assert.Equal(first.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        rig.Step(() => Click(plus));
        Assert.Equal(rig.App.Terminals.Sessions(rig.Thread.Id)[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = first.Id));
        rig.Step(() => Click(plus));

        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id);
        Assert.Equal(3, sessions.Count);
        Assert.Same(first, sessions[0]);
        Assert.Equal("Terminal 1|Terminal 2|Terminal 3", Titles(sessions)); // count + 1, at the end
        Assert.All(sessions, session => Assert.Equal(directory, session.Directory));
        Assert.All(sessions, session => Assert.True(session.IsRunning));
        Assert.Equal(sessions[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id); // immediately selected
        Assert.Equal("Terminal 1|Terminal 2|Terminal 3", TabTitles(host)); // appended to the end of the strip

        // The number is the existing session count plus one, never renumbered: closing the middle tab leaves
        // two shells, so the next one is "Terminal 3" again, appended after the survivors.
        rig.Step(() => rig.App.Terminals.Close(sessions[1]));
        rig.Step(() => Click(plus));
        var after = rig.App.Terminals.Sessions(rig.Thread.Id);
        Assert.Equal("Terminal 1|Terminal 3|Terminal 3", Titles(after));
        Assert.Equal(after[2].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal("Terminal 1|Terminal 3|Terminal 3", TabTitles(host));
    });

    [Fact]
    [Trait("Req", "PR-TERM-008")] // App/Views/Terminal/TerminalPanel.swift:45-53; Shortcuts.swift:295-298
    [Trait("Req", "UI-TERM-009")] // App/Views/Terminal/TerminalPanel.swift:44-53
    public void The_chevron_hides_the_drawer_without_ending_any_shell_and_its_tooltip_carries_the_live_chord() => TerminalPanelRig.Run(headless, rig =>
    {
        IDisposable? shortcuts = null;
        try
        {
            rig.Step(() => shortcuts = TerminalPanelRig.FreshShortcuts());
            var directory = TerminalPanelRig.Folder("hide keeps shells");
            rig.Step(() =>
            {
                rig.App.Terminals.Open(rig.Thread.Id, directory);
                rig.App.Terminals.Open(rig.Thread.Id, directory);
                rig.Runtime.IsTerminalVisible = true;
            });
            var before = rig.App.Terminals.Sessions(rig.Thread.Id).ToArray();
            var (host, _, _) = rig.Show(directory);
            var hide = ButtonNamed(host, "Hide terminal");

            Assert.Equal("Hide terminal (Ctrl+J)", ToolTip.GetTip(hide)); // :52 default chord, Windows notation (OD-0008 B7)
            Assert.Equal("chevron.down", Glyph(hide).SymbolName); // :48
            Assert.Equal(Chrome.IconFont, SwiftImage.GetFont(Glyph(hide))); // :49

            // :52 the tooltip follows the chord: cleared leaves exactly "Hide terminal", no trailing space.
            rig.Step(() => ShortcutStore.Shared.Current.Set(null, AppShortcut.ToggleTerminal));
            Assert.Equal("Hide terminal", ToolTip.GetTip(hide));
            Assert.Equal("Hide terminal", AutomationProperties.GetName(hide)); // :53 never carries the chord
            rig.Step(() => ShortcutStore.Shared.Current.Reset(AppShortcut.ToggleTerminal));
            Assert.Equal("Hide terminal (Ctrl+J)", ToolTip.GetTip(hide));
            Assert.Equal("Hide terminal", AutomationProperties.GetName(hide));

            rig.Step(() => Click(hide));

            Assert.False(rig.Runtime.IsTerminalVisible); // :46
            // Sessions are NOT terminated: they are the same objects, still running, still in the store.
            Assert.Equal(before, rig.App.Terminals.Sessions(rig.Thread.Id));
            Assert.All(before, session => Assert.True(session.IsRunning));
            // When the drawer is shown again the same shells reappear and no extra one is opened.
            var (again, _, _) = rig.Show(directory);
            Assert.Equal(before, rig.App.Terminals.Sessions(rig.Thread.Id));
            Assert.Equal("Terminal 1|Terminal 2", TabTitles(again));
        }
        finally { rig.Step(() => shortcuts?.Dispose()); }
    });

    [Fact]
    [Trait("Req", "UI-TERM-009")] // App/Views/Terminal/TerminalPanel.swift:44-56
    public void The_hide_button_sits_at_the_trailing_edge_of_the_padded_row_after_the_plus_button() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, panel, _) = rig.Show(TerminalPanelRig.Folder("hide trailing edge"), width: 600, height: 300);
        var hide = ButtonNamed(host, "Hide terminal");
        var plus = ButtonNamed(host, "New terminal");
        var hideBox = hide.TranslatePoint(new Point(), panel)!.Value;
        var plusBox = plus.TranslatePoint(new Point(), panel)!.Value;

        Assert.Equal(600 - 10, hideBox.X + hide.Bounds.Width, 3); // :55 .padding(.horizontal, 10), Spacer(minLength: 0) before it
        Assert.True(plusBox.X + plus.Bounds.Width < hideBox.X); // :44 the plus is the earlier group
        // Both are centred in the 38-point row (:56).
        Assert.InRange(hideBox.Y + hide.Bounds.Height / 2, 18.5, 19.5); // layout rounding of an odd glyph height
        Assert.InRange(plusBox.Y + plus.Bounds.Height / 2, 18.5, 19.5);
    });

    [Fact]
    [Trait("Req", "UI-TERM-008")] // App/Views/Terminal/TerminalPanel.swift:35-43; UI/Common/Components.swift:59-99
    [Trait("Req", "UI-TERM-009")] // App/Views/Terminal/TerminalPanel.swift:44-53; UI/Common/Components.swift:59-99
    public void Both_icon_buttons_use_the_chip_style_with_its_padding_secondary_glyph_and_hover_and_pressed_fills() => TerminalPanelRig.Run(headless, rig =>
    {
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var (host, _, window) = rig.Show(TerminalPanelRig.Folder("chip buttons"));
        foreach (var name in new[] { "New terminal", "Hide terminal" })
        {
            var button = ButtonNamed(host, name);
            var surface = Assert.IsType<Border>(button.Content); // :41, :51 .buttonStyle(.chip)
            Assert.Equal(new Thickness(9, 5), surface.Padding); // Components.swift:73-74
            Assert.True(surface.CornerRadius.TopLeft >= surface.Bounds.Height / 2); // :78 capsule
            Assert.Equal(0, SolidColor(surface.Background).A); // :87 rest: .primary.opacity(0)
            Assert.Equal(SwiftColor.Secondary.Resolve(surface.ActualThemeVariant), SolidColor(surface.GetValue(TextElement.ForegroundProperty))); // :75 .secondary
            var glyph = Glyph(button);
            Assert.Equal(glyph.Bounds.Width + 18, surface.Bounds.Width, 3);
            Assert.Equal(glyph.Bounds.Height + 10, surface.Bounds.Height, 3);

            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            rig.Step(() => window.MouseMove(centre, RawInputModifiers.None));
            rig.Store.Main.Advance(TimeSpan.FromSeconds(0.2));
            rig.Step(() => frames.Advance(TimeSpan.FromSeconds(0.2)));
            Assert.Equal(SwiftColor.Primary.Opacity(0.07).Resolve(surface.ActualThemeVariant), SolidColor(surface.Background)); // :87 hover

            rig.Step(() => window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton));
            Assert.Equal(SwiftColor.Primary.Opacity(0.12).Resolve(surface.ActualThemeVariant), SolidColor(surface.Background)); // :86 pressed
            rig.Step(() => window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None));
            rig.Step(() => window.MouseMove(new Point(window.Bounds.Width - 1, window.Bounds.Height - 1), RawInputModifiers.None));
            rig.Store.Main.Advance(TimeSpan.FromSeconds(0.2));
            rig.Step(() => frames.Advance(TimeSpan.FromSeconds(0.2)));
            Assert.Equal(0, SolidColor(surface.Background).A);
        }
    });

    [Fact]
    [Trait("Req", "UI-TERM-002")] // App/Views/Terminal/TerminalPanel.swift:20-34,55-56
    public void The_tab_strip_scrolls_horizontally_without_a_scrollbar_four_points_between_tabs_and_has_no_background() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("tab strip scroll");
        var wide = new string('W', 30);
        rig.Step(() =>
        {
            for (var i = 0; i < 3; i++) rig.App.Terminals.Open(rig.Thread.Id, directory, title: wide + i);
        });
        var (host, panel, window) = rig.Show(directory, width: 500, height: 300);
        var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(host)
            .Single(viewer => viewer.Content is StackPanel strip && strip.Children.OfType<TerminalTab>().Any());
        var strip = Assert.IsType<StackPanel>(scroll.Content);

        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility); // :21 showsIndicators: false
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility); // :21 .horizontal only
        Assert.Equal(Avalonia.Layout.Orientation.Horizontal, strip.Orientation); // :22
        Assert.Equal(4, strip.Spacing); // :22 HStack(spacing: 4)
        Assert.Null(scroll.Background); // No background of its own.
        Assert.Null(strip.Background);
        var tabs = strip.Children.OfType<TerminalTab>().ToArray();
        Assert.Equal(3, tabs.Length);
        Assert.Equal(4, tabs[1].Bounds.Left - tabs[0].Bounds.Right, 3);
        Assert.Equal(4, tabs[2].Bounds.Left - tabs[1].Bounds.Right, 3);
        Assert.Equal(10, scroll.TranslatePoint(new Point(), panel)!.Value.X, 3); // :55 row padding

        // Overflowing tabs scroll sideways rather than squeezing or wrapping.
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width, $"extent {scroll.Extent.Width} viewport {scroll.Viewport.Width}");
        Assert.Equal(0, scroll.Offset.X);
        var height = panel.Bounds.Height;
        var centre = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        rig.Step(() => window.MouseWheel(centre, new Vector(-3, 0), RawInputModifiers.None));
        rig.Step(() => TerminalPanelRig.Settle(window));
        Assert.True(scroll.Offset.X > 0, "a sideways wheel did not scroll the tabs");
        Assert.Equal(height, panel.Bounds.Height); // and does not resize the drawer
        Assert.Equal(260, rig.App.Settings.TerminalHeight);
    });

    [Fact]
    [Trait("Req", "UI-TERM-002")] // App/Views/Terminal/TerminalPanel.swift:81-83: the tab row is not the resize target
    public void A_sideways_or_upward_drag_across_the_tab_row_never_resizes_the_drawer() => TerminalPanelRig.Run(headless, rig =>
    {
        var (_, panel, window) = rig.Show(TerminalPanelRig.Folder("tab row drag"), width: 700, height: 400);
        var height = panel.Height;
        // Empty header space right of the one tab, below the 8-point top strip.
        Point InPanel(double x, double y) => panel.TranslatePoint(new Point(x, y), window)!.Value;
        rig.Step(() => window.MouseDown(InPanel(300, 24), MouseButton.Left, RawInputModifiers.LeftMouseButton));
        Assert.False(rig.Runtime.IsTerminalResizing);
        rig.Step(() => window.MouseMove(InPanel(180, 24), RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(InPanel(180, 10), RawInputModifiers.LeftMouseButton));
        Assert.False(rig.Runtime.IsTerminalResizing);
        Assert.Equal(height, panel.Height);
        rig.Step(() => window.MouseUp(InPanel(180, 10), MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(height, panel.Height);
        Assert.Equal(260, rig.App.Settings.TerminalHeight); // nothing committed
    });
}
