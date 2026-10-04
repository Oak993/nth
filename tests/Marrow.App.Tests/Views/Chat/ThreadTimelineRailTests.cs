using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The rail driven by the pointer, on a mounted timeline (DroppyCode/App/Views/Chat/TimelineMinimap.swift:201-240,348-365, ThreadTimeline.swift:179-200,224-252): hovering previews a tick and
// moves nothing, pressing and dragging scrubs the timeline one to one with no animation, and a release glides once to the block it lands on.
[Collection("Headless")]
public sealed class ThreadTimelineRailTests(HeadlessSession headless)
{
    private static double BlockTop(TimelineMount mount, int index) => mount.Blocks()[index].Row.TranslatePoint(default, mount.Column)!.Value.Y;

    private static double Landing(TimelineMount mount, int index) => Math.Min(BlockTop(mount, index), mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height);

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // Partial proof UI-CHAT-003
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 (the record's interactions; its geometry and its states are not claimed here): TimelineMinimap.swift:201-240,348-365 (hover previews, a press on the hovered tick jumps nothing, a drag over ticks tracks one to one unanimated, the release glides once), ThreadTimeline.swift:224-252 (the glide is .smooth(duration: 0.35))
    public void The_pointer_previews_with_a_hover_scrubs_one_to_one_with_a_press_and_a_drag_and_glides_once_on_the_release() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(8, sentences: 14).ToList()); mount.Settle();
        var rail = LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(mount.View).Single(); var ticks = LogicalQuery.SelfAndDescendants<Button>(rail).ToList(); Assert.Equal(8, ticks.Count);
        Point At(int tick) => ticks[tick].TranslatePoint(new Point(ticks[tick].Bounds.Width / 2, ticks[tick].Bounds.Height / 2), mount.Window)!.Value;
        var rest = mount.Scroll.Offset.Y;
        // Hovering alone previews the tick under the pointer and moves nothing; a press on the tick already under the pointer does not jump either.
        mount.Window.MouseMove(At(5)); mount.Advance(300);
        Assert.Equal(rest, mount.Scroll.Offset.Y, 3); Assert.True(ticks[5].Opacity > 0.9, "the hovered tick is previewed: " + ticks[5].Opacity);
        mount.Window.MouseDown(At(5), MouseButton.Left, RawInputModifiers.None); mount.Advance(100);
        Assert.Equal(rest, mount.Scroll.Offset.Y, 3);
        // Dragging to another tick scrubs the timeline there in the same instant, no animation: not a frame has run when the offset is at the block.
        mount.Window.MouseMove(At(2)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.Equal(Landing(mount, 2), mount.Scroll.Offset.Y, 1);
        mount.Window.MouseMove(At(4)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.Equal(Landing(mount, 4), mount.Scroll.Offset.Y, 1);
        // The release on the tick it landed on: the glide to the block it is already at, so it stays.
        mount.Window.MouseUp(At(4), MouseButton.Left, RawInputModifiers.None); mount.Advance(500);
        Assert.Equal(Landing(mount, 4), mount.Scroll.Offset.Y, 1);
        // A tap on another tick: the hover, the press and the release in place. The release glides to the block over .smooth(duration: 0.35), one motion.
        mount.Window.MouseMove(At(1)); mount.Advance(300); var start = mount.Scroll.Offset.Y; var target = Landing(mount, 1);
        Assert.Equal(Landing(mount, 4), start, 1);
        mount.Window.MouseDown(At(1), MouseButton.Left, RawInputModifiers.None); mount.Advance(100); Assert.Equal(start, mount.Scroll.Offset.Y, 3);
        mount.Window.MouseUp(At(1), MouseButton.Left, RawInputModifiers.None);
        var smooth = Animation.Smooth(0.35); var elapsed = 0;
        foreach (var step in new[] { 50, 50, 100, 100 })
        {
            mount.Advance(step); mount.Frames.Advance(TimeSpan.Zero); elapsed += step; // the frame of a step reads the clock before the step moves it: one more frame, with the clock where the step left it
            Assert.Equal(start + (target - start) * smooth.Progress(elapsed / 1000.0), mount.Scroll.Offset.Y, 1);
        }
        mount.Advance(500); Assert.Equal(target, mount.Scroll.Offset.Y, 1);
    });
}
