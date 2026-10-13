using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 2.5 intent cache (ED-NEW-web-w7-3), used only as the fallback when a fresh capture at gesture start misses
/// the capture budget. It keeps one recent frame per live view. While <see cref="Enabled"/> (spike b found busy-page
/// captures over budget) frames are refreshed on intent (pointer over a gutter, header or pane toggle; a workspace
/// shortcut key down; navigation finished plus 500 ms) and kept no older than <see cref="MaxAge"/> while the view is
/// visible and its window focused. A disabled cache still remembers the newest frame any capture produced.
/// </summary>
public sealed class SnapshotCache(TimeProvider time)
{
    /// <summary>The longest a visible view's frame may age before a refresh (DESIGN 2.5: 1 s).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);

    /// <summary>The delay after a navigation finishes before its refresh (DESIGN 2.5: 500 ms).</summary>
    public static readonly TimeSpan AfterNavigation = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<object, Entry> entries = [];

    /// <summary>Whether intent refreshes run (spike b decides; off until then, so no capture is spent on it).</summary>
    public bool Enabled { get; set; }

    /// <summary>Stores a frame for a view (any capture, fresh or intent).</summary>
    public void Put(object view, WebSnapshot snapshot)
    {
        var entry = Get(view);
        if (entry.Frame is null || snapshot.CapturedAt >= entry.Frame.CapturedAt) entry.Frame = snapshot;
    }

    /// <summary>The newest frame for a view, whatever its age, or null.</summary>
    public WebSnapshot? Newest(object view) => entries.TryGetValue(view, out var entry) ? entry.Frame : null;

    /// <summary>True when the view's frame is missing or older than <see cref="MaxAge"/>.</summary>
    public bool IsStale(object view)
    {
        var frame = Newest(view);
        return frame is null || time.GetUtcNow() - frame.CapturedAt > MaxAge;
    }

    /// <summary>Marks a navigation finished: a refresh falls due <see cref="AfterNavigation"/> later.</summary>
    public void NoteNavigation(object view) => Get(view).RefreshDueAt = time.GetUtcNow() + AfterNavigation;

    /// <summary>
    /// Whether a refresh should start now for a view: the cache is on, no refresh is in flight, and either intent was
    /// shown, a post-navigation refresh fell due, or the view is visible in a focused window with a stale frame.
    /// </summary>
    public bool ShouldRefresh(object view, bool intent, bool visibleAndFocused)
    {
        if (!Enabled) return false;
        var entry = Get(view);
        if (entry.InFlight) return false;
        if (intent) return true;
        if (entry.RefreshDueAt is { } due && time.GetUtcNow() >= due) return true;
        return visibleAndFocused && IsStale(view);
    }

    /// <summary>Marks a refresh in flight (one per view at a time).</summary>
    public void BeginRefresh(object view)
    {
        var entry = Get(view);
        entry.InFlight = true;
        entry.RefreshDueAt = null;
    }

    /// <summary>Ends a refresh; stores its frame if it produced one.</summary>
    public void EndRefresh(object view, WebSnapshot? snapshot)
    {
        // A close removes the entry while the renderer may still be capturing.
        // Completion must not retain that closed view again (ED-NEW-web-review-2).
        if (!entries.TryGetValue(view, out var entry)) return;
        entry.InFlight = false;
        if (snapshot is not null) Put(view, snapshot);
    }

    /// <summary>Forgets a view (closed).</summary>
    public void Remove(object view) => entries.Remove(view);

    private Entry Get(object view)
    {
        if (!entries.TryGetValue(view, out var entry)) entries[view] = entry = new Entry();
        return entry;
    }

    private sealed class Entry
    {
        public WebSnapshot? Frame { get; set; }
        public bool InFlight { get; set; }
        public DateTimeOffset? RefreshDueAt { get; set; }
    }
}
