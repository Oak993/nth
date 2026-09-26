using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.UI.Markdown;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :1-93. Parts 1 and 2 of 2 port model (:7), the quit path
// (applicationShouldTerminate(_:) :41-54, applicationWillTerminate(_:) :56-61) and the presentation gate
// userNotificationCenter(_:willPresent:) (:63-75); part 2 added tests of these members and no new member. The Boot unit
// adds applicationDidFinishLaunching(_:) (:9-34) and applicationShouldHandleReopen(_:hasVisibleWindows:) (:36-39), now
// that App.Window.WindowManager is in the tree; Port/AppShell.cs calls them where AppKit does (finishLaunching, and a
// second launch under ED-002). Still NOT PORTED, each a STOP note at the end of tests/Marrow.App.Tests/AppDelegateTests.cs:
// userNotificationCenter(_:didReceive:) (:77-92) and :13 and :15 of the launch (see the member).
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
    // :7 var model: AppModel?
    public AppModel? Model { get; set; }

    // :25 `Tour.present(model:)` and :27 `windows.model.bootstrap()`, as launch calls them. Internal and settable only so
    // the launch tests can record them: the tour window is a process-wide singleton (TourWindowController.shared) and the
    // bootstrap starts providers and git, and each is tested in its own unit. Nothing in the app sets them.
    internal Action<AppModel> PresentTour { get; set; } = Tour.Present;

    internal Func<AppModel, Task> BootstrapModel { get; set; } = model => model.Bootstrap();

    /// <summary>
    /// :9-34 <c>func applicationDidFinishLaunching(_ notification: Notification)</c>, called by the shell where AppKit's
    /// finishLaunching calls it: once, on main, after the prologue (DroppyCodeApp.swift:7-19) and before the run loop.
    /// </summary>
    public void ApplicationDidFinishLaunching()
    {
        // :10-13 URLCache.shared = URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0). NOT PORTED (STOP
        // App.AppDelegate #1): the port's shared cache (Core/Port/Http.cs URLCache.Shared, ED-118 (4)-(5)) already keeps
        // nothing on disk, and its 8 MiB memory cap has no port form while ED-118 parks any size cut-off under O-33.
        MarkdownDelegationCard.Render = (json, sent) => new HydraDelegationBlock(json, sent); // :14
        // :15 UNUserNotificationCenter.current().delegate = self. NOT PORTED (STOP App.AppDelegate #1, PR-LAUNCH-025):
        // no unit has built the Windows toast activation handler this line maps to (Port/UpdateNotificationCenter.cs
        // binds a no-op, AppModel.Notify delivers nothing), so no notification can arrive to be routed.
        var windows = WindowManager.Shared; // :16
        Model = windows.Model; // :17
        // :18-21 the capture-run branch: not ported (ED-001; PR-LAUNCH-027 NOT_APPLICABLE_WITH_EVIDENCE).
        // :22 MARROW_DEFERRED ED-103 (OD-0018, PR-LAUNCH-028 deviation): the boot sequence that replaces this plain show
        // (the particle stage and the main window forming under it) is not written; it waits on probe P12 (ED-103 (7))
        // and on F.GlassMaterial for the main window's material (ED-180). STOP Boot #1 in
        // tests/Marrow.App.Tests/AppDelegateThirdTests.cs.
        windows.ShowMain(); // :22
        windows.Model.OpenThreadOnLaunch(); // :23
        if (!windows.Model.Settings.HasSeenTour) // :24
        {
            MainActor.Spawn(async () => // :25 Task { @MainActor in ... }
            {
                _ = await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(700)); // try? await Task.sleep(for: .milliseconds(700))
                PresentTour(windows.Model);
            });
        }
        MainActor.Spawn(async () => await BootstrapModel(windows.Model)); // :27 Task { await windows.model.bootstrap() }
        UpdateChecker.Shared.StartBackgroundChecks(); // :28
        // :29 The relaunch after an install: the update story finishes on About, where it began.
        if (AppUpdater.Shared.ConsumeRelaunchMarker()) // :30
        {
            UpdateInstallProgress.Shared.ArmCelebration(); // :31
            windows.ShowSettings(page: SettingsPage.About); // :32
        }
    }

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
        var threadIDString = willPresent.UserInfo.TryGetValue("threadID", out var threadID) ? threadID as string : null; // :67
        if (threadIDString is not null && SwiftUUID.Parse(threadIDString) is { } notifiedID) // :68
        {
            var isOpenChat = await MainActor.Run(() =>
                NSApplication.IsActive.Current() && Model?.SelectedThreadID == notifiedID); // :69-71
            if (isOpenChat) return UNNotificationPresentationOptions.None; // :72
        }
        return UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound; // :74
    }
}
