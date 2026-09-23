using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), part 1 of 4 continued (R-TEST-3: a second file
// for the same Swift file): ChromeTextMenu :472-514, ChromeTextButton :516-560, SidebarSearchField :775-823,
// ChromeSearchField :825-869 and GlassPickerButton :871-932. Expected values come from those lines and the records:
//   :33 capsuleContentHeight = 28; :34 capsuleVerticalPadding = 2; :35 capsuleHorizontalPadding = 12;
//   :76 primaryText = labelColor (ED-051, SwiftColor.LabelColor); :93 danger = ThemeManager.spec.danger;
//   titles `.font(.system(size: 12.5, weight: .medium))`; `.body` is 13 points (ED-059,
//   B1:DroppyCode/UI/Common/ChatZoom.swift:51) in Inter (OD-0009).
// Real call sites give the sample strings: ChatView.swift:1355-1358 the branch menu ("Branch", "Detached"),
// SettingsView.swift:883-888 "Delete all" / "Delete every archived thread", HydraSettingsPage.swift:384 a provider picker.
// Port shape read (R-VIEW-1, R-VIEW-2, R-VIEW-7, R-VIEW-9): a SwiftUI Button is found through UI Automation (a control
// whose peer is a Button) and invoked through its Invoke pattern; a TextField is a TextBox whose PlaceholderText is the
// prompt; `@Binding` is a Binding<T>; `.help` is the ToolTip tip; `.accessibilityLabel` the automation name;
// `.accessibilityHidden(true)` AccessibilityView.Raw; `.popover` goes through the Presentation seam.
// Keys: SwiftUI `.onKeyPress` answers before the field sees the key, so a handled arrow is already handled when the
// key reaches the field's own tunnel handlers; `.onExitCommand` answers Escape and nothing above the field sees it.
// Not asserted (dependency not in the tree, see the unit report): glyphs (magnifier, xmark.circle.fill, chevron.down,
// the caller's symbol) as Lucide icons, the picker's asset image, glass, `.continuous` capsule geometry, the popover
// rows (PopoverMenu and PopoverItem belong to UI.Chrome.Popovers) and the frames of an animated change.
[Collection("Headless")]
public sealed class ChromeSecondTests(HeadlessSession headless)
{
    private static readonly Point Away = new(595, 295);

    private static Window Show(Control content, ThemeVariant? theme = null)
    {
        var window = new Window { Width = 600, Height = 300, Content = content };
        if (theme is not null)
            window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return control;
    }

    // A host of a fixed width, the way a parent proposes a width to its child.
    private static Grid Host(Control control, double width)
    {
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var host = new Grid { Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        host.Children.Add(control);
        return host;
    }

    private static void Frames(ManualFrameSource frames, int count = 30)
    {
        for (var i = 0; i < count; i++)
        {
            (SwiftClock.Provider.Current as ManualTimeProvider)?.Advance(TimeSpan.FromMilliseconds(10));
            frames.Advance(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static IEnumerable<Control> SelfAndDescendants(Control root) =>
        new[] { root }.Concat(VisualQuery.Descendants<Control>(root));

    private static List<Control> ButtonsIn(Control root) =>
        SelfAndDescendants(root).Where(c => Peer(c).GetAutomationControlType() == AutomationControlType.Button).ToList();

    private static Control TheButton(Control root) => Assert.Single(ButtonsIn(root));

    private static List<Control> ClearButtons(Control root) =>
        ButtonsIn(root).Where(b => AutomationProperties.GetName(b) == "Clear search").ToList();

    private static void Invoke(Control button)
    {
        var invoke = Peer(button).GetProvider<IInvokeProvider>();
        Assert.NotNull(invoke);
        invoke.Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Field(Control root) => Assert.Single(VisualQuery.Descendants<TextBox>(root));

    private static TextBlock Title(Control root, string text) =>
        Assert.Single(VisualQuery.Descendants<TextBlock>(root), t => t.Text == text);

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static Point Centre(Control control, Window window)
    {
        var bounds = BoundsIn(control, window);
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Key key, PhysicalKey physical, string symbol)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Effective(IBrush? brush)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1); // alpha x opacity may round either way by one step
    }

    private static Color Label(double opacity, ThemeVariant theme) => SwiftColor.LabelColor.Opacity(opacity).Resolve(theme);

    private static void AssertTitleType(TextBlock title)
    {
        Assert.Equal(12.5, title.FontSize);
        Assert.Equal(FontWeight.Medium, title.FontWeight);
        Assert.Equal("Inter", title.FontFamily.FamilyNames[0]);
        Assert.Equal(1, title.MaxLines);
    }

    // The row a capsule control lays out: the title's own container.
    private static Control Row(TextBlock title) => Assert.IsAssignableFrom<Control>(title.GetVisualParent());

    // ---- ChromeTextMenu, :472-514 ----

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:489-491 Text(verbatim: title).font(.system(size: 12.5, weight: .medium)).lineLimit(1)
    public void A_text_menu_title_is_the_verbatim_title_at_12_5_point_medium_on_one_line() => headless.OnUiThread(() =>
    {
        var menu = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "feature/*wip*", help: "Branch", content: () => new Border());
        var window = Show(TopLeft(menu));

        AssertTitleType(Title(menu, "feature/*wip*"));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:497-499 .padding(.horizontal, 12).frame(height: 28).padding(.vertical, 2)
    public void A_text_menu_is_32_points_tall_with_its_row_inset_12_points_across_and_2_down() => headless.OnUiThread(() =>
    {
        var menu = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var window = Show(TopLeft(menu));

        Assert.Equal(32, menu.Bounds.Height);
        var row = BoundsIn(Row(Title(menu, "Detached")), menu);
        Assert.Equal(12, row.X);
        Assert.Equal(12, menu.Bounds.Width - row.Right);
        Assert.Equal(2, row.Y);
        Assert.Equal(28, row.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:503 .fixedSize(): the menu takes its own size whatever it is offered
    public void A_text_menu_sizes_to_its_content_and_never_compresses() => headless.OnUiThread(() =>
    {
        var wide = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var narrow = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var free = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var panel = new StackPanel { Children = { Host(wide, 500), Host(narrow, 20), TopLeft(free) } };
        var window = Show(TopLeft(panel));

        Assert.True(free.Bounds.Width > 20);
        Assert.Equal(free.Bounds.Width, wide.Bounds.Width);
        Assert.Equal(free.Bounds.Width, narrow.Bounds.Width);
        Assert.Equal("Detached", Title(narrow, "Detached").Text);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:496 `.foregroundStyle(Chrome.primaryText.opacity(isHovering || isPresented ? 1 : 0.92))`
    public void A_text_menu_label_is_0_92_at_rest_and_full_while_hovered_or_open() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var menu = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var window = Show(TopLeft(menu));
        var theme = window.ActualThemeVariant;
        var title = Title(menu, "Detached");
        AssertColor(Label(0.92, theme), Effective(title.Foreground));

        window.MouseMove(Centre(title, window), RawInputModifiers.None);
        Frames(frames);
        AssertColor(Label(1, theme), Effective(title.Foreground));

        Click(window, Centre(title, window));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Same(TheButton(menu), popup.PlacementTarget);
        AssertColor(Label(1, theme), Effective(title.Foreground));

        Click(window, Centre(title, window));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        Assert.False(recorder.Calls[^1].Presented);
        AssertColor(Label(0.92, theme), Effective(title.Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:508 .help(help) and no .accessibilityLabel; :512 the no-drag background
    public void A_text_menu_has_its_help_as_a_tooltip_but_not_as_its_accessible_name() => headless.OnUiThread(() =>
    {
        var menu = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var window = Show(TopLeft(menu));

        var button = TheButton(menu);
        Assert.Equal("Branch", ToolTip.GetTip(button));
        Assert.NotEqual("Branch", Peer(button).GetName());
        Assert.Null(AutomationProperties.GetName(button));
        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(menu));
        Assert.Equal(menu.Bounds.Size, region.Bounds.Size);
        window.Close();
    });

    // ---- ChromeTextButton, :516-560 ----

    [Fact]
    [Trait("Req", "PR-WIN-189")] // UI/Chrome/Chrome.swift:533-541: the text menu's title, paddings and height, :545 .fixedSize()
    public void A_text_button_has_the_text_menus_title_and_geometry() => headless.OnUiThread(() =>
    {
        var control = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { });
        var narrow = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { });
        var panel = new StackPanel { Children = { TopLeft(control), Host(narrow, 20) } };
        var window = Show(TopLeft(panel));

        var title = Title(control, "Delete all");
        AssertTitleType(title);
        Assert.Equal(32, control.Bounds.Height);
        var row = BoundsIn(Row(title), control);
        Assert.Equal(12, row.X);
        Assert.Equal(12, control.Bounds.Width - row.Right);
        Assert.Equal(2, row.Y);
        Assert.Equal(28, row.Height);
        Assert.Equal(control.Bounds.Width, narrow.Bounds.Width); // never compresses
        window.Close();
    });

    [Theory]
    [Trait("Req", "PR-WIN-189")] // UI/Chrome/Chrome.swift:555-559: disabled 0.32 first, then danger when destructive, else primary; 1 hovered, 0.92 at rest
    [InlineData(true, false, false, false, 0.92)]
    [InlineData(true, false, true, false, 1.0)]
    [InlineData(true, true, false, true, 0.92)]
    [InlineData(true, true, true, true, 1.0)]
    [InlineData(false, true, true, false, 0.32)]
    [InlineData(false, false, false, false, 0.32)]
    public void A_text_button_foreground_is_disabled_first_then_destructive_then_plain(bool isEnabled, bool isDestructive, bool hover, bool danger, double opacity) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var control = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { }, isEnabled: isEnabled, isDestructive: isDestructive);
            var window = Show(TopLeft(control), theme);
            var title = Title(control, "Delete all");

            window.MouseMove(hover ? Centre(title, window) : Away, RawInputModifiers.None);
            Frames(frames);

            var expected = danger
                ? ThemeManager.Spec.Danger.Opacity(opacity).Resolve(theme)
                : Label(opacity, theme);
            AssertColor(expected, Effective(title.Foreground));
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-189")] // UI/Chrome/Chrome.swift:529 Button(action:), :544 .disabled(!isEnabled), :550-551 help and accessibility label, :552 no-drag
    public void A_text_button_acts_when_enabled_and_carries_its_help_as_tooltip_and_accessible_name() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // a click also hovers, and the hover animates (ED-117)
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var runs = 0;
        var control = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => runs++, isDestructive: true);
        var window = Show(TopLeft(control));

        var button = TheButton(control);
        Assert.Equal("Delete every archived thread", ToolTip.GetTip(button));
        Assert.Equal("Delete every archived thread", Peer(button).GetName());
        Click(window, Centre(Title(control, "Delete all"), window));
        Assert.Equal(1, runs);
        Assert.Single(VisualQuery.Descendants<NoWindowDragView>(control));
        window.Close();

        var disabled = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => runs++, isEnabled: false);
        window = Show(TopLeft(disabled));
        Click(window, Centre(Title(disabled, "Delete all"), window));
        Assert.Equal(1, runs);
        Assert.False(TheButton(disabled).IsEffectivelyEnabled);
        window.Close();
    });

    // ---- SidebarSearchField, :775-823 ----

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:777 prompt "Search", :789-792 TextField(prompt, text:).font(.system(.body)).frame(maxWidth: .infinity)
    public void The_sidebar_search_field_prompts_Search_in_the_body_style() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(new Border { Width = 240, Child = field, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top });

        var box = Field(field);
        Assert.Equal("Search", box.PlaceholderText);
        Assert.Equal(13, box.FontSize);
        Assert.Equal("Inter", box.FontFamily.FamilyNames[0]);
        Assert.Equal(30, field.Bounds.Height); // :809 .frame(height: 30)
        Assert.Equal(240 - 9, BoundsIn(box, field).Right); // :808 .padding(.horizontal, 9), the field filling the rest
        Assert.True(BoundsIn(box, field).X >= 9);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:776 @Binding var text, :789 TextField(prompt, text: $text)
    public void The_sidebar_search_field_reads_and_writes_its_binding() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("git");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v), prompt: "Filter");
        var window = Show(TopLeft(field));
        var box = Field(field);
        Assert.Equal("git", box.Text);
        Assert.Equal("Filter", box.PlaceholderText);

        box.Text = "git log";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("git log", cell.Value);

        cell.Value = "status";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("status", box.Text);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-007")] // UI/Chrome/Chrome.swift:796-806 `if !text.isEmpty { Button { text = "" } ... .accessibilityLabel(Text("Clear search")) }`
    [Trait("Req", "PR-WIN-191")]
    public void The_sidebar_clear_button_exists_only_while_the_field_has_text() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(field));
        Assert.Empty(ClearButtons(field));

        cell.Value = " "; // whitespace is text
        Dispatcher.UIThread.RunJobs();
        var clear = Assert.Single(ClearButtons(field));
        Assert.Equal("Clear search", Peer(clear).GetName());

        cell.Value = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(ClearButtons(field));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-007")] // UI/Chrome/Chrome.swift:797-799 `text = ""`
    public void The_sidebar_clear_button_empties_the_field_and_goes_away() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("claude");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(new Border { Width = 240, Child = field, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top });
        var box = Field(field);
        var clear = Assert.Single(ClearButtons(field));
        Assert.Equal(240 - 9, BoundsIn(clear, field).Right); // last in the row, inside the 9 point inset
        Assert.Equal(BoundsIn(box, field).Right + 6, BoundsIn(clear, field).X); // HStack(spacing: 6)

        Invoke(clear);

        Assert.Equal("", cell.Value);
        Assert.Equal("", box.Text);
        Assert.Empty(ClearButtons(field));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-008")] // UI/Chrome/Chrome.swift:814-815 `.onExitCommand { text = "" }`
    [Trait("Req", "PR-WIN-191")]
    public void Escape_in_the_sidebar_search_field_empties_it_and_goes_no_further() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("opus");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(field));
        var above = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => above.Add(e.Key), RoutingStrategies.Bubble);
        Field(field).Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.Escape, PhysicalKey.Escape, "\u001b");

        Assert.Equal("", cell.Value);
        Assert.DoesNotContain(Key.Escape, above); // a panel around the field never sees it
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:794-795 onKeyPress(.upArrow) move(-1), (.downArrow) move(1); :818-822 handled when onMove is set
    public void Up_and_down_in_the_sidebar_search_field_walk_the_results_one_step_per_press() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("a");
        var moves = new List<int>();
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v), onMove: moves.Add);
        var window = Show(TopLeft(field));
        var box = Field(field);
        var reachingField = new List<bool>();
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => reachingField.Add(e.Handled), RoutingStrategies.Tunnel, handledEventsToo: true);
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.Up, PhysicalKey.ArrowUp, "");
        Press(window, Key.Down, PhysicalKey.ArrowDown, "");
        Press(window, Key.Down, PhysicalKey.ArrowDown, "");

        Assert.Equal([-1, 1, 1], moves);
        Assert.Equal([true, true, true], reachingField); // .handled: the field never moves its caret for them
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:819 `guard let onMove else { return .ignored }`
    public void Without_a_move_handler_the_arrows_stay_the_fields_own() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("a");
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(field));
        var box = Field(field);
        var reachingField = new List<bool>();
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => reachingField.Add(e.Handled), RoutingStrategies.Tunnel, handledEventsToo: true);
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.Up, PhysicalKey.ArrowUp, "");
        Press(window, Key.Down, PhysicalKey.ArrowDown, "");

        Assert.Equal([false, false], reachingField); // .ignored: the keys go on to the field untouched
        Assert.Equal("a", cell.Value);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:778 onSubmit, :793 .onSubmit(onSubmit)
    public void Return_in_the_sidebar_search_field_submits() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("main");
        var submits = 0;
        var field = new SidebarSearchField(text: new Binding<string>(() => cell.Value, v => cell.Value = v), onSubmit: () => submits++);
        var window = Show(TopLeft(field));
        Field(field).Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.Enter, PhysicalKey.Enter, "\r");

        Assert.Equal(1, submits);
        Assert.Equal("main", cell.Value);
        window.Close();
    });

    // ---- ChromeSearchField, :825-869 ----

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:828 width 200, :862-865 .frame(width: 200, height: 28).padding(.vertical, 2)....fixedSize()
    public void The_chrome_search_field_is_a_fixed_200_by_32_capsule() => headless.OnUiThread(() =>
    {
        Assert.Equal(200, ChromeSearchField.Width);
        var wide = new ChromeSearchField(query: Binding<string>.Constant(""));
        var narrow = new ChromeSearchField(query: Binding<string>.Constant(""));
        var panel = new StackPanel { Children = { Host(wide, 500), Host(narrow, 60) } };
        var window = Show(TopLeft(panel));

        Assert.Equal(new Size(200, 32), wide.Bounds.Size);
        Assert.Equal(new Size(200, 32), narrow.Bounds.Size);
        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(wide)); // :867
        Assert.Equal(new Size(200, 32), region.Bounds.Size);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:831 prompt "Search", :839-845 TextField(prompt, text: $query).font(.system(size: 12.5)).foregroundStyle(Chrome.primaryText)
    public void The_chrome_search_field_prompts_Search_at_12_5_points_in_the_primary_colour() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("");
        var control = new ChromeSearchField(query: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(control), ThemeVariant.Dark);

        var box = Field(control);
        Assert.Equal("Search", box.PlaceholderText);
        Assert.Equal(12.5, box.FontSize);
        Assert.Equal("Inter", box.FontFamily.FamilyNames[0]);
        AssertColor(Label(1, ThemeVariant.Dark), Effective(box.Foreground));

        box.Text = "hydra";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("hydra", cell.Value);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:846-859: always laid out; opacity 0, disabled and hidden while the query is empty; label "Clear search"
    public void The_chrome_clear_button_is_always_laid_out_but_invisible_disabled_and_hidden_while_empty() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("");
        var control = new ChromeSearchField(query: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(control));
        var box = Field(control);

        var clear = Assert.Single(ClearButtons(control));
        Assert.Equal(0, clear.Opacity);
        Assert.False(clear.IsEffectivelyEnabled);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear));
        var emptyBounds = BoundsIn(clear, control);
        var emptyField = BoundsIn(box, control);
        Assert.Equal(200 - 10, emptyBounds.Right); // :861 .padding(.horizontal, 10)
        Assert.Equal(emptyField.Right + 6, emptyBounds.X); // HStack(spacing: 6)

        cell.Value = "x";
        Dispatcher.UIThread.RunJobs();

        Assert.Same(clear, Assert.Single(ClearButtons(control)));
        Assert.Equal(1, clear.Opacity);
        Assert.True(clear.IsEffectivelyEnabled);
        Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear));
        Assert.Equal("Clear search", Peer(clear).GetName());
        Assert.Equal(emptyBounds, BoundsIn(clear, control)); // the text never shifts when the button comes
        Assert.Equal(emptyField, BoundsIn(box, control));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:846-848 `query = ""`, :866 `.onExitCommand { query = "" }`
    public void The_chrome_search_field_empties_on_its_clear_button_and_on_Escape() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<string>("diff");
        var control = new ChromeSearchField(query: new Binding<string>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(control));

        Invoke(Assert.Single(ClearButtons(control)));
        Assert.Equal("", cell.Value);

        cell.Value = "diff";
        Dispatcher.UIThread.RunJobs();
        var above = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => above.Add(e.Key), RoutingStrategies.Bubble);
        Field(control).Focus();
        Dispatcher.UIThread.RunJobs();
        Press(window, Key.Escape, PhysicalKey.Escape, "\u001b");

        Assert.Equal("", cell.Value);
        Assert.DoesNotContain(Key.Escape, above); // it empties the field and dismisses nothing
        window.Close();
    });

    // ---- GlassPickerButton, :871-932 ----

    private static readonly (int Value, string Title)[] Efforts = [(1, "Low"), (2, "Medium"), (2, "Duplicate"), (3, "High")];

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:885 `options.first { $0.value == selection }?.title ?? ""`
    public void A_picker_shows_the_title_of_the_first_option_matching_the_selection_or_nothing() => headless.OnUiThread(() =>
    {
        var cell = new CellSample<int>(2);
        var picker = new GlassPickerButton<int>(options: Efforts, selection: new Binding<int>(() => cell.Value, v => cell.Value = v));
        var window = Show(TopLeft(picker));
        Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Medium");
        Assert.DoesNotContain(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Duplicate");

        cell.Value = 3;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "High");

        cell.Value = 9; // no option
        Dispatcher.UIThread.RunJobs();
        var title = Assert.Single(VisualQuery.Descendants<TextBlock>(picker));
        Assert.Equal("", title.Text);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:901-904 12.5 medium, lineLimit(1), truncationMode(.tail); :910-911 12 point insets, 28 tall
    public void A_picker_title_is_12_5_point_medium_on_one_tail_truncated_line_in_a_28_point_capsule() => headless.OnUiThread(() =>
    {
        var picker = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(1));
        var window = Show(TopLeft(picker));

        var title = Title(picker, "Low");
        AssertTitleType(title);
        Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        Assert.Equal(28, picker.Bounds.Height); // no vertical padding on this capsule
        var row = BoundsIn(Row(title), picker);
        Assert.Equal(12, row.X);
        Assert.Equal(12, picker.Bounds.Width - row.Right);
        Assert.Equal(28, row.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:915 `.fixedSize(horizontal: maxWidth == nil, vertical: true)`
    public void Without_a_maximum_width_a_picker_sizes_to_its_content() => headless.OnUiThread(() =>
    {
        var free = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(3));
        var wide = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(3));
        var narrow = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(3));
        var panel = new StackPanel { Children = { TopLeft(free), Host(wide, 500), Host(narrow, 20) } };
        var window = Show(TopLeft(panel));

        Assert.True(free.Bounds.Width > 20);
        Assert.Equal(free.Bounds.Width, wide.Bounds.Width);
        Assert.Equal(free.Bounds.Width, narrow.Bounds.Width);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:915-917 fixedSize off, then `.frame(maxWidth: maxWidth, alignment: .trailing)`
    public void With_a_maximum_width_a_picker_is_capped_and_sits_at_the_trailing_edge() => headless.OnUiThread(() =>
    {
        var options = new[] { (1, "An exceptionally long provider display name that cannot fit") };
        var capped = new GlassPickerButton<int>(options: options, selection: Binding<int>.Constant(1), maxWidth: 90);
        var roomy = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(1), maxWidth: 300);
        var panel = new StackPanel { Children = { Host(capped, 500), Host(roomy, 500) } };
        var window = Show(TopLeft(panel));

        Assert.Equal(90, capped.Bounds.Width);
        var cappedButton = BoundsIn(TheButton(capped), capped);
        Assert.Equal(90, cappedButton.Right);
        Assert.True(cappedButton.Width <= 90);

        Assert.Equal(300, roomy.Bounds.Width);
        var roomyButton = BoundsIn(TheButton(roomy), roomy);
        Assert.Equal(300, roomyButton.Right); // trailing
        Assert.True(roomyButton.Width < 300); // the capsule keeps its own width inside the frame
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:886-888 toggle, :909 `isHovering || isPresented ? 1 : 0.92`, :921 .popover(arrowEdge: .bottom), :930 no-drag
    public void A_picker_opens_its_options_below_it_and_reads_full_while_open() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var picker = new GlassPickerButton<int>(options: Efforts, selection: Binding<int>.Constant(1));
        var window = Show(TopLeft(picker));
        var theme = window.ActualThemeVariant;
        var title = Title(picker, "Low");
        AssertColor(Label(0.92, theme), Effective(title.Foreground));

        Click(window, Centre(title, window));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);

        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Same(TheButton(picker), popup.PlacementTarget);
        AssertColor(Label(1, theme), Effective(title.Foreground));
        Assert.Single(VisualQuery.Descendants<NoWindowDragView>(picker));
        window.Close();
    });
}
