using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 2 of unit parity-threadtimeline): what a user message row keeps when the rewind flag it is handed flips (DroppyCode/App/Views/Chat/ThreadTimeline.swift,
// B3 52bc792, :167-172 rewindable is empty while a turn runs, so the flag flips for every finished turn at every turn start and end on a provider that rewinds; TimelineRows.swift
// :64-200 UserMessageRow: `canRevert` reaches only the pencil (:131) and the menu's snapshot (:168); the attachment strip (:89-91, its @State preview slot, :1212-1214), the bubble's
// InlineText with .textSelection(.enabled) (:118-129) and the quote chips stand in the same place of the body whatever the flag says, so they are the same views with their state).
[Collection("Headless")]
public sealed class ThreadTimelineReviewRound2Tests(HeadlessSession headless)
{
    private static readonly Guid TurnA = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid TurnB = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static void Rewind(TimelineMount mount, bool supportsRewind) =>
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, ["Project", null, supportsRewind, 480.0, true]);

    private static void SetTurns(TimelineMount mount, params TurnRecord[] turns) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(mount.Runtime, new ValueList<TurnRecord>(turns));

    private static bool OffersEdit(Control row) => LogicalQuery.SelfAndDescendants<Button>(row).Any(button => ToolTip.GetTip(button) as string == "Edit message");

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW ED-NEW-parity-threadtimeline (3) "keeps every @State inside it when the block's value or its context changes": TimelineRows.swift:89-91, :118-129 (the strip and the bubble are not under `if canRevert`), :1212-1214 the strip's @State preview
    public void REVIEW_a_finished_turns_message_keeps_its_attachment_strip_and_its_selectable_bubble_while_the_rewind_flag_flips()
        => ThreadTimelineRig.Run(headless, mount =>
        {
            Rewind(mount, true);
            SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed });
            var file = new Attachment { Name = "notes.txt", Path = @"C:\no-such-folder\notes.txt", MimeType = "text/plain" };
            var prompt = Entry("p1", new TimelineItem.Content.User(new UserMessage { Text = "look at the notes I attached", Attachments = new ValueList<Attachment>([file]) }), TurnA);
            var entries = new List<TimelineEntry> { prompt, Reply("final", TurnA, "Done."), End("e1", TurnA) };
            mount.SetEntries(entries); mount.Settle();
            var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
            var user = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row));
            var strip = Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentStrip>(user));
            var bubble = Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(user));
            Assert.True(OffersEdit(user)); // nothing runs: the message may be edited
            // The reader sends the next message: while a turn runs no row offers to revert (:167-172), and the pencil goes.
            SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed }, new TurnRecord { Id = TurnB, Index = 1 });
            entries.Add(User("p2", TurnB, "next question")); mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(600);
            Assert.True(ReferenceEquals(user, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row))), "the message row is made again as the turn starts");
            Assert.False(OffersEdit(user));
            Assert.True(ReferenceEquals(strip, Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentStrip>(user))), "the attachment strip (and its @State preview slot) is made again as the turn starts"); // the strip stands ...
            Assert.True(ReferenceEquals(bubble, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(user))), "the bubble's selectable text is made again as the turn starts"); // ... and so does the bubble's text, with its selection
            // The turn ends: the pencil comes back, and the strip and the bubble are still the same views.
            entries.Add(Reply("r2", TurnB, "answer")); entries.Add(End("e2", TurnB)); mount.SetEntries(entries); mount.SetPhase(RuntimePhase.Idle); mount.Advance(600);
            Assert.True(OffersEdit(user));
            Assert.True(ReferenceEquals(strip, Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentStrip>(user))), "the attachment strip is made again as the turn ends");
            Assert.True(ReferenceEquals(bubble, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(user))), "the bubble's selectable text is made again as the turn ends");
        });

    private static double ShownOpacity(Control control, Control upTo)
    {
        var opacity = 1.0;
        for (Avalonia.Visual? at = control; at is not null; at = at.GetVisualParent())
        {
            opacity *= at.Opacity;
            if (ReferenceEquals(at, upTo)) break;
        }
        return opacity;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-026 / OD-0076 ("content that swaps crossfades", nothing on screen changes by a cut): NewThreadPrompt.swift:25-29 (the name's Text), :74-80 (the move runs under withAnimation(Chrome.panelSlide)); ThreadTimeline.swift:136-141
    public void REVIEW_picking_another_project_on_the_empty_threads_prompt_swaps_its_words_with_a_transition_not_a_cut() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        TextBlock? Showing(string words) => LogicalQuery.SelfAndDescendants<TextBlock>(prompt).FirstOrDefault(text => text.Text == words && ShownOpacity(text, prompt) > 0.001);
        Assert.NotNull(Showing("Project"));
        // The reader picks another project in the prompt's picker: the chat hands the timeline the new name.
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, ["Another", null, false, 480.0, true]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        var next = Showing("Another");
        var cut = Showing("Project") is null && next is not null && ShownOpacity(next, prompt) >= 0.999;
        Assert.False(cut, "the old name is gone and the new one is at full opacity in the same frame: the words change by a cut");
        mount.Advance(800); // and the swap is over
        Assert.Null(Showing("Project")); Assert.NotNull(Showing("Another")); Assert.Equal(1, ShownOpacity(Showing("Another")!, prompt), 3);
    });
}
