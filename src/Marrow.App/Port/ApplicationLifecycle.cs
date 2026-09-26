// PORT-ONLY: the AppKit application surface DroppyCode/App/AppDelegate.swift (B3 52bc792) reads and answers, which
// has no Windows type: NSApp.isActive (:70), NSApplication.TerminateReply (:44, :45, :53) and the sender's
// reply(toApplicationShouldTerminate:) (:51). The shell that turns the main window's close and the app's own Exit into
// the terminate handshake (ED-004, PA-0006) implements ITerminationSender; it is not in this tree yet.
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary><c>NSApplication</c>, as far as AppDelegate.swift reads it.</summary>
public static class NSApplication
{
    /// <summary><c>NSApplication.TerminateReply</c>: the two answers AppDelegate.swift gives (:45 <c>.terminateNow</c>,
    /// :53 <c>.terminateLater</c>). AppKit's third case, <c>.terminateCancel</c>, is never given by the source, so it
    /// is not declared.</summary>
    public enum TerminateReply
    {
        /// <summary><c>.terminateNow</c>: termination goes ahead into applicationWillTerminate at once.</summary>
        TerminateNow,

        /// <summary><c>.terminateLater</c>: the app stays up until the sender is answered with
        /// <see cref="ITerminationSender.Reply"/>.</summary>
        TerminateLater,
    }

    /// <summary>
    /// <c>NSApp.isActive</c>: whether this app is the active one. ENGINEERING DECISION: on Windows the app is active
    /// while one of its own windows is the active (foreground) window; Avalonia's desktop lifetime lists every window
    /// the app has open, the settings and tour windows included, so the default reads any of them, not only the main
    /// window. An Ambient (R-LIFE-2) so a test gives its own async flow an answer without an Avalonia desktop lifetime,
    /// which is process-wide state (R-TEST-5).
    /// </summary>
    public static Ambient<Func<bool>> IsActive { get; } = new((Func<bool>)AnyWindowIsActive);

    private static bool AnyWindowIsActive() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
        && desktop.Windows.Any(window => window.IsActive);
}

/// <summary>The <c>NSApplication</c> that asked <c>applicationShouldTerminate(_:)</c> and was told
/// <see cref="NSApplication.TerminateReply.TerminateLater"/>: answered later with
/// <c>reply(toApplicationShouldTerminate:)</c> (AppDelegate.swift:51).</summary>
public interface ITerminationSender
{
    /// <summary><c>reply(toApplicationShouldTerminate:)</c>: true lets termination continue into
    /// applicationWillTerminate.</summary>
    void Reply(bool toApplicationShouldTerminate);
}
