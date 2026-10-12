using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for Window/FrameClockWakeNativeTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
// ED-NEW-lean-7: the production frame source seen two ways, each counting the frame callbacks it answers, and one run's result.
// The same production source seen as a plain frame source: a draw loop asks it at every frame, as before ED-NEW-lean-7.
public sealed class CountingEveryFrameSource(IFrameSource inner) : IFrameSource
{
    public long Answered;
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> wrapped = [];

    private Action<TimeSpan> Wrap(Action<TimeSpan> frame)
    {
        if (!wrapped.TryGetValue(frame, out var counted)) wrapped[frame] = counted = time => { Answered++; frame(time); };
        return counted;
    }

    public void RequestFrame(Action<TimeSpan> frame) => inner.RequestFrame(Wrap(frame));
    public void CancelFrame(Action<TimeSpan> frame) => inner.CancelFrame(Wrap(frame));
    public int Pending => inner.Pending;
}

// The production source as a draw loop's source, counting the frame callbacks it answers.
public sealed class CountingHeldFrameSource(AvaloniaFrameSource inner) : IWakeFrameSource
{
    public long Answered;
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> wrapped = [];

    private Action<TimeSpan> Wrap(Action<TimeSpan> frame)
    {
        if (!wrapped.TryGetValue(frame, out var counted)) wrapped[frame] = counted = time => { Answered++; frame(time); };
        return counted;
    }

    public TimeSpan? WakePeriod => inner.WakePeriod;
    public void RequestFrame(Action<TimeSpan> frame) => inner.RequestFrame(Wrap(frame));
    public void RequestFrameAt(ContinuousInstant? wake, Action<TimeSpan> frame) => inner.RequestFrameAt(wake, Wrap(frame));
    public void CancelFrame(Action<TimeSpan> frame) => inner.CancelFrame(Wrap(frame));
    public int Pending => inner.Pending;
}

public sealed record WakeLinkRun(int Draws, double Seconds, long Answered, double[] LatenessMs);
