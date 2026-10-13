// PORT-ONLY (ED-NEW-textsize-3, owner 2026-10-11: "no static movement anywhere; zoom in and out must also be smooth"): the app
// zoom moves from the scale it is drawn at to a new one over about a quarter of a second instead of jumping there, keeping
// the reader's place.
//
// The source sets the new scale at once (AppZoom.swift:24, WindowManager.swift:175). Here a window root's scale follows a
// critically damped spring on the frame clock. A change gives the spring the speed of an exponential approach to the new
// scale (an ease-out: fastest at the press, slowing onto the scale), and a change while it moves adds its own push to the
// motion under way, so presses in a row accumulate from where the scale is, at its speed. Each frame lays the window out at
// that frame's scale (measured cheap, ED-NEW-textsize-3: no frame is a stretched picture of another frame's layout, so the
// edges, the toolbar and the composer are where they belong every frame and nothing moves when the motion ends), except the
// text that would rewrap every frame: a thread's column takes its width at the new scale at the press and keeps it
// (ThreadTimeline.CurrentLayoutWidth). The content at the focus stays where it was: the scroll view under the pointer (or,
// with the pointer outside the window, under the reading line a third of the way down) is scrolled so the line at the
// focus stays on its row of pixels, and every other scroll view that overflows keeps the line at its own reading line
// (ED-NEW-perf-motion-3: a pointer resting on the composer, the header or the sidebar keeps the thread's place too). The
// last frame is the new scale exactly. Reduce Motion takes it at once.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Common;

/// <summary>A window root's zoom scale on its way to a new one (ED-NEW-textsize-3).</summary>
public sealed class ZoomMotion
{
    /// <summary>
    /// The spring's rate, per second. Started as an exponential approach, the scale covers 90 % of a change in 89 ms and
    /// 99 % in 177 ms, and comes to rest (<see cref="Rest"/>) at 262 ms for a change of a tenth (100 to 110 %).
    /// </summary>
    public const double Rate = 26;

    /// <summary>The motion rests on its scale once what is left of it (and of its speed, over the spring's time) is under
    /// this fraction of the scale: a quarter of a pixel at the far edge of a window 2560 pixels wide.</summary>
    public const double Rest = 1e-4;

    /// <summary>Where the reading position is kept when the pointer is not over the window: this far down its height.</summary>
    public const double ReadingLine = 1.0 / 3;

    public static readonly AttachedProperty<ZoomMotion?> MotionProperty =
        AvaloniaProperty.RegisterAttached<ZoomMotion, Control, ZoomMotion?>("Motion");

    /// <summary>The motion of the window root <paramref name="root"/> (HostedEnvironment's body), if it zooms.</summary>
    public static ZoomMotion? Of(Control root) => root.GetValue(MotionProperty);

    /// <summary>The motion of the window root <paramref name="visual"/> is drawn in, if any.</summary>
    public static ZoomMotion? For(Visual visual) =>
        visual.GetSelfAndVisualAncestors().OfType<Control>().Select(Of).FirstOrDefault(motion => motion is not null);

    private readonly Control owner;
    private readonly Action<double> apply;
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private Action<TimeSpan>? pending;
    private ContinuousInstant last;
    private IReadOnlyList<ZoomAnchor> anchors = [];
    private Point? pointer;

