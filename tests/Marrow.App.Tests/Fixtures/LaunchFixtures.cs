using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Markdown;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests.Fixtures;

// Helpers for the launch tests of AppDelegate.swift :9-39 (AppDelegateThirdTests) and the port-only application shell
// (Port/AppShell.cs; AppShellTests, AppShellSecondTests). Helper types live here, never inside a test class (R-TEST-3).

/// <summary>
/// One launch, isolated (R-TEST-9, R-TEST-10): an <see cref="AppModelHarness"/> (manual main loop and clock, in-memory
/// settings, throwaway store root), a fresh <see cref="WindowManager"/> installed as WindowManager.shared for the calling
/// flow, whose two windows host stand-ins (RootView and SettingsView are still MARROW_STUB); a fresh UpdateChecker on the
/// empty feed (OD-0012 I1, no network) and a fresh UpdateInstallProgress; AppInfo pinned to 1.7.0 (not a development
/// build, so UpdateChecker.startBackgroundChecks does start); the tour and the bootstrap replaced by recorders on the
/// delegate, and MarkdownDelegationCard's process-wide delegation-block hook put back afterwards.
/// </summary>
public sealed class LaunchHarness : IDisposable
{
    public const string RunningVersion = "1.7.0";

    private readonly List<IDisposable> scopes = [];
    private readonly MarkdownDelegationCard.RenderDelegate? savedDelegationBlock;
    private readonly URLCache savedUrlCache;

    public LaunchHarness(Action<AppModelHarness>? prepare = null)
    {
        Harness = new AppModelHarness();
        scopes.Add(AppInfo.Running.Override(Running(RunningVersion)));
        prepare?.Invoke(Harness); // library and settings on disk and in the store before the model reads them
        Manager = Harness.Main.OnMain(() => new WindowManager());
        scopes.Add(WindowManager.OverrideShared(Manager));
        Checker = Harness.Main.OnMain(() => new UpdateChecker(AppInfo.Current, [], UpdateChecker.DefaultFeed));
        scopes.Add(UpdateChecker.OverrideShared(Checker));
        Progress = UpdateInstallProgressFixtures.NewProgress();
        scopes.Add(UpdateInstallProgress.Running.Override(Progress));
        savedDelegationBlock = MarkdownDelegationCard.Render;
        MarkdownDelegationCard.Render = null;
        // URLCache.Shared is process-wide (Core/Port/Http.cs), like MarkdownDelegationCard.Render above (R-TEST-5).
        savedUrlCache = URLCache.Shared;

        Manager.MainContent = () =>
        {
            Events.Add("main window built");
            SelectedWhenMainWindowBuilt = Manager.Model.SelectedThreadID;
            DelegationBlockWhenMainWindowBuilt = MarkdownDelegationCard.Render;
            UrlCacheWhenMainWindowBuilt = URLCache.Shared;
            return MainContent;
        };
        Manager.SettingsContent = () =>
        {
            Events.Add("settings window built");
            return SettingsContent;
        };
        Delegate.PresentTour = model =>
        {
            Events.Add("tour");
            TourModels.Add(model);
        };
        Delegate.BootstrapModel = model =>
        {
            Events.Add("bootstrap");
            BootstrapModels.Add(model);
            return Task.CompletedTask;
        };
    }

    public AppModelHarness Harness { get; }

    public WindowManager Manager { get; }

    public AppDelegate Delegate { get; } = new();

    public UpdateChecker Checker { get; }

    public UpdateInstallProgress Progress { get; }

    public Border MainContent { get; } = new();

    public Border SettingsContent { get; } = new();

    public List<string> Events { get; } = [];

    public List<AppModel> TourModels { get; } = [];

    public List<AppModel> BootstrapModels { get; } = [];

    public Guid? SelectedWhenMainWindowBuilt { get; private set; }

    public MarkdownDelegationCard.RenderDelegate? DelegationBlockWhenMainWindowBuilt { get; private set; }

    public URLCache? UrlCacheWhenMainWindowBuilt { get; private set; }

    /// <summary>The window a hosted stand-in sits in, by its logical parents.</summary>
    public static Avalonia.Controls.Window? WindowOf(Control content)
    {
        Avalonia.StyledElement? element = content;
        while (element is not null and not Avalonia.Controls.Window) element = element.Parent;
        return element as Avalonia.Controls.Window;
    }

    public void Dispose()
    {
        MarkdownDelegationCard.Render = savedDelegationBlock;
        URLCache.Shared = savedUrlCache;
        for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
        Harness.Dispose();
    }
}

/// <summary>What the shell's exit (the desktop lifetime's Shutdown in the app) and its after-termination step (the
/// settings store's Synchronize, ED-156) were asked to do, in order.</summary>
public sealed class ShellRecorder
{
    public List<string> Calls { get; } = [];

    public int Exits => Calls.Count(call => call == "exit");

    public void Exit() => Calls.Add("exit");

    public void AfterWillTerminate() => Calls.Add("after will terminate");
}
