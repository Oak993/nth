using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Store;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :1-93. Part 1 of 2 ports model (:7), the quit path
// (applicationShouldTerminate(_:) :41-54, applicationWillTerminate(_:) :56-61) and the presentation gate
// userNotificationCenter(_:willPresent:) (:63-75).
// NOT PORTED YET, each a STOP note at the end of tests/Marrow.App.Tests/AppDelegateTests.cs (missing dependency):
// applicationDidFinishLaunching(_:) (:9-34), applicationShouldHandleReopen(_:hasVisibleWindows:) (:36-39) and
// userNotificationCenter(_:didReceive:) (:77-92). Each reads WindowManager.shared or calls its showMain() or
// showSettings(page:), which App/Window/WindowManager.cs still leaves MARROW_STUB (unit App.Window.WindowManager).
// Forms: `@MainActor final class` that is not @Observable -> a plain class whose members run on main (R-ISO-1);
// NSApplication, NSApplication.TerminateReply, NSApp.isActive and the UserNotifications values are the port-only
// stand-ins in Port/ApplicationLifecycle.cs and Port/UserNotifications.cs. The unused `Notification` and
// `UNUserNotificationCenter` parameters have no port type and carry nothing the bodies read; they are dropped.

namespace Marrow.App;

/// <summary>:5-93 <c>@MainActor final class AppDelegate: NSObject, NSApplicationDelegate,
/// UNUserNotificationCenterDelegate</c>.</summary>
public sealed class AppDelegate
{
    // :7 var model: AppModel?
    public AppModel? Model { get; set; }

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
