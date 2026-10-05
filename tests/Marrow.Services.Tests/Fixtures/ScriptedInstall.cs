// PORT-ONLY: SK-15 (DL-17). A first-install seam a test scripts, item by item: it succeeds at once, holds until the test lets it go, fails with a message, or reports progress. It records every
// call, how many ran at once and which were cancelled, and it never touches a file or a network.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class ScriptedInstall : IFirstInstall
{
    private readonly object gate = new();
    private readonly Dictionary<string, Hold> holds = [];
    private readonly Dictionary<string, string> failures = [];
    private int running;

    private sealed class Hold
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<double?>? Progress { get; set; }
    }

    public List<string> Calls { get; } = [];

    public List<string> Cancelled { get; } = [];

    public int MostAtOnce { get; private set; }

    /// <summary>The item holds until <see cref="Release"/>; a cancel ends it as a cancelled transfer.</summary>
    public void HoldOn(string id)
    {
        lock (gate) holds[id] = new Hold();
    }

    public void FailOn(string id, string message)
    {
        lock (gate) failures[id] = message;
    }

    public void StopFailing(string id)
    {
        lock (gate) failures.Remove(id);
    }

    public Task Started(string id)
    {
        lock (gate) return holds[id].Started.Task;
    }

    public void Release(string id)
    {
        lock (gate) holds[id].Release.TrySetResult();
    }

    public void Report(string id, double? value)
    {
        IProgress<double?>? progress;
        lock (gate) progress = holds[id].Progress;
        progress!.Report(value);
    }

    public async Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation)
    {
        Hold? hold;
        lock (gate)
        {
            Calls.Add(item.Id);
            MostAtOnce = Math.Max(MostAtOnce, ++running);
            holds.TryGetValue(item.Id, out hold);
            if (hold is not null) hold.Progress = progress;
        }
        try
        {
            if (hold is not null)
            {
                hold.Started.TrySetResult();
                using var registration = cancellation.Register(() => hold.Release.TrySetCanceled(cancellation));
                try { await hold.Release.Task.ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    lock (gate)
                    {
                        Cancelled.Add(item.Id);
                        holds[item.Id] = new Hold();
                    }
                    throw;
                }
                lock (gate) holds.Remove(item.Id);
            }
            string? failure;
            lock (gate) failures.TryGetValue(item.Id, out failure);
            if (failure is not null) throw new InvalidOperationException(failure);
            progress.Report(1);
        }
        finally
        {
            lock (gate) running--;
        }
    }
}
