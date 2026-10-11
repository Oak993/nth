using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window;

// ED-NEW-textsize-3: a zoom motion on a bare root, on fake clocks; frames at the given rate, the clock moved first.
internal sealed class ZoomMotionRig : IDisposable
{
    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;
    public readonly ManualFrameSource Frames = new();
    public readonly ManualTimeProvider Time = new();
    public readonly List<double> Applied = [];
    public readonly ZoomMotion Motion;
    public readonly Border Owner = new();
    public int Rests;

    public ZoomMotionRig(double initial = AppZoom.BaseScale)
    {
        frameScope = FrameClock.Source.Override(Frames);
        clockScope = SwiftClock.Provider.Override(Time);
        Motion = new ZoomMotion(Owner, initial, Applied.Add);
        Motion.Rested += _ => Rests++;
        Applied.Clear();
    }

    public double Frame(double hertz = 60)
    {
        var step = TimeSpan.FromSeconds(1 / hertz);
        Time.Advance(step);
        Frames.Advance(step);
        return Motion.Value;
    }

    // Frames until the motion rests (or a second passes): the values drawn, one a frame.
    public List<double> RunOut(double hertz = 60)
    {
        var drawn = new List<double>();
        for (var frame = 0; frame < hertz && Motion.IsMoving; frame++) drawn.Add(Frame(hertz));
        return drawn;
    }

    public void Dispose()
    {
        clockScope.Dispose();
        frameScope.Dispose();
    }
}
