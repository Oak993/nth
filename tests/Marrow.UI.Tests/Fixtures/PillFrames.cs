using Avalonia;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// PORT-ONLY: the frames a <see cref="ControlPill"/> shows (ED-NEW-pcctl-30, P30). Each gesture runs through
/// <see cref="At"/>, which keeps the calls it made on the recording animator from its time on, with the motion and the
/// layout it left; a frame at any time is then read back the way the compositor plays it: a key-frame track interpolated,
/// a set value as it was set. The closed form the frame must match is <see cref="Expected"/>.
/// </summary>
public sealed class PillFrames(ControlPill pill, RecordingPillAnimator animator)
{
    private readonly List<(double From, PillAnimatorCall Call)> calls = [];
    private readonly List<(double From, PillMotion Motion, PillPose Layout, PillParts Parts)> segments = [];

    /// <summary>Runs <paramref name="gesture"/> at <paramref name="at"/> and keeps what it drew.</summary>
    public void At(double at, Action gesture)
    {
        var before = animator.Calls.Count;
        gesture();
        foreach (var call in animator.Calls.Skip(before)) calls.Add((at, call));
        segments.Add((at, pill.Motion, pill.Motion.Final, pill.Parts));
    }

    /// <summary>
    /// Lets <paramref name="seconds"/> pass on the pill's clock and runs what its timers posted, keeping what that drew from
    /// each track's own start (a line that waited for a slide's end starts at that end), else from the new time.
    /// </summary>
    public void Pass(ManualTimeProvider clock, double seconds)
    {
        var before = animator.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        foreach (var call in animator.Calls.Skip(before)) calls.Add((call.X?.Start ?? pill.Now, call));
    }

    /// <summary>Every call the gestures made, in order.</summary>
    public IEnumerable<PillAnimatorCall> All => calls.Select(c => c.Call);

    /// <summary>Every part the gestures drew.</summary>
    public IEnumerable<Visual> Targets() => calls.Select(c => c.Call.Target).Distinct();

    /// <summary>Every call the gestures made on <paramref name="target"/>.</summary>
    public IEnumerable<PillAnimatorCall> On(Visual target) => calls.Where(c => c.Call.Target == target).Select(c => c.Call);

    /// <summary>The motion in force at <paramref name="now"/>, evaluated there.</summary>
    public PillPose Expected(double now) => Segment(now).Motion.At(now);

    /// <summary>The motion in force at <paramref name="now"/>.</summary>
    public PillMotion MotionAt(double now) => Segment(now).Motion;

    /// <summary>The places the pill was laid out at for the motion in force at <paramref name="now"/>.</summary>
    public PillPlaces Laid(double now)
    {
        var segment = Segment(now);
        return PillPlaces.Of(segment.Layout, segment.Parts);
    }

    /// <summary>The anchor the layout put the stage at for the motion in force at <paramref name="now"/>.</summary>
    public PillVector LaidAnchor(double now) => Segment(now).Layout.Anchor;

    /// <summary>Where the stage draws the anchor at <paramref name="now"/> (its layout plus its translation, the scale's offset in it).</summary>
    public PillVector Anchor(double now) => LaidAnchor(now) + Offset(pill.Stage, now);

    /// <summary>The translation <paramref name="target"/> shows at <paramref name="now"/> (none until one is set).</summary>
    public PillVector Offset(Visual target, double now) => Latest(target, "Translate", "SetTranslation", now) is { } c
        ? c.Kind == "Translate" ? new PillVector(c.X!.Interpolate(now), c.Y!.Interpolate(now)) : new PillVector(c.A, c.B)
        : PillVector.Zero;

    /// <summary>The scale <paramref name="target"/> shows at <paramref name="now"/> (1 until one is set).</summary>
    public double Scale(Visual target, double now) => Latest(target, "Scale", "SetScale", now) is { } c
        ? c.Kind == "Scale" ? c.X!.Interpolate(now) : c.A
        : 1;

    /// <summary>The opacity <paramref name="target"/> shows at <paramref name="now"/> (1 until one is set).</summary>
    public double Opacity(Visual target, double now) => Latest(target, "Fade", "SetOpacity", now) is { } c
        ? c.Kind == "Fade" ? c.X!.Interpolate(now) : c.A
        : 1;

    /// <summary>The size <paramref name="target"/> shows at <paramref name="now"/> (NaN until one is set).</summary>
    public PillVector Size(Visual target, double now) => Latest(target, "Resize", "SetSize", now) is { } c
        ? c.Kind == "Resize" ? new PillVector(c.X!.Interpolate(now), c.Y!.Interpolate(now)) : new PillVector(c.A, c.B)
        : new PillVector(double.NaN, double.NaN);

    private PillAnimatorCall? Latest(Visual target, string animated, string set, double now)
    {
        PillAnimatorCall? latest = null;
        foreach (var (from, call) in calls)
        {
            if (from > now) continue;
            if (call.Target == target && (call.Kind == animated || call.Kind == set)) latest = call;
        }
        return latest;
    }

    private (double From, PillMotion Motion, PillPose Layout, PillParts Parts) Segment(double now)
    {
        var found = segments[0];
        foreach (var segment in segments)
        {
            if (segment.From > now) break;
            found = segment;
        }
        return found;
    }
}
