// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3, 15.3; unit AS-22; ED-NEW-dextra-as22-8). No Swift twin.
// Keeps the delegate's answers in the assistant's store: loaded when it starts, written as soon as an answer is added or corrected, off the
// thread that changed it (the store's atomic write is disk I/O, and the change comes from a chat's event or the owner's click), one write at a
// time so an older document never lands after a newer one. A file this build cannot write (a newer version's) is left alone; a write that
// fails is reported and tried again at the next change, never thrown into the app. Flush and Dispose write whatever is pending first.
namespace Marrow.Services.Assistant;

public sealed class DelegateAnswerPersistence : IDisposable
{
    private readonly AssistantStore store;
    private readonly DelegateAnswerBook book;
    private readonly object gate = new();
    private readonly object writing = new();
    private bool dirty;
    private bool disposed;

    /// <summary>Loads what the store holds into <paramref name="book"/>, then follows its changes.</summary>
    public DelegateAnswerPersistence(AssistantStore store, DelegateAnswerBook book)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.book = book ?? throw new ArgumentNullException(nameof(book));
        book.Load(store.DelegateAnswers());
        book.Changed += OnChanged;
    }

    /// <summary>A write failed: the line for the board. The app goes on; the next change tries again.</summary>
    public event Action<string>? Faulted;

    private void OnChanged()
    {
        lock (gate)
        {
            if (disposed) return;
            dirty = true;
        }
        _ = Task.Run(Write);
    }

    private void Write()
    {
        lock (writing)
        {
            lock (gate)
            {
                if (!dirty) return;
                dirty = false;
            }
            if (book.ToJson() is not { } document) return; // a newer version's file: left as it is
            try { store.SaveDelegateAnswers(document); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                lock (gate) dirty = true;
                Faulted?.Invoke("What Dextra answered for you could not be saved: " + error.Message);
            }
        }
    }

    /// <summary>Writes the answers now, if they changed, and returns when they are written.</summary>
    public void Flush() => Write();

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
        }
        Flush();
        lock (gate) disposed = true;
        book.Changed -= OnChanged;
    }
}
