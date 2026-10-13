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
    [Trait("Decision", "ED-NEW-web-w9-13")]
    public void Preview_ready_is_absent_until_detection_and_then_is_a_named_open_preview_gesture() => TerminalPanelRig.Run(headless, rig =>
    {
        var previous = Marrow.App.Views.Browser.ProjectPreviewHost.OpenAutomatically;
        Marrow.App.Views.Browser.ProjectPreviewHost.OpenAutomatically = () => false;
        try
        {
            var (host, _, sessions) = ShowWithTabs(rig, "preview ready", 1);
            var tab = Assert.Single(Tabs(host)); var row = Row(tab);
            Assert.Equal(3, row.Children.Count);
            rig.Step(() => sessions[0].ConfigurePreview("http://localhost:5173/"));
            var ready = row.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Preview ready · localhost:5173");
            Assert.Equal(4, row.Children.Count); Assert.Same(ready, row.Children[2]);
            Assert.True(ready.IsVisible);
            Assert.Equal("Preview ready · localhost:5173", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(ready)).Text);
            Assert.Equal("Preview ready · localhost:5173", AutomationProperties.GetName(ready));
            Assert.Equal("Open preview", ToolTip.GetTip(ready));
            var opens = 0; sessions[0].OpenPreview = () => opens++;
            rig.Step(() => ready.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            Assert.Equal(1, opens);
            Assert.Same(TabButton(tab, "Close terminal"), row.Children[3]);
        }
        finally { Marrow.App.Views.Browser.ProjectPreviewHost.OpenAutomatically = previous; }
    });
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

    // ---- Rendered chips: TERM-CHIP, the one capture behind PR-TERM-021/022/023/025 ----------------------------------------
    // SOURCE FACT: reference video 2-chat-use.mov, static.terminal-panel, C 132.600-134.300, the dark (Monokai) drawer's selected
    // chip, extracted frame work/reference-video/frames/w-2-chat-use-132.0/termL_132.15-134.95/000040.png (640 x 300, 1 px = 1 pt).
    // Measured on that frame: panel luminance 19, capsule fill luminance 36, chip 113 px wide (x 22..134) and 27 px tall, title ink
    // 56 px wide with peak 222, close cross ink 8 px wide with peak 166, glyph ink 9 px wide. The face and the glyph art differ
    // by decision (OD-0009 Inter for SF, OD-0010 Lucide for SF Symbols) and the glyph peaks differ; every other number must
    // agree. The chip is 2 px shorter here and the CAUSE IS UNRESOLVED: Inter's callout line at 15 px against SF's 16 would
    // give a 26 px chip against the frame's 27, so it does not account for the gap. Size parity stays UNVERIFIED (OD-0001).
    private static Func<int, int, int> Luminance(TopLevel window)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var stride = buffer.RowBytes;
        return (x, y) => (bytes[y * stride + x * 4] + bytes[y * stride + x * 4 + 1] + bytes[y * stride + x * 4 + 2]) / 3;
    }

    // The ink of a child: the pixels in its box whose luminance differs from the capsule behind it by more than 8, its width and
    // its strongest pixel (the brightest on a dark ground, the darkest on a light one).
    private static (int Width, int Peak) Ink(Func<int, int, int> lum, Control child, AvaloniaWindow window, int background)
    {
        var box = new Rect(child.TranslatePoint(new Point(), window)!.Value, child.Bounds.Size);
        int minX = int.MaxValue, maxX = int.MinValue, peak = background;
        for (var y = (int)Math.Floor(box.Top); y < (int)Math.Ceiling(box.Bottom); y++)
        for (var x = (int)Math.Floor(box.Left); x < (int)Math.Ceiling(box.Right); x++)
        {
            var value = lum(x, y);
            if (Math.Abs(value - background) <= 8) continue;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            if (Math.Abs(value - background) > Math.Abs(peak - background)) peak = value; // the strongest ink, lighter or darker than its ground
        }
        return maxX < minX ? (0, 0) : (maxX - minX + 1, peak);
    }

    private static int Over(Color colour, int below) => (int)Math.Round(below + (((colour.R + colour.G + colour.B) / 3.0) - below) * colour.A / 255.0);

    [Fact]
    [Trait("Req", "PR-TERM-021")] // TerminalPanel.swift:143-147,172-177; capture TERM-CHIP, frame 000040, OD-0009 face difference recorded above
    [Trait("Req", "PR-TERM-025")] // TerminalPanel.swift:175-176 selected chip, dark; the unselected chip and the light theme: OD-0098 below
    [Trait("Req", "PR-TERM-023")] // TerminalPanel.swift:159-165 the cross of the selected chip: size and colour
    [Trait("Rule", "R-TEST-4")] // the rendered half of those records; their structure is asserted by the tests around it
    public void The_selected_chip_in_the_dark_drawer_renders_like_the_reference_frame() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("chip capture");
        rig.Step(() => rig.App.Terminals.Open(rig.Thread.Id, directory, title: "Terminal 1"));
        var (host, _, window) = rig.Show(directory, 640, 300);
        rig.Step(() => window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark);
        rig.Step(() => TerminalPanelRig.Settle(window));
        var tab = Tabs(host).Single();
        var origin = tab.TranslatePoint(new Point(), window)!.Value;
        var lum = Luminance(window);
        var row = Row(tab);

        // Capsule fill: the video's 36 over its panel's 19 (the panel is white 0.08, 20 here).
        var middle = (int)(origin.Y + tab.Bounds.Height / 2);
        Assert.InRange(lum((int)origin.X + 5, middle), 35, 37);
        Assert.InRange(lum((int)origin.X - 3, middle), 19, 21);
        // Chip width: the video's 113 px (x 22..134), 114 points of layout here; height 27 there, 25 here (the face gap).
        Assert.InRange(tab.Bounds.Width, 112, 115);
        Assert.InRange(tab.Bounds.Height, 25, 28);
        // Title: 56 px of ink, peak 222 (primary at 0.85 over the 36 capsule).
        var title = Ink(lum, (Control)row.Children[1], window, 36);
        Assert.InRange(title.Width, 54, 58);
        Assert.InRange(title.Peak, 214, 226);
        Assert.InRange(Over(SwiftColor.Primary.Resolve(Avalonia.Styling.ThemeVariant.Dark), 36), 218, 226); // the token composites to the same
        // Close cross, visible on the selected chip: 8 px of ink, peak 166 in the video (secondary = primary at 0.7).
        var cross = Ink(lum, (Control)((Button)row.Children[2]).Content!, window, 36);
        Assert.InRange(cross.Width, 7, 9);
        Assert.InRange(cross.Peak, 120, 175);
        Assert.InRange(Over(Chrome.SecondaryText.Resolve(Avalonia.Styling.ThemeVariant.Dark), 36), 160, 170);
        // The glyph: ink about 9 px wide in the video (SF Symbol), a Lucide glyph of the same box here.
        var glyph = Ink(lum, (Control)row.Children[0], window, 36);
        Assert.InRange(glyph.Width, 8, 11);
    });

    // OD-0098: no frame shows the unselected chip, the light theme or an exited chip; the pinned Swift is the proof
    // (TerminalPanel.swift:175 secondary on .clear, :176 primary at 0.08, :180 opacity 0.55) against rendered pixels.
    [Fact]
    [Trait("Req", "PR-TERM-025")] // OD-0098; TerminalPanel.swift:175-176, both themes, selected and unselected, rendered
    [Trait("Req", "PR-TERM-026")] // OD-0098; TerminalPanel.swift:180 the exited chip as drawn
    [Trait("Rule", "R-TEST-4")]
    public void Selected_unselected_and_exited_chips_render_the_source_colours_in_both_themes() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("chip colours");
        rig.Step(() =>
        {
            rig.App.Terminals.Open(rig.Thread.Id, directory, title: "Terminal 1");
            rig.App.Terminals.Open(rig.Thread.Id, directory, title: "Terminal 2");
        });
        var (host, _, window) = rig.Show(directory, 640, 300);
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
        {
            rig.Step(() => window.RequestedThemeVariant = theme);
            rig.Step(() => TerminalPanelRig.Settle(window));
            var tabs = Tabs(host);
            var lum = Luminance(window);
            var panel = (int)Math.Round((theme == Avalonia.Styling.ThemeVariant.Dark ? 0.08 : 0.985) * 255); // :107
            var middle = (int)(tabs[0].TranslatePoint(new Point(0, tabs[0].Bounds.Height / 2), window)!.Value.Y);
            var unselectedLeft = (int)tabs[0].TranslatePoint(new Point(5, 0), window)!.Value.X;
            var selectedLeft = (int)tabs[1].TranslatePoint(new Point(5, 0), window)!.Value.X;
            // :176 unselected: a fully clear capsule shows the panel; selected: primary at 0.08 over it.
            Assert.InRange(lum(unselectedLeft, middle), panel - 1, panel + 1);
            var fill = Over(SwiftColor.Primary.Opacity(0.08).Resolve(theme), panel);
            Assert.InRange(lum(selectedLeft, middle), fill - 1, fill + 1);
            // :175 the title: selected primary, unselected secondary (peak ink over its own background).
            var selectedTitle = Ink(lum, (Control)Row(tabs[1]).Children[1], window, fill);
            var unselectedTitle = Ink(lum, (Control)Row(tabs[0]).Children[1], window, panel);
            var primary = Over(SwiftColor.Primary.Resolve(theme), fill);
            var secondary = Over(SwiftColor.Secondary.Resolve(theme), panel);
            Assert.True(Math.Abs(selectedTitle.Peak - primary) <= 8, $"{theme} selected title peak {selectedTitle.Peak}, primary composites to {primary}");
            Assert.True(Math.Abs(unselectedTitle.Peak - secondary) <= 20, $"{theme} unselected title peak {unselectedTitle.Peak}, secondary composites to {secondary}");
            Assert.True(theme == Avalonia.Styling.ThemeVariant.Dark ? selectedTitle.Peak > unselectedTitle.Peak : selectedTitle.Peak < unselectedTitle.Peak);
        }

        // :180 an exited selected chip is dimmed as a whole, above its 0.08 fill: the capsule and the title ink drop toward the panel.
        rig.Step(() => window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark);
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id).ToArray();
        rig.Step(() => sessions[1].Terminate());
        rig.Step(() => TerminalPanelRig.Settle(window));
        var dead = Tabs(host)[1];
        var deadLum = Luminance(window);
        var deadMiddle = (int)(dead.TranslatePoint(new Point(0, dead.Bounds.Height / 2), window)!.Value.Y);
        var deadLeft = (int)dead.TranslatePoint(new Point(5, 0), window)!.Value.X;
        var deadFill = Over(SwiftColor.Primary.Opacity(0.08).Resolve(Avalonia.Styling.ThemeVariant.Dark), 20);
        var dimmedFill = (int)Math.Round(20 + (deadFill - 20) * 0.55);
        Assert.InRange(deadLum(deadLeft, deadMiddle), dimmedFill - 1, dimmedFill + 1);
        var deadTitle = Ink(deadLum, (Control)Row(dead).Children[1], window, dimmedFill);
        var expectedDeadTitle = (int)Math.Round(20 + (Over(SwiftColor.Primary.Resolve(Avalonia.Styling.ThemeVariant.Dark), deadFill) - 20) * 0.55);
        Assert.True(Math.Abs(deadTitle.Peak - expectedDeadTitle) <= 8, $"dimmed title peak {deadTitle.Peak}, expected about {expectedDeadTitle}");
    });

    // PR-TERM-022 (TerminalPanel.swift:148-158): a plain borderless glyph at caption2 bold, secondary text. .buttonStyle(.plain)
    // paints nothing of its own on hover or press, so the rendered glyph must draw and must not change under the pointer.
    // No frame shows an exited tab (OD-0098): the cross beside it uses the same caption2 bold style and is measured in the
    // TERM-CHIP capture above; the Swift lines are the proof for the rest.
    [Fact]
    [Trait("Req", "PR-TERM-022")] // TerminalPanel.swift:148-158; ED-059 .caption2 is 10 points
    public void The_restart_glyph_of_an_exited_tab_draws_in_secondary_caption2_bold_and_shows_no_hover_wash() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, window, sessions) = ShowWithTabs(rig, "chip restart pixels", 2);
        rig.Step(() => window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark);
        rig.Step(() => sessions[0].Terminate()); // the unselected chip exits: no capsule fill behind its glyphs
        rig.Step(() => TerminalPanelRig.Settle(window));
        var tab = Tabs(host)[0];
        var restart = TabButton(tab, "Start a new shell in this tab");
        var icon = Assert.IsType<SwiftImage>(restart.Content);
        Assert.Equal(10, SwiftFont.Caption2.PointSize, 3); // ED-059
        Assert.Equal(SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold), SwiftImage.GetFont(icon));
        Assert.Equal(0, restart.BorderThickness.Left + restart.BorderThickness.Top + restart.BorderThickness.Right + restart.BorderThickness.Bottom);

        var panel = 20; // white 0.08, TerminalPanel.swift:107
        var dimmed = (int)Math.Round(panel + (Over(Chrome.SecondaryText.Resolve(Avalonia.Styling.ThemeVariant.Dark), panel) - panel) * 0.55);
        var before = Luminance(window);
        var glyph = Ink(before, icon, window, panel);
        Assert.InRange(glyph.Width, 7, 11); // a caption2 glyph box, like the 8 px cross of the same style
        // Drawn, and dimmed with the chip: below what the undimmed secondary would give. (Where the Lucide arrow's strokes
        // overlap, each stroke blends its own 0.7 alpha, so the strongest pixel sits above the single-stroke value of about
        // {dimmed}: it is bounded by the primary level at the same dimming, 20 + (220 - 20) * 0.55 = 130.)
        Assert.True(glyph.Peak > panel + 25 && glyph.Peak <= 20 + (220 - 20) * 0.55 + 2, $"the restart glyph's strongest pixel is {glyph.Peak}; a single stroke is about {dimmed}");

        // The pointer enters the tab and then rests on the button: nothing behind the glyph changes.
        var box = new Rect(restart.TranslatePoint(new Point(), window)!.Value, restart.Bounds.Size);
        var snapshot = new List<int>();
        for (var y = (int)Math.Floor(box.Top) - 2; y < (int)Math.Ceiling(box.Bottom) + 2; y++)
        for (var x = (int)Math.Floor(box.Left) - 2; x < (int)Math.Ceiling(box.Right) + 2; x++) snapshot.Add(before(x, y));
        rig.Step(() => window.MouseMove(CentreIn(restart, window), RawInputModifiers.None));
        rig.Step(() => TerminalPanelRig.Settle(window));
        var after = Luminance(window);
        var again = new List<int>();
        for (var y = (int)Math.Floor(box.Top) - 2; y < (int)Math.Ceiling(box.Bottom) + 2; y++)
        for (var x = (int)Math.Floor(box.Left) - 2; x < (int)Math.Ceiling(box.Right) + 2; x++) again.Add(after(x, y));
        Assert.Equal(snapshot, again);
    });

    // PR-TERM-021's failure_case says a long title "is truncated with an ellipsis by the text engine; the chip does not grow
    // unbounded". RECORD != SWIFT, ENGINEERING DECISION (SwiftUI layout semantics): the chips sit in
    // ScrollView(.horizontal) (TerminalPanel.swift:21), which offers its content no width limit along the scroll axis, so
    // Text(session.title).lineLimit(1) (:146) is laid out at its ideal width and is never truncated; the chip widens and the strip
    // scrolls. The port's StackPanel in a ScrollViewer lays out the same way, so no width cap is added.
    [Fact]
    [Trait("Req", "PR-TERM-021")] // TerminalPanel.swift:21,146 (the record's own failure case differs, see above)
    [Trait("Rule", "R-TEST-4")]
    public void A_long_title_widens_its_chip_inside_the_scrolling_strip_and_stays_on_one_line() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("chip long title");
        var longTitle = string.Join(' ', Enumerable.Repeat("a title far too long for the strip", 6));
        rig.Step(() => rig.App.Terminals.Open(rig.Thread.Id, directory, title: longTitle));
        var (host, _, _) = rig.Show(directory, 500, 300);
        var tab = Tabs(host).Single();
        var title = Assert.IsType<TextBlock>(Row(tab).Children[1]);
        Assert.True(tab.Bounds.Width > 500, $"the chip is {tab.Bounds.Width} wide in a 500 wide panel");
        Assert.Equal(title.DesiredSize.Width, title.Bounds.Width, 3); // laid out at its full width, not trimmed
        Assert.True(title.Bounds.Height < 20); // one line
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
    public void The_restart_button_exists_only_for_an_exited_shell_between_title_and_cross_and_its_click_starts_a_fresh_shell_that_does_not_rerun_the_script() => TerminalPanelRig.RunWithRealShell(headless, rig =>
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
