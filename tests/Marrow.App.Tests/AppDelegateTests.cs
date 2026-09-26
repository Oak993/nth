using System.Text.Json;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.Testing;
using Xunit;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :1-93. Part 1 of 2: the quit path (:41-61) and the
// notification presentation gate (:63-75). Every expected value is transcribed from the Swift line cited on the test
// or from the record, never from the C# implementation. B3 lines: the file gained :2 and :10-14 over B2, so B2's
// :2-8 are B3's :3-9 and B2's :9-87 are B3's :15-93 (git diff 1fbf81e 52bc792 -- DroppyCode/App/AppDelegate.swift).
//
// MCPHub's list of live server processes is process-wide (MCPHub.swift:21-24); in this assembly only this class
// tracks processes in it or ends them (AppDelegate.ApplicationWillTerminate), and xUnit runs one class's tests one at
// a time, so no other test sees these recording processes.
public sealed class AppDelegateTests
{
    private const string LiveSpendFileName = "token-activity-live-v2.json"; // TokenLedger.swift:356

    // A model over one project with one thread (Library.swift:204-226 shapes), the thread's runtime alive and idle.
    private static (AppModel Model, ChatThread Thread, ThreadRuntime Runtime) ModelWithLiveRuntime(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        return (model, thread, runtime);
    }

    private static UNNotification ThreadNotification(string threadID) =>
        new(new Dictionary<string, object>(StringComparer.Ordinal) { ["threadID"] = threadID }); // AppModel.swift:1265

    private static UNNotificationPresentationOptions Present(AppModelHarness h, AppDelegate appDelegate, UNNotification notification)
    {
        // The delegate method is nonisolated (:63): the notification centre calls it off main, and it hops to main
        // for the check (:69-71).
        var presented = appDelegate.UserNotificationCenter(notification);
        h.Main.Loop.RunUntil(() => presented.IsCompleted, AppModelHarness.Bound);
        return presented.GetAwaiter().GetResult();
    }

