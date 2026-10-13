using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.UI.Markdown;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :1-93. Parts 1 and 2 of 2 port model (:7), the quit path
// (applicationShouldTerminate(_:) :41-54, applicationWillTerminate(_:) :56-61) and the presentation gate
// userNotificationCenter(_:willPresent:) (:63-75); part 2 added tests of these members and no new member. The Boot unit
// adds applicationDidFinishLaunching(_:) (:9-34) and applicationShouldHandleReopen(_:hasVisibleWindows:) (:36-39), now
// that App.Window.WindowManager is in the tree; Port/AppShell.cs calls them where AppKit does (finishLaunching, and a
// second launch under ED-002). The App.AppDelegate unit adds the URLCache reconfiguration (:13, lane 1c landed
// Core/Port/Http.cs's public URLCache constructor and settable Shared). OD-0028 / ED-220 now close the historical
// notification STOP sites: delegate registration (:15) and userNotificationCenter(_:didReceive:) (:77-92).
// Forms: `@MainActor final class` that is not @Observable -> a plain class whose members run on main (R-ISO-1);
// NSApplication, NSApplication.TerminateReply, NSApp.isActive and the UserNotifications values are the port-only
// stand-ins in Port/ApplicationLifecycle.cs and Port/UserNotifications.cs. The unused `Notification`,
// `UNUserNotificationCenter` and `NSApplication` parameters have no port type and carry nothing the bodies read; they are
// dropped.

namespace Marrow.App;

/// <summary>:5-93 <c>@MainActor final class AppDelegate: NSObject, NSApplicationDelegate,
/// UNUserNotificationCenterDelegate</c>.</summary>
public sealed class AppDelegate
{
    private BootStageWindow? preparedBoot;
    private Action? showPreparedMain;

    public AppDelegate() => StartBoot = StartDefaultBoot;

    // T44: the restored shell and approved stage precede palette/model/content initialization.
    internal void PrepareBoot() => preparedBoot ??= BootStageWindow.Start(
        WindowManager.Shared.PrepareMainShell(), ReduceMotion(), () => showPreparedMain?.Invoke());

    private BootStageWindow StartDefaultBoot(WindowManager windows, Action show)
    {
        if (preparedBoot is not { } boot) return BootStageWindow.Start(windows.PrepareMain(), ReduceMotion(), show);
        windows.PrepareMain(); // GettingReadyLaunch has wrapped the factory before content is attached.
        boot.ContentReady();
        showPreparedMain = show;
        return boot;
    }
    // :7 var model: AppModel?
    public AppModel? Model { get; set; }

    // :25 `Tour.present(model:)` and :27 `windows.model.bootstrap()`, as launch calls them. Internal and settable only so
    // the launch tests can record them: the tour window is a process-wide singleton (TourWindowController.shared) and the
    // bootstrap starts providers and git, and each is tested in its own unit. Nothing in the app sets them.
    internal Action<AppModel> PresentTour { get; set; } = Tour.Present;

    internal Func<AppModel, Task> BootstrapModel { get; set; } = model => model.Bootstrap();

    /// <summary>
    /// PORT-ONLY (AS-10, ED-NEW-dextra-as10-13): nth was started hidden (<c>--hidden</c> at sign-in, <c>--duty id</c> by Task Scheduler's wake): no
    /// boot stage, no main window, no tour; the model is bootstrapped and <see cref="AfterBootstrap"/> starts the duties. Set by the entry point only.
    /// </summary>
    internal bool StartHidden { get; set; }

    /// <summary>PORT-ONLY (AS-10): runs once the model is bootstrapped, with the window shown or not (the duties start here). Set by the entry point only.</summary>
    internal Action<AppModel>? AfterBootstrap { get; set; }

    /// <summary>GR-2: the first-run preparation the Getting ready sheet shows (OD-0096). Settable only so the launch tests can script it; nothing in the app sets it.</summary>
    internal Marrow.App.Port.FirstRun.FirstRunPreparation FirstRun { get; set; } = Marrow.App.Port.FirstRun.FirstRunPreparation.Default;

