using System.Reflection;
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
[Collection("Headless")]
public sealed class TimelineMinimapAnimationAuditTests(HeadlessSession headless)
{
    private static TimelineMinimapEntry Entry(string id) => new() { Id = id, Title = id, Snippet = "", Weight = 1 };
    private static Button[] Ticks(Control rail) => rail.GetLogicalDescendants().OfType<Button>().ToArray();
    private static void Hover(TimelineMinimapRail rail, string? id) => typeof(TimelineMinimapRail)
        .GetMethod("SetHovered", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rail, [id]);
    private static Color Color(Button tick) => Assert.IsType<SolidColorBrush>(tick.Background).Color;
    private static void Step(TestRuntime runtime, ManualFrameSource frames, int milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

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
}
