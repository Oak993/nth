// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11: "when I click another chat with the mouse, the transition should be quick
// and smooth ... a quick but premium transition"). The Mac app flips the selected row's fill in place (SidebarView.swift
// :1370-1372, :1750-1754 `.animation(Chrome.hover, value: isSelected)`); the port glides the selection from the row it leaves to
// the row it lands on, whatever moved it (a click, Ctrl+1..9, Ctrl+Tab, search, a notification, a new chat, an archive).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Sidebar;

/// <summary>What a row draws while the selection glides: its own look, or the selection fill handed to the glide.</summary>
internal enum GlideHold
{
    /// <summary>The row draws itself as always.</summary>
    None,

    /// <summary>The selection is leaving this row: it draws unselected at once (the glide's fill still covers it).</summary>
    Leaving,

    /// <summary>The selection is landing on this row: the glide draws its fill; the row draws the rest of its selected look.</summary>
    Arriving,
}

/// <summary>A sidebar row the selection can glide to.</summary>
internal interface IGlideRow
{
    Guid ThreadID { get; }

    /// <summary>The row's filled shape: where the selection fill sits.</summary>
    Control FillShape { get; }

    /// <summary>Draws the row for the glide's state, at once (no animation of its own).</summary>
    void SetGlideHold(GlideHold hold);
}

/// <summary>
/// A critically damped spring on one axis, stepped exactly (closed form) frame to frame, keeping its velocity so a new
/// target mid-flight bends the motion instead of restarting it.
/// </summary>
internal struct CriticalSpring(double position)
{
    public double Position = position;
    public double Velocity;

    public void Step(double target, double omega, double seconds)
    {
        var offset = Position - target;
        var drift = Velocity + omega * offset;
        var decay = Math.Exp(-omega * seconds);
        Position = target + (offset + drift * seconds) * decay;
        Velocity = (drift - omega * (offset + drift * seconds)) * decay;
    }

    public readonly bool IsSettled(double target) => Math.Abs(Position - target) < 0.3 && Math.Abs(Velocity) < 8;
}

/// <summary>
/// The selection's glide: a fill the size and shape of a selected row (Chrome.overlay at 0.12 in the row's 7 pt continuous
/// corners) that springs from the row the selection left to the row it landed on, behind the list. At rest it is not drawn: the
/// rows draw their own selection exactly as before. The spring is stiff and critically damped (response 0.15 s, no overshoot,
/// settled in about 160 ms); it moves by a render transform, so the list never lays out for it.
/// </summary>
internal sealed class SelectionGlide : Decorator
{
    /// <summary>The spring's response: 2π / ω. 0.15 s settles a row-to-row move in about 160 ms.</summary>
    public const double Response = 0.15;

    private static readonly double Omega = 2 * Math.PI / Response;

    private readonly Control list;
    private readonly bool enabled;
    private readonly Border pill = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
    private readonly TranslateTransform place = new();
    private readonly Dictionary<Guid, IGlideRow> rows = [];
    private Guid? selected;
    private bool primed;
    private IGlideRow? arriving;
    private IGlideRow? leaving;
    private LandingSpring x, y, width, height;
    private Action<TimeSpan>? tick;
    private IFrameSource? frames;
    private ContinuousInstant lastFrame;
    private ContinuousInstant started;

    /// <param name="list">The list the rows sit in: the glide reads row frames in its space, so a scroll mid-glide carries both.</param>
    /// <param name="enabled">False in the floating sidebar, which closes as the selection changes.</param>
    public SelectionGlide(Control list, bool enabled)
    {
        this.list = list;
        this.enabled = enabled;
        IsHitTestVisible = false;
        ClipToBounds = true;
        pill.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        pill.Background = Chrome.Overlay(SidebarThreadRow.Fill(isSelected: true, isHovering: false)).Brush(pill);
        pill.RenderTransform = place;
        Child = new Canvas { Children = { pill } };
        this.Track(() => AppEnvironment.GetAppModel(this).SelectedThreadID, OnSelection);
        DetachedFromVisualTree += (_, _) => Finish();
    }

    /// <summary>Whether a glide is running (tests and the rows' own reads).</summary>
    internal bool IsGliding => tick is not null;

    /// <summary>The fill's frame in the glide's own space while it runs.</summary>
    internal Rect PillFrame => new(place.X, place.Y, pill.Width, pill.Height);

    internal void Register(IGlideRow row) => rows[row.ThreadID] = row;

    internal void Unregister(IGlideRow row)
    {
        if (rows.TryGetValue(row.ThreadID, out var known) && ReferenceEquals(known, row)) rows.Remove(row.ThreadID);
        if (ReferenceEquals(arriving, row) || ReferenceEquals(leaving, row)) Finish();
    }

