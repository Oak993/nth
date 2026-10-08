namespace Marrow.App.Tests.Fixtures;

// A callback already queued by TimerQueue can arrive after ITimer.Dispose. Retain that
// callback deliberately; ordinary manual clocks cancel it and cannot expose this race.
public sealed class RetiredPaletteTimer : TimeProvider, ITimer
{
    private TimerCallback? callback;
    private object? state;
    public bool IsDisposed { get; private set; }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.callback = callback; this.state = state;
        return this;
    }
    public void DeliverQueuedCallback() => callback!(state);
    public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;
    public void Dispose() => IsDisposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
