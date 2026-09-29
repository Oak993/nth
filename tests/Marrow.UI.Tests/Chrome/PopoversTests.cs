using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/Popovers.swift (B2 1fbf81e), the members named by this part's records:
// presentedChrome() :4-13, PopoverMenu :94-115, PopoverDivider :152-158, PopoverItem :190-337 (the Button's
// confirm/plain timings at :258-273), RowAction :341-352, RowActionItems :354-363, RowActionMenuButtons :365-382,
// RowActionsButton :384-401. Expected values are transcribed from the Swift and the records, never from the C#
// under test: :10-11 buttonBorderShape(.capsule)/tint(ThemeManager.spec.accent); :107-108 PopoverMenu padding 6,
// minWidth 210, maxWidth 360; :112 maxHeight 460; :275-330 PopoverItem's HStack(spacing 8), 16pt glyph column,
// Spacer(minLength 16), height 26, horizontal padding 8, corner radius 6, hover fill overlay(0.1), disabled
// opacity 0.4; :320 danger vs primaryText; :155-156 PopoverDivider padding 8 horizontal, 5 vertical; :259-273 the
// two Button timings; :372 the context-menu row's synchronous item.action().
// Port shape read (R-VIEW-1, R-VIEW-9, ED-095): a popover is an Avalonia Popup shown through the Presentation
// seam, recorded here by PopoverRecorder because headless cannot open a themeless Popup (Fixtures/PopoverRecorder.cs);
// MainActor.Spawn posts to the Avalonia dispatcher and only runs once Dispatcher.UIThread.RunJobs() pumps it;
// Task.sleep's timing is read off a ManualTimeProvider bound to SwiftClock.Provider. Not asserted here, because the
// dependency is not in the tree (see the unit report): SF Symbol glyphs as Lucide icons (OD-0010), the popover's
// Acrylic backdrop (OD-0005) and the exact middle-truncation point (OD-0001, no macOS reference).
[Collection("Headless")]
public sealed class PopoversTests(HeadlessSession headless)
{
    private static Window Show(Control content, ThemeVariant? theme = null, double width = 400, double height = 700)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<Control> SelfAndDescendants(Control root) =>
        new[] { root }.Concat(VisualQuery.Descendants<Control>(root));

    // ---- PR-THEME-041: presentedChrome's raw ThemeSpec.Accent tint ----

