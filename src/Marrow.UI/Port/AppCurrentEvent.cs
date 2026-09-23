// PORT-ONLY: a stand-in for AppKit's `NSApp.currentEvent`, read by AttachmentPreviewPanel.swift's
// `anchorTarget` (:120, :139) to find the click that triggered a toggle. Tracks the single most recent
// pointer or key event application-wide through each routed event's static `Raised` observable (fired for
// every instance of that event anywhere in the app, ahead of routing), matching an `NSEvent` local
// monitor's app-wide (not per-window) visibility; unlike a local monitor it never swallows or replaces
// the event, it only observes.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;

namespace Marrow.UI.Port;

public static class AppCurrentEvent
{
    // The most recently raised event's own args, whichever of the three below fired last: each routed event's
    // `Raised` stream fires once per step of that one event's route (tunnel then bubble), always with the same
    // RoutedEventArgs instance, so `lastArgs` already reflects the latest raise once any step of it is seen; only
    // its type matters for telling the three apart. `.Source` (the actual hit-tested control), not the `sender`
    // this step happens to be visiting, is what resolves to the window the event happened in.
    private static RoutedEventArgs? lastArgs;
    private static bool subscribed;

    /// The window the single most recent input event happened in, or null before any event or once that
    /// window is gone. Mirrors `NSApp.currentEvent?.window`.
    public static TopLevel? CurrentWindow
    {
        get
        {
            EnsureSubscribed();
            return lastArgs is { Source: Visual visual } ? TopLevel.GetTopLevel(visual) : null;
        }
    }

    /// The window and position (in that window's coordinates) of the most recent event, only when that
    /// event was a left mouse button release; null when the most recent event was anything else (a key
    /// press, a different button, or nothing yet). Mirrors `NSApp.currentEvent?.type == .leftMouseUp`.
    public static (TopLevel Root, Point Position)? LastLeftMouseUp
    {
        get
        {
            EnsureSubscribed();
            if (lastArgs is PointerReleasedEventArgs { InitialPressMouseButton: MouseButton.Left, Source: Visual visual } released &&
                TopLevel.GetTopLevel(visual) is { } root)
            {
                return (root, released.GetPosition(root));
            }
            return null;
        }
    }

    private static void EnsureSubscribed()
    {
        if (subscribed) return;
        subscribed = true;
        void Remember((object Sender, RoutedEventArgs Args) e) => lastArgs = e.Args;
        InputElement.PointerPressedEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(Remember));
        InputElement.PointerReleasedEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(Remember));
        InputElement.KeyDownEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(Remember));
    }
}
