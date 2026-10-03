using Avalonia;
using Avalonia.Controls;

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
    }

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
            if (!ReferenceEquals(TopLevel.GetTopLevel(owner), window)) continue;
            Open.RemoveAt(i);
            entry.Dismiss();
            return true;
        }
        return false;
    }
}