    /// <summary>
    /// :9-34 <c>func applicationDidFinishLaunching(_ notification: Notification)</c>, called by the shell where AppKit's
    /// finishLaunching calls it: once, on main, after the prologue (DroppyCodeApp.swift:7-19) and before the run loop.
    /// </summary>
    public void ApplicationDidFinishLaunching()
    {
        // :10-13 the shared cache wrote a SQLite commit for every response; a third of the app's disk writes while a
        // team ran. Memory only.
        URLCache.Shared = new URLCache(8 * 1024 * 1024, 0); // :13
        MarkdownDelegationCard.Render = (json, sent) => new HydraDelegationBlock(json, sent); // :14
        ToastNotifications.Center.Current.InstallDelegate(this); // :15, ED-220 (5)-(6), before any window
        var windows = WindowManager.Shared; // :16
        Model = windows.Model; // :17
        Marrow.App.Port.Voice.VoiceComposition.Attach(windows.Model.Settings); // VT-15: register hardware before GR-1 takes its plan; consent still gates all downloads
        if (StartHidden)
        {
            // AS-10: nothing is shown. The model is bootstrapped (providers, projects, threads) and the duties start; the owner's
            // window is made when he asks for it (the tray's Open nth, a second launch), by the same ShowMain.
            MainActor.Spawn(async () =>
            {
                await BootstrapModel(windows.Model);
                AfterBootstrap?.Invoke(windows.Model);
            });
            UpdateChecker.Shared.StartBackgroundChecks();
            UpdateEverythingProduction.Configure(windows.Model);
            return;
        }
        // PORT-ONLY (OD-0096, GR-2): when the machine is not prepared (the first run, a new build, a new graphics card or driver) the main
        // window is made with the Getting ready sheet over its RootView, so the sheet is in its first frame; armed before :22 makes the window.
        var firstRun = Marrow.App.Port.FirstRun.GettingReadyLaunch.Arm(windows, FirstRun);
        // :18-21 the capture-run branch: not ported (ED-001; PR-LAUNCH-027 NOT_APPLICABLE_WITH_EVIDENCE).
        // :22 windows.showMain(). DEVIATION OD-0018 (PR-LAUNCH-028), ED-103: the boot sequence shows the main window once
        // the stage's particles have built its outline (Port/BootStageWindow.cs; probe P12 2026-10-01).
        var mainShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var boot = StartBoot(windows, () => { windows.ShowMain(); mainShown.TrySetResult(); });
        // GR-2: once the window is shown the preparation runs behind the sheet; the first-run tour (a window over the main one) waits for the
        // sheet to lift, where it would sit over it. No sheet (the machine is prepared): nothing waits.
        var firstRunDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainActor.Spawn(async () =>
        {
            try
            {
                if (boot is null) await mainShown.Task;
                _ = await firstRun.RunAsync(boot?.Window ?? windows.MainWindow); // OD-0113: warm-up behind B while the main HWND is cloaked
            }
            finally { firstRunDone.TrySetResult(); }
        });
        windows.Model.OpenThreadOnLaunch(); // :23
        if (!windows.Model.Settings.HasSeenTour) // :24
        {
            MainActor.Spawn(async () => // :25 Task { @MainActor in ... }
            {
                // ED-103: the 700 ms are counted from the main window's appearance, so the tour never opens over the stage.
                await mainShown.Task;
                await firstRunDone.Task; // GR-2: not over the Getting ready sheet
                _ = await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(700)); // try? await Task.sleep(for: .milliseconds(700))
                PresentTour(windows.Model);
            });
        }
        MainActor.Spawn(async () => // :27 Task { await windows.model.bootstrap() }
        {
            var bootstrap = BootstrapModel(windows.Model);
            // OD-0133 (amending OD-0108 for provider checks only): B waits for the bootstrap up to its provider refresh
            // (AppModel.LaunchReady), or for all of it where it ends first; the refresh, the sign-in checks and the catalogs
            // finish after the window shows. A bootstrap that fails before then still never reveals the window, as before.
            await await Task.WhenAny(bootstrap, windows.Model.LaunchReady);
            if (boot is not null) await firstRunDone.Task;
            boot?.Ready(); // OD-0108 / OD-0113: bootstrap (to its provider refresh) AND preparation must finish before B reveals the window
            await bootstrap;
            AfterBootstrap?.Invoke(windows.Model); // AS-10: the duties start with the window up too
        });
        UpdateChecker.Shared.StartBackgroundChecks(); // :28
        UpdateEverythingProduction.Configure(windows.Model); // UE-5 (OD-0100): Update everything's items and its background check; no Swift twin
        // :29 The relaunch after an install: the update story finishes on About, where it began.
        if (AppUpdater.Shared.ConsumeRelaunchMarker()) // :30
        {
            UpdateInstallProgress.Shared.ArmCelebration(); // :31
            // ED-103: About opens over the main window once it has appeared, not over the stage.
            MainActor.Spawn(async () =>
            {
                await mainShown.Task;
                windows.ShowSettings(page: SettingsPage.About); // :32
            });
        }
    }

    // PORT-ONLY (OD-0018, ED-103): the boot sequence, or a plain show when there is no stage to run. Settable only so the
    // launch tests, which run headless and record the main window, can keep the plain show; nothing in the app sets it.
    internal Func<WindowManager, Action, BootStageWindow?> StartBoot { get; set; }

    // ED-103 (6): Windows' "Animation effects" setting off is reduce motion (as AttachmentFlightPlatform reads it).
    private static bool ReduceMotion() =>
        SystemParametersInfo(0x1042, 0, out var enabled, 0) && !enabled; // SPI_GETCLIENTAREAANIMATION

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool enabled, uint update);

    /// <summary>:36-39 <c>func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -&gt;
    /// Bool</c>. On Windows the shell runs it when a second launch reaches the running instance (ED-002).</summary>
    public bool ApplicationShouldHandleReopen(bool hasVisibleWindows)
    {
        if (!hasVisibleWindows) WindowManager.Shared.ShowMain(); // :37
        return true; // :38
    }

    /// <summary>
    /// :41-54 <c>func applicationShouldTerminate(_ sender: NSApplication) -&gt; NSApplication.TerminateReply</c>.
    /// A quit while the team's work is going out waits for it: the merge is a minute or two of git and one model call,
    /// and losing it half-way leaves the work unmerged until the user's next message. Capped, so a stuck merge never
    /// holds the quit.
    /// </summary>
    public NSApplication.TerminateReply ApplicationShouldTerminate(ITerminationSender sender)
    {
        if (Model is not { } model || !model.IsAnyHydraMergeRunning) return NSApplication.TerminateReply.TerminateNow; // :45
        MainActor.Spawn(async () =>
        {
            var deadline = SwiftDate.AddingTimeInterval(SwiftDate.Now, 4 * 60); // :47
            while (model.IsAnyHydraMergeRunning && SwiftDate.Now < deadline) // :48
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250)); // :49
            }
            sender.Reply(toApplicationShouldTerminate: true); // :51
        }); // :46-52
        return NSApplication.TerminateReply.TerminateLater; // :53
    }

    // :56-61 func applicationWillTerminate(_ notification: Notification)
    public void ApplicationWillTerminate()
    {
        WindowManager.Shared.FlushPendingFrames(); // PORT-ONLY (ED-NEW-perf-startup-structure-1): the window frame a drag was still holding
        Model?.Settings.FlushPendingWrites(); // PORT-ONLY (CZ-13.5): the settings a slider drag was still holding reach the store before the app ends
        Model?.SaveBeforeQuit(); // :57
        TokenLedger.Shared.Current.FlushLiveSpend(); // :58
        // The MCP servers the hub started go with the app.
        MCPHub.TerminateAll(); // :59-60
    }

    /// <summary>:63-75 <c>nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent
    /// notification: UNNotification) async -&gt; UNNotificationPresentationOptions</c>: nothing for the chat that is open
    /// and in front, a banner with sound for everything else.</summary>
    public async Task<UNNotificationPresentationOptions> UserNotificationCenter(UNNotification willPresent)
    {
        // PORT-ONLY (AS-9, ED-NEW-dextra-as9-3): a decision toast is not shown while the Assistant home is in front, checked once more here,
        // right before the Windows call, in case the owner opened the home between the request and now.
        if (willPresent.UserInfo.ContainsKey(DecisionToastKeys.Decision) || willPresent.UserInfo.ContainsKey(DecisionToastKeys.Mission))
        {
            var onHome = await MainActor.Run(() => Model is { } home && ToastNotifications.IsSurfaceOnScreen(home, AppSurface.AssistantHome));
            return onHome ? UNNotificationPresentationOptions.None : UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound;
        }
        var threadIDString = willPresent.UserInfo.TryGetValue("threadID", out var threadID) ? threadID as string : null; // :67
        if (threadIDString is not null && SwiftUUID.Parse(threadIDString) is { } notifiedID) // :68
        {
            var isOpenChat = await MainActor.Run(() =>
                NSApplication.IsActive.Current() && Model?.SelectedThreadID == notifiedID); // :69-71
            if (isOpenChat) return UNNotificationPresentationOptions.None; // :72
        }
        return UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound; // :74
    }

    // :77-92 nonisolated userNotificationCenter(_:didReceive:). Windows protocol and second-launch activations
    // both call this method; all four actions stay on the main actor, in source order.
    public Task UserNotificationCenter(UNNotificationResponse didReceive)
    {
        var userInfo = didReceive.Notification.UserInfo; // :81
        var identifier = userInfo.TryGetValue("threadID", out var threadID) ? threadID as string : null; // :82
        var isUpdate = userInfo.TryGetValue("update", out var update) && update is not null; // :83
        var windows = ToastActivationWindows.Current.Current;
        // PORT-ONLY (AS-9, ED-NEW-dextra-as9-5): a press on a decision toast. A button that answers its card does so without bringing nth forward;
        // every other press (the body, Open, Review, an answer the toast cannot give) brings nth forward on the card.
        if (userInfo.TryGetValue(DecisionToastKeys.Decision, out var decisionText) && decisionText is string decision && Guid.TryParse(decision, out var target))
        {
            var action = DecisionNoticeWords.Parse(userInfo.TryGetValue(DecisionToastKeys.Action, out var key) ? key as string : null) ?? NoticeAction.Review;
            var nonce = userInfo.TryGetValue(DecisionToastKeys.Nonce, out var spent) ? spent as string : null;
            return MainActor.Run(() =>
            {
                if (Model is { } assistant && assistant.DecisionToastActivated(target, nonce, action) == DecisionToastOutcome.Answered) return;
                windows.ShowMain();
                Model?.ReviewAssistantDecision(target);
                windows.Activate();
            });
        }
        // PORT-ONLY (AS-9, ED-NEW-dextra-as9-9): a press on a duty's toast (a "Notify me" finding) brings nth forward on the mission; it answers nothing.
        if (userInfo.TryGetValue(DecisionToastKeys.Mission, out var missionText) && missionText is string mission && Guid.TryParse(mission, out var run))
        {
            return MainActor.Run(() =>
            {
                windows.ShowMain();
                Model?.ReviewAssistantMission(run);
                windows.Activate();
            });
        }
        return MainActor.Run(() =>
        {
            if (!isUpdate) windows.ShowMain(); // :85
            if (identifier is not null && SwiftUUID.Parse(identifier) is { } selected && Model is { } model)
                model.SelectedThreadID = selected; // :86-87
            windows.Activate(); // :89
            if (isUpdate) windows.ShowAbout(); // :90
        });
    }
}
