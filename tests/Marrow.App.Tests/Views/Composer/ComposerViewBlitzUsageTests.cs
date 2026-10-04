using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :692-773 (the context ring, its usage popover and the pop-out
// button) and DroppyCode/UI/Chrome/Chrome.swift :412-437 (ChromeCircleButton): the blitz-composerview unit's proofs for
// UI-COMPOSER-013, 014, 015 and UI-USAGE-001 to 004, over the existing B3 tests of the same records.
public sealed partial class ComposerViewTests
{
    private const string PopOutLabel = "Open as a floating panel beside the chat";

    // The ring on screen with the presenter recording what it is asked to show, then its popover opened by a click.
    private sealed class OpenRing : IDisposable
    {
        public required ThreadRuntimePart12Harness H { get; init; }
        public required ContextMeter Meter { get; init; }
        public required AvaloniaWindow Window { get; init; }
        public required ContentRecordingPresenter Presenter { get; init; }
        public required IDisposable Seam { get; init; }
        public Control? Content { get; set; }
        public AvaloniaWindow? PopupWindow { get; set; }

        public void Dispose()
        {
            H.Main.OnMain(() => { PopupWindow?.Close(); Window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            Seam.Dispose();
        }
    }

    private static OpenRing RingOnScreen(ThreadRuntimePart12Harness h, ProviderKind provider = ProviderKind.Codex, bool usage = true)
    {
        var presenter = new ContentRecordingPresenter();
        var seam = Presentation.Presenter.Override(presenter);
        if (usage) h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 310, WindowTokens = 1000 })));
        var meter = h.Main.OnMain(() => new ContextMeter(h.Runtime, provider, null));
        var window = h.Main.OnMain(() => Show(meter, h.App, 200));
        Pump(h, window);
        return new OpenRing { H = h, Meter = meter, Window = window, Presenter = presenter, Seam = seam };
    }

    // The popover's content handed to a window of its own, which has no model above it (the popup's own window is outside the tree).
    private static void OpenPopover(OpenRing ring, bool host)
    {
        var h = ring.H;
        h.Main.OnMain(() => Click(One<Button>(ring.Meter)));
        Pump(h, ring.Window);
        var (popup, content) = Assert.Single(ring.Presenter.Shown());
        ring.Content = content!;
        if (!host) return;
        popup.Child = null;
        ring.PopupWindow = h.Main.OnMain(() =>
        {
            var holder = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var popupWindow = new AvaloniaWindow { Content = holder, Width = 600, Height = 800 };
            popupWindow.Show();
            Settle(popupWindow);
            return popupWindow;
        });
        Pump(h, ring.PopupWindow);
    }

    // ---- UI-COMPOSER-014 / UI-USAGE-003 (:743-757): the header row's geometry ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-014")] // App/Views/Composer/ComposerView.swift:743-757
    [Trait("Req", "UI-USAGE-003")] // App/Views/Composer/ComposerView.swift:743-746,755-756
    [Trait("Rule", "R-TEST-4")] // The text style, colour, margins and panel width: B3_usage_popover_toggles_and_preserves_header_geometry_and_fixed_panel_width
    public void The_usage_popover_header_row_is_32_high_with_the_title_16_in_the_pop_out_16_from_the_far_edge_and_the_panel_directly_below()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            OpenPopover(ring, host: true);
            h.Main.OnMain(() =>
            {
                var content = ring.Content!;
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(content).Single(t => t.Text == "Usage");
                var header = Assert.IsType<Grid>(title.GetLogicalParent());
                var popOut = One<ChromeCircleButton>(content);
                var panel = One<UsagePanel>(content);
                var frameOfHeader = Frame(header, content);
                Assert.Equal(360, content.Bounds.Width, 3);
                Assert.Equal(32, header.Bounds.Height, 3); // the pop-out button's 32 (Chrome.capsuleHeight) sets the row
                Assert.Equal(10, frameOfHeader.Y, 3); // .padding(.top, 10)
                Assert.Equal(16, frameOfHeader.X, 3); // .padding(.horizontal, 16)
                Assert.Equal(360 - 32, frameOfHeader.Width, 3);
                var titleFrame = Frame(title, content);
                Assert.Equal(16, titleFrame.X, 3); // the title's leading edge at the padding
                Assert.InRange(Math.Abs(frameOfHeader.Center.Y - titleFrame.Center.Y), 0, 0.5); // centred on the row (HStack's default centre alignment), to layout rounding
                Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
                Assert.True(titleFrame.Height < 24, "the title is one line");
                var popOutFrame = Frame(popOut, content);
                Assert.Equal(new Size(32, 32), popOutFrame.Size);
                Assert.Equal(360 - 16, popOutFrame.Right, 3); // the trailing edge at width - 16
                Assert.True(popOutFrame.X - titleFrame.Right >= 8, "a spacer of at least 8 between the title and the button");
                Assert.Equal(frameOfHeader.Bottom, Frame(panel, content).Y, 3); // the panel starts directly below the row
            });
        });
    }

    // ---- UI-COMPOSER-015 / UI-USAGE-004 (:748-753): the pop-out click ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:748-753
    [Trait("Req", "UI-USAGE-004")] // App/Views/Composer/ComposerView.swift:748-753
    [Trait("Rule", "R-TEST-4")] // The button's own look and tooltip: B3_popout_uses_32_point_interactive_glass_... and the next tests
    public void The_pop_out_click_closes_the_popover_then_switches_the_panel_on_inside_panelSlide_and_the_ring_goes()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            OpenPopover(ring, host: false);
            var state = typeof(ContextMeter).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ring.Meter)!;
            var isPresented = state.GetType().GetProperty("IsPresented")!;
            Assert.True((bool)isPresented.GetValue(state)!);
            Animation? atWrite = null;
            bool? popoverOpenAtWrite = null;
            var writes = 0;
            h.Main.OnMain(() =>
            {
                // Swift's willSet: the settings write is observed before it lands, inside the transaction that carries it.
                ObservationTracking.WithTracking(() => h.App.Settings.ShowsUsagePanel, () =>
                {
                    writes++;
                    atWrite = Transaction.Current;
                    popoverOpenAtWrite = (bool)isPresented.GetValue(state)!;
                });
                Assert.False(h.App.Settings.ShowsUsagePanel);
                Click(Named(ring.Content!, PopOutLabel));
            });
            Assert.Equal(1, writes);
            Assert.Equal(Chrome.PanelSlide, atWrite); // withAnimation(Chrome.panelSlide) { showsUsagePanel = true }
            Assert.False(popoverOpenAtWrite); // isPresented = false came first (:749)
            Pump(h, ring.Window);
            Assert.True(h.App.Settings.ShowsUsagePanel);
            Assert.False(ring.Presenter.Calls[^1].Presented); // the popover is dismissed
            Assert.Empty(ring.Presenter.Shown());
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:748-753; the note on the record: dismissing the panel brings the ring back
    [Trait("Req", "UI-USAGE-004")] // App/Views/Composer/ComposerView.swift:748-753
    [Trait("Rule", "R-TEST-4")] // The click itself: The_pop_out_click_closes_the_popover_then_switches_the_panel_on_inside_panelSlide_and_the_ring_goes
    public void Dismissing_the_floating_panel_brings_the_ring_back_and_it_opens_the_popover_again()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            OpenPopover(ring, host: false);
            h.Main.OnMain(() => Click(Named(ring.Content!, PopOutLabel)));
            Pump(h, ring.Window);
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, ring.Window);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(ring.Meter)); // ChatView.swift:523 turns the panel off from its close button
            h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => h.App.Settings.ShowsUsagePanel = false));
            Pump(h, ring.Window);
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, ring.Window);
            Assert.Single(LogicalQuery.SelfAndDescendants<Button>(ring.Meter));
            h.Main.OnMain(() => Click(One<Button>(ring.Meter)));
            Pump(h, ring.Window);
            Assert.Single(ring.Presenter.Shown()); // the popover opens again
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-004")] // App/Views/Composer/ComposerView.swift:748-753; UI/Chrome/Chrome.swift:413-436
    [Trait("Rule", "R-TEST-4")] // The click and the size, glyph, tooltip and hover end values: B3_popout_uses_32_point_interactive_glass_... and the pop-out click test
    public void The_pop_out_button_is_a_circle_that_hits_only_inside_it_with_the_glass_tint_the_arrow_up_right_icon_and_a_hover_that_is_halfway_at_half_the_time()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            OpenPopover(ring, host: true);
            var surface = h.Main.OnMain(() => One<ChromeCircleButton>(ring.Content!));
            h.Main.OnMain(() =>
            {
                var image = One<SwiftImage>(surface);
                Assert.Equal("arrow.up.right", image.SymbolName);
                Assert.Equal("arrow-up-right", image.Mapping!.Lucide); // OD-0010: the published symbol-by-symbol table
                var glass = LogicalQuery.SelfAndDescendants<Control>(surface).Select(SwiftGlass.GetEffect).Single(effect => effect is not null)!;
                Assert.Equal(Chrome.GlassTint, glass.TintColor); // glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive())
                Assert.Equal(0.3, glass.TintOpacity);
                // The circle is the hit shape: the centre reaches the button, the square's corner does not.
                var popOutFrame = Frame(surface, ring.Content!);
                var centre = surface.TranslatePoint(new Point(16, 16), ring.PopupWindow!)!.Value;
                var corner = surface.TranslatePoint(new Point(1, 1), ring.PopupWindow!)!.Value;
                var button = Named(surface, PopOutLabel);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the hit test reads the committed scene
                Settle(ring.PopupWindow!);
                Assert.True(Reaches(ring.PopupWindow!.InputHitTest(centre) as Visual, button), $"the centre {centre} reaches the button: {ring.PopupWindow!.InputHitTest(centre)}; frame {popOutFrame}; window {ring.PopupWindow!.Bounds}");
                Assert.False(Reaches(ring.PopupWindow!.InputHitTest(corner) as Visual, button), $"the corner {corner} does not: {ring.PopupWindow!.InputHitTest(corner)}");
                Assert.Equal(new Size(32, 32), popOutFrame.Size);
                // Hover: the glyph's 0.92 rises toward 1 over Chrome.hover, and is part-way at half the time.
                var rest = Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color;
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(image.ActualThemeVariant), rest);
                var hover = button.GetLogicalParent() as Control ?? throw new Exception("Missing glass wrapper");
                hover.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, hover, null!, hover, default, 0, new PointerPointProperties(), KeyModifiers.None));
            });
            Pump(h, ring.PopupWindow!);
            h.Main.Advance(TimeSpan.FromSeconds(0.05));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.05)));
            Pump(h, ring.PopupWindow!);
            h.Main.OnMain(() =>
            {
                var image = One<SwiftImage>(surface);
                var halfway = Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color;
                var progress = Animation.EaseOut(duration: 0.1).Progress(0.05);
                var expected = Chrome.PrimaryText.Opacity(0.92 + 0.08 * progress).Resolve(image.ActualThemeVariant);
                Assert.InRange(progress, 0.2, 0.95);
                Assert.True(Math.Abs(halfway.A - expected.A) <= 2, $"alpha {halfway.A} vs {expected.A}");
                Assert.NotEqual(Chrome.PrimaryText.Opacity(0.92).Resolve(image.ActualThemeVariant).A, halfway.A);
            });
        });
    }
}
