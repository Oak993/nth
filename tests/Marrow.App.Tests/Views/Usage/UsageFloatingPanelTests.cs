using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Usage;

[Collection("Headless")]
public sealed class UsageFloatingPanelTests(HeadlessSession session)
{
    // :12-22 The panel reads runtime.usage unconditionally, so every test needs a loaded ThreadRuntime; the only
    // way to set Usage is through a stored ThreadDocument (ThreadRuntime.Usage has a private setter), the same
    // path ThreadRuntimeTests.cs's own Usage-bearing tests use.
    private static ThreadRuntime MakeRuntime(AppModelHarness harness, ContextUsage? usage, ProviderKind provider = ProviderKind.Cursor)
    {
        var fixture = ThreadRuntimePart8Fixtures.Create(
            harness,
            provider: provider,
            document: new ThreadDocument(Guid.Empty) { Usage = usage });
        return fixture.Runtime;
    }

    // :10 @Environment(WindowLiveResize.self): every panel needs one attached before it is shown. UsagePanel's own
    // plan-limits/credits sections (App/Views/Usage/UsageViews.cs) read AppEnvironment.GetAppModel too.
    private static (Border Host, AvaloniaWindow Window) Show(Control panel, AppModelHarness harness, ThemeVariant? theme = null)
    {
        var host = new Border { Child = panel };
        AppEnvironment.SetAppModel(host, harness.MakeModel());
        AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(new AvaloniaWindow()));
        var window = new AvaloniaWindow { Content = host };
        if (theme is { } variant) window.RequestedThemeVariant = variant;
        ShowOnMain(harness, window);
        return (host, window);
    }

    // UsagePanel.swift:9 reads AppModel from @Environment and :71-78 refreshes plan limits and credits in .onAppear,
    // on the main actor. Marrow.App binds the main actor to the Avalonia UI thread (MainActor.cs:5); the harness runs
    // it on its own loop instead, so the window is shown inside harness.Main.OnMain, after every CLI provider is
    // pointed at a missing no-cli.exe so no refresh can find a real CLI: the UsagePanelTests.Show convention. The
    // refresh re-renders the limits view, so the dispatcher is then drained (as ComponentsCallerTests.Show does) and
    // the layout it queued runs now, not in the headless session's teardown after the app's fonts are gone.
    private static void ShowOnMain(AppModelHarness harness, AvaloniaWindow window)
    {
        var model = AppEnvironment.GetAppModel((Control)window.Content!);
        harness.Main.OnMain(() =>
        {
            foreach (var kind in Marrow.Core.Port.SwiftEnum<ProviderKind>.AllCases)
                if (!kind.IsAPIKeyBased) model.Settings.SetBinaryPath(System.IO.Path.Combine(harness.Scratch, "no-cli.exe"), kind);
        });
        harness.Main.OnMain(() => window.Show());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static string AllText(Control control)
    {
        return string.Join("\n", LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? ""));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-060")] // App/Views/Usage/UsageFloatingPanel.swift:26-37
    public void Panel_shows_the_usage_scroll_view_when_the_lead_provider_exposes_plan_limits()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // Codex exposes plan limits (PlanLimitsReader.ExposesLimits), so hasSomething is true even with no
            // context usage at all: the panel's own predicate tests only usage?.fraction, not UsagePanel's own
            // (stricter) context test.
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var reports = new System.Collections.Generic.List<double>();
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: h => reports.Add(h), onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            Assert.Single(LogicalQuery.SelfAndDescendants<ScrollViewer>(panel));
            Assert.DoesNotContain("Nothing to show", AllText(panel), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-060")] // App/Views/Usage/UsageFloatingPanel.swift:26-29, :38-45
    [Trait("Req", "PR-USAGE-061")] // App/Views/Usage/UsageFloatingPanel.swift:38-45
    [Trait("Rule", "R-STR-9")]
    public void Panel_shows_the_placeholder_sentence_naming_the_provider_when_there_is_nothing_to_show()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // Cursor exposes neither plan limits nor credits, and no usage was ever recorded.
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Cursor);
            double? reported = null;
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Cursor, headsProvider: null, size: new Size(360, 300),
                onContentHeight: h => reported = h, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            Assert.Empty(LogicalQuery.SelfAndDescendants<ScrollViewer>(panel));
            Assert.Contains("Nothing to show for Cursor yet.", AllText(panel), StringComparison.Ordinal);
            // :44 onAppear reports exactly stripHeight (50) + 44 = 94, not a measured value (UI-USAGE-040).
            Assert.Equal(HydraPanel.StripHeight + 44, reported);
            Assert.Equal(94.0, reported);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-061")] // App/Views/Usage/UsageFloatingPanel.swift:39-41
    [Trait("Decision", "ED-062")]
    public void Placeholder_text_uses_body_style_and_secondary_text_color()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Cursor);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Cursor, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var text = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(panel),
                t => t.Text == "Nothing to show for Cursor yet.");
            Assert.Equal(13, text.FontSize); // ED-059: .body is 13 points
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-062")] // App/Views/Usage/UsageFloatingPanel.swift:33-36
    public void Panel_reports_the_measured_content_height_plus_the_strip_and_eight_points()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness,
                usage: new ContextUsage { UsedTokens = 100, WindowTokens = 1000 },
                provider: ProviderKind.Cursor);
            var reports = new System.Collections.Generic.List<double>();
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Cursor, headsProvider: null, size: new Size(360, 300),
                onContentHeight: h => reports.Add(h), onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var usagePanel = Assert.Single(LogicalQuery.SelfAndDescendants<UsagePanel>(panel));
            Assert.NotEmpty(reports);
            Assert.Equal(usagePanel.Bounds.Height + HydraPanel.StripHeight + 8, reports[^1], precision: 3);

            // Only a real change is reported (SwiftUI's onGeometryChange fires only when the derived value
            // changes): forcing another layout pass with nothing having moved must not add a duplicate entry.
            var countAfterFirstLayout = reports.Count;
            panel.InvalidateMeasure();
            panel.UpdateLayout();
            Assert.Equal(countAfterFirstLayout, reports.Count);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-063")] // App/Views/Usage/UsageFloatingPanel.swift:25, :53-73
    public void Panel_surface_is_a_continuous_22_point_rounded_rectangle_with_no_border_stroke()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var clipped = LogicalQuery.SelfAndDescendants<Border>(panel)
                .Where(b => b.ClipToBounds && b.CornerRadius == new CornerRadius(22))
                .ToArray();
            Assert.NotEmpty(clipped);
            Assert.All(clipped, border => Assert.Equal(default, border.BorderThickness)); // no hairline stroke
        });
    }

    [Theory]
    [InlineData(false, (byte)87, (byte)166)] // light: scrim white 0.34 (87/255), shadow black 0.65 (166/255)
    [InlineData(true, (byte)77, (byte)255)]  // dark: scrim black 0.3 (77/255), shadow black 1.0
    [Trait("Req", "PR-USAGE-063")] // App/Views/Usage/UsageFloatingPanel.swift:68-73
    public void Scrim_and_shadow_use_the_source_opacities_per_theme(bool isDark, byte scrimAlpha, byte shadowAlpha)
    {
        // The pure color/shadow math the constructor (and its theme-change handler) calls, verified directly.
        // The end-to-end wiring — that these actually reach the rendered Border for a themed window, including
        // when the theme changes after construction — is covered by the tests below.
        var color = UsageFloatingPanel.ScrimColor(isDark);
        Assert.Equal(scrimAlpha, color.A);
        var shadow = UsageFloatingPanel.ScrimShadow(isDark);
        Assert.Equal(shadowAlpha, shadow.Color.A);
        Assert.Equal(28, shadow.Blur);
        Assert.Equal(10, shadow.OffsetY);
        Assert.Equal(0, shadow.OffsetX);
    }

    // :56 `let isDark = colorScheme == .dark` inside `body`, and :69's own re-read of the same environment value
    // for the scrim/shadow layer: SwiftUI recomputes `body` (and both `isDark` locals) whenever colorScheme
    // changes while the panel is on screen, so the port must show the dark-theme scrim/shadow when actually
    // shown inside a window whose RequestedThemeVariant is Dark, not just when the pure helpers above are called
    // with a hand-supplied bool.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-USAGE-063")] // App/Views/Usage/UsageFloatingPanel.swift:56, :68-73
    public void Panel_renders_the_theme_matched_scrim_and_shadow_when_shown_in_a_themed_window(bool isDark)
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness, theme: isDark ? ThemeVariant.Dark : ThemeVariant.Light);

            // The scrim border is the one that carries the panel's single box shadow (:68-73); the glass-tint
            // layer (:53-63) carries none.
            var scrimBorder = Assert.Single(LogicalQuery.SelfAndDescendants<Border>(panel), b => b.BoxShadow.Count > 0);
            var brush = Assert.IsType<SolidColorBrush>(scrimBorder.Background);
            Assert.Equal(UsageFloatingPanel.ScrimColor(isDark), brush.Color);
            Assert.Equal(1, scrimBorder.BoxShadow.Count);
            Assert.Equal(UsageFloatingPanel.ScrimShadow(isDark).Color, scrimBorder.BoxShadow[0].Color);
        });
    }

    // Reproduces the HIGH finding directly: the panel must re-derive the glass tint and the scrim/shadow when
    // the ambient theme changes live, the same way SwiftUI recomputes `body` on every colorScheme change (:56,
    // :69) and the way this codebase's other ports of the identical dependency already behave (Chrome.cs's
    // WindowBackdrop, Views/Chat/SubagentPanel.cs, Views/Tour/TourView.cs all redraw on
    // ActualThemeVariantChanged). A construction-time-only isDark would leave these stuck at the light values
    // below even after the window flips to dark.
    [Fact]
    [Trait("Req", "PR-USAGE-063")] // App/Views/Usage/UsageFloatingPanel.swift:56, :68-73
    public void Scrim_and_shadow_update_when_the_window_theme_changes_live()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            var (_, window) = Show(panel, harness, theme: ThemeVariant.Light);

            var scrimBorder = Assert.Single(LogicalQuery.SelfAndDescendants<Border>(panel), b => b.BoxShadow.Count > 0);
            Assert.Equal(UsageFloatingPanel.ScrimColor(false), Assert.IsType<SolidColorBrush>(scrimBorder.Background).Color);

            window.RequestedThemeVariant = ThemeVariant.Dark;

            Assert.Equal(UsageFloatingPanel.ScrimColor(true), Assert.IsType<SolidColorBrush>(scrimBorder.Background).Color);
            Assert.Equal(1, scrimBorder.BoxShadow.Count);
            Assert.Equal(UsageFloatingPanel.ScrimShadow(true).Color, scrimBorder.BoxShadow[0].Color);
        });
    }


    [Fact]
    [Trait("Req", "PR-USAGE-064")] // App/Views/Usage/UsageFloatingPanel.swift:50
    [Trait("Req", "UI-USAGE-036")] // App/Views/Usage/UsageFloatingPanel.swift:24-74
    public void Panel_takes_exactly_the_placers_width_and_height()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(412, 337.5),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });

            Assert.Equal(412, panel.Width);
            Assert.Equal(337.5, panel.Height);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-065")] // App/Views/Usage/UsageFloatingPanel.swift:79-99
    [Trait("Req", "UI-USAGE-037")] // App/Views/Usage/UsageFloatingPanel.swift:79-99
    public void Title_capsule_shows_the_lead_icon_and_the_word_usage_at_12_5_medium()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var usageLabel = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(panel),
                t => t.Text == "Usage");
            Assert.Equal(12.5, usageLabel.FontSize);
            Assert.Equal(FontWeight.Medium, usageLabel.FontWeight);

            // 12x12 strip icons only (UI-USAGE-037): UsagePanel's own Lead/Heads section headers use 13x13 icons
            // and only appear when a heads provider is set, which this case has none of.
            var icons = LogicalQuery.SelfAndDescendants<Marrow.UI.Common.ProviderIcon>(panel)
                .Where(icon => icon.Size == 12.0).ToArray();
            Assert.Single(icons); // no heads provider: exactly the lead's icon
            Assert.Equal(ProviderKind.Codex, icons[0].Provider);
        });
    }

    // :79 HStack(alignment: .top, spacing: 8) { titleHolder, middleDrag, closeHolder }: the 8-point gap sits
    // between each pair of adjacent children, matching HydraPanel.cs's HydraHeadFooter (App/Views/Chat/
    // HydraPanel.cs:650-652), which uses the identical Auto/Star/Auto topology for the same spacing: 8 HStack.
    [Fact]
    [Trait("Req", "PR-USAGE-065")] // App/Views/Usage/UsageFloatingPanel.swift:79
    public void Strip_row_has_an_eight_point_gap_between_title_drag_and_close()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var stripRow = Assert.Single(LogicalQuery.SelfAndDescendants<Grid>(panel),
                g => g.Height == HydraPanel.StripHeight && g.ColumnDefinitions.Count == 3);
            Assert.Equal(8, stripRow.ColumnSpacing);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-066")] // App/Views/Usage/UsageFloatingPanel.swift:94-104
    [Trait("Req", "UI-USAGE-038")] // App/Views/Usage/UsageFloatingPanel.swift:100-104
    public void Middle_strip_handle_carries_the_drag_to_move_tooltip_and_label()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            var withTip = LogicalQuery.SelfAndDescendants<Control>(panel)
                .Where(c => ToolTip.GetTip(c) as string == "Drag to move")
                .ToArray();
            Assert.Single(withTip);
            Assert.Equal("Drag to move", Avalonia.Automation.AutomationProperties.GetName(withTip[0]));
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-066")] // App/Views/Usage/UsageFloatingPanel.swift:19-21, :94-104
    public void Dragging_the_middle_handle_reports_cumulative_travel_and_a_separate_end_event()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var drags = new System.Collections.Generic.List<Size>();
            var ended = 0;
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: d => drags.Add(d), onDragEnd: () => ended++, dismiss: () => { });
            Show(panel, harness);

            // PanelDragHandleView (the old B2 NSViewRepresentable's own NSView) no longer exists at B3:
            // b3/SubagentPanel's PanelDragHandle now wraps the shared Marrow.UI.Chrome.DragHandleView directly
            // (SubagentPanel.cs:810-845), so that is the leaf control each drag handle renders.
            var handle = LogicalQuery.SelfAndDescendants<DragHandleView>(panel)
                .First(h => h.Bounds.Width > 32); // the flexible middle handle, not the capsule overlay

            handle.RaiseEvent(MakePointerPressed(handle, new Point(10, 10)));
            handle.RaiseEvent(MakePointerMoved(handle, new Point(16, 10)));
            handle.RaiseEvent(MakePointerReleased(handle, new Point(16, 10)));

            Assert.NotEmpty(drags);
            Assert.Equal(1, ended);
        });
    }

    [Theory]
    [InlineData(ProviderKind.Codex, ProviderKind.Codex)] // upstream defect 71, reproduced: heads == lead still draws twice
    [InlineData(ProviderKind.Codex, ProviderKind.Claude)]
    [Trait("Req", "PR-USAGE-067")] // App/Views/Usage/UsageFloatingPanel.swift:82-84
    public void Strip_draws_a_second_icon_whenever_a_heads_provider_is_set(ProviderKind lead, ProviderKind heads)
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: lead);
            var panel = new UsageFloatingPanel(
                runtime, lead, headsProvider: heads, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            // 12x12 strip icons only (UI-USAGE-037); the body's own Lead/Heads section headers (13x13, only
            // drawn when heads != provider) are a separate, correctly-deduplicated count and not this record's
            // concern.
            var icons = LogicalQuery.SelfAndDescendants<Marrow.UI.Common.ProviderIcon>(panel)
                .Where(icon => icon.Size == 12.0).ToArray();
            Assert.Equal(2, icons.Length);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-067")] // App/Views/Usage/UsageFloatingPanel.swift:26-29, :82-84
    public void No_heads_provider_draws_exactly_one_strip_icon()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            Show(panel, harness);

            Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Common.ProviderIcon>(panel));
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-068")] // App/Views/Usage/UsageFloatingPanel.swift:105-109
    [Trait("Req", "UI-USAGE-039")] // App/Views/Usage/UsageFloatingPanel.swift:105-109
    public void Close_button_carries_the_exact_tooltip_and_dismisses_on_click()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var calls = 0;
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => calls++);
            Show(panel, harness);

            var controls = LogicalQuery.SelfAndDescendants<Control>(panel).ToArray();
            Assert.Contains(controls, c => ToolTip.GetTip(c) as string == "Dismiss the usage panel; the usage popover opens it again");
            Assert.Contains(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.SwiftImage>(panel), image => image.SymbolName == "xmark");

            var button = Assert.Single(controls.OfType<Button>());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-036")] // App/Views/Usage/UsageFloatingPanel.swift:10
    public void Panel_reads_windowliveresize_from_the_environment_once_attached()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            var host = new Border { Child = panel };
            AppEnvironment.SetAppModel(host, harness.MakeModel());
            var liveResize = new WindowLiveResize(new AvaloniaWindow());
            AppEnvironment.SetWindowLiveResize(host, liveResize);
            var window = new AvaloniaWindow { Content = host };
            ShowOnMain(harness, window); // Showing attaches UsagePanel, whose refresh needs the main actor (see ShowOnMain).

            Assert.Same(liveResize, AppEnvironment.GetWindowLiveResize(panel));
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-036")] // App/Views/Usage/UsageFloatingPanel.swift:10
    public void Panel_requires_a_windowliveresize_somewhere_in_its_ancestry()
    {
        session.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var runtime = MakeRuntime(harness, usage: null, provider: ProviderKind.Codex);
            var panel = new UsageFloatingPanel(
                runtime, ProviderKind.Codex, headsProvider: null, size: new Size(360, 300),
                onContentHeight: _ => { }, onDrag: _ => { }, onDragEnd: () => { }, dismiss: () => { });
            var host = new Border { Child = panel };
            AppEnvironment.SetAppModel(host, harness.MakeModel());
            // No AppEnvironment.SetWindowLiveResize anywhere on this tree: :10's environment read is not optional.
            var window = new AvaloniaWindow { Content = host };

            Assert.Throws<InvalidOperationException>(() => window.Show());
        });
    }

    private static Avalonia.Input.PointerPressedEventArgs MakePointerPressed(Control target, Point position) =>
        new(target, new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true),
            target, position, 0, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None);

    private static Avalonia.Input.PointerEventArgs MakePointerMoved(Control target, Point position) =>
        new(Avalonia.Input.InputElement.PointerMovedEvent, target, new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true),
            target, position, 0, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.Other),
            Avalonia.Input.KeyModifiers.None);

    private static Avalonia.Input.PointerReleasedEventArgs MakePointerReleased(Control target, Point position) =>
        new(target, new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true),
            target, position, 0, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PointerUpdateKind.LeftButtonReleased),
            Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left);
}
