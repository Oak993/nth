namespace Marrow.App.Runtime;

// PORT-ONLY: OD-0114; one visible view's share of the app's account report polling.
internal sealed class AccountReportObservation(Action release) : IDisposable
{
    private Action? onRelease = release;
    public void Dispose()
    {
        Marrow.Core.Port.MainActor.AssumeIsolated();
        Interlocked.Exchange(ref onRelease, null)?.Invoke();
    }
}