    private static long DailyTotal(string liveSpendFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(liveSpendFile));
        return document.RootElement.GetProperty("daily").EnumerateObject().Sum(day => day.Value.GetInt64());
    }

    // ===== applicationShouldTerminate(_:) :41-54 =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-049")] // App/AppDelegate.swift:7, :45
    [Trait("Req", "PR-LAUNCH-075")] // App/AppDelegate.swift:7, :45
    public void A_quit_before_the_delegate_has_its_model_is_answered_terminate_now_and_never_answered_again()
    {
        using var runtime = new TestRuntime();
        var appDelegate = new AppDelegate();
        var sender = new RecordingTerminationSender();

        var reply = runtime.OnMain(() => appDelegate.ApplicationShouldTerminate(sender));

        Assert.Equal(NSApplication.TerminateReply.TerminateNow, reply);
        runtime.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(sender.Replies);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-049")] // App/AppDelegate.swift:45; App/AppModel+HydraMerge.swift:36
    public void A_quit_while_no_live_runtime_is_merging_is_answered_terminate_now_with_no_wait()
    {
        using var h = new AppModelHarness();
        var (model, _, _) = ModelWithLiveRuntime(h);
        var appDelegate = new AppDelegate { Model = model };
        var sender = new RecordingTerminationSender();

        var reply = h.Main.OnMain(() => appDelegate.ApplicationShouldTerminate(sender));

        Assert.Equal(NSApplication.TerminateReply.TerminateNow, reply);
        h.Main.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(sender.Replies);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-050")] // App/AppDelegate.swift:45-53
    public void A_quit_during_a_merge_is_answered_terminate_later_and_replies_true_at_the_first_250_ms_poll_after_the_merge_ends()
    {
        using var h = new AppModelHarness();
        var (model, _, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var appDelegate = new AppDelegate { Model = model };
        var sender = new RecordingTerminationSender();

        var reply = h.Main.OnMain(() => appDelegate.ApplicationShouldTerminate(sender));

        Assert.Equal(NSApplication.TerminateReply.TerminateLater, reply); // :53
        Assert.Empty(sender.Replies);
        h.Main.Advance(TimeSpan.FromSeconds(1)); // polls at 0.25, 0.5, 0.75 and 1 s find the merge still running (:48-49)
        Assert.Empty(sender.Replies);
        h.Main.OnMain(() => runtime.IsHydraMerging = false); // the merge ends between two polls
        Assert.Empty(sender.Replies);
        h.Main.AdvanceMilliseconds(249);
        Assert.Empty(sender.Replies);
        h.Main.AdvanceMilliseconds(1); // the poll at 1.25 s sees no merge and replies (:51)
        Assert.Equal([true], sender.Replies);
        h.Main.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal([true], sender.Replies); // one reply, never another
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-050")] // App/AppDelegate.swift:47-51
    public void A_quit_during_a_merge_that_never_ends_replies_true_exactly_when_4_minutes_have_passed()
    {
        using var h = new AppModelHarness();
        var (model, _, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var appDelegate = new AppDelegate { Model = model };
        var sender = new RecordingTerminationSender();

        var reply = h.Main.OnMain(() => appDelegate.ApplicationShouldTerminate(sender));

        Assert.Equal(NSApplication.TerminateReply.TerminateLater, reply);
        h.Main.Advance(TimeSpan.FromSeconds(4 * 60) - TimeSpan.FromMilliseconds(1));
        Assert.Empty(sender.Replies);
        h.Main.AdvanceMilliseconds(1); // the poll at 240 s: Date.now < deadline is false (:48)
        Assert.Equal([true], sender.Replies); // the quit is never refused (:51)
        Assert.True(h.Main.OnMain(() => runtime.IsHydraMerging)); // it went ahead with the merge still running
    }

    // ===== applicationWillTerminate(_:) :56-61 =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-051")] // App/AppDelegate.swift:56-61
    public void Termination_saves_the_model_then_flushes_the_live_spend_then_ends_the_MCP_servers()
    {
        using var h = new AppModelHarness();
        var (model, _, _) = ModelWithLiveRuntime(h);
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        h.Main.OnMain(() => ledger.Record(1234)); // a debounced live save is pending (TokenLedger.swift:138-144)
        File.Delete(Storage.LibraryURL); // loaded already; the quit-time save writes it again
        var atTerminate = (Library: false, LiveSpend: false);
        var server = new RecordingServerProcess("server", onTerminate: () =>
            atTerminate = (File.Exists(Storage.LibraryURL), File.Exists(FreshTokenLedger.LiveSpendFile)));
        MCPHub.Track(server);
        var appDelegate = new AppDelegate { Model = model };
        using var order = new FolderWriteOrder(Storage.Root);

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        var written = order.WaitFor(AppModelHarness.Bound, "library.json", LiveSpendFileName);
        Assert.True(
            written.ToList().IndexOf("library.json") < written.ToList().IndexOf(LiveSpendFileName),
            "library.json (:57) must be written before the live spend (:58): " + string.Join(", ", written));
        Assert.Equal((true, true), atTerminate); // both writes are done when the servers are ended (:60)
        Assert.Equal(["terminate"], server.Calls);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-075")] // App/AppDelegate.swift:57-60
    [Trait("Req", "PR-LAUNCH-051")] // App/AppDelegate.swift:57
    public void Termination_with_no_model_skips_the_save_and_still_flushes_the_live_spend_and_ends_the_MCP_servers()
    {
        using var h = new AppModelHarness();
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        h.Main.OnMain(() => ledger.Record(77));
        var server = new RecordingServerProcess("server");
        MCPHub.Track(server);
        var appDelegate = new AppDelegate();

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        Assert.False(File.Exists(Storage.LibraryURL)); // model?.saveBeforeQuit() did nothing (:57)
        Assert.True(File.Exists(FreshTokenLedger.LiveSpendFile)); // :58 still ran
        Assert.Equal(77, DailyTotal(FreshTokenLedger.LiveSpendFile));
        Assert.Equal(["terminate"], server.Calls); // :60 still ran
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-057")] // App/AppDelegate.swift:58; Services/Store/TokenLedger.swift:148-153
    public void Termination_writes_a_pending_live_spend_at_once_and_its_debounced_save_never_runs()
    {
        using var h = new AppModelHarness();
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        h.Main.OnMain(() => ledger.Record(1234));
        Assert.False(File.Exists(FreshTokenLedger.LiveSpendFile)); // the debounced save waits 2 s (TokenLedger.swift:140)
        var appDelegate = new AppDelegate();

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        Assert.True(File.Exists(FreshTokenLedger.LiveSpendFile)); // written with no time passing
        Assert.Equal(1234, DailyTotal(FreshTokenLedger.LiveSpendFile));
        File.Delete(FreshTokenLedger.LiveSpendFile);
        h.Main.Advance(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(FreshTokenLedger.LiveSpendFile)); // the cancelled save wrote nothing (:150)
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-057")] // App/AppDelegate.swift:58; Services/Store/TokenLedger.swift:149
    public void Termination_with_no_pending_live_save_writes_no_live_spend_file()
    {
        using var h = new AppModelHarness();
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var appDelegate = new AppDelegate();

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        Assert.False(File.Exists(FreshTokenLedger.LiveSpendFile));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-058")] // App/AppDelegate.swift:59-60; Services/MCP/MCPHub.swift:367-376
    public void Termination_signals_every_running_MCP_server_first_then_kills_each_one_still_running()
    {
        using var h = new AppModelHarness();
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var log = new List<string>();
        var stubborn = new RecordingServerProcess("stubborn", log) { ExitsOnTerminate = false };
        var polite = new RecordingServerProcess("polite", log);
        MCPHub.Track(stubborn);
        MCPHub.Track(polite);
        var appDelegate = new AppDelegate();

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        Assert.Equal(["stubborn:terminate", "polite:terminate", "stubborn:killTree"], log);
        Assert.False(stubborn.IsRunning);
        Assert.False(polite.IsRunning);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-059")] // App/AppDelegate.swift:56-61; App/UpdateChecker.swift:132-150
    public void Termination_cancels_no_background_loop_and_the_update_schedule_still_runs_its_next_check()
    {
        using var h = new AppModelHarness();
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var checker = h.Main.OnMain(() => new UpdateChecker(Running("1.7.0"), [], UpdateChecker.DefaultFeed));
        using var checkerScope = UpdateChecker.OverrideShared(checker);
        h.Main.OnMain(checker.StartBackgroundChecks); // the launch check waits 8 s (UpdateChecker.swift:135)
        var appDelegate = new AppDelegate();

        h.Main.OnMain(appDelegate.ApplicationWillTerminate);

        h.Main.Advance(TimeSpan.FromSeconds(8) - TimeSpan.FromMilliseconds(1));
        Assert.Null(h.Main.OnMain(() => checker.LastCheckedAt));
        h.Main.AdvanceMilliseconds(1);
        Assert.NotNull(h.Main.OnMain(() => checker.LastCheckedAt)); // the loop outlived the quit path
        UpdateCheckerLoop.Stop(checker);
    }

    // ===== userNotificationCenter(_:willPresent:) :63-75 =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:67-72
    public void A_notification_for_the_selected_thread_while_the_app_is_active_is_presented_with_no_banner_and_no_sound()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(SwiftUUID.UuidString(thread.Id)));

        Assert.Equal(UNNotificationPresentationOptions.None, options);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:68-72; Foundation UUID(uuidString:) takes either case
    public void A_lowercase_threadID_still_parses_and_is_suppressed_for_the_selected_thread_while_active()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(SwiftString.Lowercased(SwiftUUID.UuidString(thread.Id))));

        Assert.Equal(UNNotificationPresentationOptions.None, options);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:70, :74
    public void A_notification_for_the_selected_thread_while_the_app_is_not_active_shows_a_banner_with_sound()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var inactive = NSApplication.IsActive.Override(() => false);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(SwiftUUID.UuidString(thread.Id)));

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:70, :74
    public void A_notification_for_a_thread_that_is_not_the_selected_one_shows_a_banner_with_sound_while_active()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(SwiftUUID.UuidString(Guid.NewGuid())));

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:67-68, :74
    [InlineData("not-a-uuid")]
    [InlineData("")]
    public void A_threadID_that_does_not_parse_as_a_UUID_shows_a_banner_with_sound(string threadID)
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(threadID));

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:70 `self.model?.selectedThreadID == notifiedID`, :74
    public void A_thread_notification_before_the_delegate_has_its_model_shows_a_banner_with_sound()
    {
        using var h = new AppModelHarness();
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate();

        var options = Present(h, appDelegate, ThreadNotification(SwiftUUID.UuidString(Guid.NewGuid())));

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-025")] // App/AppDelegate.swift:67-68, :74; App/UpdateChecker.swift:252
    public void An_update_notification_while_the_app_is_active_shows_a_banner_with_sound()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        using var active = NSApplication.IsActive.Override(() => true);
        var appDelegate = new AppDelegate { Model = model };
        var update = new UNNotification(new Dictionary<string, object>(StringComparer.Ordinal) { ["update"] = "1.7.1" });

        var options = Present(h, appDelegate, update);

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:70 NSApp.isActive, :74
    public void With_no_window_of_the_app_active_the_app_is_not_active_and_the_selected_threads_notification_shows()
    {
        using var h = new AppModelHarness();
        var (model, thread, _) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        var appDelegate = new AppDelegate { Model = model };

        var options = Present(h, appDelegate, ThreadNotification(SwiftUUID.UuidString(thread.Id)));

        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    }
}

// STOP App.AppDelegate #1 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppDelegate.swift:9-34 (B3 52bc792), applicationDidFinishLaunching(_:)
// Requirements: PR-LAUNCH-025, PR-LAUNCH-026, PR-LAUNCH-028, PR-LAUNCH-029, PR-LAUNCH-037, PR-LAUNCH-038,
// PR-LAUNCH-046, PR-LAUNCH-047, PR-LAUNCH-071, PR-UPDATE-059
// What is unclear or missing: from :16 on every line goes through `windows`, WindowManager.shared: :17 takes the
// model from it, :22 calls showMain(), :23-27 use windows.model, :32 calls showSettings(page: .about).
// src/Marrow.App/Window/WindowManager.cs is still the c100 skeleton (Shared, the constructor that makes the one
// AppModel, ShowMain and ShowSettings all throw MARROW_STUB; unit App.Window.WindowManager is TODO), so the launch
// cannot be written past :15 without becoming a no-op, and the member stays unwritten as a whole. The three lines
// before it: :13 `URLCache.shared = URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0)` is new in B3; the
// port's shared cache (src/Marrow.Core/Port/Http.cs URLCache.Shared, ED-118 (4)-(5)) already keeps nothing on disk
// but has no memory capacity, and ED-118 parks any size cut-off under O-33, so the 8 MiB cap has no port form yet.
// :14 `MarkdownDelegationCard.render = { AnyView(HydraDelegationBlock(json: $0, sent: $1)) }` (new in B3) is the
// hook the port already has as MarkdownBlockView.HydraDelegationBlock (src/Marrow.UI/Markdown/MarkdownView.cs, the
// B2 name of the same App-set renderer): `MarkdownBlockView.HydraDelegationBlock = (json, sent) => new
// HydraDelegationBlock(json, sent);`. :15 `UNUserNotificationCenter.current().delegate = self` needs the Windows toast
// activation handler (PR-LAUNCH-025 windows_path); no unit has built one (Port/UpdateNotificationCenter.cs binds a
// no-op, AppModel.Notify delivers nothing). :18-21, the capture-run branch (B3 reads CaptureRun.isEnabled where B2
// read WebsiteCaptures.isEnabled), is not ported (ED-001). The boot sequence that replaces the plain show at :22
// (OD-0018, ED-102, ED-103, PR-LAUNCH-028 deviation) needs the main window's restored frame, also WindowManager's.
// B3 change to a record: PR-LAUNCH-025 says the delegate is set by the first statement (B2 :9); in B3 :13 and :14
// run before it (:15).
// Readings or options: (a) wait for App.Window.WindowManager, then port :9-34 in source order -> exact; (b) port now
// against the skeleton -> every launch throws NotImplementedException out of WindowManager.Shared and no test can
// run it; (c) route the launch through a port-only window seam -> an invented API the WindowManager unit does not
// know.
// Needed to proceed: unit App.Window.WindowManager; a decision on :13 (the 8 MiB memory cap: O-33, ED-118); the
// Windows toast delivery and activation unit for :15.
// Done so far: nothing implemented at this site; no test written (the member is not declared, so no test compiles
// against it). To test then, in this order: :13-:15 before :16; model assigned (:17); showMain (:22) before
// openThreadOnLaunch (:23); the tour 700 ms later only while hasSeenTour is false (:24-26); bootstrap started and not
// awaited (:27); startBackgroundChecks (:28); consumeRelaunchMarker, then armCelebration and showSettings(.about)
// (:30-33).

// STOP App.AppDelegate #2 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppDelegate.swift:36-39 (B3 52bc792), applicationShouldHandleReopen(_:hasVisibleWindows:),
// "if !flag { WindowManager.shared.showMain() }" then "return true"
// Requirements: PR-LAUNCH-048
// What is unclear or missing: the one statement with behaviour calls WindowManager.shared.showMain(), still
// MARROW_STUB. On Windows the path runs on a second launch of the single-instance app (ED-002, PR-LAUNCH-048
// windows_path), which also needs the second process to reach the first; DroppyCodeApp.TryAcquireSingleInstanceLock
// only refuses the second instance and nothing carries the reopen to the running one (DroppyCodeAppTests STOP #1).
// Readings or options: (a) port it with WindowManager and the second-launch signal -> exact; (b) port the
// `return true` alone -> a no-op where the source shows the main window.
// Needed to proceed: unit App.Window.WindowManager; the second-launch signal ED-002 asks for (a decision on its
// mechanism, owned by the shell or App.DroppyCodeApp).
// Done so far: nothing implemented at this site; no test written.

// STOP App.AppDelegate #3 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppDelegate.swift:77-92 (B3 52bc792), userNotificationCenter(_:didReceive:)
// Requirements: PR-LAUNCH-064, PR-LAUNCH-065, PR-UPDATE-024
// What is unclear or missing: the routing calls WindowManager.shared.showMain() (:85) and
// WindowManager.shared.showSettings(page: .about) (:90), both MARROW_STUB, and NSApp.activate() (:89), whose Windows
// form WindowManager's present(_:) owns (WindowManager.swift:57-60). No Windows toast activation pipeline exists to
// deliver a response to it (port_impacts.md entries 115 and 116).
// Readings or options: (a) port it once WindowManager exists, parsing the response's userInfo with the same
// UNNotification shape Port/UserNotifications.cs gives willPresent -> exact; (b) port now -> every tap throws.
// Needed to proceed: unit App.Window.WindowManager; the Windows toast delivery and activation unit.
// Done so far: nothing implemented at this site; no test written.

// STOP App.AppDelegate #4 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppDelegate.swift:1-93 (no applicationShouldTerminateAfterLastWindowClosed), :41-54, :56-61
// Requirements: PR-LAUNCH-074
// What is unclear or missing: OD-0008 C1 makes the main window's close (and Alt+F4, C2) quit the app through the
// terminate handshake. The delegate's side is ported and tested here (PR-LAUNCH-049, -050, -051); what is missing is
// the window and the shell that turn a close into ApplicationShouldTerminate, implement ITerminationSender (continue
// into ApplicationWillTerminate and exit on a true reply) and handle WM_QUERYENDSESSION and WM_ENDSESSION as ED-004
// rules. The main window is WindowManager's makeMainWindow() (MARROW_STUB) and no application shell hosts it yet
// (Program.cs is still MARROW_STUB_ENTRY_POINT).
// Readings or options: (a) wire it in the WindowManager unit or the shell unit that hosts the main window -> exact;
// (b) wire a close handler here -> this file owns no window.
// Needed to proceed: unit App.Window.WindowManager and the application shell (Program.cs's entry point).
// Done so far: the terminate decision and the three termination steps are implemented and green in this file; the
// close wiring is not implemented.
