using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 3 of unit parity-threadtimeline): the body's branch between the empty thread's prompt and the timeline (DroppyCode/App/Views/Chat/ThreadTimeline.swift,
// B3 52bc792, :134-144: the prompt while no block is there and no turn runs, the timeline otherwise). The Mac swaps the two in one frame when the first message is sent
// (E:/Droppy/work/reference-video/CATALOG.md, 2-chat-use.mov 87.86 "the thread starts in ONE frame"); OD-0076 rules that nothing on screen changes by a cut, that content that
// swaps crossfades, and that where the Mac changes state without animation the port still transitions (an owner-ruled deviation wherever the source snaps), over about 0.3 s on
// the frame clock, with the system's reduce-motion setting stilling it. ED-NEW-parity-threadtimeline (3): the swap crossfades in both directions; the prompt comes in with its own
// appear (NewThreadPrompt.swift:50-53, easeOut over 0.3 s), the timeline and the leaving branch with the same curve. A timeline that comes in from nothing (its first pass, or back
// after it had gone) comes in whole, its rows, badges and rail placed with it under that one fade (round 4: a thread reverted to nothing faded its next message twice).
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

    /// <summary>How much of a view shows, 0 when it is not in the tree at all.</summary>
    private static double Showing(Control control, Control upTo) => control.IsAttachedToVisualTree() ? ShownOpacity(control, upTo) : 0;

    private static void Send(TimelineMount mount, string text = "first question")
    {
        mount.SetEntries([User("p1", TurnA, text)]); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
    }

    private static void RevertToEmpty(TimelineMount mount)
    {
        mount.SetTurnStartedAt(null); mount.SetPhase(RuntimePhase.Idle); mount.SetEntries([]);
    }

    /// <summary>The two series sampled the frame the change lands (index 0) and then at every 16 ms frame of the frame clock.</summary>
    private static (List<double> Leaving, List<double> Arriving) Sample(TimelineMount mount, Func<double> leaving, Func<double> arriving, int frames = 30)
    {
        var out_ = new List<double> { leaving() }; var in_ = new List<double> { arriving() };
        for (var frame = 0; frame < frames; frame++) { mount.Advance(16); out_.Add(leaving()); in_.Add(arriving()); }
        return (out_, in_);
    }

    /// <summary>The proof of a transition, frame by frame: the old branch is still there at full opacity when the change lands and the new one is not yet showing; the old only
    /// fades and the new only comes in; no frame steps more than 0.15; both follow the ease-out of the prompt's own appear (NewThreadPrompt.swift:50-53, 0.3 s) within 0.02 at every
    /// frame of 16 ms, which is one fade each (a branch faded twice, or on another curve, is not it); both are over after about 0.3 s; and a frame in the middle has both partly showing.</summary>
    private static void AssertCrossfade(IReadOnlyList<double> leaving, IReadOnlyList<double> arriving)
    {
        Assert.Equal(1, leaving[0], 3); Assert.Equal(0, arriving[0], 3);
        for (var frame = 1; frame < leaving.Count; frame++)
        {
            var progress = Math.Min(1, Animation.EaseOut(duration: 0.3).Progress((frame - 1) * 0.016)); // the harness runs the frames of a step before its time moves: a frame sees the time of the one before
            Assert.True(Math.Abs(leaving[frame] - (1 - progress)) < 0.02, $"frame {frame}: the leaving branch follows the ease-out: {leaving[frame]:0.000} against {1 - progress:0.000}");
            Assert.True(Math.Abs(arriving[frame] - progress) < 0.02, $"frame {frame}: the arriving branch follows the ease-out: {arriving[frame]:0.000} against {progress:0.000}");
            Assert.True(leaving[frame] <= leaving[frame - 1] + 1e-9, $"frame {frame}: the leaving branch fades only: {leaving[frame - 1]:0.000} -> {leaving[frame]:0.000}");
            Assert.True(arriving[frame] >= arriving[frame - 1] - 1e-9, $"frame {frame}: the arriving branch comes in only: {arriving[frame - 1]:0.000} -> {arriving[frame]:0.000}");
            Assert.True(Math.Abs(leaving[frame] - leaving[frame - 1]) < 0.15, $"frame {frame}: the leaving branch jumps: {leaving[frame - 1]:0.000} -> {leaving[frame]:0.000}");
            Assert.True(Math.Abs(arriving[frame] - arriving[frame - 1]) < 0.15, $"frame {frame}: the arriving branch jumps: {arriving[frame - 1]:0.000} -> {arriving[frame]:0.000}");
        }
        var gone = leaving.ToList().FindIndex(value => value <= 0.001); var there = arriving.ToList().FindIndex(value => value >= 0.999);
        Assert.InRange(gone, 19, 21); Assert.InRange(there, 19, 21); // 0.3 s of 16 ms frames, and the frame that runs before the time moves
        Assert.InRange(leaving[9], 0.05, 0.95); Assert.InRange(arriving[9], 0.05, 0.95); // the middle: both partly showing
    }

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

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): ThreadTimeline.swift:134-144, NewThreadPrompt.swift:50-53: the first message sent crossfades the prompt out and the timeline in, frame by frame
    public void The_first_message_sent_on_an_empty_thread_crossfades_the_prompt_out_and_the_timeline_in_over_about_0_3_s_with_no_frame_that_jumps() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Send(mount);
        var message = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View));
        var (leaving, arriving) = Sample(mount, () => Showing(prompt, mount.View), () => Showing(message, mount.View));
        AssertCrossfade(leaving, arriving);
        Assert.False(prompt.IsAttachedToVisualTree()); // the prompt is out of the tree once its fade is over
        Assert.Same(message, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View)));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): ThreadTimeline.swift:134-144, NewThreadPrompt.swift:50-53: a turn that ends with nothing said crossfades the timeline out and the prompt (its own appear) in, frame by frame
    public void A_turn_that_ends_with_nothing_said_crossfades_the_timeline_out_and_the_prompt_in_over_about_0_3_s_with_no_frame_that_jumps() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle(); // a turn runs and nothing is said: the timeline with its working line
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View));
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        RevertToEmpty(mount);
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var content = Assert.IsAssignableFrom<Control>(prompt.Child);
        var (leaving, arriving) = Sample(mount, () => Showing(line, mount.View), () => Showing(content, mount.View));
        AssertCrossfade(leaving, arriving);
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View)); Assert.Null(mount.ScrollOrNull); // the timeline is out of the tree once its fade is over
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): a thread reverted to nothing crossfades its rows out and the prompt in
    public void A_thread_reverted_to_nothing_crossfades_its_rows_out_and_the_prompt_in_over_about_0_3_s_with_no_frame_that_jumps() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(3)); mount.Settle();
        var row = mount.Rows[^1];
        mount.SetEntries([]);
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var content = Assert.IsAssignableFrom<Control>(prompt.Child);
        var (leaving, arriving) = Sample(mount, () => Showing(row, mount.View), () => Showing(content, mount.View));
        AssertCrossfade(leaving, arriving);
        Assert.Null(mount.ScrollOrNull);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): the system's reduce-motion setting stills the swap: it lands in one frame, in both directions
    public void With_reduce_motion_on_the_prompt_and_the_timeline_swap_in_one_frame_in_both_directions() => ThreadTimelineRig.Run(headless, mount =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(mount.Host, true);
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Send(mount);
        var message = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View));
        Assert.False(prompt.IsAttachedToVisualTree()); Assert.Equal(1, Showing(message, mount.View), 3); // the swap landed whole as it was made
        mount.Advance(16); Assert.Equal(1, Showing(message, mount.View), 3);
        mount.Settle();
        RevertToEmpty(mount);
        Assert.False(message.IsAttachedToVisualTree()); Assert.Null(mount.ScrollOrNull);
        var again = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.NotSame(prompt, again);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): a swap turned back part way goes on from the opacity each branch had, no frame jumping
    public void A_swap_that_is_turned_back_part_way_goes_on_from_the_opacity_each_branch_had_and_ends_on_the_prompt() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var first = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Send(mount);
        var scroll = mount.Scroll; // the timeline's one scroll view: the layer that is coming in
        mount.Advance(80);
        var rows = Showing(scroll, mount.View); var old = Showing(first, mount.View);
        Assert.InRange(rows, 0.05, 0.95); Assert.InRange(old, 0.05, 0.95);
        // The first message is taken back: nothing is said and no turn runs. The prompt that was leaving goes on leaving, the rows that were coming turn round, and a prompt comes in.
        RevertToEmpty(mount);
        var second = LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View).Single(prompt => !ReferenceEquals(prompt, first));
        var content = Assert.IsAssignableFrom<Control>(second.Child);
        Assert.True(Math.Abs(rows - Showing(scroll, mount.View)) < 0.001, $"the rows that were coming in turn round from where they were: {rows:0.000} -> {Showing(scroll, mount.View):0.000}");
        Assert.True(Math.Abs(old - Showing(first, mount.View)) < 0.001, $"the prompt that was leaving goes on from where it was: {old:0.000} -> {Showing(first, mount.View):0.000}");
        var (leaving, arriving) = Sample(mount, () => Showing(scroll, mount.View), () => Showing(content, mount.View), 30);
        for (var frame = 1; frame < leaving.Count; frame++)
        {
            Assert.True(leaving[frame] <= leaving[frame - 1] + 1e-9, $"frame {frame}: the rows go on fading: {leaving[frame - 1]:0.000} -> {leaving[frame]:0.000}");
            Assert.True(Math.Abs(leaving[frame] - leaving[frame - 1]) < 0.15, $"frame {frame}: the rows jump: {leaving[frame - 1]:0.000} -> {leaving[frame]:0.000}");
            Assert.True(arriving[frame] >= arriving[frame - 1] - 1e-9, $"frame {frame}: the new prompt comes in only: {arriving[frame - 1]:0.000} -> {arriving[frame]:0.000}");
        }
        Assert.True(leaving[^1] < 0.001, $"the rows are gone at the end: {leaving[^1]:0.000}"); Assert.True(arriving[^1] > 0.999, $"the new prompt is in at the end: {arriving[^1]:0.000}");
        Assert.False(first.IsAttachedToVisualTree()); Assert.False(scroll.IsAttachedToVisualTree()); Assert.Null(mount.ScrollOrNull);
        Assert.Same(second, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): the timeline that comes back while it is leaving is the same view; the layer that is leaving is not hit testable (it is out of the pointer's way), the one on show is
    public void A_timeline_that_comes_back_while_it_is_leaving_is_the_same_view_and_only_the_layer_on_show_is_hit_testable() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(3).ToList();
        mount.SetEntries(entries); mount.Settle();
        var scroll = mount.Scroll; var row = mount.Rows[^1];
        mount.SetEntries([]); mount.Advance(96);
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var rowBefore = Showing(row, mount.View);
        Assert.InRange(rowBefore, 0.05, 0.95); // the rows are on their way out
        var overlay = Assert.IsAssignableFrom<Control>(scroll.Parent);
        Assert.True(prompt.IsHitTestVisible); Assert.False(overlay.IsHitTestVisible); // the layer on show takes input, the one that is leaving does not
        // Rows come back: the rows that were leaving come in again from where they were, the prompt (made after them, so over them) leaves.
        mount.SetEntries(entries);
        Assert.Same(scroll, mount.Scroll); Assert.Equal(rowBefore, Showing(row, mount.View), 3);
        Assert.False(prompt.IsHitTestVisible); Assert.True(overlay.IsHitTestVisible); // the layers swap places as to input at once
        mount.Advance(400);
        Assert.False(prompt.IsAttachedToVisualTree()); Assert.Equal(1, Showing(row, mount.View), 3);
        Assert.Same(scroll, mount.Scroll); Assert.True(overlay.IsHitTestVisible);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076; ThreadTimeline.swift:139-140 `.onAppear`): the on-appear resets are made as the prompt comes and a timeline leaving does not undo them
    public void The_rows_leaving_under_the_prompt_do_not_put_back_the_travel_or_the_jump_button_the_prompt_cleared() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(6, sentences: 10)); mount.Settle(); // the reader is at the end: the end is on screen
        Assert.True(mount.Chrome.Progress > 0, "the chrome's scroll travel drives the veil");
        mount.State.ShowsJumpButton = true;
        var leaving = mount.Scroll;
        mount.SetEntries([]);
        Assert.Equal(0, mount.Chrome.Progress); Assert.False(mount.State.ShowsJumpButton); // as the prompt appears, not a frame later
        mount.Advance(64);
        leaving.Offset = new Vector(0, 100); mount.Advance(64); // the rows that are leaving move (a late layout, a notch of the wheel): their end goes off screen and they have travelled
        Assert.Equal(0, mount.Chrome.Progress); Assert.False(mount.State.ShowsJumpButton);
        mount.Advance(400);
        Assert.Null(mount.ScrollOrNull); Assert.Equal(0, mount.Chrome.Progress); Assert.False(mount.State.ShowsJumpButton);
    });

    private static string Series(IEnumerable<double> values) => string.Join(" ", values.Select(value => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));

    private static void Ask(TimelineMount mount, params QuestionRequest[] requests) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(mount.Runtime, new ValueList<QuestionRequest>(requests));

    private static TimelineMinimapColumn? Rail(TimelineMount mount) => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(mount.View).SingleOrDefault();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076): ThreadTimeline.swift:134-144 (the timeline branch that comes back is a new view, and what it shows comes with it), :393-397 (the badges), :179-200 (the rail): a timeline that had gone comes back whole under its one fade
    public void A_timeline_that_comes_back_after_it_had_gone_brings_its_question_badge_in_with_its_one_fade_and_not_the_rail_or_the_badge_it_had_before() => ThreadTimelineRig.Run(headless, mount =>
    {
        static QuestionRequest Question(string id) => new(id, new([new QuestionRequest.Question("choice-" + id, "Header", "Choose", new([]), false, false, false)]));
        mount.SetEntries(Turns(3)); Ask(mount, Question("old")); mount.Settle();
        var rail = Assert.IsType<TimelineMinimapColumn>(Rail(mount)); // three sent messages: the rail is on show
        var answered = Assert.Single(LogicalQuery.SelfAndDescendants<QuestionBadgeRow>(mount.View)); Assert.Equal(1, Showing(answered, mount.View), 3); // and a question's badge
        RevertToEmpty(mount); mount.Advance(800);
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)); Assert.Null(mount.ScrollOrNull); // the timeline has gone, its rail and its badge with it
        // While the prompt is up (its branch reads the blocks and the turn only, :136) the old question is answered and a new one waits: its badge comes back with the timeline.
        Ask(mount, Question("q")); mount.Advance(16);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View))); Assert.False(answered.IsAttachedToVisualTree());
        Send(mount, "a new first question");
        var badge = Assert.Single(LogicalQuery.SelfAndDescendants<QuestionBadgeRow>(mount.View), row => row.Name == "question-q");
        var leaving = new List<double> { Showing(prompt, mount.View) }; var arriving = new List<double> { Showing(badge, mount.View) };
        var oldRail = new List<double> { Showing(rail, mount.View) }; var oldBadge = new List<double> { Showing(answered, mount.View) };
        for (var frame = 0; frame < 30; frame++)
        {
            mount.Advance(16);
            leaving.Add(Showing(prompt, mount.View)); arriving.Add(Showing(badge, mount.View)); oldRail.Add(Showing(rail, mount.View)); oldBadge.Add(Showing(answered, mount.View));
        }
        AssertCrossfade(leaving, arriving); // the badge is faded once: by the timeline's fade, not again by its own soft appear (:396)
        Assert.True(oldRail.All(value => value < 0.001), "the rail the timeline had before it went does not show again (one sent message has none): " + Series(oldRail));
        Assert.True(oldBadge.All(value => value < 0.001), "the badge of the question answered while the timeline had gone does not show again: " + Series(oldBadge));
        Assert.Null(Rail(mount)); Assert.False(rail.IsAttachedToVisualTree()); Assert.False(answered.IsAttachedToVisualTree());
        Assert.Same(badge, Assert.Single(LogicalQuery.SelfAndDescendants<QuestionBadgeRow>(mount.View)));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 and UI-CHAT-003 (ED-NEW-parity-threadtimeline (3), OD-0076): ThreadTimeline.swift:134-144, :179-200 (the rail is part of the timeline that comes in; its own fade, `.animation(Chrome.panelSlide, value: showsMinimap)`, is the flag's)
    public void A_timeline_that_comes_in_with_two_sent_messages_brings_its_rail_in_with_its_one_fade_on_its_first_pass_and_when_it_comes_back() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        mount.SetEntries(Turns(2)); // the timeline's first pass
        var rail = Assert.IsType<TimelineMinimapColumn>(Rail(mount));
        var (leaving, arriving) = Sample(mount, () => Showing(prompt, mount.View), () => Showing(rail, mount.View));
        AssertCrossfade(leaving, arriving); // the rail is faded once, by the timeline's fade, not again by the panel slide's
        // Down to one sent message the rail goes (the timeline on show: the slide's fade), then the thread is reverted to nothing and the timeline goes.
        mount.SetEntries([User("only", TurnA, "one message")]); mount.Settle(); Assert.Null(Rail(mount));
        RevertToEmpty(mount); mount.Advance(800); Assert.Null(mount.ScrollOrNull);
        var again = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        // It comes back with two sent messages: a rail it did not have when it went comes in with it, under the same one fade.
        mount.SetEntries(Turns(2, prefix: "back-"));
        var back = Assert.IsType<TimelineMinimapColumn>(Rail(mount)); Assert.NotSame(rail, back);
        (leaving, arriving) = Sample(mount, () => Showing(again, mount.View), () => Showing(back, mount.View));
        AssertCrossfade(leaving, arriving);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 (ED-NEW-parity-threadtimeline (3), OD-0076): ThreadTimeline.swift:317-321 (a row that joins a stack on screen arrives with its transition): a timeline turned back while it was still leaving is partly on screen, so a row new to it arrives from nothing
    public void A_message_that_joins_a_timeline_turned_back_while_it_was_leaving_arrives_with_its_own_transition_from_nothing_not_at_the_layers_opacity() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(3)); mount.Settle();
        var scroll = mount.Scroll; var overlay = Assert.IsAssignableFrom<Control>(scroll.Parent);
        RevertToEmpty(mount); mount.Advance(96);
        Assert.InRange(Showing(overlay, mount.View), 0.05, 0.95); // the timeline is on its way out
        Send(mount, "a new first question");
        Assert.Same(scroll, mount.Scroll); // the same timeline, turned back
        var message = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View));
        var shown = new List<double> { Showing(message, mount.View) };
        for (var frame = 0; frame < 40; frame++) { mount.Advance(16); shown.Add(Showing(message, mount.View)); }
        Assert.True(shown[0] < 0.001, "the message arrives from nothing, not at the opacity the layer turned back from: " + Series(shown));
        for (var frame = 1; frame < shown.Count; frame++)
        {
            Assert.True(shown[frame] >= shown[frame - 1] - 1e-9, $"frame {frame}: the message comes in only: {Series(shown)}");
            Assert.True(shown[frame] - shown[frame - 1] < 0.15, $"frame {frame}: the message jumps: {Series(shown)}");
        }
        Assert.Equal(1, shown[^1], 3);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-027 (ED-NEW-parity-threadtimeline (3), OD-0076): reduce motion stills the swap; a timeline that faded out (opacity 0, out of the pointer's way) and is cut back in once reduce motion is on is whole and takes input
    public void A_timeline_that_faded_out_comes_back_whole_and_taking_input_when_reduce_motion_was_turned_on_while_it_was_gone() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(Turns(3)); mount.Settle();
        var scroll = mount.Scroll; var overlay = Assert.IsAssignableFrom<Control>(scroll.Parent);
        RevertToEmpty(mount); mount.Advance(800); // faded out under the prompt, then out of the tree
        Assert.False(overlay.IsAttachedToVisualTree()); Assert.Equal(0, overlay.Opacity, 3); Assert.False(overlay.IsHitTestVisible);
        EnvironmentValues.SetAccessibilityReduceMotion(mount.Host, true);
        Send(mount, "again");
        Assert.Same(scroll, mount.Scroll); // the same timeline, cut back in
        Assert.Equal(1, overlay.Opacity, 3); Assert.True(overlay.IsHitTestVisible);
        Assert.Equal(1, Showing(Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(mount.View)), mount.View), 3);
        mount.Advance(400);
        Assert.Equal(1, overlay.Opacity, 3); Assert.True(overlay.IsHitTestVisible);
    });
}
