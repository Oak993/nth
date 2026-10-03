using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 3 of unit parity-threadtimeline): the body's branch between the empty thread's prompt and the timeline (DroppyCode/App/Views/Chat/ThreadTimeline.swift,
// B3 52bc792, :134-144: the prompt while no block is there and no turn runs, the timeline otherwise). The Mac swaps the two in one frame when the first message is sent
// (E:/Droppy/work/reference-video/CATALOG.md, 2-chat-use.mov 87.86 "the thread starts in ONE frame"); OD-0076 rules that nothing on screen changes by a cut, that content that
// swaps crossfades, and that where the Mac changes state without animation the port still transitions (an owner-ruled deviation wherever the source snaps).
[Collection("Headless")]
public sealed class ThreadTimelineReviewRound3Tests(HeadlessSession headless)
{
    private static readonly Guid TurnA = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

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

    private static bool OnScreen(Control control, Control upTo) => control.IsAttachedToVisualTree() && ShownOpacity(control, upTo) > 0.001;

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-027 / UI-CHAT-001 / OD-0076: ThreadTimeline.swift:134-144 (the prompt branch and the timeline branch), :136 `blocks.isEmpty && !runtime.isRunning`
    public void REVIEW_the_first_message_sent_on_an_empty_thread_swaps_the_prompt_for_the_timeline_with_a_transition_not_a_cut() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var shown = Assert.IsAssignableFrom<Control>(prompt.Child);
        Assert.Equal(1, ShownOpacity(shown, mount.View), 3); // the prompt, faded in
        // The reader sends the first message: it lands in the thread and the turn runs.
        mount.SetEntries([User("p1", TurnA, "first question")]); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        // The first frame on which the sent message is on screen: a transition still shows the prompt there, or shows the message on its way in.
        UserMessageRow? message = null;
        for (var frame = 0; frame < 10 && message is null; frame++)
        {
            message = LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View).FirstOrDefault(row => row.IsAttachedToVisualTree());
            if (message is null) mount.Advance(16);
        }
        Assert.NotNull(message);
        var cut = !OnScreen(shown, mount.View) && ShownOpacity(message!, mount.View) >= 0.999;
        Assert.False(cut, "the prompt is gone and the sent message is at full opacity in the same frame: the empty thread's prompt and the timeline swap by a cut");
        mount.Advance(800); // and the swap is over
        Assert.False(OnScreen(shown, mount.View));
        Assert.Equal(1, ShownOpacity(Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View)), mount.View), 3);
    });
}
