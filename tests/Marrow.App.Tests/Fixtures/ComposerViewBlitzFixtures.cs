using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// The blitz-composerview unit's test helpers (ComposerViewBlitz*Tests).

// Moved out of ComposerViewBlitzListTests (a test class holds no nested type, R-TEST-2).
// Puts what a popup shows into a window at once.
internal sealed class HostingPresenter(Decorator host, Action layout) : IPopoverPresenter
{
    public int Shown { get; private set; }

    public void SetPresented(Popup popup, bool presented)
    {
        if (!presented || popup.Child is not { } frame) return;
        popup.Child = null;
        host.Child = frame;
        layout();
        Shown++;
    }
}

// Moved out of ComposerViewBlitzPanelTests (a test class holds no nested type, R-TEST-2).
// A pill on screen whose list is hosted in a window of 340 wide as it is when presented, the first suggestions shown.
internal sealed class HostedList : IDisposable
{
    public required ThreadRuntimePart12Harness H { get; init; }
    public required ComposerView View { get; init; }
    public required AvaloniaWindow PillWindow { get; init; }
    public required AvaloniaWindow ListWindow { get; init; }
    public required Decorator Host { get; init; }
    public required IDisposable Seam { get; init; }

    public void Dispose()
    {
        Close(H, ListWindow);
        Close(H, PillWindow);
        Seam.Dispose();
    }
}

// Moved out of ComposerViewBlitzSendTests (a test class holds no nested type, R-TEST-2).
// OD-0136 amendment 2, step 9: the face (NthSendFace) is what the button shows, so the look is its running flag and how much of itself it keeps (Dim), not a fill and an ink.
internal sealed record SendLook(bool Enabled, bool Running, string Name, string Tip, double Dim);

// Moved out of ComposerViewBlitzUsageTests (a test class holds no nested type, R-TEST-2).
// The ring on screen with the presenter recording what it is asked to show, then its popover opened by a click.
internal sealed class OpenRing : IDisposable
{
    public required ThreadRuntimePart12Harness H { get; init; }
    public required ContextMeter Meter { get; init; }
    public required AvaloniaWindow Window { get; init; }
    public required ContentRecordingPresenter Presenter { get; init; }
    public required IDisposable Seam { get; init; }
    public Control? Content { get; set; }
    public AvaloniaWindow? PopupWindow { get; set; }

    public void Dispose()
    {
        H.Main.OnMain(() => { PopupWindow?.Close(); Window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
        Seam.Dispose();
    }
}
