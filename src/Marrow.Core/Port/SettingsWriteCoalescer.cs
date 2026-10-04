namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY (customization-SPEC CZ-13.5, voice-SPEC V-16.6; first-release SA-1). A slider drag must not write the settings store
/// every frame: the owner of the in-memory value (AppSettings) keeps it current on every change and hands the stored form to
/// this class, which writes the store at most once per <see cref="Interval"/> for each key while values keep changing. A key
/// first change after a quiet interval is written at once (a lone change, such as a switch, never waits); changes inside the
/// interval are held, only the latest is kept, and it is written when the interval ends. <see cref="Flush"/> writes everything
/// held now: AppSettings calls it when a drag ends and on app exit. Keys wait independently of each other, so two keys changed
/// together (CZ-13.4: <c>voiceOrb</c> and <c>voiceTyping</c> on "Spoken language") both reach the store in the same turn.
/// Main-actor only; the waiting timer is <see cref="MainActor.AsyncAfter"/> on <see cref="SwiftClock"/>, so a test drives it.
/// </summary>
public sealed class SettingsWriteCoalescer
{
    /// <summary>CZ-13.5: "at most every 250 ms while values keep changing".</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

    private readonly ISettingsStore store;
    private readonly Dictionary<string, ContinuousInstant> lastWrite = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> pending = new(StringComparer.Ordinal);
    private WorkItem? timer;
    private ContinuousInstant timerDue;

    public SettingsWriteCoalescer(ISettingsStore store, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        Interval = interval ?? DefaultInterval;
    }

    /// <summary>How long a key waits between two writes while its value keeps changing.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Whether a changed value is waiting for its interval to end.</summary>
    public bool HasPending => pending.Count > 0;

    /// <summary>The value for <paramref name="key"/> changed (<c>null</c> removes the key): write it now or hold it for the interval.</summary>
    public void Write(string key, object? value)
    {
        var now = SwiftClock.Now;
        if (!lastWrite.TryGetValue(key, out var last) || now - last >= Interval)
        {
            pending.Remove(key);
            Put(key, value, now);
            return;
        }
        pending[key] = value;
        Arm(now);
    }

    /// <summary>Writes every held value now (a drag ended, or the app is exiting) and drops the wait.</summary>
    public void Flush()
    {
        timer?.Cancel();
        timer = null;
        if (pending.Count == 0) return;
        var now = SwiftClock.Now;
        foreach (var (key, value) in pending.ToArray())
        {
            pending.Remove(key);
            Put(key, value, now);
        }
    }

    private void Put(string key, object? value, ContinuousInstant now)
    {
        lastWrite[key] = now;
        if (value is null) store.RemoveObject(key); else store.Set(key, value);
    }

    private void Arm(ContinuousInstant now)
    {
        // The earliest held key's interval end decides; a key whose interval ends before the armed timer re-arms it
        // (SA-1 review: a later timer must not hold an earlier key past its interval).
        var due = pending.Keys.Min(key => lastWrite[key] + Interval);
        if (timer is not null && timerDue <= due) return;
        timer?.Cancel();
        timerDue = due;
        timer = MainActor.AsyncAfter(due - now, OnTimer);
    }

    private void OnTimer()
    {
        timer = null;
        var now = SwiftClock.Now;
        foreach (var key in pending.Keys.ToArray())
        {
            if (now - lastWrite[key] < Interval) continue;
            var value = pending[key];
            pending.Remove(key);
            Put(key, value, now);
        }
        if (pending.Count > 0) Arm(now);
    }
}
