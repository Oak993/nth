using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Port.MultiAccount;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-ui-thread-5). The handoff journal's writes, off the thread that streams the reply. <see cref="HandoffJournal.AppendBatch"/> creates the folder, stats and appends
/// the file and, at 4 MB, moves it, compresses it (a gzip of the whole segment) and lists the folder; the runtime handed it its lines from the UI thread at every save and every turn's end.
/// Now the lines are handed to one queue (FIFO, one batch at a time, so the file is appended in the order the events came) and the UI thread goes on.
/// A reader asks for the lines through <see cref="HandoffJournal.Read"/>, which waits for the queue first, so it never sees a journal that is behind what was handed over; a quit waits for
/// the queue as it waits for the thread documents (AppModel.SaveBeforeQuit). A batch the disk refused (a full disk, a locked file) is kept and written first with the next batch of its
/// folder, and a reader gets it after the lines already on disk: nothing is dropped and the order stays.
/// </summary>
public static class HandoffJournalWriter
{
    private static readonly Logger FailureLog = new(Logger.Subsystem, "handoff");
    private static readonly SerialQueue Queue = new("droppycode.handoff.write");
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string[]> Refused = new(StringComparer.OrdinalIgnoreCase);

    // PORT-ONLY test seam (R-TEST-10): where a batch goes. The process default is the journal's own AppendBatch; a test holds, fails or records it.
    internal static Ambient<Action<string, IReadOnlyList<string>>> Sink { get; } = new((Action<string, IReadOnlyList<string>>)HandoffJournal.AppendBatch);

    /// <summary>Hands <paramref name="lines"/> to the queue and returns at once; the caller may reuse its list.</summary>
    public static void Append(string threadFolder, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        var batch = lines.ToArray();
        Queue.Async(() =>
        {
            string[] toWrite;
            lock (Gate) toWrite = Refused.Remove(threadFolder, out var kept) ? [.. kept, .. batch] : batch;
            try { Sink.Current(threadFolder, toWrite); }
            catch (Exception error) // a worker thread must not take the app down for a disk that said no
            {
                lock (Gate) Refused[threadFolder] = toWrite;
                FailureLog.Error($"nth could not write the handoff journal of a thread: {error.GetType().Name}"); // the lines are kept for the next batch
            }
        });
    }

    /// <summary>Blocks until every batch handed over so far has been written (or refused and kept). Never call it from the queue itself.</summary>
    public static void WaitForQueuedWrites() => Queue.Sync(() => { });

    /// <summary>The batches the disk refused for this folder and that wait for the next write: they follow the lines on disk.</summary>
    internal static string[] RefusedFor(string threadFolder)
    {
        lock (Gate) return Refused.TryGetValue(threadFolder, out var kept) ? [.. kept] : [];
    }
}
