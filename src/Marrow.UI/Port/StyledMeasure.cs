// PORT-ONLY: SwiftUI's `NSHostingView(rootView:).fittingSize` and `intrinsicContentSize` measure a view that is already hosted, so its styles and templates are in effect. An Avalonia control that is not yet in a window has none (no application styles reach it and a templated control such as a Button, a TextBox or a ScrollViewer measures short or empty), so every size taken from a control before it is shown has to be taken in a styled tree (ED-071, R-VIEW-1).
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY. Measures a control as it will measure once it is in a window. A control that is not yet in a visual tree
/// has had no styles or control templates applied (the key guide's field and buttons came out 16 to 28 pt short measured
/// that way), so <see cref="Of"/> puts it in a window that is never shown, measures it there and takes it out again.
/// </summary>
public static class StyledMeasure
{
    // One host per application (a headless test session makes its own application, so a session's host dies with it).
    // The host is never shown and never closed: closing a window raises Window.WindowClosedEvent, which a desktop
    // lifetime in ShutdownMode.OnLastWindowClose answers with a shutdown when no shown window is left.
    private static readonly ConditionalWeakTable<Application, Window> hosts = new();

    /// <summary>
    /// The size <paramref name="content"/> wants at <paramref name="available"/>, measured in a styled tree. Content that
    /// is already in a window is measured where it is. Content that has a parent but no window yet (a closed popup's
    /// child) cannot be moved into the host, so the caller takes it from its parent first; it is measured in place, as
    /// before. The content is left detached and measured, as it was found.
    /// </summary>
    public static Size Of(Control content, Size available)
    {
        if (Application.Current is not { } application || TopLevel.GetTopLevel(content) is not null ||
            content.Parent is not null || content.GetVisualParent() is not null)
            return MeasuredInPlace(content, available);

        var host = hosts.GetValue(application, static _ => new Window { ShowInTaskbar = false });
        // The host is busy when an attach handler of the content being measured measures another control: that one is
        // measured in place, as the host cannot hold two.
        if (host.Content is not null) return MeasuredInPlace(content, available);

        host.Content = content;
        try
        {
            host.Measure(new Size(available.Width, double.PositiveInfinity)); // applies the window's template: the content is now in a tree
            content.Measure(available);
            return content.DesiredSize;
        }
        finally
        {
            host.Content = null;
        }
    }

    private static Size MeasuredInPlace(Control content, Size available)
    {
        content.Measure(available);
        return content.DesiredSize;
    }
}