    /// <param name="owner">The window root, outside the zoom: its points are the window's.</param>
    /// <param name="initial">The scale it is drawn at.</param>
    /// <param name="apply">Draws the root at a scale (and lays nothing out: the motion does when it keeps the focus).</param>
    public ZoomMotion(Control owner, double initial, Action<double> apply)
    {
        this.owner = owner;
        this.apply = apply;
        frame = Frame;
        Value = Target = initial;
        owner.SetValue(MotionProperty, this);
        // The pointer over the window is the focus: tunnelled and handled ones too, so nothing under it hides it.
        owner.AddHandler(InputElement.PointerMovedEvent, (_, e) => pointer = e.GetPosition(owner), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        owner.AddHandler(InputElement.PointerEnteredEvent, (_, e) => pointer = e.GetPosition(owner), RoutingStrategies.Direct, true);
        owner.AddHandler(InputElement.PointerExitedEvent, (_, _) => pointer = null, RoutingStrategies.Direct, true);
        owner.DetachedFromVisualTree += (_, _) => Finish();
    }

    /// <summary>The scale drawn now.</summary>
    public double Value { get; private set; }

    /// <summary>The scale the motion is headed to (the scale drawn, at rest).</summary>
    public double Target { get; private set; }

    /// <summary>The scale's speed, per second.</summary>
    public double Velocity { get; private set; }

    /// <summary>Whether a frame is awaited.</summary>
    public bool IsMoving => pending is not null;

    /// <summary>A new scale was asked for (the zoom indicator shows).</summary>
    public event Action<double>? Retargeted;

    /// <summary>The root was drawn at a scale (each frame of a motion, and at once under Reduce Motion).</summary>
    public event Action<double>? Moved;

    /// <summary>The motion came to rest on its target.</summary>
    public event Action<double>? Rested;

    /// <summary>The focus in the owner's points: the pointer over the window, else null (the reading line).</summary>
    public Point? Pointer => pointer;

    /// <summary>Moves to <paramref name="target"/>: along the spring, or at once without a frame clock or under Reduce Motion.</summary>
    public void To(double target)
    {
        if (target == Target && (IsMoving || Value == target)) return;
        anchors = ZoomAnchor.CaptureAll(owner, pointer);
        if (!FrameClock.IsBound || ReduceMotion)
        {
            Stop();
            Value = Target = target;
            Velocity = 0;
            Retargeted?.Invoke(target);
            Show(target);
            anchors = [];
            Rested?.Invoke(target);
            return;
        }
        // The push: from rest, the speed of an exponential approach (c2 = 0 below); while moving, it adds to the speed.
        Velocity += Rate * (target - Target);
        Target = target;
        if (pending is null)
        {
            last = SwiftClock.Now;
            source = FrameClock.Source.Current;
            pending = frame;
            source.RequestFrame(frame);
        }
        Retargeted?.Invoke(target); // moving: what follows the motion sees it under way
        // What the press changed in the layout (a thread column taking its width at the new scale) is laid out now, with the
        // focus kept, so no frame shows it before the motion's first.
        Keep();
    }

    /// <summary>A motion under way lands on its target at once (the root leaves the window, or Reduce Motion came on).</summary>
    public void Finish()
    {
        if (pending is null) return;
        Stop();
        Value = Target;
        Velocity = 0;
        Show(Target);
        anchors = [];
        Rested?.Invoke(Target);
    }

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(owner) || Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void Frame(TimeSpan _)
    {
        // The motion stays under way (IsMoving) through the frame's own layout, so what holds still while it moves (a thread's
        // column) holds through the last frame too.
        var now = SwiftClock.Now;
        var dt = Math.Max(0, (now - last).TotalSeconds);
        last = now;
        // The critically damped spring solved exactly over the frame's span, so the curve is the same at 60 and 144 Hz:
        // x(t) = target + (c1 + c2 t) e^(-rate t).
        var c1 = Value - Target;
        var c2 = Velocity + Rate * c1;
        var decay = Math.Exp(-Rate * dt);
        var value = Target + (c1 + c2 * dt) * decay;
        var velocity = (c2 - Rate * (c1 + c2 * dt)) * decay;
        var rests = Math.Max(Math.Abs(value - Target), Math.Abs(velocity) / Rate) <= Rest * Target;
        Value = rests ? Target : value;
        Velocity = rests ? 0 : velocity;
        Show(Value);
        if (rests)
        {
            pending = null;
            anchors = [];
            Rested?.Invoke(Target);
            return;
        }
        pending = frame;
        source!.RequestFrame(frame);
    }

    private void Show(double scale)
    {
        apply(scale);
        Keep();
        Moved?.Invoke(scale);
    }

    private void Keep()
    {
        foreach (var anchor in anchors) anchor.Keep();
    }

    private void Stop()
    {
        if (pending is { } waiting) source?.CancelFrame(waiting);
        pending = null;
    }
}

/// <summary>
/// The content at the zoom's focus, kept on its row of the window while the scale changes (ED-NEW-textsize-3): the innermost
/// scroll view at the focus that scrolls up and down, the innermost element of its content at the focus, and the point of
/// that element at the focus, as a fraction of its height (a paragraph that rewraps keeps the line at the focus near it).
/// A scroll view at its end stays at its end.
/// </summary>
internal sealed class ZoomAnchor
{
    private readonly Control root;
    private readonly ScrollViewer viewer;
    private readonly Visual? element;
    private readonly double fraction;
    private readonly double row;
    private readonly bool atEnd;

    private ZoomAnchor(Control root, ScrollViewer viewer, Visual? element, double fraction, double row, bool atEnd)
    {
        this.root = root;
        this.viewer = viewer;
        this.element = element;
        this.fraction = fraction;
        this.row = row;
        this.atEnd = atEnd;
    }

    /// <summary>The scroll view kept, for tests.</summary>
    internal ScrollViewer Viewer => viewer;

