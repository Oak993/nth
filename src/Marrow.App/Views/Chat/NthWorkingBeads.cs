// PORT-ONLY: Calculi WU5 (ED-NEW-calculi-wu5-1; DESIGN.md sections 5, 5.1, 5.2, 6, 7 and 9). The working line's beads: one NthBeadsControl per
// thread, which follows the turn's state and is handed from place to place, never made again: the running line's slot, the finished header that takes
// the running block's place at the turn's end (the exit plays there, beside the "Worked for" label that is already shown), and the next turn's line
// (a new turn while the last is leaving plays Rejoin from wherever the beads are). The UI follows the turn state, never the animation: nothing here
// waits for the beads, and nothing listens for gone.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;

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
    private Panel? claimant; // the slot that wants the beads
    private bool polling;
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
                // A new turn, also while the last is leaving: Enter, which plays Rejoin from wherever the beads are (section 5.2), never a reset.
                turn = at; state = now.State; description = said;
                Beads.SetState(now.State);
                Beads.Enter(EntranceFor(now.PreviousEnd, at), SeedKey(at));
                return;
            }
            // A new tool description is a step (section 6); the first one comes with the state change itself.
            if (said is not null && state == NthBeadsState.Tool && description is not null && said != description) Beads.Step();
            state = now.State; description = said;
            Beads.SetState(now.State);
        }
        else if (!now.Running && turn is not null)
        {
            // The turn ended: the composer and the text have already changed (they follow the runtime); the exit is cosmetic.
            turn = null; description = null;
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

    /// <summary>A running line's slot wants the beads. They come at once unless they are on screen somewhere else and the slot is not yet fully
    /// opaque (a line that is arriving fades in; the beads never fade): then they come on the first frame it is.</summary>
    public void Claim(Panel slot) { claimant = slot; TryTake(); }

    /// <summary>The slot no longer wants the beads (it left the tree, or its title is leaving).</summary>
    public void Unclaim(Panel slot) { if (ReferenceEquals(claimant, slot)) claimant = null; }

    /// <summary>The beads move into <paramref name="slot"/> now (the turn-end hand-off to the finished header).</summary>
    public void Host(Panel slot) { claimant = slot; Move(slot); }

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
        if (claimant is not { } slot || ReferenceEquals(Beads.Parent, slot) || !slot.IsAttachedToVisualTree()) return;
        if (Beads.IsAttachedToVisualTree() && Beads.IsEffectivelyVisible && !Opaque(slot))
        {
            if (!polling) { polling = true; FrameClock.Source.Current.RequestFrame(poll); }
            return;
        }
        Move(slot);
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
        Visual? found = null; double x = 0, y = 0;
        for (Visual? at = Beads; at is not null; at = at.GetVisualParent())
        {
            if (at.GetValue(ReflowAnchorProperty)) { found = at; break; }
            x += at.Bounds.X; y += at.Bounds.Y;
        }
        if (found is null) { placed = false; anchor = null; return; }
        if (placed && ReferenceEquals(found, anchor))
        {
            double dx = x - placeX, dy = y - placeY;
            if ((Math.Abs(dx) > .01 || Math.Abs(dy) > .01) && Math.Abs(dx) <= ReflowLimit && Math.Abs(dy) <= ReflowLimit) Beads.Reflow(dx, dy);
        }
        anchor = found; placeX = x; placeY = y; placed = true;
    }
}
