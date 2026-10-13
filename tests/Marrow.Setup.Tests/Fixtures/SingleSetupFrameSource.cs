using Marrow.Setup.Motion;

namespace Marrow.Setup.Tests.Fixtures;

// One pending frame, no fixture allocation in the animator's measured loop.
public sealed class SingleSetupFrameSource : IFrameSource
{
    private Action<TimeSpan>? pending;
    private TimeSpan now;
    public bool Pending => pending is not null;
    public void RequestFrame(Action<TimeSpan> frame) => pending = frame;
    public void Frame()
    {
        now += TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var callback = pending;
        pending = null;
        callback?.Invoke(now);
    }
}
