// PORT-ONLY: Tactus pill v2, dragging and releasing the pill (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3). Pure: the release
// velocity from the pointer's own sample times, the flick's projection and the anchor it lands on, the rubber bands at the
// screen's edges, and when a press becomes a drag. Times are milliseconds on the input events' clock; positions are DIP.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>The three resting places on a monitor's top edge.</summary>
public enum PillAnchor
{
    Left,
    Centre,
    Right,
}

/// <summary>A monitor's work area in DIP (the taskbar left out).</summary>
public readonly record struct PillArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CentreX => Left + (Width / 2);

    public bool Contains(PillVector point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>The distance from a point to this area (0 inside).</summary>
    public double DistanceTo(PillVector point)
    {
        var dx = Math.Max(Math.Max(Left - point.X, 0), point.X - Right);
        var dy = Math.Max(Math.Max(Top - point.Y, 0), point.Y - Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>The release velocity: the least-squares slope of the last 80 ms of pointer samples.</summary>
public sealed class PillVelocityEstimator
{
    /// <summary>Samples older than this before the newest are left out.</summary>
    public const double WindowMs = 80;

    /// <summary>At least this many samples, or the velocity is zero.</summary>
    public const int MinimumSamples = 3;

    /// <summary>A newest sample older than this at release means the user stopped before letting go.</summary>
    public const double StaleMs = 50;

    /// <summary>The fastest release on each axis (DIP/s).</summary>
    public const double MaxSpeed = 8_000;

    private readonly List<(double At, PillVector Point)> samples = [];

    /// <summary>Adds a pointer sample, timed by the input event's own timestamp.</summary>
    public void Add(double atMs, PillVector point)
    {
        samples.Add((atMs, point));
        // Keep a little more than the window, so a late release still finds its samples.
        samples.RemoveAll(s => s.At < atMs - (WindowMs * 4));
    }

    public void Clear() => samples.Clear();

    /// <summary>The velocity at a release at <paramref name="releaseMs"/>, in DIP/s.</summary>
    public PillVector Velocity(double releaseMs)
    {
        if (samples.Count == 0) return PillVector.Zero;
        var newest = samples[^1].At;
        if (releaseMs - newest > StaleMs) return PillVector.Zero;
        var recent = samples.Where(s => s.At >= newest - WindowMs).ToList();
        if (recent.Count < MinimumSamples) return PillVector.Zero;
        var meanT = recent.Average(s => s.At);
        var meanX = recent.Average(s => s.Point.X);
        var meanY = recent.Average(s => s.Point.Y);
        var tt = recent.Sum(s => (s.At - meanT) * (s.At - meanT));
        if (tt <= 0) return PillVector.Zero;
        var vx = recent.Sum(s => (s.At - meanT) * (s.Point.X - meanX)) / tt * 1000;
        var vy = recent.Sum(s => (s.At - meanT) * (s.Point.Y - meanY)) / tt * 1000;
        // Each axis is clamped, as the canvas measures it.
        return new PillVector(Math.Clamp(vx, -MaxSpeed, MaxSpeed), Math.Clamp(vy, -MaxSpeed, MaxSpeed));
    }
}

/// <summary>Where the pill rests, and where a release sends it.</summary>
public static class PillAnchors
{
    /// <summary>Gap from the work area's top.</summary>
    public const double TopGap = 8;

    /// <summary>Gap from the work area's side for Left and Right.</summary>
    public const double SideGap = 16;

    /// <summary>UIKit's flick projection with deceleration rate 0.998: 0.998 / (1 - 0.998) / 1000 = 0.499 s.</summary>
    public const double ProjectionSeconds = 0.998 / (1 - 0.998) / 1000;

    /// <summary>The pill's top-left at <paramref name="anchor"/> on a work area, for a pill <paramref name="width"/> wide.</summary>
    public static PillVector Position(PillAnchor anchor, PillArea work, double width) => anchor switch
    {
        PillAnchor.Left => new PillVector(work.Left + SideGap, work.Top + TopGap),
        PillAnchor.Right => new PillVector(work.Right - SideGap - width, work.Top + TopGap),
        _ => new PillVector(work.CentreX - (width / 2), work.Top + TopGap),
    };

    /// <summary>The x of the pill's centre at <paramref name="anchor"/>.</summary>
    public static double CentreX(PillAnchor anchor, PillArea work, double width) => Position(anchor, work, width).X + (width / 2);

    /// <summary>The monitor under a point, else the nearest one (MonitorFromPoint, nearest).</summary>
    public static int MonitorAt(IReadOnlyList<PillArea> monitors, PillVector point)
    {
        if (monitors.Count == 0) throw new ArgumentException("There is no monitor.", nameof(monitors));
        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Contains(point)) return i;
        }
        var best = 0;
        for (var i = 1; i < monitors.Count; i++)
        {
            if (monitors[i].DistanceTo(point) < monitors[best].DistanceTo(point)) best = i;
        }
        return best;
    }

    /// <summary>
    /// The anchor a release chooses: on the monitor under the release point (a flick never throws the pill to another
    /// monitor), the anchor nearest the pill's projected centre (release centre + 0.499 s of its velocity, clamped to that
    /// work area). The vertical position plays no part.
    /// </summary>
    public static (int Monitor, PillAnchor Anchor) Choose(IReadOnlyList<PillArea> monitors, PillVector releasePoint, double pillCentreX, double velocityX, double width)
    {
        var monitor = MonitorAt(monitors, releasePoint);
        var work = monitors[monitor];
        var projected = Math.Clamp(pillCentreX + (velocityX * ProjectionSeconds), work.Left, work.Right);
        var best = PillAnchor.Centre;
        var bestDistance = double.MaxValue;
        foreach (var anchor in new[] { PillAnchor.Left, PillAnchor.Centre, PillAnchor.Right })
        {
            var distance = Math.Abs(CentreX(anchor, work, width) - projected);
            if (distance < bestDistance)
            {
                best = anchor;
                bestDistance = distance;
            }
        }
        return (monitor, best);
    }
}

/// <summary>The rubber bands the pill meets while dragged: shown = D (1 - 1 / (d 0.55 / D + 1)) for a pointer offset d.</summary>
public static class PillRubberBand
{
    public const double Stretch = 0.55;

    /// <summary>The most the pill shows below its row.</summary>
    public const double Down = 120;

    /// <summary>The most the pill shows above its row (it cannot leave the top).</summary>
    public const double Up = 8;

    /// <summary>The most the pill shows past a Left or Right anchor with no monitor beyond.</summary>
    public const double Side = 16;

    /// <summary>The shown offset for a pointer offset <paramref name="offset"/> (either sign) against a band of <paramref name="limit"/>.</summary>
    public static double Shown(double offset, double limit)
    {
        if (offset == 0 || limit <= 0) return 0;
        var magnitude = limit * (1 - (1 / ((Math.Abs(offset) * Stretch / limit) + 1)));
        return Math.Sign(offset) * magnitude;
    }

    /// <summary>The vertical offset shown for a pointer offset (down positive): 120 DIP down at most, 8 up.</summary>
    public static double Vertical(double offset) => Shown(offset, offset >= 0 ? Down : Up);
}

/// <summary>How a press on the pill ends.</summary>
public enum PillPressOutcome
{
    /// <summary>Still pressed.</summary>
    Pending,

    /// <summary>Released on the pill, under the threshold, not on a button: the pill's click.</summary>
    Click,

    /// <summary>Released on the button it began on: that button fires.</summary>
    Button,

    /// <summary>Released off the button it began on: nothing fires.</summary>
    Cancelled,

    /// <summary>The end of a drag: the release and the click it makes are swallowed; the pill travels.</summary>
    DragEnd,
}

/// <summary>What pressed the pill.</summary>
public enum PillPointerKind
{
    Mouse,
    Pen,
    Touch,
}

/// <summary>
/// One press on the pill (DESIGN.md 10.3). A press on a button never becomes a drag, however much it wobbles, and fires on
/// release if the pointer is still on it, so a shaky press on Stop always stops. A press elsewhere becomes a drag past the
/// threshold (the larger of the system drag size and 4 DIP for a mouse; 10 DIP for pen and touch); at the threshold the
/// grab point is re-based to where the pointer is, so the pill starts from rest with no jump, and tracks 1:1 from there.
/// </summary>
public sealed class PillPress
{
    public const double MouseMinimum = 4;
    public const double TouchThreshold = 10;

    private readonly PillVector pillAtPress;

    public PillPress(PillVector point, PillVector pillPosition, bool onButton, PillPointerKind kind, double systemDragDip = MouseMinimum)
    {
        Start = point;
        pillAtPress = pillPosition;
        OnButton = onButton;
        Threshold = kind == PillPointerKind.Mouse ? Math.Max(systemDragDip, MouseMinimum) : TouchThreshold;
    }

    public PillVector Start { get; }

    public bool OnButton { get; }

    /// <summary>The drag threshold for this press (DIP).</summary>
    public double Threshold { get; }

    /// <summary>True once this press became a drag.</summary>
    public bool Dragging { get; private set; }

    /// <summary>The grab point the drag follows from (re-based at the threshold).</summary>
    public PillVector Grab { get; private set; }

    /// <summary>The pointer moved: the pill's position while dragging (1:1 from the re-based grab), or null while it is a press.</summary>
    public PillVector? Move(PillVector point)
    {
        if (OnButton) return null;
        if (!Dragging)
        {
            if ((point - Start).Length < Threshold) return null;
            Dragging = true;
            Grab = point;
        }
        return pillAtPress + (point - Grab);
    }

    /// <summary>The release: a drag's end, the pill's click, a button's press, or a button press cancelled by leaving it.</summary>
    public PillPressOutcome Release(bool stillOnButton)
    {
        if (OnButton) return stillOnButton ? PillPressOutcome.Button : PillPressOutcome.Cancelled;
        return Dragging ? PillPressOutcome.DragEnd : PillPressOutcome.Click;
    }

    /// <summary>A keyboard activation (Space or Enter on a focused button) is never swallowed, whatever a drag did.</summary>
    public static bool SwallowsKeyboardActivation => false;
}
