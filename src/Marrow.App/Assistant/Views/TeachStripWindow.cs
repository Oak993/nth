// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1; unit AS-19; ED-NEW-dextra-as19-8, -9, -10). No Swift twin.
// The window the recording strip sits in, and the host that puts it up and takes it away. The window is borderless, always on top, left out of
// the taskbar and out of every screen copy (SetWindowDisplayAffinity: so a frame never shows it), and never takes the focus (WS_EX_NOACTIVATE:
// what the owner types must go to the app he is showing). The host is the product's indicator: a recording starts only if the strip goes up.
// The native styles are the lead's to check on a real desktop (TeachStripNativeTests).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Assistant.Teach;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Assistant.Views;

public sealed class TeachStripWindow : AvaloniaWindow
{
    /// <summary>The gap between the top of the work area and the strip.</summary>
    public const int TopGap = 8;

    public TeachStripWindow(TeachStripModel model)
    {
        Model = model;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Title = "Teach recording";
        Content = Strip = new TeachStrip(model);
    }

    public TeachStripModel Model { get; }

    public TeachStrip Strip { get; }

    /// <summary>Whether Windows accepted the display affinity: the strip is left out of every frame.</summary>
    public bool ExcludedFromCapture { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Place();
        if (TryGetPlatformHandle()?.Handle is { } handle && handle != 0) Style(handle);
    }

    // Top centre of the main screen's work area.
    private void Place()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var width = (int)Math.Ceiling(Bounds.Width * screen.Scaling);
        Position = new PixelPoint(screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - width) / 2), screen.WorkingArea.Y + (int)(TopGap * screen.Scaling));
    }

    private void Style(nint handle)
    {
        ExcludedFromCapture = TeachNative.SetWindowDisplayAffinity(handle, TeachNative.WdaExcludeFromCapture);
        var style = TeachNative.GetWindowLongPtr(handle, TeachNative.GwlExStyle);
        TeachNative.SetWindowLongPtr(handle, TeachNative.GwlExStyle, (nint)((long)style | TeachNative.WsExNoActivate | TeachNative.WsExToolWindow));
    }
}

/// <summary>The product's indicator: puts the strip up when a recording starts and takes it away when it ends.</summary>
public sealed class TeachStripHost : ITeachIndicator
{
    private TeachStripWindow? window;
    private TeachStripModel? model;

    public bool Show(TeachController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return OnUi(() =>
        {
            Close();
            try
            {
                model = new TeachStripModel(controller);
                window = new TeachStripWindow(model);
                window.Show();
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
            {
                Close();
                return false;
            }
        });
    }

    public void Hide() => Dispatcher.UIThread.Post(Close);

    /// <summary>The strip now, for tests and the Native check.</summary>
    public TeachStripWindow? Window => window;

    private void Close()
    {
        window?.Close();
        window = null;
        model?.Dispose();
        model = null;
    }

    private static bool OnUi(Func<bool> work) => Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.Invoke(work);
}
