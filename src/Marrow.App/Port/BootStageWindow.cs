// PORT-ONLY. OD-0018 / ED-103: every launch opens with the boot sequence instead of a plain show of the main window
// (AppDelegate.swift:22). The particle stage is a transparent, borderless, click-through, taskbar-less window over the
// work area of the monitor the main window will open on; the mark is centred on the main window's frame as it is
// restored; when the outline has landed the main window is shown, its root content following the share of particles
// landed (opacity from 45 % to 100 % of them, scale .985 to 1), and the stage closes 0.35 s after the last particle lands.
// The drawing and the timeline are F.LoadingMark's (BootStage, BootSequence, ED-102, ED-154); this file is the launch
// side ED-154 (4) gives it: the stage window and the hand-over.
//
// Probe P12 (ED-103 (7), 2026-10-01, E:\Droppy\work\probes\p12): on Windows 11 an Avalonia window with
// WindowDecorations.None, TransparencyLevelHint Transparent and WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW |
// WS_EX_NOACTIVATE draws the stage over the desktop (ActualTransparencyLevel Transparent) and every point under it
// hit-tests to the window below (WindowFromPoint never returned the stage); with the app ready at 2.0 s the main window
// was shown at 5.26 s and the stage closed at 5.84 s, as ED-103 (5) gives.
//
// ED-236 (OD-0041, probes P13 and P14): the main window carries an Acrylic backdrop (WindowChrome.Configure), whose tint
// shows ~80-120 ms after Show and whose blur ~230 ms later. So the main window is shown early and cloaked (WindowCloak,
// DWMWA_CLOAK) as the disintegration begins (reduce motion: 0.5 s before it is due), under the stage, its root content
// kept at the stage's opacity; at ED-103 (5)'s hand-over it is uncloaked and then shown/activated as before. Where it
// cannot be cloaked (no DWM window, DWM refuses) nothing is shown early and the hand-over is the plain Show.
//
// ENGINEERING DECISIONs (no product effect beyond ED-103's own): the content areas the window particles land on are the
// main window's title bar (top 38 DIP), its sidebar (left 260 DIP), its composer (bottom 96 DIP of the rest) and its
// conversation (the remainder), from the main window's own layout constants at launch (UNVERIFIED sizes);
// "the app is ready" is the end of AppModel.bootstrap() (AppDelegate.swift:27).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
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
    private readonly BootStage stage;
    private readonly Action showMain;
    private readonly Func<AvaloniaWindow, bool, bool> cloak;
    private readonly bool reduceMotion;
    private Control? content;
    private bool warmTried;
    private bool cloaked;
    private bool following;

    /// <summary>ED-236: reduce motion shows the cloaked main window this long before the hand-over (2.5 s).</summary>
    internal const double ReduceMotionWarmLead = 0.5;

    /// <summary>The stage control (for tests).</summary>
    internal BootStage Stage => stage;

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
        var frame = new Rect((main.Position.X - area.X) / scale, (main.Position.Y - area.Y) / scale, main.Width, main.Height);
        stage = new BootStage(new BootSequence(Layout(frame), reduceMotion));
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
        stage.FrameChanged += OnFrame;
        stage.MainWindowShown += (_, _) => ShowMain();
        stage.Closed += (_, _) => Finish();
    }

    /// <summary>The main window cloaked early (ED-236); false until then, and for good where it cannot be cloaked (tests).</summary>
    internal bool MainIsCloaked => cloaked;

    /// <summary>Opens the stage over <paramref name="main"/>'s restored frame; <paramref name="showMain"/> shows it when
    /// the outline has landed. <paramref name="cloak"/> (window, cloaked) is ED-236's early, hidden show of the main
    /// window; it returns whether the window took it. The app passes none, which is DWM cloaking (<see cref="WindowCloak"/>).</summary>
    public static BootStageWindow Start(AvaloniaWindow main, bool reduceMotion, Action showMain, Func<AvaloniaWindow, bool, bool>? cloak = null)
    {
        var boot = new BootStageWindow(main, reduceMotion, showMain, cloak ?? WindowCloak.Set);
        boot.stageWindow.Show();
        return boot;
    }

    /// <summary>ED-103 (3): the app is ready; the mark comes to its final rest at the end of the current turn.</summary>
    public void Ready() => stage.Ready();

    /// <summary>The layout the window particles fly to, in stage coordinates (see the file note).</summary>
    internal static BootWindowLayout Layout(Rect frame)
    {
        const double titleBar = 38, sidebar = 260, composer = 96;
        var body = new Rect(frame.X, frame.Y + titleBar, frame.Width, Math.Max(0, frame.Height - titleBar));
        var side = new Rect(body.X, body.Y, Math.Min(sidebar, body.Width), body.Height);
        var chat = new Rect(side.Right, body.Y, Math.Max(0, body.Right - side.Right), Math.Max(0, body.Height - composer));
        var input = new Rect(side.Right, chat.Bottom, chat.Width, Math.Min(composer, body.Height));
        return new BootWindowLayout(frame, 14, [new Rect(frame.X, frame.Y, frame.Width, titleBar), side, chat, input]);
    }

    // ED-236: the early, cloaked show. The window is cloaked before it is shown, and shown without activation (the launch
    // activates it at the hand-over, as before), so it is never displayed and never takes the foreground under the stage.
    private void WarmMain()
    {
        warmTried = true;
        if (main.IsVisible) return;
        var activated = main.ShowActivated;
        main.ShowActivated = false;
        try
        {
            if (!cloak(main, true)) return;
            cloaked = true;
            Follow();
            main.Show();
        }
        finally { main.ShowActivated = activated; }
    }

    // ED-236: the disintegration is the earliest the hand-over can be (the outline lands at least 0.8 s into it, ED-103 (4));
    // reduce motion has a fixed hand-over at 2.5 s.
    private bool WarmDue() => reduceMotion
        ? stage.Elapsed >= 2.5 - ReduceMotionWarmLead
        : stage.Frame.Phase == BootPhase.Disintegrating;

    // ED-103 (5): from now on the root content follows the stage.
    private void Follow()
    {
        content = main.Content as Control;
        if (content is null || following) return;
        following = true;
        Apply(stage.Frame);
    }

    private void ShowMain()
    {
        warmTried = true; // the hand-over is here: no early show after it
        Follow();
        if (cloaked)
        {
            cloaked = false;
            cloak(main, false); // the content is already at this frame's opacity (Follow), so the first frame shown is the hand-over's
        }
        showMain();
        MainWindowShown?.Invoke(this, EventArgs.Empty);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!warmTried && WarmDue()) WarmMain();
        if (following) Apply(stage.Frame);
    }

    // ED-103 (5): the root content's opacity follows the share of all particles landed, its scale .985 to 1.
    private void Apply(BootFrame frame)
    {
        if (content is null) return;
        content.Opacity = frame.ContentOpacity;
        content.RenderTransformOrigin = RelativePoint.Center;
        content.RenderTransform = new ScaleTransform(frame.ContentScale, frame.ContentScale);
    }

    private void Finish()
    {
        stage.FrameChanged -= OnFrame;
        if (cloaked)
        {
            cloaked = false; // the hand-over never came (a stage closed before it): do not leave the window hidden
            cloak(main, false);
        }
        if (content is not null)
        {
            content.Opacity = 1;
            content.RenderTransform = null;
        }
        stageWindow.Close();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint window, int index, long value);
}
