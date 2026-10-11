using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-segmenthover-1 (owner 2026-10-11: "Hovering mouse over these interacts with the whole pills whereas it should also interact with
// the icons within when hovered over them? Maybe they light up a bit?"). A glass pill of icon buttons keeps its whole-pill hover and each
// segment now also lights on its own: its glyph eases from the secondary colour toward the primary one and a soft plate appears behind just
// that segment, a little stronger while it is pressed, all on Chrome.Hover; moving to the next segment crossfades the two.
[Collection("Headless")]
public sealed class ChromeSegmentHoverTests(HeadlessSession headless)
{
    // The label colour is not fully opaque in the headless theme, so the alphas are taken from it: the primary glyph is its alpha, the
    // resting one 0.92 of it (the Mac's, ED-NEW-segmenthover-1 item 1), and a plate is the stated share of it.
    private static (double rest, double lit, double plateHover, double platePressed) Alphas(AppModelHarness h, AvaloniaWindow window)
    {
        var lit = h.Main.OnMain(() => (double)Chrome.PrimaryText.Resolve(window.ActualThemeVariant).A);
        return (lit * 0.92, lit, lit * 0.1, lit * 0.18);
    }

    // Both ways the app builds a pill: the icon buttons, and the menu buttons exactly as ChatView does for [open in | run | git]
    // (OpenInMenu, ScriptsMenu and GitActionsMenu each wrap a ChromeMenuButton; their menu content is built only when the popover opens).
    private static Control NewSegment(bool menus, string symbol, string tip) =>
        menus ? new ChromeMenuButton(symbol, tip, () => new Border()) : new ChromeIconButton(symbol, tip, () => { });

    private static (ChromeCapsule Pill, AvaloniaWindow Window) MakePill(AppModelHarness h, bool menus = false) => h.Main.OnMain(() =>
    {
        var pill = new ChromeCapsule(() => new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Children =
            {
                NewSegment(menus, "arrow.up.forward.app", "Open in another app"),
                new ChromeDivider(),
                NewSegment(menus, "play", "Run a project script"),
                new ChromeDivider(),
                NewSegment(menus, "arrow.triangle.branch", "Commit, push and open pull requests"),
            },
        }) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(40) };
        var window = new AvaloniaWindow { Content = pill, Width = 400, Height = 200 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        return (pill, window);
    });

    private static void Step(AppModelHarness h, AvaloniaWindow window, ManualTimeProvider time, ManualFrameSource frames, int milliseconds)
    {
        time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        h.Main.OnMain(() => window.UpdateLayout());
        h.Main.RunUntilIdle();
    }

    private static Control Segment(AppModelHarness h, Control root, string tip) => h.Main.OnMain(() =>
        LogicalQuery.SelfAndDescendants<Control>(root).Where(control => control is ChromeIconButton or ChromeMenuButton)
            .Single(segment => Equals(ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(segment).Single()), tip)));

    // The segment's drawn look: the alpha of its glyph's colour and of its plate's fill (0 to 255).
    private static (double Glyph, double Plate) Look(AppModelHarness h, Control segment) => h.Main.OnMain(() =>
    {
        var glyph = (ISolidColorBrush)TextElement.GetForeground(LogicalQuery.SelfAndDescendants<SwiftImage>(segment).Single())!;
        var plate = (ISolidColorBrush)LogicalQuery.SelfAndDescendants<Border>(segment).Single(border => border.Name == "SegmentPlate").Background!;
        return ((double)glyph.Color.A, (double)plate.Color.A);
    });

    private static void AssertRgb(AppModelHarness h, AvaloniaWindow window, Control segment) => h.Main.OnMain(() =>
    {
        var theme = window.ActualThemeVariant;
        var primary = Chrome.PrimaryText.Resolve(theme);
        var glyph = ((ISolidColorBrush)TextElement.GetForeground(LogicalQuery.SelfAndDescendants<SwiftImage>(segment).Single())!).Color;
        var plate = ((ISolidColorBrush)LogicalQuery.SelfAndDescendants<Border>(segment).Single(border => border.Name == "SegmentPlate").Background!).Color;
        Assert.Equal((primary.R, primary.G, primary.B), (glyph.R, glyph.G, glyph.B));
        var overlay = Chrome.Overlay(Chrome.SegmentPlateHover).Resolve(theme);
        Assert.Equal((overlay.R, overlay.G, overlay.B), (plate.R, plate.G, plate.B));
    });

    private static Point Centre(AppModelHarness h, Control control, AvaloniaWindow window) => h.Main.OnMain(() =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-segmenthover-1; Chrome.swift:361-383 (ChromeIconButton), :385-409 (ChromeMenuButton), :333-359 (ChromeIconLabel)
    public void Hovering_one_segment_of_a_pill_lights_only_that_segment_and_leaving_puts_it_back(bool menus)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (pill, window) = MakePill(h, menus);
            var (Rest, Lit, PlateHover, PlatePressed) = Alphas(h, window);
            var open = Segment(h, pill, "Open in another app");
            var run = Segment(h, pill, "Run a project script");
            void Move(Point at) { h.Main.OnMain(() => window.MouseMove(at, RawInputModifiers.None)); Step(h, window, time, frames, 0); }
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 300);

            // At rest neither segment is lit: secondary glyphs and no plates.
            foreach (var segment in new[] { open, run })
            {
                var rest = Look(h, segment);
                Assert.Equal(Rest, rest.Glyph, 1.5);
                Assert.Equal(0, rest.Plate, 0.5);
            }

            // The pointer on the first segment: its glyph is primary and its plate is up; the other segment has not moved.
            Move(Centre(h, open, window));
            Step(h, window, time, frames, 300);
            var lit = Look(h, open);
            Assert.Equal(Lit, lit.Glyph, 1.5);
            Assert.Equal(PlateHover, lit.Plate, 1.5);
            AssertRgb(h, window, open); // the colours are the theme's primary text and overlay, not only their alphas.
            var other = Look(h, run);
            Assert.Equal(Rest, other.Glyph, 1.5);
            Assert.Equal(0, other.Plate, 0.5);

            // Across to the second: a crossfade on Chrome.Hover. Half way along the curve the one is falling and the other rising.
            Move(Centre(h, run, window));
            Step(h, window, time, frames, 40);
            var progress = Chrome.Hover.Progress(0.04);
            Assert.InRange(progress, 0.05, 0.95);
            var leaving = Look(h, open);
            var arriving = Look(h, run);
            Assert.InRange(leaving.Plate, 0.5, PlateHover - 0.5);
            Assert.InRange(arriving.Plate, 0.5, PlateHover - 0.5);
            Assert.InRange(leaving.Glyph, Rest + 1.5, Lit - 1.5);
            Assert.InRange(arriving.Glyph, Rest + 1.5, Lit - 1.5);
            Assert.Equal(PlateHover * (1 - progress), leaving.Plate, 1.5);
            Assert.Equal(PlateHover * progress, arriving.Plate, 1.5);
            Step(h, window, time, frames, 300);
            var left = Look(h, open);
            Assert.Equal(Rest, left.Glyph, 1.5);
            Assert.Equal(0, left.Plate, 0.5);
            var now = Look(h, run);
            Assert.Equal(Lit, now.Glyph, 1.5);
            Assert.Equal(PlateHover, now.Plate, 1.5);

            // Off the pill altogether: everything is back at rest.
            Move(new Point(5, 190));
            Step(h, window, time, frames, 300);
            foreach (var segment in new[] { open, run })
            {
                var back = Look(h, segment);
                Assert.Equal(Rest, back.Glyph, 1.5);
                Assert.Equal(0, back.Plate, 0.5);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-segmenthover-1: a slightly stronger plate while the segment is pressed
    public void A_pressed_segment_lifts_a_stronger_plate_than_a_hovered_one_and_drops_it_to_the_hover_on_release(bool menus)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (pill, window) = MakePill(h, menus);
            var (Rest, Lit, PlateHover, PlatePressed) = Alphas(h, window);
            var open = Segment(h, pill, "Open in another app");
            var run = Segment(h, pill, "Run a project script");
            Step(h, window, time, frames, 0);
            var at = Centre(h, run, window);
            h.Main.OnMain(() => window.MouseMove(at, RawInputModifiers.None));
            Step(h, window, time, frames, 300);
            Assert.Equal(PlateHover, Look(h, run).Plate, 1.5);
            h.Main.OnMain(() => window.MouseDown(at, MouseButton.Left, RawInputModifiers.None));
            Step(h, window, time, frames, 300);
            var pressed = Look(h, run);
            Assert.Equal(PlatePressed, pressed.Plate, 1.5);
            Assert.Equal(Lit, pressed.Glyph, 1.5);
            Assert.Equal(0, Look(h, open).Plate, 0.5); // the other segment stays out of it.
            h.Main.OnMain(() => window.MouseUp(at, MouseButton.Left, RawInputModifiers.None));
            Step(h, window, time, frames, 300);
            Assert.Equal(PlateHover, Look(h, run).Plate, 1.5);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-segmenthover-1; Chrome.swift:385-409: an open menu keeps its segment lit and its plate still follows the pointer
    public void An_open_menu_segment_stays_lit_with_the_active_circle_and_its_plate_follows_the_pointer()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (pill, window) = MakePill(h, menus: true);
            var (Rest, Lit, PlateHover, _) = Alphas(h, window);
            var git = Segment(h, pill, "Commit, push and open pull requests");
            var run = Segment(h, pill, "Run a project script");
            int Circles() => h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(git).Count());
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 300);
            Assert.Equal(0, Circles());
            Assert.Equal(Rest, Look(h, git).Glyph, 1.5);

            var at = Centre(h, git, window);
            h.Main.OnMain(() => window.MouseMove(at, RawInputModifiers.None));
            Step(h, window, time, frames, 300);
            h.Main.OnMain(() => { window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); });
            Step(h, window, time, frames, 300);

            // Presented: the active circle is mounted, the glyph is primary and the plate is the hover one while the pointer is on it.
            Assert.Equal(1, Circles());
            var open = Look(h, git);
            Assert.Equal(Lit, open.Glyph, 1.5);
            Assert.Equal(PlateHover, open.Plate, 1.5);

            // The pointer moves to the neighbour: the open segment keeps its glyph and circle but its plate goes; the neighbour lights.
            var next = Centre(h, run, window);
            h.Main.OnMain(() => window.MouseMove(next, RawInputModifiers.None));
            Step(h, window, time, frames, 300);
            var away = Look(h, git);
            Assert.Equal(Lit, away.Glyph, 1.5);
            Assert.Equal(0, away.Plate, 0.5);
            Assert.Equal(1, Circles());
            Assert.Equal(PlateHover, Look(h, run).Plate, 1.5);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-segmenthover-1: the left toolbar's round buttons light their glyph the same way (secondary to primary on Chrome.Hover)
    public void A_round_chrome_button_lights_its_glyph_from_secondary_to_primary_under_the_pointer_and_back()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (circle, window) = h.Main.OnMain(() =>
            {
                var circle = new ChromeCircleButton("square.and.pencil", "New thread", () => { }) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(40) };
                var window = new AvaloniaWindow { Content = circle, Width = 300, Height = 200 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                return (circle, window);
            });
            var (Rest, Lit, _, _) = Alphas(h, window);
            double Glyph() => h.Main.OnMain(() => ((ISolidColorBrush)TextElement.GetForeground(LogicalQuery.SelfAndDescendants<SwiftImage>(circle).Single())!).Color.A);
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 300);
            Assert.Equal(Rest, Glyph(), 1.5);
            var at = Centre(h, circle, window);
            h.Main.OnMain(() => window.MouseMove(at, RawInputModifiers.None));
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 50);
            Assert.InRange(Glyph(), Rest + 1.5, Lit - 1.5); // eased, not snapped.
            Step(h, window, time, frames, 300);
            Assert.Equal(Lit, Glyph(), 1.5);
            h.Main.OnMain(() => window.MouseMove(new Point(5, 190), RawInputModifiers.None));
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 300);
            Assert.Equal(Rest, Glyph(), 1.5);
        });
    }
}
