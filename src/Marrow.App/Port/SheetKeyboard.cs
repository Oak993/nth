// PORT-ONLY (ED-333 5, review of 4a608bb54): a sheet over a window takes the window's keyboard, as an NSWindow's attached sheet is the key
// window while it shows: the window's Escape closes it before anything under it acts on the key (the composer would take a running turn back,
// ComposerView.cs's Escape), and, for a sheet drawn in the window, no key or text reaches the content under it. Its window reads it as its attached
// sheet (ThreadWindow.AttachedSheet: the thread chords stand still under a sheet, ThreadWindow.swift:12-14, :26). The same hook as PopoverHost.Listen:
// tunnelling handlers on the window, which see a key before the element that holds the keyboard.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Window;

namespace Marrow.App.Port;

internal static class SheetKeyboard
{
    // The windows a sheet has taken the keyboard of, one entry per sheet showing (VT-10 review: a voice session's Escape yields to a sheet opened after it went live).
    private static readonly List<WeakReference<TopLevel>> Taken = [];

    /// <summary>Whether a sheet showing now has taken the keyboard of <paramref name="window"/>.</summary>
    public static bool IsTaken(TopLevel window) => Taken.Exists(entry => entry.TryGetTarget(out var held) && ReferenceEquals(held, window));

    /// <summary>
    /// While the sheet shows: Escape on <paramref name="window"/> calls <paramref name="dismiss"/> and is handled; with <paramref name="blockUnder"/>, a key or
    /// text whose target is not inside the sheet (<paramref name="inside"/>) is handled before it reaches it. Returns the call that stops it.
    /// </summary>
    public static Action Take(TopLevel window, Control sheet, Func<Visual?, bool> inside, Action dismiss, bool blockUnder)
    {
        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                dismiss();
                return;
            }
            if (blockUnder && !inside(e.Source as Visual)) e.Handled = true;
        }
        void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (blockUnder && !e.Handled && !inside(e.Source as Visual)) e.Handled = true;
        }
        void OnText(object? sender, TextInputEventArgs e)
        {
            if (blockUnder && !e.Handled && !inside(e.Source as Visual)) e.Handled = true;
        }
        window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.TextInputEvent, OnText, RoutingStrategies.Tunnel);
        var threadWindow = window as ThreadWindow;
        if (threadWindow is not null) threadWindow.AttachedSheet = sheet;
        var entry = new WeakReference<TopLevel>(window);
        Taken.Add(entry);
        return () =>
        {
            Taken.Remove(entry);
            window.RemoveHandler(InputElement.KeyDownEvent, OnKey);
            window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            window.RemoveHandler(InputElement.TextInputEvent, OnText);
            if (threadWindow is not null && ReferenceEquals(threadWindow.AttachedSheet, sheet)) threadWindow.AttachedSheet = null;
        };
    }

    /// <summary>Whether <paramref name="visual"/> is <paramref name="root"/> or inside it.</summary>
    public static bool Within(Visual? visual, Visual root) => visual is not null && (ReferenceEquals(visual, root) || root.IsVisualAncestorOf(visual));
}
