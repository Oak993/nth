// PORT-ONLY: the Avalonia Application of the Windows app, standing for SwiftUI's App and AppKit's NSApplication, which
// DroppyCode/App/DroppyCodeApp.swift (B3 52bc792) gets from the framework. No theme package is referenced: every
// control the port shows carries its own template (Marrow.UI/Port/PlainControls.cs), and a control built without one
// gets only the port's default themes (Marrow.UI/Port/PortControlThemes.cs: ScrollViewer), exactly as the headless TestApp
// of both test assemblies (tests/*/Fixtures/HeadlessFixtures.cs). Without one, a TextBox, Slider, ProgressBar,
// Separator or ContextMenu draws nothing and a ToggleSwitch throws (PROGRESS.md, 2026-10-01). The process-wide appearance
// (Application.RequestedThemeVariant) is set by WindowManager's HostedRoot (WindowManager.swift:152-159).
using Avalonia;
using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Port.Voice.Notes;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY. The application object (NSApp).</summary>
public sealed class MarrowApplication : Application
{
    /// <summary>
    /// The app's builder: the platform detected for this machine (Win32 with Skia and HarfBuzz on Windows, as probe P4b
    /// ran the live-resize remedy OD-0017 adopted: porting/probes/p4b/Program.cs) and the port's faces, registered at the
    /// one point ED-107 (5) names, shared with the headless TestApp.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MarrowApplication>()
        .UsePlatformDetect()
        .WithSwiftFonts()
        .WithPortControlThemes() // default templates for controls built without one (Marrow.UI/Port/PortControlThemes.cs)
        .AfterSetup(_ =>
        {
            SystemReduceMotion.Install(); // ED-084: every window follows Windows' "Animation effects" setting
            SystemWheel.Install(); // ED-NEW-softscroll-2: a wheel notch scrolls Windows' "lines to scroll" setting's distance (read only)
            SystemHighContrast.Install(); // ED-NEW-web-w20-1: every window follows Windows' contrast theme
            Marrow.UI.NthUi.Diagram.NthUiDiagramSeat.Install(); // ED-NEW-web-w16c-1: every window has a seat for the hidden diagram renderer's web view (empty until a renderer is bound)
            WindowsTaskbarOverlay.Install(); // ED-219: the Dock unread badge is the taskbar overlay icon
            CudaOfferComposition.Install(StoragePaths.Root);
            ProcessorFeatureSeams.Install();
            VoiceComposition.Install(); // VT-14 (voice-SPEC V-2.4): the voice seams' production parts; the storage roots are bound before the app is built (Program.cs)
        });
}
