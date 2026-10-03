using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 4 of unit parity-threadtimeline, scoped to the changes since 8136f5851): the crossfade between the empty thread's prompt and the timeline
// (OD-0076; ED-NEW-parity-threadtimeline (3): one fade per side, 0.3 s on the ease-out of the prompt's own appear, NewThreadPrompt.swift:50-53) on a thread that had rows before.
[Collection("Headless")]
public sealed class ThreadTimelineReviewRound4Tests(HeadlessSession headless)
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

    private static double Showing(Control control, Control upTo) => control.IsAttachedToVisualTree() ? ShownOpacity(control, upTo) : 0;

    private static string Series(IEnumerable<double> values) => string.Join(" ", values.Select(value => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-027 / PR-CHAT-026 / OD-0076 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:134-144, NewThreadPrompt.swift:50-53
    public void REVIEW_a_message_sent_on_a_thread_reverted_to_nothing_comes_in_with_the_one_fade_of_the_timeline_as_the_first_message_does() => ThreadTimelineRig.Run(headless, mount =>
    {
        // A thread with rows, reverted to nothing: the prompt is on show and the timeline has left the tree.
        mount.SetEntries(Turns(3)); mount.Settle();
        mount.SetEntries([]); mount.Advance(800);
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.Null(mount.ScrollOrNull);
        // The reader sends a message: the timeline comes back with it.
        mount.SetEntries([User("p1", TurnA, "a new first question")]); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        var message = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View));
        var leaving = new List<double> { Showing(prompt, mount.View) }; var arriving = new List<double> { Showing(message, mount.View) };
        for (var frame = 0; frame < 30; frame++) { mount.Advance(16); leaving.Add(Showing(prompt, mount.View)); arriving.Add(Showing(message, mount.View)); }
        // As on a thread that never had rows (ThreadTimelineReviewRound3Tests AssertCrossfade): each side follows the one ease-out of the swap, frame by frame within 0.02
        // (the message faded by the timeline's layer and again by its row's own arrival is two fades, not the crossfade).
        var series = $"prompt {Series(leaving)} | message {Series(arriving)}";
        for (var frame = 1; frame < arriving.Count; frame++)
        {
            var progress = Math.Min(1, Animation.EaseOut(duration: 0.3).Progress((frame - 1) * 0.016));
            Assert.True(Math.Abs(leaving[frame] - (1 - progress)) < 0.02, $"frame {frame}: the prompt follows the ease-out: {leaving[frame]:0.000} against {1 - progress:0.000}; {series}");
            Assert.True(Math.Abs(arriving[frame] - progress) < 0.02, $"frame {frame}: the sent message follows the one ease-out of the swap: {arriving[frame]:0.000} against {progress:0.000}; {series}");
        }
        Assert.False(prompt.IsAttachedToVisualTree());
    });
}
