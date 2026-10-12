using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.1, 7.4; ED-NEW-web-w24-6). Where a live answer stands in the thread: in a turn that
// is running it follows the working line (the sentence that introduces it is in the working card above), in a turn that is finished it stays in
// the folded answer after the words that introduce it and is not buried among the steps, and open steps show it once, in its place. A real
// ThreadTimeline on a real ThreadRuntime, the manual frame clock, the harness's manual time.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w24-6")]
public sealed class LiveAnswerTimelineTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("9d3c5e10-6a7b-4c82-b1f4-2e8a0c6d4b77");

    private const string Hosting = "card \"Hosting estimate\"\n  state n = 3\n  stat \"Instances\" = n\n";

    private static TimelineEntry Live(string id, Guid turn, string text = Hosting, bool complete = true) =>
        Entry(id, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Other,
            Title = "Live answers",
            ToolCallStatus = complete ? ToolCall.Status.Completed : ToolCall.Status.Running,
            Payload = new ToolPayload { LiveView = new ToolLiveView { Id = id, Text = text, Complete = complete, Title = "Hosting estimate" } },
        }), turn);

    private static double Top(TimelineMount mount, Control control) => control.TranslatePoint(default, mount.Column)!.Value.Y;

    private static string[] Spoken(Control answer) => [.. LogicalQuery.SelfAndDescendants<MarkdownView>(answer).Select(view => view.Text)];

    private static string[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "").ToArray();

    [Fact]
    public void A_live_answer_is_a_row_of_its_own_and_the_tool_steps_around_it_stay_steps()
    {
        var entries = new[]
        {
            User("u", Turn), ToolEntry("read-1", Turn), ToolEntry("read-2", Turn), Live("live", Turn), ToolEntry("read-3", Turn), Reply("r", Turn),
        };
        var groups = TimelineGroup.Build(entries, showReasoning: false);
        Assert.Equal(
            ["Single:u", "Work:read-1+read-2", "Single:live", "Work:read-3", "Single:r"],
            groups.Select(group => group switch
            {
                TimelineGroup.Single single => "Single:" + single.Value.Id,
                TimelineGroup.Work work => "Work:" + string.Join("+", work.Entries.Select(entry => entry.Id)),
                _ => "?",
            }));
        Assert.True(LiveAnswerRows.IsLive(entries[3]));
        Assert.False(LiveAnswerRows.IsLive(entries[1]));
        Assert.False(LiveAnswerRows.IsLive(entries[5]));
    }

    [Fact]
    public void In_a_running_turn_the_live_answer_follows_the_working_line_and_streams_in_place() => ThreadTimelineRig.Run(headless, mount =>
    {
        var live = Live("live", Turn, text: "card \"Hosting estimate\"\n", complete: false);
        mount.SetEntries([User("prompt", Turn, "estimate hosting"), Reply("said", Turn, "Here is an estimate."), live]);
        mount.SetTurnStartedAt(SwiftDate.Now);
        mount.SetPhase(RuntimePhase.Running);
        mount.Settle();

        var running = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var row = Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(running.Row));
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(running.Row));
        Assert.True(Top(mount, line) < Top(mount, row), "the words and the working line, then the view");
        Assert.True(Top(mount, LogicalQuery.SelfAndDescendants<UserMessageRow>(running.Row).Single()) < Top(mount, line));
        Assert.Equal("card \"Hosting estimate\"\n", row.ShownText);

        // The model writes on: the same row takes the new lines, and no other row is made.
        mount.Runtime.Entries.Single(entry => entry.Id == "live").Item = live.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Tool(((TimelineItem.Content.Tool)live.Item.TimelineItemContent).Value with
            {
                Payload = new ToolPayload { LiveView = new ToolLiveView { Id = "live", Text = Hosting, Complete = false, Title = "Hosting estimate" } },
            }),
        };
        mount.Advance(400);
        Assert.Same(row, Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row)));
        Assert.Equal(Hosting, row.ShownText);
    });

    [Fact]
    public void In_a_finished_turn_the_folded_answer_keeps_the_live_answer_after_its_words_and_open_steps_show_it_once() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(
        [
            User("prompt", Turn, "estimate hosting"), Reply("said", Turn, "Here is an estimate."), Live("live", Turn), End("end", Turn),
        ]);
        mount.Settle();

        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn { Summary: not null });
        var row = Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(folded.Row));
        var words = LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded.Row).Single();
        Assert.Contains("Here is an estimate.", Spoken(words));
        Assert.True(Top(mount, words) < Top(mount, row), "the words that introduce the view come first");
        Assert.Empty(LogicalQuery.SelfAndDescendants<ToolRow>(folded.Row));

        var header = LogicalQuery.SelfAndDescendants<Button>(folded.Row).Single(button => ToolTip.GetTip(button) as string == "Show this turn's steps");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        mount.Advance(600);
        folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn { Summary: not null });
        Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(folded.Row));
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<WorkGroup>(folded.Row), group => Texts(group).Contains("Live answers"));
    });

    [Fact]
    public void A_live_answer_does_not_end_the_answer_so_far_but_a_tool_after_it_still_does() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(
        [
            User("prompt", Turn), Reply("first", Turn, "first words"), Live("live", Turn), ToolEntry("run", Turn), Reply("last", Turn, "last words"), End("end", Turn),
        ]);
        mount.Settle();

        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn { Summary: not null });
        var shown = LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded.Row).SelectMany(Spoken).ToArray();
        Assert.DoesNotContain("first words", shown); // a tool after it ended the answer so far, as ever
        Assert.Contains("last words", shown);
        Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(folded.Row)); // the view stays
    });

    [Fact]
    public void A_finished_turn_whose_only_reply_is_a_live_answer_still_has_a_response() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries([User("prompt", Turn), Live("live", Turn), End("end", Turn)]);
        mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn { Summary: not null });
        Assert.Single(LogicalQuery.SelfAndDescendants<LiveAnswerRow>(folded.Row));
        Assert.Empty(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded.Row));
    });
}
