// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3; ED-NEW-web-w24-1). The process's record of the live views each chat
// has shown: what ui_show made and ui_update changed, by id, so a later call finds the view it names. It is the one authority the
// tools write to (the nth-ui server on its relay thread, the native tool loop on the session's); the chat's runtime hears of every
// change through Subscribe and draws it. After a restart the record is empty, and Find asks the app's lookup seam, which reads the
// chat's saved timeline, so a view from an earlier session can still be updated. No Swift counterpart.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Marrow.Core.Models;

namespace Marrow.Services.Web;

/// <summary>A live view as the tools hold it: its id, kind, text, title and version.</summary>
public sealed record LiveViewRecord(string Id, LiveViewKind Kind, string Text, string? Title, int Version, bool Lite, int? Height);

public enum LiveChangeKind
{
    /// <summary>ui_show or html_widget: a view made, or one replaced by a new one of the same id.</summary>
    Shown,

    /// <summary>ui_update: the view with its text and state changed in place.</summary>
    Updated,
}

/// <param name="Seq">A number that grows with every change in the process.</param>
/// <param name="Replaced">For Shown: true when the id was already in use, so the earlier row is superseded.</param>
/// <param name="StateSets">For Updated: the states the patch sets, as a JSON object; null when it set none.</param>
public sealed record LiveViewChange(long Seq, Guid Thread, LiveChangeKind Kind, LiveViewRecord View, bool Replaced, string? StateSets);

public sealed class LiveAnswerHost
{
    /// <summary>The most views one chat's record keeps in memory (the oldest go first; the timeline keeps them all).</summary>
    public const int MaxViewsPerChat = 64;

    private readonly Func<bool> enabled;
    private readonly Func<Guid, string, Task<LiveViewRecord?>>? lookup;
    private readonly object gate = new();
    private readonly Dictionary<Guid, List<LiveViewRecord>> views = [];
    private readonly ConcurrentDictionary<Guid, List<Action<LiveViewChange>>> listeners = new();
    private long seq;

    /// <param name="enabled">Settings > Tabula > "Live answers in chat" (on by default); off offers the model no live-answer tool.</param>
    /// <param name="lookup">Finds the view with an id in a chat's saved timeline, for a view shown before this process started.</param>
    public LiveAnswerHost(Func<bool>? enabled = null, Func<Guid, string, Task<LiveViewRecord?>>? lookup = null)
    {
        this.enabled = enabled ?? (() => true);
        this.lookup = lookup;
    }

    public bool Enabled => enabled();

    /// <summary>The view with <paramref name="id"/> in a chat, from the record or, failing that, the chat's saved timeline.</summary>
    public async Task<LiveViewRecord?> Find(Guid thread, string id)
    {
        lock (gate)
        {
            if (views.TryGetValue(thread, out var list) && list.FirstOrDefault(view => string.Equals(view.Id, id, StringComparison.OrdinalIgnoreCase)) is { } known) return known;
        }
        if (lookup is null) return null;
        LiveViewRecord? found;
        try { found = await lookup(thread, id).ConfigureAwait(false); }
        catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { return null; }
        if (found is not null) lock (gate) Put(thread, found);
        return found;
    }

    /// <summary>The ids in the chat's record, newest last.</summary>
    public IReadOnlyList<string> Ids(Guid thread)
    {
        lock (gate) return views.TryGetValue(thread, out var list) ? [.. list.Select(view => view.Id)] : [];
    }

    /// <summary>A new id for a view the model did not name: short, letters and digits, unused in the record.</summary>
    public string NewId(Guid thread)
    {
        while (true)
        {
            var id = "view-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
            lock (gate)
            {
                if (!views.TryGetValue(thread, out var list) || !list.Any(view => view.Id == id)) return id;
            }
        }
    }

    /// <summary>
    /// Records a view that ui_show, html_widget or a fenced block made: a new one at version 1, or the replacement of the view with
    /// the same id at the next version. <paramref name="raise"/> false is for a caller that draws the view itself (the fence filter).
    /// </summary>
    public LiveViewChange Show(Guid thread, LiveViewRecord view, bool raise = true)
    {
        LiveViewChange change;
        lock (gate)
        {
            var known = views.TryGetValue(thread, out var list) ? list.FirstOrDefault(old => string.Equals(old.Id, view.Id, StringComparison.OrdinalIgnoreCase)) : null;
            var next = view with { Version = known is null ? 1 : known.Version + 1 };
            Put(thread, next);
            change = new LiveViewChange(++seq, thread, LiveChangeKind.Shown, next, known is not null, null);
        }
        if (raise) Raise(change);
        return change;
    }

    /// <summary>Records the new text of a view that ui_update changed (the next version) and tells the chat.</summary>
    public LiveViewChange Update(Guid thread, string id, string text, string? title, string? stateSets, bool raise = true)
    {
        LiveViewChange change;
        lock (gate)
        {
            var known = (views.TryGetValue(thread, out var list) ? list.FirstOrDefault(old => string.Equals(old.Id, id, StringComparison.OrdinalIgnoreCase)) : null)
                ?? throw new InvalidOperationException("There is no view " + id + " to update.");
            var next = known with { Text = text, Title = title ?? known.Title, Version = known.Version + 1 };
            Put(thread, next);
            change = new LiveViewChange(++seq, thread, LiveChangeKind.Updated, next, false, stateSets);
        }
        if (raise) Raise(change);
        return change;
    }

    /// <summary>Calls <paramref name="handler"/>, on whatever thread made the change, for every change to a view of <paramref name="thread"/> until disposed.</summary>
    public IDisposable Subscribe(Guid thread, Action<LiveViewChange> handler)
    {
        var list = listeners.GetOrAdd(thread, _ => []);
        lock (list) list.Add(handler);
        return new Subscription(() => { lock (list) list.Remove(handler); });
    }

    /// <summary>Forgets a chat's views (the chat was deleted).</summary>
    public void Forget(Guid thread)
    {
        lock (gate) views.Remove(thread);
        listeners.TryRemove(thread, out _);
    }

    private void Put(Guid thread, LiveViewRecord view)
    {
        if (!views.TryGetValue(thread, out var list)) views[thread] = list = [];
        list.RemoveAll(old => string.Equals(old.Id, view.Id, StringComparison.OrdinalIgnoreCase));
        list.Add(view);
        if (list.Count > MaxViewsPerChat) list.RemoveAt(0);
    }

    private void Raise(LiveViewChange change)
    {
        if (!listeners.TryGetValue(change.Thread, out var list)) return;
        Action<LiveViewChange>[] handlers;
        lock (list) handlers = [.. list];
        foreach (var handler in handlers)
        {
            try { handler(change); }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { /* a chat that is closing hears nothing */ }
        }
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? pending = release;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
