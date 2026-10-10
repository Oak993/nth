// PORT-ONLY: OD-0113 Boot B supersedes ED-103/154 Comet startup. The locked Aurora page drives
// one growing frame, content/glass fade, and ED-236 cloaked preparation.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

/// <summary>The launch's boot sequence (see the file note).</summary>
public sealed class BootStageWindow
{
    private const int GwlExStyle = -20;
    private const long ClickThrough = 0x20 | 0x80000 | 0x80 | 0x08000000; // TRANSPARENT | LAYERED | TOOLWINDOW | NOACTIVATE

    private readonly AvaloniaWindow main;
    private readonly AvaloniaWindow stageWindow;
    private readonly AuroraBootStage stage;
    private readonly AuroraBootPresentation presentation;
    private readonly Action showMain;
    private readonly Func<AvaloniaWindow, bool, bool> cloak;
    private readonly bool reduceMotion;
    private Control? content;
    private bool warmTried;
    private bool cloaked;
    private bool following;
    private readonly IDisposable idleHold;

    /// <summary>The stage control (for tests).</summary>
    internal AuroraBootStage Stage => stage;

    /// <summary>The exact visible frame and content areas, in stage coordinates (for tests).</summary>
    internal BootWindowLayout OutlineLayout => Layout(stage.WindowFrame);
    internal bool WholeWindowOpacitySupported => presentation.OpacitySupported;

    /// <summary>The stage window (for tests).</summary>
    internal AvaloniaWindow Window => stageWindow;

    /// <summary>Runs once the main window has been shown by the sequence.</summary>
    public event EventHandler? MainWindowShown;

