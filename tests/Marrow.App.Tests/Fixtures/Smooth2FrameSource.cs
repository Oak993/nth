using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Native-only observer: counts a changed decoration pose after each real callback, not merely delivered callbacks.
public sealed class Smooth2FrameSource(AvaloniaFrameSource inner, Func<double> pose) : IWakeFrameSource
{
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> wrapped = [];
    private double previous = double.NaN;
    public long Answered { get; private set; }
    public long Poses { get; private set; }
    private Action<TimeSpan> Wrap(Action<TimeSpan> frame)
    {
        if (!wrapped.TryGetValue(frame, out var counted))
            wrapped[frame] = counted = time =>
            {
                Answered++; frame(time);
                var next = pose();
                if (next != previous) Poses++;
                previous = next;
            };
        return counted;
    }
    public TimeSpan? WakePeriod => inner.WakePeriod;
    public void RequestFrame(Action<TimeSpan> frame) => inner.RequestFrame(Wrap(frame));
    public void RequestFrameAt(ContinuousInstant? wake, Action<TimeSpan> frame) => inner.RequestFrameAt(wake, Wrap(frame));
    public void CancelFrame(Action<TimeSpan> frame) => inner.CancelFrame(Wrap(frame));
    public int Pending => inner.Pending;
}