    /// <summary>
    /// The anchors a press keeps: the innermost scroll view at the focus (the pointer over the window, else the root's reading
    /// line), and every other scroll view that overflows and is scrolled from its top, outside that one and outside each other (the
    /// outermost), at its own reading line, half its width across and a third of its height down (ED-NEW-perf-motion-3).
    /// </summary>
    public static IReadOnlyList<ZoomAnchor> CaptureAll(Control root, Point? pointer)
    {
        if (!root.IsAttachedToVisualTree() || root.Bounds.Width <= 0 || root.Bounds.Height <= 0) return [];
        var window = new Rect(root.Bounds.Size);
        var focus = pointer is { } at && window.Contains(at)
            ? at
            : new Point(root.Bounds.Width / 2, root.Bounds.Height * ZoomMotion.ReadingLine);
        var viewers = new List<(ScrollViewer Viewer, Rect Box)>();
        foreach (var candidate in root.GetVisualDescendants().OfType<ScrollViewer>()) // ancestors before their descendants
        {
            if (!candidate.IsEffectivelyVisible || candidate.Extent.Height <= candidate.Viewport.Height + 0.5) continue;
            if (Box(candidate, root) is { Width: > 0, Height: > 0 } box && box.Intersects(window)) viewers.Add((candidate, box));
        }
        var anchors = new List<ZoomAnchor>();
        var taken = new List<ScrollViewer>();
        ScrollViewer? focused = null;
        foreach (var (candidate, box) in viewers)
            if (box.Contains(focus) && (focused is null || candidate.GetVisualAncestors().Contains(focused))) focused = candidate; // the innermost
        if (focused is not null)
        {
            taken.Add(focused);
            if (CaptureIn(root, focused, focus) is { } anchor) anchors.Add(anchor);
        }
        foreach (var (candidate, box) in viewers)
        {
            if (taken.Any(held => ReferenceEquals(held, candidate) || held.IsVisualAncestorOf(candidate) || candidate.IsVisualAncestorOf(held))) continue;
            taken.Add(candidate);
            if (candidate.Offset.Y <= 0.5) continue; // one at its top stays at its top, as before
            var line = new Point(box.X + box.Width / 2, box.Y + box.Height * ZoomMotion.ReadingLine);
            if (window.Contains(line) && CaptureIn(root, candidate, line) is { } anchor) anchors.Add(anchor);
        }
        return anchors;
    }

    private static ZoomAnchor? CaptureIn(Control root, ScrollViewer viewer, Point focus)
    {
        var end = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        if (viewer.Offset.Y >= end - 0.5) return new ZoomAnchor(root, viewer, null, 0, focus.Y, atEnd: true);
        // The innermost element of the content at the focus row (and column, where one holds it).
        Visual? element = null;
        var level = viewer.Content as Visual;
        while (level is not null && Rows(level, root, focus.Y) is { } bounds && bounds.Height > 0)
        {
            element = level;
            level = level.GetVisualChildren().Where(child => child.IsVisible && child.Bounds.Height > 0)
                .OrderBy(child => Contains(child, root, focus) ? 0 : 1)
                .FirstOrDefault(child => Rows(child, root, focus.Y) is not null);
        }
        if (element is null) return null;
        var top = element.TranslatePoint(default, root)!.Value.Y;
        var height = element.TranslatePoint(new Point(0, element.Bounds.Height), root)!.Value.Y - top;
        return new ZoomAnchor(root, viewer, element, height > 0 ? (focus.Y - top) / height : 0, focus.Y, atEnd: false);
    }

    private static Rect? Box(Visual visual, Visual root) =>
        visual.TranslatePoint(default, root) is { } top && visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), root) is { } bottom
            ? new Rect(top, bottom) : null;

    // The element's span of the root's rows, when it holds the row.
    private static Rect? Rows(Visual visual, Visual root, double y)
    {
        if (visual.TranslatePoint(default, root) is not { } top || visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), root) is not { } bottom) return null;
        return y >= top.Y && y < bottom.Y ? new Rect(top, bottom) : null;
    }

    private static bool Contains(Visual visual, Visual root, Point point) =>
        visual.TranslatePoint(default, root) is { } top && visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), root) is { } bottom
        && new Rect(top, bottom).Contains(point);

    /// <summary>Lays the window out at the scale just set and scrolls so the content at the focus is back on its row.</summary>
    public void Keep()
    {
        if (!viewer.IsAttachedToVisualTree()) return;
        viewer.UpdateLayout();
        var end = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        double wanted;
        if (atEnd) wanted = end;
        else
        {
            if (element is null || !element.IsAttachedToVisualTree()) return;
            if (element.TranslatePoint(default, root) is not { } top || element.TranslatePoint(new Point(0, element.Bounds.Height), root) is not { } bottom) return;
            var now = top.Y + fraction * (bottom.Y - top.Y);
            // The offset is in the scroll view's own points; the root's rows are that many points times its scale.
            var scale = viewer.TransformToVisual(root) is { } matrix && matrix.M22 > 0 ? matrix.M22 : 1;
            // From the offset the content is drawn at (layout rounding puts it on a whole device pixel of the scroll view), not the
            // offset asked for: carrying the asked offset's fraction over would add up to a point to the rounding of the next one.
            var shown = viewer.Content is Avalonia.Layout.Layoutable content && content.GetVisualParent() is Visual presenter && content.TranslatePoint(default, presenter) is { } drawn
                ? content.Margin.Top - drawn.Y
                : viewer.Offset.Y;
            wanted = shown + (now - row) / scale;
        }
        wanted = Math.Clamp(wanted, 0, end);
        if (Math.Abs(wanted - viewer.Offset.Y) < 1e-6) return;
        var offset = new Vector(viewer.Offset.X, wanted);
        if (SoftScroll.GetIsEnabled(viewer)) SoftScroll.ScrollTo(viewer, offset, null);
        else viewer.Offset = offset;
        viewer.UpdateLayout();
    }
}
