// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11: "when I use the shortcut to move to another chat, the transition is smooth
// and feels premium"; "when I click another chat with the mouse, the transition should be quick and smooth ... quick but
// premium"). The Mac app flips the chat column's visibility with no animation (RootView.swift ThreadColumns, :224-243); the
// port gives every chat switch one short, render-only move: the chat left fades out quickly where it stands, the chat opened
// comes in over it with a soft 4-point settle. It runs for whatever selected the chat, since every path ends in Coordinator.Show.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views;

/// <summary>
/// The chat switch's motion over the columns of one pane. <see cref="Enter"/>: the chat's content (transcript, header,
/// panels, terminal) goes from transparent to opaque in <see cref="EnterSeconds"/> on a cubic ease-out, the transcript rising
/// <see cref="Rise"/> points into place; what every chat draws alike (the sheet's wash, the veil, the composer) is there from
/// the first frame. <see cref="Leave"/>: the chat's content (and its veil) fades out in <see cref="LeaveSeconds"/> where it
/// stands, over the chat coming in, its shared parts gone at once, then the column hides. Both are opacity and a render transform, so no frame lays anything out. A switch
/// mid-move turns each column around from the pose it has; Reduce Motion (or no frame clock) switches at once.
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
    private IFrameSource? frames;
    private Action<TimeSpan>? tick;

    /// <summary>Whether a switch can move here now: a frame clock is bound and Reduce Motion is off.</summary>
    public static bool CanAnimate(Control context) => FrameClock.IsBound && !Sidebar.SessionNumbers.ReducesMotion(context);

    /// <summary>Whether the column is still leaving (drawn, fading, taking no input).</summary>
    public bool IsLeaving(FrameHostingView column) => runs.TryGetValue(column, out var run) && !run.Entering;

    /// <summary>The content's opacity and the transcript's rise drawn now for <paramref name="column"/> (1 and 0 at rest).</summary>
    public (double Opacity, double Rise) PoseOf(FrameHostingView column) => poses.TryGetValue(column, out var pose) ? pose : (1, 0);

    /// <summary>The column opened comes in: from transparent (or from where a leave left it) to opaque, rising into place.</summary>
    public void Enter(FrameHostingView column)
    {
        var (opacity, rise) = Take(column);
        var fresh = opacity >= 1 && rise == 0 && !poses.ContainsKey(column);
        var from = fresh ? 0 : opacity;
        var riseFrom = fresh ? Rise : rise;
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        column.ZIndex = 0; // under a chat still leaving: its fading text stays over this backdrop, never washed by it
        Pose(column, from, riseFrom, backdrop: true);
        if (from >= 1 && riseFrom == 0)
        {
            Settle(column);
            return;
        }
        Start(new Run(column, entering: true, from, riseFrom, EnterSeconds * Math.Max(1 - from, riseFrom / Rise), null));
    }

    /// <summary>
    /// The column left goes: its shared parts at once, its content fading where it stands; <paramref name="hidden"/> then hides
    /// it. It takes no input from the first frame (<see cref="FrameHostingView.IsLeaving"/>).
    /// </summary>
    public void Leave(FrameHostingView column, Action hidden)
    {
        var (opacity, rise) = Take(column);
        column.IsLeaving = true;
        column.IsHitTestVisible = false;
        column.ZIndex = 1;
        Pose(column, opacity, rise, backdrop: false);
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
        runs.Remove(column);
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        column.ZIndex = 0;
        Pose(column, 1, 0, backdrop: true);
        poses.Remove(column);
    }

    private (double Opacity, double Rise) Take(FrameHostingView column)
    {
        runs.Remove(column);
        return PoseOf(column);
    }

    private void Start(Run run)
    {
        runs[run.Column] = run;
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
                else Pose(run.Column, run.From + (1 - run.From) * eased, run.RiseFrom * (1 - eased), backdrop: true);
            }
            else if (done)
            {
                runs.Remove(run.Column);
                Land(run.Column, run.Landed);
            }
            else Pose(run.Column, run.From * (1 - eased), run.RiseFrom, backdrop: false);
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
        Pose(column, 0, 0, backdrop: false);
        hidden?.Invoke();
        // Hidden now: its parts go back to rest, out of sight, so a later show finds them as they were.
        column.IsLeaving = false;
        column.IsHitTestVisible = true;
        column.ZIndex = 0;
        Pose(column, 1, 0, backdrop: true);
        poses.Remove(column);
    }

    private void Pose(FrameHostingView column, double opacity, double rise, bool backdrop)
    {
        poses[column] = (opacity, rise);
        if (ChatOf(column) is not { } chat) return;
        var (content, riser, shared, veils) = chat.SwitchParts;
        foreach (var part in content) part.Opacity = opacity;
        foreach (var part in shared) part.Opacity = backdrop ? 1 : 0;
        foreach (var veil in veils) veil.Opacity = backdrop ? 1 : opacity;
        if (riser.RenderTransform is not TranslateTransform translate)
        {
            if (rise == 0 && riser.RenderTransform is null) return;
            translate = new TranslateTransform();
            riser.RenderTransform = translate;
        }
        translate.Y = rise;
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