    [Theory]
    [Trait("Req", "PR-THEME-041")] // UI/Chrome/Popovers.swift:11 .tint(ThemeManager.spec.accent), UPSTREAM DEFECT 66
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void PresentedChrome_tints_with_null_for_the_three_system_hue_themes(AppTheme theme) => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = theme;
            var control = new Border();
            control.PresentedChrome();
            Assert.Null(Popovers.GetTint(control));
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(control)); // :10 buttonBorderShape(.capsule)
        }
        finally
        {
            ThemeManager.Current = original;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-041")] // UI/Chrome/Popovers.swift:11 .tint(ThemeManager.spec.accent): a themed accent is not null
    public void PresentedChrome_tints_with_the_themes_own_accent_when_it_has_one() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Dracula;
            var control = new Border();
            control.PresentedChrome();
            var tint = Assert.IsType<SwiftColor>(Popovers.GetTint(control));
            Assert.Equal(SwiftColor.Hex("BD93F9").Resolve(ThemeVariant.Dark), tint.Resolve(ThemeVariant.Dark)); // AppTheme.swift:119
        }
        finally
        {
            ThemeManager.Current = original;
        }
    });

    // ---- UI-SIDEBAR-012: PopoverMenu container ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-012")] // UI/Chrome/Popovers.swift:104-108 VStack padding 6, minWidth 210, maxWidth 360, leading
    public void PopoverMenu_pads_6_and_is_at_least_210_and_at_most_360_wide() => headless.OnUiThread(() =>
    {
        var narrow = new PopoverMenu(() => new TextBlock { Text = "x" });
        var wide = new PopoverMenu(() => new Border { Width = 800, Height = 20 });
        var window = Show(new StackPanel { Children = { narrow, wide } });

        var narrowBorder = Assert.Single(VisualQuery.Descendants<Border>(narrow), b => b.MinWidth == 210);
        Assert.Equal(new Thickness(6), narrowBorder.Padding); // :107
        Assert.Equal(210, narrowBorder.MinWidth); // :108
        Assert.Equal(360, narrowBorder.MaxWidth); // :108
        Assert.Equal(HorizontalAlignment.Left, narrowBorder.HorizontalAlignment); // :108 alignment: .leading

        var wideBorder = Assert.Single(VisualQuery.Descendants<Border>(wide), b => b.MinWidth == 210);
        Assert.True(wideBorder.Bounds.Width <= 360.01, $"width {wideBorder.Bounds.Width} exceeded 360");
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-012")] // UI/Chrome/Popovers.swift:112 .frame(idealHeight:maxHeight: 460): default cap is 460
    public void PopoverMenu_caps_its_scroll_at_460_by_default_and_at_idealHeight_when_given() => headless.OnUiThread(() =>
    {
        var defaultCap = new PopoverMenu(() => new Border());
        var withIdeal = new PopoverMenu(() => new Border(), idealHeight: 120);
        var window = Show(new StackPanel { Children = { defaultCap, withIdeal } });

        var defaultScroll = Assert.IsType<ScrollViewer>(defaultCap.Child);
        Assert.Equal(460, defaultScroll.MaxHeight); // :112 maxHeight default
        Assert.True(double.IsNaN(defaultScroll.Height));

        var idealScroll = Assert.IsType<ScrollViewer>(withIdeal.Child);
        Assert.Equal(120, idealScroll.Height); // :112 idealHeight forces the open height
        window.Close();
    });

    // ---- UI-SIDEBAR-011, PR-SIDEBAR-105: RowActionsButton (the ellipsis) ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-011")] // UI/Chrome/Popovers.swift:389-396 RowAccessoryIcon("ellipsis"), .help("More")
    [Trait("Req", "PR-SIDEBAR-105")] // UI/Chrome/Popovers.swift:384-399 the ellipsis opens a popover menu under the row
    public void RowActionsButton_is_tooltipped_More_and_toggles_a_popover_below_it() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var box = new CellSample<bool>(false);
        var isPresented = new Binding<bool>(() => box.Value, v => box.Value = v);
        var actions = new List<RowAction> { new() { Title = "Rename", Action = () => { } } };
        var control = new RowActionsButton(actions, isPresented);
        var window = Show(control);

        var panel = Assert.IsType<Panel>(control.Child);
        var button = Assert.IsType<Button>(panel.Children[0]);
        Assert.IsType<RowAccessoryIcon>(button.Content); // :393 label: RowAccessoryIcon("ellipsis")
        Assert.Equal("More", ToolTip.GetTip(button)); // :396 .help("More")

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // :391 isPresented.toggle()
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.Value);
        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement); // :397 arrowEdge: .bottom

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.Value);
        window.Close();
    });

    // ---- PR-SIDEBAR-106, UI-SIDEBAR-013: PopoverItem geometry, colour and hover ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:275-327 row is 26 tall, 8 horizontal padding, 16pt glyph column
    [Trait("Req", "UI-SIDEBAR-013")] // UI/Chrome/Popovers.swift:190-336
    public void PopoverItem_is_26_tall_with_8_horizontal_padding_and_a_16_point_glyph_column() => headless.OnUiThread(() =>
    {
        var item = new PopoverItem("Rename", () => { }, symbol: "pencil");
        var window = Show(item, ThemeVariant.Light);

        var row = Assert.Single(SelfAndDescendants(item), c => c is StackPanel sp && sp.Orientation == Orientation.Horizontal);
        Assert.Equal(26, row.Height); // :322
        Assert.Equal(new Thickness(8, 0), row.Margin); // :321

        var glyphSlot = Assert.Single(VisualQuery.Descendants<Panel>(item), p => p.Width == 16 && p.Height == 16);
        Assert.NotEmpty(((Panel)glyphSlot).Children); // :304 the 16pt symbol column holds the glyph
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:320 foregroundStyle(isDestructive ? Chrome.danger : Chrome.primaryText)
    public void PopoverItem_is_drawn_in_danger_when_destructive_and_primaryText_otherwise() => headless.OnUiThread(() =>
    {
        var plain = new PopoverItem("Rename", () => { });
        var destructive = new PopoverItem("Delete", () => { }, isDestructive: true);
        var window = Show(new StackPanel { Children = { plain, destructive } }, ThemeVariant.Light);

        var plainRow = Assert.Single(SelfAndDescendants(plain), c => c is StackPanel sp && sp.Orientation == Orientation.Horizontal);
        var destructiveRow = Assert.Single(SelfAndDescendants(destructive), c => c is StackPanel sp && sp.Orientation == Orientation.Horizontal);

        var plainBrush = Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Controls.Documents.TextElement.GetForeground(plainRow));
        var destructiveBrush = Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Controls.Documents.TextElement.GetForeground(destructiveRow));

        Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Light), plainBrush.Color); // Chrome.primaryText
        Assert.NotEqual(plainBrush.Color, destructiveBrush.Color); // Chrome.danger differs from primaryText
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:323-326 hover fill overlay(0.1), Clear otherwise
    public void PopoverItem_fills_with_overlay_0_1_on_hover_and_nothing_when_disabled() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var enabled = new PopoverItem("Rename", () => { });
        var disabled = new PopoverItem("Rename", () => { }, isEnabled: false);
        var window = Show(new StackPanel { Children = { enabled, disabled } }, ThemeVariant.Light);

        var enabledButton = Assert.Single(SelfAndDescendants(enabled), c => c is Button);
        var enabledFill = Assert.IsAssignableFrom<Border>(((Button)enabledButton).Content);
        var enabledBrush = Assert.IsAssignableFrom<ISolidColorBrush>(enabledFill.Background);
        Assert.Equal(0, enabledBrush.Color.A); // :325 Color.clear at rest

        enabledButton.RaiseEvent(new Avalonia.Input.PointerEventArgs(
            Avalonia.Input.InputElement.PointerEnteredEvent, enabledButton, null!, enabledButton, default,
            0, new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None));
        for (var i = 0; i < 15; i++)
        {
            (SwiftClock.Provider.Current as ManualTimeProvider)?.Advance(TimeSpan.FromMilliseconds(10));
            frames.Advance(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.RunJobs();
        }
        var overlayBrush = Assert.IsAssignableFrom<ISolidColorBrush>(enabledFill.Background);
        Assert.True(overlayBrush.Color.A > 0); // :325 overlay(0.1) while hovering

        var disabledButton = Assert.Single(SelfAndDescendants(disabled), c => c is Button);
        Assert.False(((Button)disabledButton).IsEnabled); // :330 .disabled(!isEnabled)
        Assert.Equal(0.4, disabledButton.Opacity); // :331
        window.Close();
    });

    // ---- RowActionItems: the startsGroup divider ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-013")] // UI/Chrome/Popovers.swift:354-363 RowActionItems, :155-156 PopoverDivider padding
    public void RowActionItems_precedes_a_startsGroup_row_with_a_divider_padded_8_and_5() => headless.OnUiThread(() =>
    {
        var actions = new List<RowAction>
        {
            new() { Title = "Rename", Action = () => { } },
            new() { Title = "Delete", Action = () => { }, IsDestructive = true, StartsGroup = true }, // :359
        };
        var control = new RowActionItems(actions);
        var window = Show(control, ThemeVariant.Light);

        var divider = Assert.Single(VisualQuery.Descendants<PopoverDivider>(control));
        var bar = Assert.IsType<Border>(divider.Child);
        Assert.Equal(new Thickness(8, 5), bar.Margin); // :155-156
        Assert.Equal(1, bar.Height);
        Assert.Equal(2, VisualQuery.Descendants<PopoverItem>(control).Count());
        window.Close();
    });

    // ---- PR-SIDEBAR-104: the three RowAction timings ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-104")] // UI/Chrome/Popovers.swift:269-273 a plain popover row closes first, then runs action() one turn later
    public void A_plain_popover_row_closes_first_and_runs_its_action_one_main_actor_turn_later() => headless.OnUiThread(() =>
    {
        var ran = false;
        var closed = false;
        var item = new PopoverItem("Rename", () => ran = true);
        Popovers.SetClosePopover(item, () => closed = true);
        var window = Show(item);
        var button = Assert.Single(SelfAndDescendants(item), c => c is Button);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(closed); // :271 close runs synchronously, before the deferred action
        Assert.False(ran); // :273 Task { @MainActor in action() }: not yet run

        Dispatcher.UIThread.RunJobs(); // the deferred main-actor turn
        Assert.True(ran);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-104")] // UI/Chrome/Popovers.swift:258-268 a confirming row runs action() at once, then closes after a hold
    public void A_confirming_row_runs_its_action_at_once_and_closes_after_the_confirmation_hold() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // the checkmark's snappy(0.22) swap animates (Popovers.swift:263)
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var ran = false;
        var closed = false;
        var item = new PopoverItem("Copy", () => ran = true, confirmation: "Copied");
        Popovers.SetClosePopover(item, () => closed = true);
        var window = Show(item);
        var button = Assert.Single(SelfAndDescendants(item), c => c is Button);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(ran); // :262 action() runs synchronously, in place
        Assert.True(item.IsConfirmed); // :263 isConfirmed = true
        Assert.False(closed); // :264-267 the close is deferred behind the hold

        Dispatcher.UIThread.RunJobs(); // starts the spawned Task up to its first await, arming the sleep timer
        time.Advance(TimeSpan.FromMilliseconds(649));
        Dispatcher.UIThread.RunJobs();
        Assert.False(closed); // :214 confirmationHold = 650ms: not yet due

        time.Advance(TimeSpan.FromMilliseconds(2));
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed); // :265-266 the hold elapsed, closePopoverBox?.close() runs
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-104")] // UI/Chrome/Popovers.swift:365-382 a context-menu row runs item.action() synchronously
    public void A_context_menu_row_runs_its_action_synchronously_inside_the_click_callback() => headless.OnUiThread(() =>
    {
        var ran = false;
        var actions = new List<RowAction> { new() { Title = "Rename", Action = () => ran = true } };
        var menu = new RowActionMenuButtons(actions);
        var menuItem = Assert.IsType<MenuItem>(Assert.Single(menu.Items));

        menuItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); // :372 item.action()
        Assert.True(ran); // synchronous: no Dispatcher.UIThread.RunJobs() needed
    });
}
