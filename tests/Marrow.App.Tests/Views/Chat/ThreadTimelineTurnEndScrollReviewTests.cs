using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (rec-e1, PR-CHAT-104, OD-0076): the turn-end swap read as the reader sees it, frame by frame at 16 ms in a thread longer than the viewport. A reader pinned to the end is carried to the
// end as the block's height eases, never left behind it once the frame's jobs have run, and the running block's rows are never on screen with the finished block's; a reader who had scrolled up sees
// no row above the block move.
[Collection("Headless")]
public sealed class ThreadTimelineTurnEndScrollReviewTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("22345678-abcd-1234-abcd-123456789abc");

    private static void Frame(TimelineMount mount, int ms = 16)
    {
        mount.Frames.Advance(TimeSpan.FromMilliseconds(ms));
        mount.Store.Main.Advance(TimeSpan.FromMilliseconds(ms));
        mount.Window.UpdateLayout();
    }

    private static double Distance(TimelineMount mount) => mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height - mount.Scroll.Offset.Y;

    private static Control[] Ordered(TimelineMount mount) => mount.Blocks().Select(block => block.Row).OrderBy(row => row.TranslatePoint(default, mount.Column)!.Value.Y).ToArray();

    private static List<TimelineEntry> RunningThread(TimelineMount mount)
    {
        var entries = new List<TimelineEntry>(Turns(5)) { User("prompt", Turn, "send out two heads"), Reply("said", Turn, "I will send two heads out"), ToolEntry("run", Turn) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        return entries;
    }

    private static void EndTurn(TimelineMount mount, List<TimelineEntry> entries)
    {
        var answer = string.Join(Environment.NewLine + Environment.NewLine, Enumerable.Repeat("Both heads are out and working on their parts of the task.", 6));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Entries))!.SetValue(mount.Runtime, new ValueList<TimelineEntry>(entries.Append(Reply("final", Turn, answer)).Append(TimelineMount.End("end", Turn)).ToArray()));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(mount.Runtime, RuntimePhase.Idle);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
    }

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // OD-0076; ThreadTimeline.swift:1907-1926: the Mac cuts and keeps a pinned reader at the end; the eased block must not leave one behind it
    public void A_reader_pinned_to_the_end_is_carried_with_the_eased_turn_end_block_and_never_sees_the_two_blocks_together() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = RunningThread(mount);
        var runningId = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn { Summary: null }).Id;
        Assert.True(Math.Abs(Distance(mount)) <= 1.5);
        EndTurn(mount, entries);
        var finished = Assert.Single(LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(mount.Blocks().Single(block => block.Id == runningId).Row));
        var anchor = Ordered(mount)[^2];
        var previousAnchor = anchor.TranslatePoint(default, mount.Scroll)!.Value.Y;
        var moved = false;
        for (var i = 0; i <= 40; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout(); // the frame's jobs have run: the follow of the end has been made
            Assert.True(Math.Abs(Distance(mount)) <= 1.5, $"frame {i} ({i * 16} ms): the viewport is {Distance(mount):0.0} pt from the end");
            Assert.Empty(LogicalQuery.SelfAndDescendants<TurnRunningBlock>(mount.Column));
            Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.Column));
            Assert.Same(finished, Assert.Single(LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(mount.Column), block => block.Bounds.Height > 0 && block == finished));
            // The row above rises smoothly with the growth: it never goes back down while the block is growing.
            var top = anchor.TranslatePoint(default, mount.Scroll)!.Value.Y;
            Assert.True(top <= previousAnchor + 0.5, $"frame {i}: the row above went back down, {previousAnchor:0.0} -> {top:0.0}");
            moved |= top < previousAnchor - 0.5;
            previousAnchor = top;
            Frame(mount);
        }
        Assert.True(moved, "the finished block was never taller than the running one: the thread does not exercise the ease");
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // OD-0076: a reader who scrolled up reads on while the block at the end eases
    public void A_reader_scrolled_up_sees_no_row_above_the_block_move_while_the_turn_end_block_eases() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = RunningThread(mount);
        mount.Scroll.Offset = new Vector(0, Math.Max(0, mount.Scroll.Offset.Y - 600)); mount.Advance(500);
        var anchor = Ordered(mount).OrderBy(row => Math.Abs(row.TranslatePoint(new Point(0, row.Bounds.Height / 2), mount.Scroll)!.Value.Y - 240)).First();
        var before = anchor.TranslatePoint(default, mount.Scroll)!.Value.Y;
        EndTurn(mount, entries);
        for (var i = 0; i <= 40; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
            Assert.True(Math.Abs(anchor.TranslatePoint(default, mount.Scroll)!.Value.Y - before) <= 1, $"frame {i}: a row above the block moved from {before:0.0} to {anchor.TranslatePoint(default, mount.Scroll)!.Value.Y:0.0}");
            Frame(mount);
        }
    });
}
