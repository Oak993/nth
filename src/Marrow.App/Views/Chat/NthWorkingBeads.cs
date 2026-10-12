// PORT-ONLY: Calculi WU5 (ED-NEW-calculi-wu5-1; DESIGN.md sections 5, 5.1, 5.2, 6, 7 and 9). The working line's beads: one NthBeadsControl per
// thread, which follows the turn's state and is handed from place to place, never made again: the running line's slot, the finished header that takes
// the running block's place at the turn's end (the exit plays there, beside the "Worked for" label that is already shown), and the next turn's line
// (a new turn while the last is leaving plays Rejoin from wherever the beads are). The UI follows the turn state, never the animation: nothing here
// waits for the beads, and no UI listens for gone. ED-NEW-calculi-wu5-2: the beads never share a fade and never jump. A turn enters only once its
// beads sit in a wholly opaque running line; a hand-over to a slot further than a reflow glides waits for the beads to draw nothing (the exit
// they play where they are ends, the designed end), then the new line's entrance plays there (the designed start); a mid-turn split's line holds
// its beads at their place, unfaded, while they leave.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using AnimatedDouble = Marrow.App.Views.Tour.AnimatedDouble;

namespace Marrow.App.Views.Chat;

/// <summary>The working beads of one thread's working line (Calculi).</summary>
internal sealed class NthWorkingBeads
{
    /// <summary>The working line's n box, in DIP (section 9).</summary>
    public const double GlyphSize = 18;

    /// <summary>Scriptio on the first turn after more than this idle (or the first turn of a conversation), Ortus otherwise (section 5).</summary>
    public static readonly TimeSpan IdleForScriptio = TimeSpan.FromMinutes(2);

    /// <summary>A layout move of the glyph up to this far (DIP, each axis) is reported as a reflow, so the beads glide after it (section 9: a whole
    /// 26 DIP line peaks at 0.41 bead widths a frame; two lines stay calm). A longer move (a long answer between the last header and the next line)
    /// is taken rigidly, as a scroll is: a glide across the text would not be calm.</summary>
    public const double ReflowLimit = 52;

    /// <summary>A visual inside the timeline's scrolled content: the glyph's layout place is measured from it, so scrolling never counts as a move.</summary>
    public static readonly AttachedProperty<bool> ReflowAnchorProperty = AvaloniaProperty.RegisterAttached<NthWorkingBeads, Visual, bool>("ReflowAnchor");

    public static void SetReflowAnchor(Visual visual, bool value) => visual.SetValue(ReflowAnchorProperty, value);

    private static readonly ConditionalWeakTable<ThreadRuntime, NthWorkingBeads> Lines = new();

    /// <summary>The thread's beads, made on first use.</summary>
    public static NthWorkingBeads For(ThreadRuntime runtime) => Lines.GetValue(runtime, made => new NthWorkingBeads(made));

    /// <summary>The thread's beads if any line has used them.</summary>
    public static NthWorkingBeads? Existing(ThreadRuntime runtime) => Lines.TryGetValue(runtime, out var line) ? line : null;

    /// <summary>What the beads read from the runtime: the turn (its start), whether it runs, its state and tool description, when the turn before it
    /// ended, and how this one ended.</summary>
    public readonly record struct TurnState(DateTimeOffset? StartedAt, bool Running, NthBeadsState State, string? Description, DateTimeOffset? PreviousEnd, TurnStatus? EndStatus);

    private readonly Action<TimeSpan> poll;
    private DateTimeOffset? turn; // the start of the turn the beads are on (null: none, or it has ended)
    private NthBeadsState state;
    private string? description;
    private Panel? claimant; // the running line's slot that wants the beads
    private bool polling;
    private (NthBeadsEntrance Entrance, string Seed)? pending; // a turn that has started and not yet entered (it enters in an opaque running line)

