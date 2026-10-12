using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.UI.Web;

// PORT-ONLY: web surfaces W3b, the pane workspace's chords (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7 "How: web views
// raise AcceleratorKeyPressed before the page sees a key. nth's handler checks the reserved list, marks the event Handled and
// runs the chord. Other keys pass through"; ED-NEW-web-w3b-1). No Swift counterpart. The key read from a window event, the
// commands that belong to the workspace, and the wiring that lets a chord pressed inside a page run in the pane that holds it.

namespace Marrow.App.Views.Panes;

/// <summary>The workspace's commands and the chords that reach them from the window and from inside pages.</summary>
public static class PaneShortcuts
{
    private static readonly HashSet<AppShortcut> Commands =
    [
        AppShortcut.ToggleBrowser, AppShortcut.ToggleSideChat, AppShortcut.ToggleArtifacts, AppShortcut.ToggleBackgroundTasks,
        AppShortcut.ClosePane, AppShortcut.ExpandPane, AppShortcut.NextPane, AppShortcut.PreviousPane,
        AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater, AppShortcut.JoinPaneTabs,
    ];

    /// <summary>Whether the workspace of the window runs this command (the others belong to AppCommands).</summary>
    public static bool IsPaneCommand(AppShortcut command) => Commands.Contains(command);

    /// <summary>The chord a key-down presses (ED-099): the physical key's scan code and the Control, Alt and Shift held; null for a key no chord can hold.</summary>
    public static KeyChord? ChordOf(KeyEventArgs e)
    {
        if (MenuKeyEquivalents.ScanCodeOf(e.PhysicalKey) is not { } code) return null;
        return new KeyChord(code, (ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers);
    }

    /// <summary>
    /// Connects a web surface to the shortcut layer (W7 left the seam, ED-NEW-web-w7-5): the chords that reach nth from inside a
    /// page are the reserved ones under the user's bindings, and one that does is run in the workspace holding that page, for
    /// the pane the page is in. Disposing the result disconnects it.
    /// </summary>
    public static IDisposable Bind(WebSurface surface, ShortcutStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var previous = surface.IsReservedChord;
        void Pressed(WebSurfaceView view, KeyChord chord)
        {
            var bindings = store ?? ShortcutStore.Shared.Current;
            if (ShortcutScopes.Resolve(bindings, chord) is not { } command)
            {
                // The Tactus Stop chord is no workspace command; it belongs to the Tactus layer.
                return;
            }
            if (WebViewHost.HostFor(view) is not { } host) return;
            if (host.FindAncestorOfType<PaneWorkspace>() is not { } workspace) return;
            var origin = host.FindAncestorOfType<PaneCard>()?.Kind;
            workspace.Perform(command, origin);
        }
        surface.IsReservedChord = chord => ShortcutScopes.IsReserved(store ?? ShortcutStore.Shared.Current, chord);
        surface.ReservedChordPressed += Pressed;
        return new Binding(() =>
        {
            surface.ReservedChordPressed -= Pressed;
            surface.IsReservedChord = previous;
        });
    }

    private sealed class Binding(Action undo) : IDisposable
    {
        private Action? pending = undo;

        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
