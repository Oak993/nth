// PORT-ONLY: first-release feature unit SK-16 (DL-19). One running download of one item and the surfaces that wait on it.
namespace Marrow.Services.Port.Skills.Fetch;

internal sealed class FetchFlight(string id)
{
    private readonly object gate = new();
    private readonly List<IProgress<double?>> listeners = [];
    private double? last;
    private bool reported;

    public string Id { get; } = id;

    /// <summary>Resolved with the installed folder, or with the failure, or cancelled.</summary>
    public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationTokenSource Cancel { get; } = new();

    /// <summary>The askers still waiting; the SkillsDownloads lock guards it. When the last one leaves the transfer stops.</summary>
    public int Askers { get; set; }

    public double? Last { get { lock (gate) return last; } }

    /// <summary>Starts telling the listener where the download is: now, then at every report. Disposing stops it.</summary>
    public IDisposable Listen(IProgress<double?> listener)
    {
        lock (gate)
        {
            if (reported) listener.Report(last);
            listeners.Add(listener);
        }
        return new Leave(this, listener);
    }

    public void Report(double? value)
    {
        IProgress<double?>[] observers;
        lock (gate) { last = value; reported = true; observers = listeners.ToArray(); }
        foreach (var observer in observers)
        {
            try { observer.Report(value); }
            catch (Exception) { /* an observer cannot stop a download */ }
        }
    }

    private sealed class Leave(FetchFlight owner, IProgress<double?> listener) : IDisposable
    {
        public void Dispose() { lock (owner.gate) owner.listeners.Remove(listener); }
    }
}
