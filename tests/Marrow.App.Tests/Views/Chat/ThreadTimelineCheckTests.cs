using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The check the timeline makes of itself (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792, :609-615 `.onChange(of: tracking.checkRequest)`, :770-803 repairLayout):
// asked for when the app or this window comes back to the front, when a blank watch finds no row on screen and when a scroll phase that never settled is cleared.
// The lazy stack's own repairs (telling it where the viewport is, remaking it) have nothing to do in the port's eager stack (PR-CHAT-056/057/167/168 windows_path); what a
// check does to the reader's place is ported whole. ED-255 (the placeholder the lead numbers at the merge).
[Collection("Headless")]
public sealed class ThreadTimelineCheckTests(HeadlessSession headless)
{
    private static void Bump(FrontMonitor monitor, Avalonia.Controls.Window? window) =>
        typeof(FrontMonitor).GetMethod("Bump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(monitor, [window]);

    private static double End(TimelineMount mount) => Math.Max(0, mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height);

    private static void Repair(TimelineMount mount) =>
        typeof(ThreadTimeline).GetMethod("RepairLayout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, null);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-137 (what the check does for a reader pinned to the end, and its deferral): ThreadTimeline.swift:616-623,1137-1142 (the return to the front asks for a check), :609-615,770-803 (what the check does: the end, deferred while the reader scrolls); ED-255 (2)
    public void The_window_coming_back_checks_the_timeline_which_puts_a_reader_pinned_to_the_end_back_on_it_and_waits_for_a_reader_who_is_scrolling() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(8, sentences: 8).ToList()); mount.Settle();
        var tracking = mount.Field<TimelineScrollTracking>("tracking"); var monitor = FrontMonitor.Shared.Current;
        var end = End(mount); Assert.True(end > 600, "a thread longer than its pane");
        // The reader left the end, and the flag that says they follow it is left set: the conversation changed shape while nobody looked.
        mount.Scroll.Offset = new Vector(0, end - 300); mount.Advance(500); tracking.IsPinnedToBottom = true;
        Assert.False(tracking.IsReaderScrolling); Assert.Equal(end - 300, mount.Scroll.Offset.Y, 1);
        // A reader who is scrolling when the check is asked for is not fought: it waits for them.
        tracking.IsReaderScrolling = true;
        var checks = tracking.CheckRequest; Bump(monitor, mount.Window); mount.Advance(50);
        Assert.Equal(checks + 1, tracking.CheckRequest); Assert.True(tracking.RepairSkippedWhileScrolling);
        Assert.Equal(end - 300, mount.Scroll.Offset.Y, 1);
        // At rest it goes to the end, at once, never sliding.
        tracking.IsReaderScrolling = false; tracking.RepairSkippedWhileScrolling = false;
        Bump(monitor, mount.Window); mount.Advance(50);
        Assert.Equal(end, mount.Scroll.Offset.Y, 1); Assert.True(tracking.IsPinnedToBottom);
        // A reader who is not pinned and is not past the end stays where they are, whatever the check.
        mount.Scroll.Offset = new Vector(0, 200); mount.Advance(500); Assert.False(tracking.IsPinnedToBottom);
        Bump(monitor, mount.Window); mount.Advance(100);
        Assert.Equal(200, mount.Scroll.Offset.Y, 1); Assert.False(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-137 (the record's verification method, a tooltip over the chat; the return to the front and the check's repairs are proven apart): ThreadTimeline.swift:616-623 (another window coming on screen, a popover or a tooltip opening over the chat included, is not the reader coming back), :1290-1350
    public void A_tooltip_opening_over_the_conversation_checks_nothing_and_moves_nobody() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(8, sentences: 8).ToList()); mount.Settle();
        var tracking = mount.Field<TimelineScrollTracking>("tracking"); var monitor = FrontMonitor.Shared.Current;
        mount.Scroll.Offset = new Vector(0, 200); mount.Advance(500); Assert.False(tracking.IsPinnedToBottom);
        var offset = mount.Scroll.Offset.Y; var checks = tracking.CheckRequest; var revision = monitor.Revision;
        // A real tooltip over a row of the conversation, in the popup layer of the window.
        var text = mount.Rows[1].GetVisualDescendants().OfType<TextBlock>().First();
        ToolTip.SetTip(text, "A tooltip over the conversation"); ToolTip.SetIsOpen(text, true); mount.Advance(300);
        Assert.True(ToolTip.GetIsOpen(text), "the tooltip is open");
        Assert.Contains(mount.Window.GetVisualDescendants().OfType<TextBlock>(), shown => shown.Text == "A tooltip over the conversation");
        // It is not a window of the app, and the timeline did not take it for the reader coming back: no revision, no check, no scroll, nothing pinned.
        Assert.Equal(revision, monitor.Revision); Assert.Equal(checks, tracking.CheckRequest); Assert.Equal(offset, mount.Scroll.Offset.Y, 3); Assert.False(tracking.IsPinnedToBottom);
        ToolTip.SetIsOpen(text, false); mount.Advance(300);
        Assert.Equal(revision, monitor.Revision); Assert.Equal(checks, tracking.CheckRequest); Assert.Equal(offset, mount.Scroll.Offset.Y, 3);
    }, layers: true);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-137 (the repairs of a viewport past the end and of a blank one, which ED-255 (2) ports; the lazy stack's own are not ported): ThreadTimeline.swift:770-803 (a viewport past the end of the conversation is never a valid place to be; a blank one is checked again a beat from now, three times, the last pinning the reader to the end)
    public void A_check_brings_a_viewport_past_the_end_back_and_a_blank_one_is_repaired_in_three_steps_the_last_going_to_the_end() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(8, sentences: 8).ToList()); mount.Settle();
        var tracking = mount.Field<TimelineScrollTracking>("tracking"); var end = End(mount);
        // Past the end (the content shrank under the offset): back to the end, whether or not the reader was following.
        mount.Scroll.Offset = new Vector(0, end - 300); mount.Advance(500); Assert.False(tracking.IsPinnedToBottom);
        tracking.NoteGeometry(end + 80, -80, 0); Repair(mount);
        Assert.Equal(end, mount.Scroll.Offset.Y, 1);
        // Blank: no row of the conversation is on screen. The reader is somewhere in the thread, not at its end.
        mount.Scroll.Offset = new Vector(0, end - 300); mount.Advance(500); tracking.NoteGeometry(end - 300, 300, 300);
        tracking.ClearVisible(); Assert.True(tracking.ShowsNothing, "blank after the visible set was cleared"); Assert.Equal(0, tracking.BlankRepairs);
        var checks = tracking.CheckRequest;
        Repair(mount); Assert.Equal(1, tracking.BlankRepairs); Assert.Equal(end - 300, mount.Scroll.Offset.Y, 1);
        Repair(mount); Assert.Equal(2, tracking.BlankRepairs); Assert.Equal(end - 300, mount.Scroll.Offset.Y, 1);
        Repair(mount); Assert.Equal(3, tracking.BlankRepairs); Assert.Equal(end, mount.Scroll.Offset.Y, 1); Assert.True(tracking.IsPinnedToBottom, "the third step pins");
        // The fourth blank in a row is left alone.
        mount.Scroll.Offset = new Vector(0, end - 300); mount.Advance(500); tracking.NoteGeometry(end - 300, 300, 300); tracking.ClearVisible(); tracking.BlankRepairs = 3; tracking.IsPinnedToBottom = false;
        Repair(mount); Assert.Equal(3, tracking.BlankRepairs); Assert.Equal(end - 300, mount.Scroll.Offset.Y, 1);
        // The snap of a reader pinned to the end and away from it is made at the fourth blank too, and counts for nothing more (:780).
        tracking.IsPinnedToBottom = true; tracking.BlankRepairs = 3; Repair(mount);
        Assert.Equal(end, mount.Scroll.Offset.Y, 1); Assert.Equal(3, tracking.BlankRepairs);
        mount.Scroll.Offset = new Vector(0, end - 300); mount.Advance(500); tracking.NoteGeometry(end - 300, 300, 300); tracking.ClearVisible(); tracking.IsPinnedToBottom = false;
        // Each step that did something asks for a look a beat from now: the blank watch finds a viewport still blank and asks for a check by itself.
        // (Time only, with no layout pass between: a layout would report the rows that are on screen and the viewport would not be blank.)
        tracking.BlankRepairs = 0; Repair(mount); Assert.Equal(1, tracking.BlankRepairs); checks = tracking.CheckRequest; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        mount.Store.Main.AdvanceMilliseconds(299); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal(checks, tracking.CheckRequest);
        mount.Store.Main.AdvanceMilliseconds(1); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(tracking.CheckRequest > checks, "the blank watch asked");
    });
}