    private void OnSelection(Guid? next)
    {
        if (!primed)
        {
            primed = true;
            selected = next;
            return;
        }
        var previous = selected;
        selected = next;
        if (previous == next) return;
        if (!enabled || next is not { } nextID || !rows.TryGetValue(nextID, out var to) || !FrameClock.IsBound ||
            SessionNumbers.ReducesMotion(this) || !IsOnScreen(to))
        {
            Finish();
            return;
        }
        // A running glide is retargeted from where it is, its velocity kept; a new one starts on the row the selection left.
        if (!IsGliding)
        {
            if (previous is not { } previousID || !rows.TryGetValue(previousID, out var fromRow) || FrameOf(fromRow) is not { } frame)
            {
                Finish();
                return;
            }
            x = new LandingSpring(frame.X);
            y = new LandingSpring(frame.Y);
            width = new LandingSpring(frame.Width);
            height = new LandingSpring(frame.Height);
        }
        // The row it left draws unselected now, the row it lands on hands its fill to the glide; a row passed through on a
        // retarget is released.
        var departed = arriving is { } passing && !ReferenceEquals(passing, to) ? passing : null;
        if (leaving is { } oldLeaving && !ReferenceEquals(oldLeaving, to)) oldLeaving.SetGlideHold(GlideHold.None);
        leaving = departed ?? (previous is { } p && rows.TryGetValue(p, out var left) && !ReferenceEquals(left, to) ? left : null);
        leaving?.SetGlideHold(GlideHold.Leaving);
        arriving = to;
        to.SetGlideHold(GlideHold.Arriving);
        Draw();
        pill.Opacity = 1;
        if (tick is not null) return; // the running frame loop takes the new target
        frames = FrameClock.Source.Current;
        lastFrame = SwiftClock.Now;
        started = lastFrame;
        tick = Frame;
        frames.RequestFrame(tick);
    }

    private void Frame(TimeSpan _)
    {
        if (tick is null || arriving is not { } to || FrameOf(to) is not { } target)
        {
            Finish();
            return;
        }
        var now = SwiftClock.Now;
        var seconds = Math.Clamp((now - lastFrame).TotalSeconds, 0, 0.05);
        lastFrame = now;
        // ED-NEW-perf-motion-10: each axis lands on the row exactly, with no speed, so the hand-over to the row's own fill is no step.
        var settled = Follow(ref x, target.X, seconds) & Follow(ref y, target.Y, seconds) & Follow(ref width, target.Width, seconds) &
            Follow(ref height, target.Height, seconds);
        if (settled || (now - started).TotalSeconds > 0.6)
        {
            Finish();
            return;
        }
        Draw();
        frames?.RequestFrame(tick);
    }

    // A row that moved (the list scrolled, a row above grew) gives its axis a new segment from where it is, at its speed.
    private static bool Follow(ref LandingSpring spring, double target, double seconds)
    {
        if (spring.Target != target) spring.Aim(target, Omega, Rest);
        return spring.Advance(seconds);
    }

    /// <summary>What may be left of a glide (and of its speed over the spring's time) when it rests, in points.</summary>
    internal const double Rest = 0.3;

    private void Draw()
    {
        var origin = list.TranslatePoint(new Point(x.Position, y.Position), this) ?? default;
        place.X = origin.X;
        place.Y = origin.Y;
        // Only a size change (rows of two heights) asks the fill, and nothing else, to measure again.
        if (double.IsNaN(pill.Width) || Math.Abs(pill.Width - width.Position) > 0.01) pill.Width = Math.Max(0, width.Position);
        if (double.IsNaN(pill.Height) || Math.Abs(pill.Height - height.Position) > 0.01) pill.Height = Math.Max(0, height.Position);
    }

    /// <summary>Lands the glide: the fill goes and the row it reached draws its selection, in the same frame.</summary>
    private void Finish()
    {
        if (tick is { } running) frames?.CancelFrame(running);
        tick = null;
        frames = null;
        pill.Opacity = 0;
        var landed = arriving;
        var left = leaving;
        arriving = null;
        leaving = null;
        landed?.SetGlideHold(GlideHold.None);
        left?.SetGlideHold(GlideHold.None);
    }

    private Rect? FrameOf(IGlideRow row)
    {
        var shape = row.FillShape;
        if (TopLevel.GetTopLevel(shape) is null || !shape.IsEffectivelyVisible) return null;
        return shape.TranslatePoint(default, list) is { } origin ? new Rect(origin, shape.Bounds.Size) : null;
    }

    private bool IsOnScreen(IGlideRow row) => FrameOf(row) is not null && Bounds.Width >= 1 && Bounds.Height >= 1 && TopLevel.GetTopLevel(this) is not null;
}
