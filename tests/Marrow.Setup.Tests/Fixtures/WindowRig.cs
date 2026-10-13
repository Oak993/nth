using Avalonia.Threading;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>A setup window on a hand-driven clock, with settings and a shutdown block a test controls (create it on the UI thread).</summary>
public sealed class WindowRig
{
    public const string Root = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth";

    public WindowRig(SystemLook? look = null)
    {
        Source = new FakeLookSource(look ?? SystemLook.Default);
        Settings = new SystemSettings(Source);
        Window = new SetupWindow(Settings, Frames, Blocker);
    }

    /// <summary>
    /// Where the sessions' route points (<see cref="Root"/>, a long name whose path takes two lines in Options, unless a test
    /// gives a typical one: the window's opening height follows the tallest screen it can reach, ED-NEW-inst-fix-7).
    /// </summary>
    public string InstallRoot { get; init; } = Root;

    public FakeLookSource Source { get; }

    public SystemSettings Settings { get; }

    public ManualFrameSource Frames { get; } = new();

    public FakeShutdownBlocker Blocker { get; } = new();

    public SetupWindow Window { get; }

    /// <summary>What the screens did outside the window (unit U2).</summary>
    public FakeSetupActions Actions { get; } = new();

    public static SystemLook Calm => SystemLook.Default with { Animations = false };

    public static SystemLook Light => SystemLook.Default with { Dark = false };

    public static SystemLook Contrast => SystemLook.Default with { HighContrast = true };

    /// <summary>Changes Windows' settings as a WM_SETTINGCHANGE would make the window read them.</summary>
    public void Change(SystemLook look)
    {
        Source.Look = look;
        Settings.Refresh();
    }

    /// <summary>A session on this window with the canvas's screens (unit U2), starting on <paramref name="screen"/>.</summary>
    public SetupSession Session(SetupScreen screen, string? installed = null, bool canInstall = true, params string[] arguments)
    {
        var action = screen switch
        {
            SetupScreen.Update => InstallerAction.Upgrade,
            SetupScreen.UpToDate => InstallerAction.AlreadyUpToDate,
            SetupScreen.NewerInstalled => InstallerAction.RefuseNewerInstalled,
            _ => InstallerAction.Install,
        };
        var route = new SetupRoute(screen, action, new InstalledRoot(InstallRoot, installed, installed is not null), InstallRoot);
        return new SetupSession(Window, route, new CanvasScreens(), "1.1.0", canInstall, Actions, arguments);
    }

    /// <summary>Lays the window out and lets the headless platform apply the size it asked for (it posts resizes to the dispatcher).</summary>
    public void Settle()
    {
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    /// <summary>Runs frames of the window's clock for <paramref name="seconds"/> (and lays out after each).</summary>
    public void Run(double seconds) => Frames.Run(seconds, () => Window.UpdateLayout());
}
