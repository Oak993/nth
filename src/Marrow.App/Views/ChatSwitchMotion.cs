// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11: "when I use the shortcut to move to another chat, the transition is smooth
// and feels premium"; "when I click another chat with the mouse, the transition should be quick and smooth ... quick but
// premium"). The Mac app flips the chat column's visibility with no animation (RootView.swift ThreadColumns, :224-243); the
// port gives every chat switch one short, render-only move: the chat opened comes in over the chat left with a soft 4-point
// settle while the chat left fades out under it; what both draw alike is drawn once. It runs for whatever selected the chat,
// since every path ends in Coordinator.Show.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views;

/// <summary>The parts of a chat column a switch moves (<see cref="ChatView.SwitchParts"/>).</summary>
internal sealed record ChatSwitchParts(
    IReadOnlyList<Control> Content,
    Control Rise,
    Control? Chrome,
    Control Composer,
    Control? Draft,
    IReadOnlyList<Control> Backdrop,
    IReadOnlyList<Control> Veils)
{
    /// <summary>The words a part shows: its text boxes' and text blocks' text, in tree order, those it hides left out.</summary>
    internal static string WordsOf(Control root)
    {
        var words = new System.Text.StringBuilder();
        foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualDescendants(root))
        {
            var text = visual switch { TextBox box => box.Text, TextBlock block => block.Text, _ => null };
            if (string.IsNullOrEmpty(text) || !ShownWithin(visual, root)) continue;
            words.Append(text).Append('\u0001');
        }
        return words.ToString();
    }

    private static bool ShownWithin(Visual visual, Visual root)
    {
        // Below the root only: the root's own opacity is the switch's to set, and a hidden column is laid out as shown.
        for (Visual? node = visual; node is not null; node = Avalonia.VisualTree.VisualExtensions.GetVisualParent(node))
        {
            if (ReferenceEquals(node, root)) return true;
            if (!node.IsVisible || node.Opacity <= 0) return false;
        }
        return true;
    }
}

/// <summary>
/// The chat switch's motion over the columns of one pane. <see cref="Enter"/>: the column opened goes on top; its content
/// (transcript, panels, terminal) goes from transparent to opaque in <see cref="EnterSeconds"/> on a cubic ease-out, the
/// transcript rising <see cref="Rise"/> points into place, and its draft too when it differs from the draft left; its glass
/// (the header's capsules, the composer) and its top veil are there from the first frame. <see cref="Leave"/>: the column left
/// goes under it; its content fades out in <see cref="LeaveSeconds"/> where it stands, its glass and veil gone at once (two glass
/// layers over each other never show), then it hides. The sheet's wash is drawn by the lowest column alone. Everything is
/// opacity and a render transform, so no frame lays anything out. A switch mid-move turns each column around from the pose it
/// has; Reduce Motion (or no frame clock) switches at once.
/// </summary>
internal sealed class ChatSwitchMotion
{
    /// <summary>The chat opened settles in 140 ms.</summary>
    public const double EnterSeconds = 0.14;

    /// <summary>The chat left is gone in 90 ms.</summary>
    public const double LeaveSeconds = 0.09;

    /// <summary>The transcript of the chat opened rises this far (points) as it settles.</summary>
    public const double Rise = 4;

    private sealed class Run(FrameHostingView column, bool entering, double from, double rise, double seconds, Action? landed)
    {
        public FrameHostingView Column { get; } = column;
        public bool Entering { get; } = entering;
        public double From { get; } = from;
        public double RiseFrom { get; } = rise;
        public double Seconds { get; } = seconds;
        public Action? Landed { get; } = landed;
        public ContinuousInstant Start { get; set; } = SwiftClock.Now;
    }

    private readonly Dictionary<FrameHostingView, Run> runs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FrameHostingView, (double Opacity, double Rise)> poses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FrameHostingView> draftCrosses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FrameHostingView, EventHandler> relayouts = new(ReferenceEqualityComparer.Instance);
    private IFrameSource? frames;
    private Action<TimeSpan>? tick;

