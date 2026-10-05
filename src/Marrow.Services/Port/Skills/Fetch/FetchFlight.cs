// PORT-ONLY: first-release feature unit SK-16 (DL-19). One running download of one item and the surfaces that wait on it.
namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// ENGINEERING DECISIONS (ED-NEW-cloud-sk-16, review): (1) no listener is ever called while the flight's lock is held, so a callback that reads this flight (or the downloads that own it) from another
/// thread cannot deadlock; (2) every report carries a sequence number and each listener keeps what it has been given last, so a replay for a late joiner can never arrive after a newer report;
/// (3) a flight also keeps the own progress of each item of its chain (<see cref="Parts"/>), so a surface that waits on several flights can count a shared dependency once.
/// </summary>
internal sealed class FetchFlight(string id)
{
    private readonly object gate = new();
    private readonly List<Listener> listeners = [];
    private readonly Dictionary<string, (double Fraction, long Seq)> parts = new(StringComparer.Ordinal);
    private double? last;
    private long lastSeq;
    private bool reported;
    private long seq;

    public string Id { get; } = id;

    /// <summary>Resolved with the installed folder, or with the failure, or cancelled.</summary>
    public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationTokenSource Cancel { get; } = new();

    /// <summary>The askers still waiting; the SkillsDownloads lock guards it. When the last one leaves the transfer stops.</summary>
    public int Askers { get; set; }

    public double? Last { get { lock (gate) return last; } }

    /// <summary>Starts telling the listener where the download is, and the own progress of each item of the chain: now, then at every report. Disposing stops it.</summary>
    public IDisposable Listen(IProgress<double?> progress, Action<string, double>? part = null)
    {
        var listener = new Listener(progress, part);
        double? replay;
        long replaySeq;
        KeyValuePair<string, (double Fraction, long Seq)>[] replayParts;
        bool had;
        lock (gate)
        {
            listeners.Add(listener);
            had = reported;
            replay = last;
            replaySeq = lastSeq;
            replayParts = parts.ToArray();
        }
        if (had) listener.Progress(replaySeq, replay);
        foreach (var (key, value) in replayParts) listener.Part(key, value.Seq, value.Fraction);
        return new Leave(this, listener);
    }

    public void Report(double? value)
    {
        Listener[] observers;
        long number;
        lock (gate) { last = value; reported = true; number = lastSeq = ++seq; observers = listeners.ToArray(); }
        foreach (var observer in observers) observer.Progress(number, value);
    }

    /// <summary>The own progress (0 to 1) of one item of the chain.</summary>
    public void ReportPart(string item, double fraction)
    {
        Listener[] observers;
        long number;
        lock (gate) { number = ++seq; parts[item] = (fraction, number); observers = listeners.ToArray(); }
        foreach (var observer in observers) observer.Part(item, number, fraction);
    }

    private sealed class Listener(IProgress<double?> progress, Action<string, double>? part)
    {
        private readonly object delivery = new();
        private readonly Dictionary<string, long> partSeqs = new(StringComparer.Ordinal);
        private long progressSeq;

        public void Progress(long number, double? value)
        {
            lock (delivery)
            {
                if (number <= progressSeq) return;
                progressSeq = number;
                try { progress.Report(value); }
                catch (Exception) { /* an observer cannot stop a download */ }
            }
        }

        public void Part(string item, long number, double fraction)
        {
            if (part is null) return;
            lock (delivery)
            {
                if (partSeqs.TryGetValue(item, out var seen) && number <= seen) return;
                partSeqs[item] = number;
                try { part(item, fraction); }
                catch (Exception) { /* an observer cannot stop a download */ }
            }
        }
    }

    private sealed class Leave(FetchFlight owner, Listener listener) : IDisposable
    {
        public void Dispose() { lock (owner.gate) owner.listeners.Remove(listener); }
    }
}
