using Avalonia.VisualTree;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineMinimap.swift (B3 52bc792), :139-287.
// PR-CHAT-073: every clause is proven from the pinned Swift (:193-199, :262-268) by the tests below, in both theme variants; the record's capture
// comparison needs a frame the reference videos do not contain, so OD-0098 makes the Swift the proof (the light half is the label colour's
// appearance dependence, ENGINEERING DECISION: Chrome.PrimaryText resolves per variant, as SwiftUI's .primary does).
[Collection("Headless")]
public sealed class TimelineMinimapAnimationAuditTests(HeadlessSession headless)
{
    private static TimelineMinimapEntry Entry(string id) => new() { Id = id, Title = id, Snippet = "", Weight = 1 };
    private static Button[] Ticks(Control rail) => rail.GetLogicalDescendants().OfType<Button>().ToArray();
    private static void Hover(TimelineMinimapRail rail, string? id) => typeof(TimelineMinimapRail)
        .GetMethod("SetHovered", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rail, [id]);
    private static Color Color(Button tick) => Assert.IsType<SolidColorBrush>(tick.Background).Color;
    private static Control Card(TimelineMinimapRail rail) => Assert.Single(Assert.IsType<Grid>(rail.Child).Children.Skip(2));
    private static TranslateTransform Offset(Control card) => Assert.IsType<TranslateTransform>(card.RenderTransform);
    private static void Step(TestRuntime runtime, ManualFrameSource frames, int milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "UI-CHAT-005")] // App/Views/Chat/TimelineMinimap.swift:224-231,289-300 (geometry re-evaluation)
    public void Resizing_a_hovered_rail_reclamps_only_the_preview_card() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var rail = new TimelineMinimapRail(Enumerable.Range(0, 20).Select(i => Entry(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray(), 100, null, (_, _) => { })
            { Height = 100, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 400, Height = 300, Content = rail };
        window.Show();
        try
        {
            window.UpdateLayout();
            var ticks = Ticks(rail);
            Hover(rail, "19");
            Step(runtime, frames, 1000);
            Assert.Equal(29.5, Offset(Card(rail)).Y); // half=18.5, bottom=100-18.5-2; subtract railHeight/2.
            rail.Height = 40;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(40, rail.Bounds.Height);
            Assert.Equal(0.5, Offset(Card(rail)).Y); // max(top=20.5, bottom=19.5) - 40/2.
            Assert.Equal(ticks, Ticks(rail));
            Assert.Equal(0, frames.Pending);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:195-199,224-240 (preview transition and glide)
    public void Preview_fades_and_slides_then_glides_between_ticks_and_fades_out() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var rail = new TimelineMinimapRail(new[] { Entry("first"), Entry("second") }, 200, null, (_, _) => { });
        rail.Measure(new Size(400, 200));
        rail.Arrange(new Rect(0, 0, 400, 200));
        Hover(rail, "first");
        var card = Card(rail);
        Assert.Equal(0, card.Opacity);
        Assert.Equal(-6, Offset(card).X);
        Step(runtime, frames, 50);
        var progress = Animation.Smooth(duration: 0.22).Progress(0.05);
        Assert.Equal(progress, card.Opacity, 10);
        Assert.Equal(-6 + 6 * progress, Offset(card).X, 10);
        Step(runtime, frames, 1000);
        Assert.Equal(1, card.Opacity);
        Assert.Equal(0, Offset(card).X);
        Assert.Equal(-5, Offset(card).Y);
        Hover(rail, "second");
        card = Card(rail);
        Assert.Equal(-5, Offset(card).Y);
        Step(runtime, frames, 50);
        Assert.Equal(-5 + 10 * progress, Offset(card).Y, 10);
        Step(runtime, frames, 1000);
        Assert.Equal(5, Offset(card).Y);
        Hover(rail, null);
        Assert.Same(card, Card(rail));
        Step(runtime, frames, 50);
        Assert.Equal(1 - progress, card.Opacity, 10);
        Assert.Equal(-6 * progress, Offset(card).X, 10);
        Step(runtime, frames, 1000);
        Assert.Empty(Assert.IsType<Grid>(rail.Child).Children.Skip(2));
        Assert.Equal(0, frames.Pending);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:195-199,241-274,284-287
    public void Hover_retains_ticks_and_tweens_brush_opacity_and_width_using_the_smooth_transaction() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var rail = new TimelineMinimapRail(new[] { Entry("first"), Entry("second") }, 100, "first", (_, _) => { });
        var ticks = Ticks(rail);
        var selected = ticks[0];
        var accent = Color(selected);
        Assert.Equal(1, selected.Opacity);
        Assert.Equal(22, ticks[1].Width);
        Assert.Equal(0, frames.Pending);
        Hover(rail, "first");
        Assert.Same(selected, Ticks(rail)[0]);
        Assert.Equal(accent, Color(selected));
        Assert.Equal(1, selected.Opacity);
        Assert.True(frames.Pending > 0);
        Step(runtime, frames, 50);
        Assert.InRange(selected.Opacity, 0.950001, 0.999999);
        Assert.Equal(1 + (0.95 - 1) * Animation.Smooth(duration: 0.22).Progress(0.05), selected.Opacity, 10); // :199 smooth(duration: 0.22)
        Assert.NotEqual(accent, Color(selected));
        Assert.NotEqual(Chrome.PrimaryText.Resolve(rail.ActualThemeVariant), Color(selected));
        Assert.InRange(ticks[1].Width, 22.000001, 23.999999);
        Step(runtime, frames, 2000);
        Assert.Equal(0.95, selected.Opacity);
        Assert.Equal(Chrome.PrimaryText.Resolve(rail.ActualThemeVariant), Color(selected));
        Assert.Equal(24, ticks[1].Width);
        Assert.Equal(0.4, ticks[1].Opacity);
        Assert.Equal(0, frames.Pending);
        Hover(rail, null);
        Step(runtime, frames, 2000);
        Assert.Equal(accent, Color(selected));
        Assert.Equal(1, selected.Opacity);
        Assert.Equal(0.25, ticks[1].Opacity);
        Assert.Equal(22, ticks[1].Width);
        Assert.Equal(0, frames.Pending);
    });

    // PR-CHAT-073 "capture comparison in both themes", the port half: what each tick state draws in the Light and in the Dark theme variant
    // (the label colour is appearance-dependent, Chrome.PrimaryText; the accent is the theme's). The reference video shows the dark rail only
    // (idle grey ticks, the accent block, a white hovered tick, E:\Droppy\work\reference-video CATALOG timeline.turn-rail and
    // timeline.rail-hover-preview), so the light half is port-derived and UNVERIFIED against a Mac (OD-0001).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:193-199,262-268
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-073 capture clause, closed from the Swift by OD-0098: the colours per theme
    public void Tick_colours_and_opacities_follow_the_theme_variant_at_rest_expanded_hovered_and_selected(bool dark) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var variant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var rail = new TimelineMinimapRail(new[] { Entry("first"), Entry("second"), Entry("third") }, 100, "first", (_, _) => { });
        var window = new Avalonia.Controls.Window { Width = 200, Height = 200, Content = rail, RequestedThemeVariant = variant };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var ticks = Ticks(rail);
            var label = Chrome.PrimaryText.Resolve(variant);
            Assert.NotEqual(Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Light), Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Dark));
            // Rest: the unselected ticks are the label colour at 0.25; the block the reader is on is the accent, full strength.
            Assert.Equal(label, Color(ticks[1]));
            Assert.Equal(0.25, ticks[1].Opacity);
            Assert.Equal(label, Color(ticks[2]));
            Assert.Equal(Chrome.Accent.Resolve(variant), Color(ticks[0]));
            Assert.Equal(1, ticks[0].Opacity);
            // Expanded by a hover on the third tick: every unselected tick rises to 0.4; the hovered one is plain ink at 0.95; the accent stays on the
            // selected one while it is not the hovered one.
            Hover(rail, "third");
            Step(runtime, frames, 1000);
            Assert.Equal(0.4, ticks[1].Opacity);
            Assert.Equal(label, Color(ticks[1]));
            Assert.Equal(label, Color(ticks[2]));
            Assert.Equal(0.95, ticks[2].Opacity);
            Assert.Equal(Chrome.Accent.Resolve(variant), Color(ticks[0]));
            Assert.Equal(1, ticks[0].Opacity);
            // Hovering the selected tick overrides the accent with plain ink at 0.95.
            Hover(rail, "first");
            Step(runtime, frames, 1000);
            Assert.Equal(label, Color(ticks[0]));
            Assert.Equal(0.95, ticks[0].Opacity);
            Hover(rail, null);
            Step(runtime, frames, 1000);
            Assert.Equal(0.25, ticks[1].Opacity);
            Assert.Equal(Chrome.Accent.Resolve(variant), Color(ticks[0]));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:195-199 (live resize or reduce motion -> nil)
    public void Inherited_live_resize_and_reduce_motion_make_the_change_instant(bool live, bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        runtime.OnMain(() =>
        {
            var rail = new TimelineMinimapRail(new[] { Entry("first") }, 100, "first", (_, _) => { });
            var window = new Avalonia.Controls.Window { Content = rail };
            var resize = new WindowLiveResize(window);
            AppEnvironment.SetWindowLiveResize(window, resize);
            EnvironmentValues.SetAccessibilityReduceMotion(window, reduced);
            if (live)
            {
                var handled = false;
                resize.WndProc(0, 0x0231, 0, 0, ref handled); // WM_ENTERSIZEMOVE
                resize.WndProc(0, 0x0214, 0, 0, ref handled); // WM_SIZING
            }
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var tick = Assert.Single(Ticks(rail));
                Hover(rail, "first");
                Assert.Same(tick, Assert.Single(Ticks(rail)));
                Assert.Equal(0.95, tick.Opacity);
                Assert.Equal(Chrome.PrimaryText.Resolve(rail.ActualThemeVariant), Color(tick));
                Assert.Equal(1, Card(rail).Opacity);
                Assert.Equal(0, Offset(Card(rail)).X);
                Hover(rail, null);
                Assert.Empty(Assert.IsType<Grid>(rail.Child).Children.Skip(2));
                Assert.Equal(0, frames.Pending);
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:139-161,241-243,245-265 (selectedID animation)
    public void Selection_tracking_updates_the_existing_rail_and_animates_the_new_accent_tick() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        TimelineScrollTracking tracking = null!;
        MinimapSelection selection = null!;
        Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            tracking = new TimelineScrollTracking();
            tracking.SetOutline([("first", true), ("second", true)]);
            selection = new MinimapSelection(new[] { Entry("first"), Entry("second") },
                new[] { new MinimapOutlineBlock { Id = "first", HasUserMessage = true }, new MinimapOutlineBlock { Id = "second", HasUserMessage = true } },
                tracking, 100, (_, _) => { });
            window = new Avalonia.Controls.Window { Content = selection };
            window.Show();
        });
        try
        {
            var rail = Assert.IsType<TimelineMinimapRail>(selection.Child);
            var ticks = Ticks(rail);
            Assert.Equal(0.25, ticks[0].Opacity);
            Assert.Equal(1, ticks[1].Opacity); // nil active ID falls back to last user block (:150-152)
            runtime.OnMain(() => tracking.SetVisible("first", true));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(rail, selection.Child);
            Assert.True(frames.Pending > 0);
            Step(runtime, frames, 50);
            Assert.InRange(ticks[0].Opacity, 0.250001, 0.999999);
            Step(runtime, frames, 2000);
            Assert.Equal(1, ticks[0].Opacity);
            Assert.Equal(Chrome.Accent.Resolve(rail.ActualThemeVariant), Color(ticks[0]));
            Assert.Equal(0.25, ticks[1].Opacity);
            Assert.Equal(0, frames.Pending);
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:164-167 (no continuous work at rest),195-199
    public void Interrupted_hover_retargets_from_the_current_values_and_detach_cancels_all_frames() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var rail = new TimelineMinimapRail(new[] { Entry("first") }, 100, null, (_, _) => { });
        var window = new Avalonia.Controls.Window { Content = rail };
        window.Show();
        try
        {
            var tick = Assert.Single(Ticks(rail));
            Hover(rail, "first");
            Step(runtime, frames, 50);
            var current = tick.Opacity;
            Assert.InRange(current, 0.250001, 0.949999);
            Hover(rail, null);
            Assert.Equal(current, tick.Opacity);
            Step(runtime, frames, 50);
            Assert.True(tick.Opacity < current);
            window.Content = null;
            Assert.Equal(0, frames.Pending);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // App/Views/Chat/TimelineMinimap.swift:303-318 the mounted preview keeps its identity and its title and snippet crossfade (.contentTransition(.opacity)) under the hover animation
    public void Moving_to_another_tick_crossfades_the_preview_text_in_the_same_card() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var rail = new TimelineMinimapRail(new[] { Entry("first"), Entry("second") }, 200, null, (_, _) => { });
        rail.Measure(new Size(400, 200));
        rail.Arrange(new Rect(0, 0, 400, 200));
        Hover(rail, "first");
        Step(runtime, frames, 1000);
        var card = Card(rail);
        Hover(rail, "second");
        Assert.Same(card, Card(rail));
        Step(runtime, frames, 50);
        var progress = Animation.Smooth(duration: 0.22).Progress(0.05);
        var first = LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => text.Text == "first");
        var second = LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => text.Text == "second");
        Assert.Equal(1 - progress, FadeOf(first), 10);
        Assert.Equal(progress, FadeOf(second), 10);
        Step(runtime, frames, 1000);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(card), text => text.Text == "first");
        Assert.Equal(1, FadeOf(LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => text.Text == "second")));
    });

    private static double FadeOf(Control control)
    {
        var opacity = 1.0;
        for (Avalonia.Visual? visual = control; visual is not null && !ReferenceEquals(visual, null); visual = visual.GetVisualParent())
            opacity *= visual.Opacity;
        return opacity;
    }
}
