using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A frame source a test drives by hand that makes no object of its own while it runs (fixed arrays, no requests objects): an allocation probe measures the code under test and not the source (CZ-T44).</summary>
public sealed class QuietFrameSource : IFrameSource
{
    private Action<TimeSpan>[] waiting = new Action<TimeSpan>[256], running = new Action<TimeSpan>[256];
    private int waitingCount;

    public int Pending => waitingCount;
    public object? ObservedOwner { get; set; }
    public Action<TimeSpan>? ObservedFrame { get; private set; }

    public TimeSpan Now { get; private set; }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        if (waitingCount == waiting.Length) throw new InvalidOperationException("The quiet frame source holds 256 requests.");
        waiting[waitingCount++] = frame;
        if (ReferenceEquals(frame.Target, ObservedOwner)) ObservedFrame = frame;
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        for (var i = 0; i < waitingCount; i++)
        {
            if (!ReferenceEquals(waiting[i], frame)) continue;
            Array.Copy(waiting, i + 1, waiting, i, waitingCount - i - 1);
            waiting[--waitingCount] = null!;
            return;
        }
    }

    /// <summary>Moves the frame time forward and delivers one frame to the requests that waited for it; a request made during the frame waits for the next.</summary>
    public void Advance(TimeSpan by)
    {
        Now += by;
        (waiting, running) = (running, waiting);
        var count = waitingCount;
        waitingCount = 0;
        for (var i = 0; i < count; i++)
        {
            var frame = running[i];
            running[i] = null!;
            frame(Now);
        }
    }
}