    private BootStageWindow(AvaloniaWindow main, bool reduceMotion, Action showMain, Func<AvaloniaWindow, bool, bool> cloak)
    {
        this.main = main;
        this.showMain = showMain;
        this.cloak = cloak;
        this.reduceMotion = reduceMotion;
        var screen = main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, (int)main.Width, (int)main.Height);
        var scale = screen?.Scaling ?? 1;
        // ED-284 (1): main.Position is the HWND origin, one invisible strip (WindowInvisibleBorder, pixels) left of the visible window and
        // level with its top; the outline the boot hands over to is the visible window, so its left edge moves right by the strip.
        var strip = WindowInvisibleBorder.Pixels.Current(main, scale);
        var frame = new Rect((main.Position.X + strip - area.X) / scale, (main.Position.Y - area.Y) / scale, main.Width, main.Height);
        presentation = new AuroraBootPresentation(main);
        stage = new AuroraBootStage(frame, reduceMotion);
        stageWindow = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            CanResize = false,
            Topmost = true,
            ShowActivated = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = area.Position,
            Width = area.Width / scale,
            Height = area.Height / scale,
            Content = stage,
            IsHitTestVisible = false,
            Focusable = false,
        };
        stageWindow.Opened += (_, _) =>
        {
            if (stageWindow.TryGetPlatformHandle()?.Handle is { } handle and not 0)
                SetWindowLongPtr(handle, GwlExStyle, GetWindowLongPtr(handle, GwlExStyle) | ClickThrough);
        };
        DesktopPerformanceProbe.ObserveWindow(main, "main");
        DesktopPerformanceProbe.ObserveWindow(stageWindow, "boot stage");
        stage.FrameChanged += OnFrame;
        stage.MainWindowShown += (_, _) => ShowMain();
        // T44: HWND destruction performs a synchronous compositor commit. Never reenter it from RenderCore.
        stage.Closed += (_, _) => Dispatcher.UIThread.Post(Finish, DispatcherPriority.Background);
        stageWindow.Closed += (_, _) => FinishWithoutClosing();
        // ED-NEW-launch-fix (6): the idle warm-ups (the model chips') compile nothing on the render thread while B draws; a
        // stage that is not shown (closed, never opened) holds nothing
        idleHold = Marrow.App.Views.Composer.GpuWarmup.HoldIdle(() => stageWindow.IsVisible);
    }

    /// <summary>The main window cloaked early (ED-236); false until then, and for good where it cannot be cloaked (tests).</summary>
    internal bool MainIsCloaked => cloaked;

    /// <summary>Opens the stage over <paramref name="main"/>'s restored frame; <paramref name="showMain"/> shows it when
    /// the outline has landed. <paramref name="cloak"/> (window, cloaked) is ED-236's early, hidden show of the main
    /// window; it returns whether the window took it. The app passes none, which is DWM cloaking (<see cref="WindowCloak"/>).</summary>
    public static BootStageWindow Start(AvaloniaWindow main, bool reduceMotion, Action showMain, Func<AvaloniaWindow, bool, bool>? cloak = null)
    {
        AuroraStartupPreparation.Prepare();
        AuroraBootPreparation.Prepare();
        var boot = new BootStageWindow(main, reduceMotion, showMain, cloak ?? WindowCloak.Set);
        boot.stageWindow.Show();
        return boot;
    }

    /// <summary>OD-0113: bootstrap and preparation are ready; B can begin its grow after its launch.</summary>
    public void Ready()
    {
        using var work = AuroraBootTrace.Begin("Boot: initialization ready");
        stage.Ready();
    }

    /// <summary>The final window layout, in stage coordinates.</summary>
    internal static BootWindowLayout Layout(Rect frame)
    {
        const double titleBar = 38, sidebar = 260, composer = 96;
        var body = new Rect(frame.X, frame.Y + titleBar, frame.Width, Math.Max(0, frame.Height - titleBar));
        var side = new Rect(body.X, body.Y, Math.Min(sidebar, body.Width), body.Height);
        var chat = new Rect(side.Right, body.Y, Math.Max(0, body.Right - side.Right), Math.Max(0, body.Height - composer));
        var input = new Rect(side.Right, chat.Bottom, chat.Width, Math.Min(composer, body.Height));
        return new BootWindowLayout(frame, 8, [new Rect(frame.X, frame.Y, frame.Width, titleBar), side, chat, input]);
    }

    // ED-236: the early, cloaked show. The window is cloaked before it is shown, and shown without activation (the launch
    // activates it at the hand-over, as before), so it is never displayed and never takes the foreground under the stage.
    private void WarmMain()
    {
        using var work = AuroraBootTrace.Begin("Boot: UI cloaked show");
        warmTried = true;
        if (!stageWindow.IsVisible || main.IsVisible) return;
        var activated = main.ShowActivated;
        main.ShowActivated = false;
        try
        {
            if (!cloak(main, true)) return;
            cloaked = true;
            content = main.Content as Control;
            if (content is not null) content.Opacity = 0;
            main.Show();
            UpdateVisibleFrame();
            Follow();
            Apply(stage.Frame);
        }
        finally { main.ShowActivated = activated; }
    }

    private bool WarmDue() => true; // ED-236: cloak on the first stage frame, while bootstrap/preparation run.

    private void UpdateVisibleFrame()
    {
        if (presentation.VisibleFrame() is not { } visible) return;
        var origin = stageWindow.Position;
        stage.SetWindowFrame(new Rect((visible.X - origin.X) / stageWindow.RenderScaling, (visible.Y - origin.Y) / stageWindow.RenderScaling,
            visible.Width / stageWindow.RenderScaling, visible.Height / stageWindow.RenderScaling));
    }

    // OD-0113: content and the native glass share the stage's growing rectangle.
    private void Follow()
    {
        content = main.Content as Control;
        if (content is null || following) return;
        following = true;
        Apply(stage.Frame);
    }

    // T44: content attached after the first stage frame inherits the current clip/fade while cloaked.
    internal void ContentReady()
    {
        following = false;
        Follow();
    }

    private void ShowMain()
    {
        using var work = AuroraBootTrace.Begin("Boot: hand-over");
        warmTried = true; // the hand-over is here: no early show after it
        if (!following && main.Content is Control hiddenContent) hiddenContent.Opacity = 0;
        if (cloaked)
        {
            cloaked = false;
            cloak(main, false); // the content is already at this frame's opacity (Follow), so the first frame shown is the hand-over's
        }
        presentation.Behind(stageWindow);
        showMain();
        if (!following) { UpdateVisibleFrame(); Follow(); }
        Apply(stage.Frame);
        MainWindowShown?.Invoke(this, EventArgs.Empty);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!warmTried && WarmDue())
        {
            warmTried = true;
            Dispatcher.UIThread.Post(WarmMain, DispatcherPriority.Background);
        }
        if (following) Apply(stage.Frame);
    }

    // OD-0113: glass and content fade inside the one growing frame; the content itself stays at scale 1.
    private void Apply(AuroraBootFrame frame)
    {
        if (content is null) return;
        var complete = reduceMotion ? frame.ContentOpacity >= 1 : frame.Progress >= 1;
        presentation.Apply(complete ? null : frame.ContentClip, frame.CornerRadius, frame.ContentOpacity);
        // Native alpha fades glass and pixels together; fallback/headless fades the root. Never multiply the fade twice.
        content.Opacity = presentation.OpacitySupported ? 1 : frame.ContentOpacity;
        content.RenderTransform = null;
        content.Clip = complete ? null : new RectangleGeometry(frame.ContentClip, frame.CornerRadius, frame.CornerRadius);
    }

    private void Finish()
    {
        using var work = AuroraBootTrace.Begin("Boot: finish");
        idleHold.Dispose(); // ED-NEW-launch-fix (6)
        stage.FrameChanged -= OnFrame;
        presentation.Restore();
        if (cloaked)
        {
            cloaked = false; // the hand-over never came (a stage closed before it): do not leave the window hidden
            cloak(main, false);
        }
        if (content is not null)
        {
            content.Opacity = 1;
            content.RenderTransform = null;
            content.Clip = null;
        }
        stageWindow.Close();
    }

    private void FinishWithoutClosing()
    {
        idleHold.Dispose(); // ED-NEW-launch-fix (6)
        stage.FrameChanged -= OnFrame; presentation.Restore();
        if (cloaked) { cloaked = false; cloak(main, false); }
        if (content is not null) { content.Opacity = 1; content.RenderTransform = null; content.Clip = null; }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint window, int index, long value);
}
