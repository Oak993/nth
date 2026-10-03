using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Marrow.UI.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/FloatingGuidePanel.swift (B3 52bc7926), :1-71.

/// <summary>
/// PORT-ONLY (OD-0075, owner 2026-10-02: "the wedge is filled with the window's scrim"; ED-236 UNRESOLVED (a)). The view both guide panels put in their
/// window (MCPKeyPanelView, MCPSetupPanelView). The panel's window is a rectangle of the system's Acrylic (<see cref="FloatingGuidePanel"/> MakePanel) and
/// the view draws its 26 pt shape over it (<c>.clipShape(RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous))</c>,
/// MCPKeyPanel.swift:57, MCPSetupPanel.swift:130), so between the shape and the rectangle the blur would show without the scrim, the wedge the main and
/// Settings windows fill in RootView and SettingsView. The view is the window's whole content, so it paints the wedge itself under what it holds, with
/// the scrim its own <c>WindowBackdrop(opacity: 0.5)</c> draws (the same drawing as <see cref="WindowCornerWedge"/>, which cannot be a layer of the
/// setup panel's view: everything it holds is clipped to the shape).
/// </summary>
public abstract class FloatingGuidePanelView : Decorator
{
    /// <summary>MCPKeyPanel.swift:56, MCPSetupPanel.swift:129 <c>WindowBackdrop(opacity: 0.5)</c>: the opacity of the backdrop whose scrim the corners wear.</summary>
    public const double BackdropOpacity = 0.5;

    protected FloatingGuidePanelView()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual(); // the scrim is white in the light theme and black in the dark one
    }

    public override void Render(DrawingContext context)
    {
        WindowCornerWedge.Paint(context, Bounds.Size, BackdropOpacity, ActualThemeVariant);
        base.Render(context);
    }
}

/// <summary>:6-71 <c>@MainActor class FloatingGuidePanel</c>: the floating, non-activating panel the MCP key and
/// setup guides share: sized to its content, parked in the top-right corner of the main screen and closed on
/// Escape.</summary>
public class FloatingGuidePanel
{
    private const int CbtHook = 5; // ED-063 WH_CBT
    private const int CbtActivate = 5; // ED-063 HCBT_ACTIVATE

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    // ED-063: Avalonia can call SetFocus on a panel even when WS_EX_NOACTIVATE and MA_NOACTIVATE are used.
    // Refuse activation for this panel's HWND with a thread-local WH_CBT hook, made per instance (each
    // subclass has its own singleton and its own panel, so a hook shared across all instances would refuse
    // activation for the wrong panel once two are presented at once).
    private readonly HookProcedure activationHookProcedure;
    private IntPtr activationHook;
    private IntPtr activationPanelHandle;

    private readonly double width; // :10
    protected AvaloniaWindow? panel; // :8 private(set) var panel
    private bool escapeMonitorInstalled; // :9
    private IDisposable? escapeSubscription;

    // :12-14 init(width:)
    protected FloatingGuidePanel(double width)
    {
        this.width = width;
        activationHookProcedure = RefusePanelActivation;
    }

    // :16-26 func present(_ content: some View)
    protected void Present(Control content)
    {
        var window = panel ?? MakePanel(); // :18
        window.Content = content; // :19
        window.Width = width;
        // :17 fittingSize of the content at the panel's width, measured once it is in the panel: a detached control has
        // no styles or templates yet, so its fields and buttons measured short (the key guide by 16-28 pt).
        window.Measure(new Size(width, double.PositiveInfinity));
        content.Measure(new Size(width, double.PositiveInfinity));
        var measuredHeight = Math.Ceiling(content.DesiredSize.Height);
        window.Height = Math.Max(measuredHeight, 100); // :20

        // :21-23 Place at the main screen's visible top right with a 24-point inset.
        if (window.Screens.Primary is { } screen)
        {
            var workingArea = screen.WorkingArea;
            var scaling = screen.Scaling;
            window.Position = new PixelPoint(
                checked((int)Math.Round(workingArea.X + workingArea.Width - (width + 24) * scaling)),
                checked((int)Math.Round(workingArea.Y + 24 * scaling)));
        }

        // :24-25
        StartEscapeMonitor();
        window.Show();
        StartActivationHook(window);
    }

    // :28-31 func close()
    public virtual void Close()
    {
        StopEscapeMonitor();
        StopActivationHook();
        panel?.Hide();
    }

    // :33-56 private func makePanel() -> NSPanel
    private AvaloniaWindow MakePanel()
    {
        var window = new AvaloniaWindow
        {
            Title = "",
            Width = width,
            Height = 200,
            MinHeight = 100,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            // :46-48 `panel.isOpaque = false / panel.backgroundColor = .clear`. Both subclasses lay a
            // WindowBackdrop(0.5) (no wallpaper) over this panel's content, and WindowBackdrop.GlassBase()
            // relies on the window itself compositing the Acrylic material where it shows the desktop
            // (OD-0005, OD-0014; Marrow.UI.Chrome.Chrome.WindowBackdrop.GlassBase()) - a plain Transparent
            // hint would leave the scrim over raw, unblurred desktop content instead of that material.
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur],
            Background = Brushes.Transparent,
        };
        panel = window;
        return window;
    }

    // :58-64 private func startEscapeMonitor(): an application-wide key-down monitor, installed once. Mirrors
    // NSEvent.addLocalMonitorForEvents(matching: .keyDown), which is scoped to the app process, not one window;
    // InputElement.KeyDownEvent.Raised fires for every KeyDown raised anywhere in the app (Marrow.UI.Port.AppCurrentEvent
    // uses the same primitive for the same reason).
    private void StartEscapeMonitor()
    {
        if (escapeMonitorInstalled)
            return;

        escapeSubscription = InputElement.KeyDownEvent.Raised.Subscribe(
            new AnonymousObserver<(object Sender, RoutedEventArgs Args)>(OnAppKeyDown));
        escapeMonitorInstalled = true;
    }

    // :66-69 private func stopEscapeMonitor()
    private void StopEscapeMonitor()
    {
        escapeSubscription?.Dispose();
        escapeSubscription = null;
        escapeMonitorInstalled = false;
    }

    // :59-63 the monitor body: swallow Escape and close while the panel is visible; pass everything else through.
    private void OnAppKeyDown((object Sender, RoutedEventArgs Args) e)
    {
        if (panel?.IsVisible != true || e.Args is not KeyEventArgs { Key: Key.Escape } keyArgs)
            return;

        keyArgs.Handled = true;
        Close();
    }

    // ED-063: install a thread-local WH_CBT hook for the visible panel HWND.
    private void StartActivationHook(AvaloniaWindow window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || activationPanelHandle == handle && activationHook != IntPtr.Zero)
            return;

        StopActivationHook();
        activationPanelHandle = handle;
        activationHook = SetWindowsHookEx(CbtHook, activationHookProcedure, IntPtr.Zero, GetCurrentThreadId());
    }

    // ED-063: the hook is needed only while the panel is presented.
    private void StopActivationHook()
    {
        if (activationHook != IntPtr.Zero)
            UnhookWindowsHookEx(activationHook);

        activationHook = IntPtr.Zero;
        activationPanelHandle = IntPtr.Zero;
    }

    // ED-063: refuse HCBT_ACTIVATE for the panel HWND, allowing other windows through.
    private IntPtr RefusePanelActivation(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == CbtActivate && wParam == activationPanelHandle && activationPanelHandle != IntPtr.Zero)
            return new IntPtr(1);

        return CallNextHookEx(activationHook, code, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProcedure procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
