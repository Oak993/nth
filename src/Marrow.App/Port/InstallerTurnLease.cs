using Marrow.Core.Port;

namespace Marrow.App;

// PORT-ONLY: ED-NEW-inst-c7-5. One captured intent owns admission and its own durability stage.
internal sealed class InstallerTurnLease(AppModel model, Guid thread, bool requiresDurability) : IDisposable
{
    private static readonly AsyncLocal<InstallerTurnLease?> executing = new();
    private readonly CancellationTokenSource stop = new();
    private int released;
    internal Guid Thread { get; } = thread;
    internal DateTimeOffset Started { get; } = SwiftDate.Now;
    internal bool RequiresDurability { get; } = requiresDurability;
    internal bool IsDurable { get; private set; }
    internal static InstallerTurnLease? Current => executing.Value;
    internal bool IsLive => Volatile.Read(ref released) == 0;
    internal CancellationToken StopToken => stop.Token;
    internal Action? StopExisting { get; set; }
    internal void CancelPrivateWork()
    {
        if (RequiresDurability) return;
        try { stop.Cancel(); }
        finally { StopExisting?.Invoke(); }
    }
    internal void MarkDurable()
    {
        MainActor.AssumeIsolated();
        IsDurable = true;
    }
    internal async Task RunAsync(Func<Task> body)
    {
        var previous = executing.Value;
        executing.Value = this;
        try { await body(); }
        finally { executing.Value = previous; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        if (MainActor.IsIsolated) Release();
        else MainActor.Post(Release);
        void Release()
        {
            model.ReleaseInstallerTurnAdmission(this);
            stop.Dispose();
        }
    }
}