    /// <summary>Whether a switch can move here now: a frame clock is bound and Reduce Motion is off.</summary>
    public static bool CanAnimate(Control context) => FrameClock.IsBound && !Sidebar.SessionNumbers.ReducesMotion(context);

    /// <summary>Whether the column is still leaving (drawn, fading, taking no input).</summary>
    public bool IsLeaving(FrameHostingView column) => runs.TryGetValue(column, out var run) && !run.Entering;

    /// <summary>The content's opacity and the transcript's rise drawn now for <paramref name="column"/> (1 and 0 at rest).</summary>
    public (double Opacity, double Rise) PoseOf(FrameHostingView column) => poses.TryGetValue(column, out var pose) ? pose : (1, 0);

    /// <summary>
    /// The column opened comes in on top: from transparent (or from where a leave left it) to opaque, rising into place.
    /// <paramref name="left"/>, the column the switch left, decides whether the draft crosses (the two drafts differ).
    /// </summary>
    public void Enter(FrameHostingView column, FrameHostingView? left = null)
    {
        var (opacity, rise) = Take(column);
        var fresh = opacity >= 1 && rise == 0 && !poses.ContainsKey(column);
        var from = fresh ? 0 : opacity;
        var riseFrom = fresh ? Rise : rise;
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        column.ZIndex = 1; // over the chat leaving: its glass and veil are the ones drawn
        draftCrosses.Remove(column);
        if (left is not null && ChatOf(left) is { } leftChat && ChatOf(column) is { } chat && leftChat.DraftWords != chat.DraftWords)
            draftCrosses.Add(column);
        poses[column] = (from, riseFrom);
        Recompose(column);
        if (from >= 1 && riseFrom == 0)
        {
            Settle(column);
            return;
        }
        Start(new Run(column, entering: true, from, riseFrom, EnterSeconds * Math.Max(1 - from, riseFrom / Rise), null));
    }

    /// <summary>
    /// The column left goes under the one opened: its glass and veil at once, its content fading where it stands;
    /// <paramref name="hidden"/> then hides it. It takes no input from the first frame (<see cref="FrameHostingView.IsLeaving"/>).
    /// </summary>
    public void Leave(FrameHostingView column, Action hidden)
    {
        var (opacity, rise) = Take(column);
        column.IsLeaving = true;
        column.IsHitTestVisible = false;
        column.ZIndex = 0;
        draftCrosses.Remove(column);
        ChatOf(column)?.EndTitleCross(); // its header goes at once
        poses[column] = (opacity, rise);
        Recompose(column);
        if (opacity <= 0)
        {
            Land(column, hidden);
            return;
        }
        Start(new Run(column, entering: false, opacity, rise, LeaveSeconds * opacity, hidden));
    }

    /// <summary>Stops any move on the column and draws it at rest (shown and settled).</summary>
    public void Settle(FrameHostingView column)
    {
        Stop(column);
        ChatOf(column)?.EndTitleCross();
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        draftCrosses.Remove(column);
        poses.Remove(column);
        Recompose(column);
    }

    private (double Opacity, double Rise) Take(FrameHostingView column)
    {
        Stop(column);
        return PoseOf(column);
    }

    private void Stop(FrameHostingView column)
    {
        runs.Remove(column);
        if (relayouts.Remove(column, out var relayout)) column.LayoutUpdated -= relayout;
    }

    private void Start(Run run)
    {
        runs[run.Column] = run;
        // A part a column builds or rebuilds while it moves (its composer for a new folder, say) takes the pose on the layout
        // that brings it in, before that frame is drawn.
        EventHandler relayout = (_, _) => Apply(run.Column);
        relayouts[run.Column] = relayout;
        run.Column.LayoutUpdated += relayout;
        if (tick is not null) return;
        frames = FrameClock.Source.Current;
        tick = Frame;
        frames.RequestFrame(tick);
    }

