using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace Marrow.UI.Port;

// PORT-ONLY (OD-0088, ED-253): what a window shows over itself for a moment, a popover or a menu. On the Mac each is a window
// of its own (NSPopover's panel, NSMenu's tracking window): an Escape pressed while one is open goes to it and closes it, and
// the window under it never sees that key (the Settings window's own Escape, ThreadWindow.swift:47-55, closes the window).
// The port draws them over the window, so the window's key handler asks here first: the newest one open over it is closed.

/// <summary>The transients (popovers, menus) open over each window, newest last.</summary>
public static class WindowTransients
{
    private sealed record Entry(object Key, WeakReference<Visual> Owner, Action Dismiss);

    private static readonly List<Entry> Open = [];

    /// <summary>
    /// <paramref name="key"/> is open over the window <paramref name="owner"/> is in; <paramref name="dismiss"/> closes it
    /// (as its own Escape would). Showing a key again moves it to the newest.
    /// </summary>
    public static void Show(object key, Visual owner, Action dismiss)
    {
        Hide(key);
        Open.Add(new Entry(key, new WeakReference<Visual>(owner), dismiss));
        // A popup closes without the app's say-so when its anchor leaves the tree (Avalonia closes a popup whose placement target is gone), and nothing then calls Hide:
        // the entry would hold the popup, which is a visual child of the row it hung from, and so the row, its entry and its text, for good (PR-CHAT-161: a row that scrolls
        // away with its popover open). The popup's own Closed takes its entry out; the subscription is made once per popup.
        if (key is Popup popup && watched.TryAdd(popup, popup)) popup.Closed += (_, _) => Hide(popup);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Popup, object> watched = new();

    /// <summary><paramref name="key"/> is no longer open.</summary>
    public static void Hide(object key) => Open.RemoveAll(entry => ReferenceEquals(entry.Key, key));

    /// <summary>Whether <paramref name="key"/> is open over a window.</summary>
    public static bool IsShown(object key) => Open.Exists(entry => ReferenceEquals(entry.Key, key));

    /// <summary>Closes the newest transient open over <paramref name="window"/>; false when there is none.</summary>
    public static bool DismissNewest(TopLevel window)
    {
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            var entry = Open[i];
            if (!entry.Owner.TryGetTarget(out var owner))
            {
                Open.RemoveAt(i);
                continue;
            }
            if (!ReferenceEquals(WindowOf(owner), window)) continue;
            Open.RemoveAt(i);
            entry.Dismiss();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The window <paramref name="visual"/> is over. A popover's content is in a top level of its own (a PopupRoot), whose
    /// logical parent is its popup, opened over the top level of the popup's placement target (Popup.Open: PlacementTarget,
    /// else its nearest logical ancestor control); a popover opened inside another popover (the MCP editor's Kind picker) is
    /// over that one's. Each popup's top level is walked up to the window that owns it (ED-253, round 4).
    /// </summary>
    public static TopLevel? WindowOf(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual);
        for (var hop = 0; hop < 64 && top is PopupRoot { Parent: Popup popup }; hop++)
        {
            var placement = popup.PlacementTarget ?? popup.FindLogicalAncestorOfType<Control>();
            var next = placement is null ? null : TopLevel.GetTopLevel(placement);
            if (next is null || ReferenceEquals(next, top)) break;
            top = next;
        }
        return top;
    }
}
