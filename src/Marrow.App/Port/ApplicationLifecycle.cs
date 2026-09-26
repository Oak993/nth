// PORT-ONLY: the AppKit application surface DroppyCode/App/AppDelegate.swift (B3 52bc792) reads and answers, which
// has no Windows type: NSApp.isActive (:70, and the other sites that read it), NSApplication.TerminateReply (:44,
// :45, :53) and the sender's reply(toApplicationShouldTerminate:) (:51). The shell that turns the main window's close
// and the app's own Exit into the terminate handshake (ED-004, PA-0006) implements ITerminationSender; it is not in
// this tree yet.
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

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
    /// <c>NSApp.isActive</c>: whether this app is the active one. The port's one reading of it, read by every site the
    /// Swift reads NSApp.isActive at: AppDelegate.swift:70, AppModel.swift:1172, :1186, :1259,
    /// AppModel+HydraMerge.swift:924 and Runtime/AutoContinue.swift:152 (B3 52bc792). ENGINEERING DECISION (proposed
    /// to the orchestrator for recording): on Windows the app is active while one of its own windows is the active
    /// (foreground) window. NSApp.isActive belongs to the application, not to a window, so the settings and tour
    /// windows count as well as the main window; the Avalonia desktop lifetime lists every window the app has open
    /// (IClassicDesktopStyleApplicationLifetime.Windows). With no desktop lifetime there is no window, and the app is
    /// not active. An Ambient (R-LIFE-2) so a test gives its own async flow an answer without an Avalonia desktop
    /// lifetime, which is process-wide state (R-TEST-5).
    /// </summary>
    public static Ambient<Func<bool>> IsActive { get; } = new((Func<bool>)(() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
        && AnyWindowIsActive(desktop.Windows)));

    /// <summary>The reading <see cref="IsActive"/> makes over the app's open windows: true when any one of them is the
    /// active window, whichever it is.</summary>
    public static bool AnyWindowIsActive(IEnumerable<AvaloniaWindow> windows) => windows.Any(window => window.IsActive);
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