    private void Frame(TimeSpan _)
    {
        var now = SwiftClock.Now;
        foreach (var run in runs.Values.ToArray())
        {
            var span = TimeSpan.FromSeconds(run.Seconds);
            var progress = run.Seconds <= 0 ? 1 : Math.Clamp((now - run.Start).TotalSeconds / run.Seconds, 0, 1);
            var done = run.Seconds <= 0 || (now - run.Start).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks; // ED-117 (2)
            var eased = 1 - Math.Pow(1 - progress, 3); // cubic ease-out: quick to read, soft to land
            if (run.Entering)
            {
                if (done) Settle(run.Column);
                else
                {
                    poses[run.Column] = (run.From + (1 - run.From) * eased, run.RiseFrom * (1 - eased));
                    Apply(run.Column);
                }
            }
            else if (done)
            {
                Stop(run.Column);
                Land(run.Column, run.Landed);
            }
            else
            {
                poses[run.Column] = (run.From * (1 - eased), run.RiseFrom);
                Apply(run.Column);
            }
        }
        if (runs.Count == 0)
        {
            tick = null;
            frames = null;
            return;
        }
        frames?.RequestFrame(tick!);
    }

    private void Land(FrameHostingView column, Action? hidden)
    {
        poses[column] = (0, 0);
        Apply(column);
        hidden?.Invoke();
        // Hidden now: its parts go back to rest, out of sight, so a later show finds them as they were.
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        column.ZIndex = 0;
        poses.Remove(column);
        Recompose(column);
    }

    // The column and the columns beside it in the pane draw again: which one draws the backdrop depends on all of them.
    private void Recompose(FrameHostingView column)
    {
        if (column.Parent is Panel pane)
        {
            foreach (var sibling in pane.Children.OfType<FrameHostingView>()) Apply(sibling);
        }
        else Apply(column);
    }

    // The lowest visible column of the pane draws the sheet's wash; with one column visible, that one.
    private static bool DrawsBackdrop(FrameHostingView column)
    {
        if (!column.IsVisible || column.Parent is not Panel pane) return true;
        FrameHostingView? lowest = null;
        foreach (var sibling in pane.Children.OfType<FrameHostingView>())
            if (sibling.IsVisible && (lowest is null || sibling.ZIndex < lowest.ZIndex)) lowest = sibling;
        return ReferenceEquals(lowest ?? column, column);
    }

    private void Apply(FrameHostingView column)
    {
        if (ChatOf(column) is not { } chat) return;
        var (opacity, rise) = PoseOf(column);
        var leaving = column.IsLeaving && column.IsVisible;
        var parts = chat.SwitchParts;
        foreach (var part in parts.Content) Set(part, opacity);
        Set(parts.Composer, leaving ? 0 : 1);
        if (parts.Chrome is { } chrome) Set(chrome, leaving ? 0 : 1);
        if (parts.Draft is { } draft) Set(draft, !leaving && draftCrosses.Contains(column) ? opacity : 1);
        // The header's words: the words left fade out over this chat's, which fade in, in step with the transcript.
        if (!leaving && poses.ContainsKey(column) && chat.TitleCross is { } cross)
        {
            Set(cross.Arriving, opacity);
            Set(cross.Leaving, 1 - opacity);
        }
        foreach (var veil in parts.Veils) Set(veil, leaving ? 0 : 1);
        var backdrop = DrawsBackdrop(column) ? 1 : 0;
        foreach (var part in parts.Backdrop) Set(part, backdrop);
        var riser = parts.Rise;
        if (riser.RenderTransform is not TranslateTransform translate)
        {
            if (rise == 0 && riser.RenderTransform is null) return;
            translate = new TranslateTransform();
            riser.RenderTransform = translate;
        }
        if (translate.Y != rise) translate.Y = rise;
    }

    private static void Set(Control part, double opacity)
    {
        if (part.Opacity != opacity) part.Opacity = opacity;
    }

    /// <summary>The ChatView a column hosts: a few levels under the host, found breadth first.</summary>
    internal static ChatView? ChatOf(Control column)
    {
        var level = new List<Visual> { column };
        for (var depth = 0; depth < 6 && level.Count > 0; depth++)
        {
            var next = new List<Visual>();
            foreach (var visual in level)
            {
                if (visual is ChatView chat) return chat;
                next.AddRange(Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual));
            }
            level = next;
        }
        return null;
    }
}
