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

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :692-773 (the context ring): the blitz-composerview unit's
// proofs for UI-COMPOSER-013 and UI-USAGE-001, 002.
public sealed partial class ComposerViewTests
{
    private static double EffectiveOpacity(Visual control, Visual root)
    {
        var opacity = 1.0;
        for (Visual? at = control; at is not null && !ReferenceEquals(at, root); at = at.GetVisualParent()) opacity *= at.Opacity;
        return opacity;
    }

    private static bool Reaches(Visual? hit, Control target) => hit is not null && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target));

    private static void Elapse(ThreadRuntimePart12Harness h, ManualFrameSource frames, AvaloniaWindow window, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds));
        h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(seconds)));
        Pump(h, window);
    }

    // ---- UI-COMPOSER-013 / UI-USAGE-001 (:713, :717-736): the ring ----

    [Theory]
    [InlineData(true)] // the pop-out button: inside withAnimation(Chrome.panelSlide)
    [InlineData(false)] // the Settings switch writes with no transaction of its own
    [Trait("Req", "UI-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:709-713; VIDEO settings.usage-panel-switch-live S 36.340-38.950, usage.pop-out C 56.915-57.307
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:709-713
    [Trait("Rule", "R-TEST-4")] // The ring's geometry, colours, labels and hit area: the B3 ring tests and the tests below
    public void The_ring_fades_out_when_the_usage_panel_pops_out_and_back_in_when_it_is_dismissed_and_takes_no_click_while_leaving(bool inPanelSlide)
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            var host = Host(ring.Window);
            var button = h.Main.OnMain(() => One<Button>(ring.Meter));
            Assert.Equal(1, h.Main.OnMain(() => EffectiveOpacity(button, host))); // a ring that is there from the first render does not fade in
            void Write(bool popped)
            {
                if (inPanelSlide) h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => h.App.Settings.ShowsUsagePanel = popped));
                else h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = popped);
                Pump(h, ring.Window);
            }
            Write(true);
            Assert.Equal(1, h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<Button>(ring.Meter).Count())); // still there, mid-fade
            Elapse(h, frames, ring.Window, 0.06);
            var leaving = h.Main.OnMain(() => EffectiveOpacity(button, host));
            Assert.InRange(leaving, 0.05, 0.95);
            Assert.False(h.Main.OnMain(() => button.IsHitTestVisible)); // a view on its way out takes no click
            Elapse(h, frames, ring.Window, 2);
            Assert.Empty(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<Button>(ring.Meter).ToArray()));
            Write(false);
            Elapse(h, frames, ring.Window, 0.06);
            var arriving = h.Main.OnMain(() => One<Button>(ring.Meter));
            var rising = h.Main.OnMain(() => EffectiveOpacity(arriving, host));
            Assert.InRange(rising, 0.05, 0.95);
            Elapse(h, frames, ring.Window, 2);
            Assert.Equal(1, h.Main.OnMain(() => EffectiveOpacity(arriving, host)));
            Assert.True(h.Main.OnMain(() => arriving.IsHitTestVisible));
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:713, :737
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:713, :737
    [Trait("Rule", "R-TEST-4")] // The fade itself: The_ring_fades_out_when_the_usage_panel_pops_out_...
    public void A_ring_that_leaves_with_its_popover_open_closes_the_popover_and_the_returned_ring_opens_it_again()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            OpenPopover(ring, host: false);
            Assert.Single(ring.Presenter.Shown());
            h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => h.App.Settings.ShowsUsagePanel = true));
            Pump(h, ring.Window);
            Assert.Empty(ring.Presenter.Shown()); // the popover closes with the ring's going, not when its fade ends
            Elapse(h, frames, ring.Window, 2);
            h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => h.App.Settings.ShowsUsagePanel = false));
            Pump(h, ring.Window);
            Elapse(h, frames, ring.Window, 2);
            h.Main.OnMain(() => Click(One<Button>(ring.Meter)));
            Pump(h, ring.Window);
            Assert.Single(ring.Presenter.Shown()); // the returned ring opens its popover again
        });
    }

    // What the ring draws at points on its centre line, clockwise from twelve o'clock (angles in degrees), over a white ground: whether the
    // pixel is the tint (the arc) rather than grey (the quaternary track), and its brightness.
    private static readonly int[] RingAngles = [20, 70, 110, 160, 200, 250, 290, 340];

    private static (bool Tint, double Level)[] RingSamples(ThreadRuntimePart12Harness h, ContextMeter meter, AvaloniaWindow window)
    {
        h.Main.OnMain(() => Host(window).Background = Brushes.White);
        Pump(h, window);
        var ringGrid = h.Main.OnMain(() => Assert.IsType<Grid>(One<Button>(meter).Content));
        var centre = h.Main.OnMain(() => Frame(ringGrid, window).Center);
        const double radius = 7.5;
        var points = RingAngles.Select(angle =>
        {
            var radians = angle * Math.PI / 180;
            return ((int)Math.Floor(centre.X + radius * Math.Sin(radians)), (int)Math.Floor(centre.Y - radius * Math.Cos(radians)));
        }).ToArray();
        var hues = h.Main.OnMain(() => FramePixels.Hues(window, points));
        var levels = h.Main.OnMain(() => FramePixels.Levels(window, points));
        return hues.Select((hue, index) => (hue.Green < 0.6, levels[index])).ToArray();
    }

    private static string Describe((bool Tint, double Level)[] samples) =>
        string.Join(" ", samples.Select((sample, index) => $"{RingAngles[index]}:{(sample.Tint ? "tint" : "grey")}/{sample.Level:0.00}"));

    private static void AssertRing((bool Tint, double Level)[] samples, int tintedUpTo)
    {
        for (var index = 0; index < RingAngles.Length; index++)
        {
            var wanted = RingAngles[index] < tintedUpTo;
            Assert.True(samples[index].Tint == wanted, $"at {RingAngles[index]} degrees the ring should be {(wanted ? "the tint" : "the track")}: {Describe(samples)}");
            if (!wanted) Assert.True(samples[index].Level < 0.97, $"the track runs the whole way round (at {RingAngles[index]}): {Describe(samples)}");
        }
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:718-723
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:718-723
    [Trait("Rule", "R-TEST-4")] // Colours, widths, round cap, rotation and warning threshold: the B3 ring tests
    public void The_arc_sweeps_clockwise_from_twelve_oclock_a_quarter_ends_at_three_a_ring_with_no_fraction_draws_only_the_track()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            // No usage reported: the plan's limits alone give the ring, and only its track is drawn.
            using (var ring = RingOnScreen(h, usage: false)) AssertRing(RingSamples(h, ring.Meter, ring.Window), tintedUpTo: 0);
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 250, WindowTokens = 1000 })));
            using (var ring = RingOnScreen(h, usage: false)) AssertRing(RingSamples(h, ring.Meter, ring.Window), tintedUpTo: 90);
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 500, WindowTokens = 1000 })));
            using (var ring = RingOnScreen(h, usage: false)) AssertRing(RingSamples(h, ring.Meter, ring.Window), tintedUpTo: 180);
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 750, WindowTokens = 1000 })));
            using (var ring = RingOnScreen(h, usage: false)) AssertRing(RingSamples(h, ring.Meter, ring.Window), tintedUpTo: 270);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:727-737 (the popover is the ring's, and shows no change on the ring)
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:704-736
    [Trait("Rule", "R-TEST-4")] // The ring's own states: the tests above and the B3 ring tests
    public void Opening_the_usage_popover_changes_nothing_on_the_ring_and_the_padded_rectangle_is_the_hit_area()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var ring = RingOnScreen(h);
            (Color Track, Color Arc, Rect ArcBounds, double Opacity) Look() => h.Main.OnMain(() =>
            {
                var grid = Assert.IsType<Grid>(One<Button>(ring.Meter).Content);
                var track = Assert.IsType<Avalonia.Controls.Shapes.Path>(grid.Children[0]);
                var arc = Assert.IsType<Avalonia.Controls.Shapes.Path>(grid.Children[1]);
                return (Assert.IsAssignableFrom<ISolidColorBrush>(track.Stroke).Color, Assert.IsAssignableFrom<ISolidColorBrush>(arc.Stroke).Color, arc.Data!.Bounds, EffectiveOpacity(grid, Host(ring.Window)));
            });
            var before = Look();
            var pixelsBefore = RingSamples(h, ring.Meter, ring.Window);
            OpenPopover(ring, host: false);
            Assert.Equal(before, Look());
            Assert.Equal(pixelsBefore, RingSamples(h, ring.Meter, ring.Window));
            // contentShape(.rect): the padded rectangle (6 / 5 around the 15 point ring) is the button, one point beyond it is not.
            h.Main.OnMain(() =>
            {
                var button = One<Button>(ring.Meter);
                var frame = Frame(button, ring.Window);
                Assert.Equal(new Size(27, 25), frame.Size);
                Assert.True(Reaches(ring.Window.InputHitTest(new Point(frame.X + 0.5, frame.Y + 0.5)) as Visual, button), "the padded corner is the button");
                Assert.True(Reaches(ring.Window.InputHitTest(new Point(frame.Right - 0.5, frame.Bottom - 0.5)) as Visual, button), "so is the far corner");
                Assert.False(Reaches(ring.Window.InputHitTest(new Point(frame.Right + 1.5, frame.Center.Y)) as Visual, button), "one point to the right is not");
                Assert.False(Reaches(ring.Window.InputHitTest(new Point(frame.Center.X, frame.Y - 1.5)) as Visual, button), "nor one above");
            });
        });
    }

    // ---- UI-USAGE-002 (:732-736, :764-772): the tooltip shows on a hover dwell ----

    [Theory]
    [InlineData(ProviderKind.Codex, 859, false, "85% of the context window used")]
    [InlineData(ProviderKind.Codex, -1, false, "Usage limits")]
    [InlineData(ProviderKind.Deepseek, -1, true, "Remaining credits")]
    [Trait("Req", "UI-USAGE-002")] // App/Views/Composer/ComposerView.swift:732, :764-772
    [Trait("Rule", "R-TEST-4")] // The strings, the label and the read-only value: B3_context_accessibility_keeps_label_and_read_only_value_separate
    public void Hovering_the_ring_for_the_dwell_opens_its_tooltip_with_the_help_text(ProviderKind provider, int used, bool credits, string tip)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                if (credits) h.App.Settings.SetAPIKeyInput("fixture-only", provider);
                if (used >= 0) h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = 1000 }));
            });
            var meter = h.Main.OnMain(() => new ContextMeter(h.Runtime, provider, null));
            var window = h.Main.OnMain(() =>
            {
                var holder = new Border { Child = meter, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                AppEnvironment.SetAppModel(holder, h.App);
                var tipWindow = new AvaloniaWindow { Content = holder, Width = 1400, Height = 900 };
                SettingsProviderPopupLayers.Install(tipWindow); // a real overlay layer for the real tooltip
                tipWindow.Show();
                Settle(tipWindow);
                return tipWindow;
            });
            Pump(h, window);
            var button = h.Main.OnMain(() => One<Button>(meter));
            h.Main.OnMain(() =>
            {
                button.SetValue(ToolTip.ShowDelayProperty, 0); // the macOS help-tag delay is in neither the source nor the video; the dwell itself is the proof
                Assert.False(ToolTip.GetIsOpen(button));
                var opened = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                using var watch = ToolTip.ToolTipOpeningEvent.AddClassHandler(typeof(Control), (sender, _) =>
                {
                    if (ReferenceEquals(sender, button)) opened.TrySetResult();
                }, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
                try
                {
                    window.MouseMove(new Point(1, 1));
                    Settle(window);
                    var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    Assert.True(Reaches(window.InputHitTest(at) as Visual, button));
                    window.MouseMove(at);
                    Settle(window);
                    HeadlessSession.RunDispatcherUntil(opened.Task, TimeSpan.FromSeconds(5));
                    Settle(window);
                    Assert.True(ToolTip.GetIsOpen(button));
                    Assert.Equal(tip, ToolTip.GetTip(button));
                }
                finally
                {
                    ToolTip.SetIsOpen(button, false);
                    window.MouseMove(new Point(1, 1));
                    Settle(window);
                }
            });
            Close(h, window);
        });
    }
}
