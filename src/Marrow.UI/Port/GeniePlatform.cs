// PORT-ONLY: the AppKit and SwiftUI reads UI/Common/Genie.swift makes (NSWorkspace, NSScreen, ImageRenderer), over Windows and Avalonia.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Marrow.UI.Port;

/// <summary>
/// The three platform reads of UI/Common/Genie.swift: <c>NSWorkspace.shared.accessibilityDisplayShouldReduceMotion</c>
/// (:64, :84), <c>NSScreen.main?.backingScaleFactor</c> (:87) and <c>ImageRenderer(content:)</c> with its
/// <c>scale</c> and <c>nsImage</c> (:85-88). Tests script them; the app uses <see cref="WindowsGeniePlatform"/>.
/// </summary>
public interface IGeniePlatform
{
    /// <summary>Read live at each call, never cached (port_impacts.md X4, ED-084).</summary>
    bool AccessibilityDisplayShouldReduceMotion { get; }

    /// <summary>The main screen's scale factor, or null when there is no main screen to ask.</summary>
    double? MainScreenBackingScaleFactor { get; }

    /// <summary>Renders <paramref name="content"/> once, at its own size, into a flat image; null when nothing renders.</summary>
    IImage? RenderImage(Control content, double scale);
}

/// <summary>The Windows 11 answers to the three reads.</summary>
public sealed class WindowsGeniePlatform : IGeniePlatform
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool enabled, uint update);

    // ED-084, port_impacts.md X4: Windows' "Animation effects" switch, inverted, queried at every call as the source
    // reads the current accessibility value at each morph and launch.
    public bool AccessibilityDisplayShouldReduceMotion =>
        OperatingSystem.IsWindows() && SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) && !enabled;

    // ENGINEERING DECISION: NSScreen.main is the screen holding the key window; the port asks the screen of the app's
    // main window, as AttachmentFlightPlatform does for the same AppKit read. No window or screen gives null, which
    // Genie.swift:87 turns into 2.
    public double? MainScreenBackingScaleFactor
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not { } main) return null;
            return main.Screens.ScreenFromWindow(main)?.Scaling;
        }
    }

    // ImageRenderer with no proposed size lets the view size itself (the ghost fixes its own frame), and renders at
    // `scale` pixels per point. ENGINEERING DECISION: a fractional pixel count rounds up so no edge of the row is lost;
    // an empty size gives no image, as ImageRenderer's nsImage is nil for a view with nothing to draw.
    public IImage? RenderImage(Control content, double scale)
    {
        content.Measure(Size.Infinity);
        content.Arrange(new Rect(content.DesiredSize));
        var size = content.DesiredSize;
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        if (pixels.Width < 1 || pixels.Height < 1) return null;
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(content);
        return bitmap;
    }
}