    /// <summary>A hand-over to a far slot waits for the exit to end; if the render thread has not said so by these times after the exit was asked
    /// (the measured worst cases with an entrance's hand-off before them: Occasus and Stop 0.84 s, the error hold 2.32 s, plus 0.55 s), the beads
    /// draw nothing anyway (section 5.2's gates), and they move.</summary>
    public const double ExitBound = 1.5, FailBound = 3;
    private Visual? anchor;
    private double placeX, placeY;
    private bool placed;
    private long detachedAt;

    /// <summary>The beads.</summary>
    public NthBeadsControl Beads { get; }

    private NthWorkingBeads(ThreadRuntime runtime)
    {
        poll = _ => { polling = false; TryTake(); };
        Beads = new NthBeadsControl { GlyphSize = GlyphSize };
        Beads.Track(() => Read(runtime), Apply);
        Beads.LayoutUpdated += (_, _) => Placed();
        Beads.DetachedFromVisualTree += (_, _) => detachedAt = Stopwatch.GetTimestamp();
        Beads.AttachedToVisualTree += (_, _) => { if (Stopwatch.GetElapsedTime(detachedAt).TotalSeconds > NthBeadsDriver.StallSeconds) placed = false; };
    }

    // ---- the turn ----

    /// <summary>The runtime's turn as the beads see it.</summary>
    public static TurnState Read(ThreadRuntime runtime)
    {
        var entries = runtime.Entries; var phase = runtime.Phase; var started = runtime.TurnStartedAt;
        var waiting = runtime.Approvals.Count != 0 || runtime.Questions.Count != 0;
        var heads = phase != RuntimePhase.Idle && runtime.HasWorkingHeads;
        // The trailing turn's items (as WorkingBlockView's TurnEntries finds them), how it ended, and when the turn before it ended: the newest turn end
        // dated before this turn started, or (a turn with no end entry) the last thing it did.
        Guid? trailing = null; var items = new List<TimelineItem>(); DateTimeOffset? previousEnd = null; TurnStatus? endStatus = null;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var item = entries[i].Item;
            if (item.TimelineItemContent is TimelineItem.Content.TurnEnd end)
            {
                if (started is { } at && item.Date <= at) { previousEnd = item.Date; break; }
                endStatus ??= end.Value.Status; trailing ??= item.TurnID;
                continue;
            }
            if (item.TurnID is not { } id) continue;
            trailing ??= id;
            if (id != trailing) { previousEnd = item.Date; break; }
            items.Add(item);
        }
        items.Reverse();
        var (st, said) = StateOf(phase, waiting, heads, items);
        return new TurnState(started, phase != RuntimePhase.Idle, st, said, previousEnd, endStatus);
    }

    /// <summary>The turn's working state and, while a tool runs, its description (section 6).</summary>
    public static (NthBeadsState State, string? Description) StateOf(RuntimePhase phase, bool waiting, bool heads, IReadOnlyList<TimelineItem> turn)
    {
        if (waiting) return (NthBeadsState.Waiting, null);
        if (heads) return (NthBeadsState.Hydra, null);
        ToolCall? tool = null; TimelineItem? last = null;
        foreach (var item in turn)
        {
            if (item.TimelineItemContent is TimelineItem.Content.User) continue;
            last = item;
            if (item.TimelineItemContent is TimelineItem.Content.Tool { Value: { ToolCallStatus: ToolCall.Status.Running } running }) tool = running;
        }
        if (tool is not null) return (NthBeadsState.Tool, tool.Title);
        if (last?.TimelineItemContent is TimelineItem.Content.Assistant { Value.IsStreaming: true }) return (NthBeadsState.Streaming, null);
        if (last is null && phase == RuntimePhase.Starting) return (NthBeadsState.Connecting, null);
        return (NthBeadsState.Thinking, null);
    }

    /// <summary>Scriptio after more than two minutes idle or on a conversation's first turn, Ortus otherwise.</summary>
    public static NthBeadsEntrance EntranceFor(DateTimeOffset? previousEnd, DateTimeOffset startedAt) =>
        previousEnd is not { } end || startedAt - end > IdleForScriptio ? NthBeadsEntrance.Scriptio : NthBeadsEntrance.Ortus;

    private void Apply(TurnState now)
    {
        if (now.Running && now.StartedAt is { } at)
        {
            var said = now.State == NthBeadsState.Tool ? now.Description : null;
            if (turn != at)
            {
                // A new turn, also while the last is leaving: it enters once the beads are in the running line and the line is wholly opaque (an
                // arriving line fades in; the beads never fade). Enter during an exit plays Rejoin from wherever the beads are (section 5.2), never a reset.
                turn = at; state = now.State; description = said;
                pending = (EntranceFor(now.PreviousEnd, at), SeedKey(at));
                TryTake();
                return;
            }
            // A new tool description is a step (section 6); the first one comes with the state change itself. A turn that has not entered yet keeps
            // its state for the entrance.
            if (pending is null && said is not null && state == NthBeadsState.Tool && description is not null && said != description) Beads.Step();
            state = now.State; description = said;
            if (pending is null) Beads.SetState(now.State);
        }
        else if (!now.Running && turn is not null)
        {
            // The turn ended: the composer and the text have already changed (they follow the runtime); the exit is cosmetic (a turn that never
            // entered has none).
            turn = null; description = null; pending = null;
            switch (now.EndStatus)
            {
                case TurnStatus.Failed: Beads.Fail(); break;
                case TurnStatus.Interrupted: Beads.Stop(); break;
                default: Beads.Finish(); break;
            }
        }
    }

    private string SeedKey(DateTimeOffset at) => "turn-" + at.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ---- where the beads are ----

    /// <summary>A running line's slot wants the beads. They come at once if they draw nothing where they are. If they are drawn somewhere else they
    /// come on the first frame the slot is wholly opaque (a line that is arriving fades in; the beads never fade), gliding there if it is within a
    /// reflow (<see cref="ReflowLimit"/>); a slot further away gets them once they draw nothing: an exit in progress ends where it is, and a turn
    /// still running there leaves with its exit first. A turn enters only in an opaque slot.</summary>
    public void Claim(Panel slot) { claimant = slot; TryTake(); }

    /// <summary>The slot no longer wants the beads (it left the tree, or its title is leaving).</summary>
    public void Unclaim(Panel slot) { if (ReferenceEquals(claimant, slot)) claimant = null; }

    /// <summary>The beads move into <paramref name="slot"/> now, at their place: the turn-end hand-off to the finished header, or a split line's hold
    /// while its title fades. The slot wants them for no turn: the next running line claims them.</summary>
    public void Host(Panel slot)
    {
        if (ReferenceEquals(claimant, slot)) claimant = null;
        Move(slot);
    }

    /// <summary>How the room a label left for the beads closes (<see cref="CloseRoom"/>): the label glides to its own place.</summary>
    public static readonly Animation RoomClose = Animation.Smooth(duration: .35);

    /// <summary>Calculi (ED-NEW-calculi-rv-1): the room a label leaves for the beads beside it (the finished header's slot, a split line's hold)
    /// closes once the beads are done there: they have left <paramref name="slot"/> (a new line took them) or their exit in it has ended. The label
    /// then glides to the place a view made again gives it (the Mac's header), so a turn that ended here and the same turn reopened read the same.
    /// Nothing waits for this: the label says how the turn ended from the first frame (section 5.1); only the room left for the beads waits for them.
    /// <paramref name="apply"/> gets the room's share, from 1 to 0 (Reduce Motion: at once, the end state).</summary>
    public void CloseRoom(Panel slot, Control owner, Action<double> apply)
    {
        Action<TimeSpan>? poll = null;
        poll = _ =>
        {
            if (ReferenceEquals(Beads.Parent, slot) && !Left()) { FrameClock.Source.Current.RequestFrame(poll!); return; }
            var room = new AnimatedDouble(apply);
            room.Set(1, null);
            room.Set(0, EnvironmentValues.GetAccessibilityReduceMotion(owner) ? null : RoomClose);
        };
        FrameClock.Source.Current.RequestFrame(poll);
    }

    /// <summary>Whether the beads are inside <paramref name="row"/>, and where they sit in it (the running row that the finished block replaces).</summary>
    public bool IsIn(Visual row, out double x)
    {
        x = 0;
        for (Visual? at = Beads; at is not null; at = at.GetVisualParent())
        {
            if (ReferenceEquals(at, row)) return true;
            x += at.Bounds.X;
        }
        return false;
    }

    private void TryTake()
    {
        if (claimant is not { } slot || !slot.IsAttachedToVisualTree()) return;
        if (!ReferenceEquals(Beads.Parent, slot))
        {
            if (Drawn())
            {
                // drawn somewhere else: they never share a fade, and they never jump (no teleport, no glide across the text)
                if (!Opaque(slot)) { Wait(); return; }
                if (!Near(slot))
                {
                    if (turn is { } at && Beads.Current.Exit == NthBeadsExit.None) { Beads.Finish(); pending ??= (NthBeadsEntrance.Ortus, SeedKey(at)); }
                    if (!Left()) { Wait(); return; }
                }
            }
            Move(slot);
        }
        if (pending is { } enter)
        {
            if (!Opaque(slot)) { Wait(); return; }
            pending = null;
            Beads.SetState(state);
            Beads.Enter(enter.Entrance, enter.Seed);
        }
    }

    private void Wait()
    {
        if (!polling) { polling = true; FrameClock.Source.Current.RequestFrame(poll); }
    }

    // Whether the beads draw anything where they are now.
    private bool Drawn() => Beads.IsAttachedToVisualTree() && Beads.IsEffectivelyVisible && !Beads.IsGone;

    // Whether the exit asked has ended (the render thread says so, or its bound has passed).
    private bool Left()
    {
        var now = Beads.Current;
        if (now.Exit == NthBeadsExit.None) return false;
        return Beads.IsGone || Beads.Time - now.ExitAt >= (now.Exit == NthBeadsExit.Fail ? FailBound : ExitBound);
    }

    // Whether the slot is within a reflow of where the beads are (the same scrolled content, each axis within the limit).
    private bool Near(Panel slot)
    {
        if (Beads.Parent is not Visual from || Place(from) is not { } a || Place(slot) is not { } b || !ReferenceEquals(a.Anchor, b.Anchor)) return false;
        return Math.Abs(b.X - a.X) <= ReflowLimit && Math.Abs(b.Y - a.Y) <= ReflowLimit;
    }

    // A visual's layout place in the scrolled content (render transforms and scrolling are not layout places).
    private static (Visual Anchor, double X, double Y)? Place(Visual visual)
    {
        double x = 0, y = 0;
        for (Visual? at = visual; at is not null; at = at.GetVisualParent())
        {
            if (at.GetValue(ReflowAnchorProperty)) return (at, x, y);
            x += at.Bounds.X; y += at.Bounds.Y;
        }
        return null;
    }

    private void Move(Panel slot)
    {
        if (ReferenceEquals(Beads.Parent, slot)) return;
        switch (Beads.Parent) { case Panel panel: panel.Children.Remove(Beads); break; case Decorator decorator: decorator.Child = null; break; }
        slot.Children.Add(Beads);
    }

    private static bool Opaque(Visual visual)
    {
        for (Visual? at = visual; at is not null; at = at.GetVisualParent()) if (at.Opacity < 1 || !at.IsVisible) return false;
        return true;
    }

    // The glyph's layout place, from the anchor (render transforms and scrolling are not layout moves). A move is reported once, after the layout
    // pass that made it; the beads keep their place on screen and glide after it.
    private void Placed()
    {
        if (Place(Beads) is not { } place) { placed = false; anchor = null; return; }
        var (found, x, y) = place;
        if (placed && ReferenceEquals(found, anchor) && !Beads.IsGone) // beads that draw nothing have no place to keep (a room closing around them)
        {
            double dx = x - placeX, dy = y - placeY;
            if ((Math.Abs(dx) > .01 || Math.Abs(dy) > .01) && Math.Abs(dx) <= ReflowLimit && Math.Abs(dy) <= ReflowLimit) Beads.Reflow(dx, dy);
        }
        anchor = found; placeX = x; placeY = y; placed = true;
    }
}
