// PORT-ONLY: Avalonia and Windows surface for the AppKit attachment ghost in AttachmentFlight.swift.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace Marrow.App.Port;

/// <summary>The three platform lookups and one ghost window that the Swift flight needs.</summary>
public interface IAttachmentFlightPlatform
{
    bool ReduceMotion { get; }
    IImage? ResolveImage(IImage image);
    Rect? MainScreen { get; }
    IAttachmentFlightSurface Create(Rect frame, Rect screen, IImage image);
}

/// <summary>A click-through ghost surface, with a drawn image frame and corner radius.</summary>
public interface IAttachmentFlightSurface
{
    void Apply(Rect frame, double radius);
    void Show();
    void OrderOut();
}

/// <summary>The Windows 11 version of the three AppKit queries in AttachmentFlight.swift:55-64.</summary>
public sealed class WindowsAttachmentFlightPlatform : IAttachmentFlightPlatform
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool enabled, uint update);

    // port_impacts X4: query each time, because the source reads the current accessibility value at each flight.
    public bool ReduceMotion => SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) && !enabled;

    // NSImage.cgImage can fail; a non-drawable, zero-sized Avalonia image is the corresponding early return.
    public IImage? ResolveImage(IImage image) => image.Size.Width > 0 && image.Size.Height > 0 ? image : null;

    // The main app window provides NSScreen.main's current screen. Without it there is no flight.
    public Rect? MainScreen
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not { } main) return null;
            var screen = main.Screens.ScreenFromWindow(main);
            if (screen is null) return null;
            var bounds = screen.Bounds;
            var scale = screen.Scaling;
            return new Rect(bounds.X / scale, bounds.Y / scale, bounds.Width / scale, bounds.Height / scale);
        }
    }

    public IAttachmentFlightSurface Create(Rect frame, Rect screen, IImage image) => new AttachmentFlightWindow(frame, screen, image);
}

/// <summary>A borderless transparent window at the top of the app's visual stack.</summary>
internal sealed class AttachmentFlightWindow : Avalonia.Controls.Window, IAttachmentFlightSurface
{
    private readonly AttachmentFlightLayer layer;
    public AttachmentFlightWindow(Rect frame, Rect screen, IImage image)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = frame.Width;
        Height = frame.Height;
        var scale = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.RenderScaling ?? 1
            : 1;
        Position = new PixelPoint((int)Math.Round(frame.X * scale), (int)Math.Round(frame.Y * scale));
        layer = new AttachmentFlightLayer(image);
        Content = layer;
    }

    public void Apply(Rect frame, double radius)
    {
        layer.ImageFrame = frame;
        layer.Radius = radius;
        layer.InvalidateVisual();
    }

    void IAttachmentFlightSurface.Show()
    {
        base.Show();
        // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW: no key/main role and mouse pass-through.
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, -20);
        SetWindowLongPtr(handle, -20, style | 0x08000000 | 0x00000020 | 0x00000080);
    }

    public void OrderOut() => Close();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}

internal sealed class AttachmentFlightLayer(IImage image) : Control
{
    public Rect ImageFrame { get; set; }
    public double Radius { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushClip(new RoundedRect(ImageFrame, Radius)))
        {
            var size = image.Size;
            var ratio = Math.Min(ImageFrame.Width / size.Width, ImageFrame.Height / size.Height);
            var drawSize = new Size(size.Width * ratio, size.Height * ratio);
            var draw = new Rect(
                ImageFrame.X + (ImageFrame.Width - drawSize.Width) / 2,
                ImageFrame.Y + (ImageFrame.Height - drawSize.Height) / 2,
                drawSize.Width, drawSize.Height);
            context.DrawImage(image, new Rect(image.Size), draw);
        }
    }
}
