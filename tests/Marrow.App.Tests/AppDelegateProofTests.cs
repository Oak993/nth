using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests;

[Collection("Headless")]
public sealed class AppDelegateProofTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-027")] // App/AppDelegate.swift:18-21; ED-001/ED-061 explicitly exclude capture code
    public void Shipped_sources_contain_no_capture_subsystem_or_capture_argument_handling()
    {
        var source = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src"));
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            // These names legitimately occur in source-citation comments. The Gate-C obligation is executable code.
            var code = Regex.Replace(File.ReadAllText(file), @"/\*[\s\S]*?\*/|//[^\r\n]*", "", RegexOptions.CultureInvariant);
            Assert.False(Regex.IsMatch(code, @"\b(CaptureRun|WebsiteCaptures|TourCaptures)\b|--(website|tour)-captures", RegexOptions.CultureInvariant), file);
        }
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

    [Fact]
    [Trait("Req", "PR-WIN-068")] // App/AppDelegate.swift:15-33 load-bearing launch order, asynchronous bootstrap
    [Trait("Req", "PR-LAUNCH-038")] // App/AppDelegate.swift:27 bootstrap is neither awaited nor cancelled by launch
    public void Launch_runs_the_source_sequence_and_shows_the_window_before_bootstrap_completes() => headless.OnUiThread(() =>
    {
        var members = CompiledLaunchOrder.Members(typeof(AppDelegate).GetMethod(nameof(AppDelegate.ApplicationDidFinishLaunching))!);
        // DEVIATION OD-0018 (ED-103): :22's showMain is the boot sequence's (StartBoot), and :32's showSettings waits in a
        // task for the main window to appear; the order of the steps is otherwise :15-33's.
        string[] names = ["InstallDelegate", "set_Model", "get_StartBoot", "OpenThreadOnLaunch", "get_HasSeenTour", "Spawn", "Spawn", "StartBackgroundChecks", "ConsumeRelaunchMarker", "ArmCelebration", "Spawn"];
        var at = -1;
        foreach (var name in names)
        {
            var next = members.ToList().FindIndex(at + 1, member => member is MethodBase && member.Name == name);
            Assert.True(next > at, "Missing or reordered launch step " + name);
            at = next;
        }
        Assert.DoesNotContain(members, member => member is MethodBase && member.Name == "Cancel"); // :27 discards task handle
        using var launch = new LaunchHarness();
        var pending = new TaskCompletionSource();
        var started = false;
        launch.Delegate.BootstrapModel = model =>
        {
            Assert.Same(launch.Manager.Model, model);
            Assert.True(launch.Manager.MainWindow!.IsVisible);
            started = true;
            return pending.Task;
        };
        try
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Assert.True(started);
            Assert.False(pending.Task.IsCompleted);
            Assert.True(launch.Manager.MainWindow!.IsVisible);
        }
        finally { pending.SetResult(); launch.Harness.Main.Loop.RunUntilIdle(); }
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-038")] // App/AppDelegate.swift:27; App/AppModel.swift:240-287 awaited order
    public void Bootstrap_compiles_the_source_step_order_and_suspends_at_each_awaited_step()
    {
        var method = typeof(AppModel).GetMethod(nameof(AppModel.Bootstrap))!;
        var state = method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var members = CompiledLaunchOrder.Members(state.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!);
        // Swift :244, :245, :246, :248, :250, :255-261, :263, :267-271, :273, :278-282, :286.
        string[] names = ["WarmDocuments", "LoadKeychainInBackground", "Load", "SweepHydraCopies", "ResumeHydraMerges", "Cancel", "Spawn", "RefreshAll", "Run", "LoadCatalog", "Spawn", "SweepOrphanedAttachments"];
        var at = -1;
        var positions = new List<int>();
        foreach (var name in names)
        {
            var next = members.ToList().FindIndex(at + 1, member => member is MethodBase && member.Name == name);
            Assert.True(next > at, "Missing or reordered bootstrap step " + name);
            positions.Add(next);
            at = next;
        }
        // These are single source statements; the two Spawn sites are the repeating sweep and the updater task.
        // In particular :286 is the only orphan sweep, so an extra early sweep is not accepted as "last".
        foreach (var name in names.Distinct(StringComparer.Ordinal))
            Assert.Equal(names.Count(value => value == name), members.Count(member => member is MethodBase && member.Name == name));
        // Each source await must retain a compiler suspension path before the next step. This checks actual IL,
        // in addition to the safe real-bootstrap tests and the blocked updater stand-in integration test.
        foreach (var index in new[] { 1, 2, 4, 7, 8, 9 })
            Assert.Contains(members.Skip(positions[index] + 1).Take(positions[index + 1] - positions[index] - 1),
                member => member.Name == "AwaitUnsafeOnCompleted");
        Assert.DoesNotContain(members.Skip(positions[10] + 1).Take(positions[11] - positions[10] - 1),
            member => member.Name == "GetAwaiter"); // :278 task deliberately not awaited before :286
        Assert.Contains(members.Skip(positions[11] + 1), member => member.Name == "AwaitUnsafeOnCompleted"); // final sweep awaited
        using var h = new AppModelHarness();
        h.Bootstrap(h.MakeModel()); // existing seam: missing CLI paths, fake keychain, isolated store, no real AI tools
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:13-15, three statements in exactly this order
    public void Cache_then_renderer_then_delegate_are_the_first_three_launch_writes()
    {
        var members = CompiledLaunchOrder.Members(typeof(AppDelegate).GetMethod(nameof(AppDelegate.ApplicationDidFinishLaunching))!);
        var cache = members.ToList().FindIndex(member => member.DeclaringType == typeof(URLCache) && member.Name == "set_Shared");
        var renderer = members.ToList().FindIndex(member => member.DeclaringType == typeof(MarkdownDelegationCard) && member.Name == "set_Render");
        var notification = members.ToList().FindIndex(member => member.DeclaringType == typeof(IToastCenter) && member.Name == nameof(IToastCenter.InstallDelegate));
        Assert.True(cache >= 0 && cache < renderer && renderer < notification); // Swift :13, :14, :15
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-029")] // App/AppModel.swift:584 guard let project = currentProject else { return }
    public void Launch_without_a_current_project_selects_and_creates_nothing() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.WriteLibrary([], []));
        Assert.Equal("New thread", ChatThread.Untitled); // Core/Models/Library.swift:122 literal, independently of sample factories
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.CurrentProject));
        Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID));
        Assert.Empty(launch.Harness.Main.OnMain(() => launch.Manager.Model.Threads));
        Dispatcher.UIThread.RunJobs();
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-084")] // App/AppDelegate.swift:14; UI/Markdown/MarkdownView.swift:323-329
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:13-15 renderer receives json and sent before delegate/windows
    [InlineData("hydra", "Summoning a head", false)]
    [InlineData("hydra-sent", "Summoned a head", true)]
    [InlineData("hydra-leaving", "Summoned a head", true)]
    public void Launch_renderer_renders_each_delegation_fence_with_its_json_and_sent_state(string fence, string title, bool sent) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var installed = false;
        var center = new RecordingAppToastCenter { Installed = value =>
        {
            Assert.Same(launch.Delegate, value);
            Assert.Equal(8 * 1024 * 1024, URLCache.Shared.MemoryCapacity); // :13
            Assert.Equal(0, URLCache.Shared.DiskCapacity); // :13
            Assert.Empty(launch.Events); // :15 precedes any window
            Assert.NotNull(MarkdownDelegationCard.Render);
            Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound,
                value.UserNotificationCenter(new UNNotification(new Dictionary<string, object> { ["update"] = "1.7.1" })).GetAwaiter().GetResult()); // :74 notifications already route during launch
            // TimelineRows.swift:745-751: complete one unnamed delegation -> verb + "a head".
            // The task literal is test input; seeing it proves the json argument survived the installed closure.
            var card = new MarkdownBlockView(new MarkdownBlock.Code(fence, "[{\"task\":\"Proof brief\",\"prompt\":\"Prove it.\"}]"));
            var block = Assert.IsType<HydraDelegationBlock>(card.Child);
            var window = new Avalonia.Controls.Window { Content = card };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var text = block.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text).ToArray();
                Assert.Contains(title, text);
                Assert.Contains("Proof brief", text);
                Assert.DoesNotContain(block.GetVisualDescendants(), item => item is CodeBlock);
                Assert.Equal(sent, block.GetVisualDescendants().OfType<Marrow.UI.Port.SwiftImage>().Any(item => item.SymbolName == "checkmark.circle.fill")); // :664-669
            }
            finally { window.Close(); }
            installed = true;
        } };
        using var binding = ToastNotifications.Center.Override(center);
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        Assert.True(installed);
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-025")] // App/AppDelegate.swift:67-74; App/UpdateChecker.swift:249-253; ED-271
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:63-75; App/UpdateChecker.swift:249-253; ED-271
    public void Update_delivery_in_an_active_app_is_still_posted_and_is_silent_because_the_update_content_sets_no_sound() => headless.OnUiThread(() =>
    {
        // UpdateChecker.swift:249-253 builds the content with a title, a body and userInfo and no content.sound, so willPresent's
        // [.banner, .sound] (AppDelegate.swift:74) has no sound to play; ED-271 (OD-0028: "none when the source passes nil").
        using var h = new AppModelHarness();
        var platform = new RecordingToastPlatform();
        var center = new WindowsToastCenter(platform, ToastBuildIdentity.ForBuild(false, false));
        center.InstallDelegate(new AppDelegate { Model = h.MakeModel() });
        using var active = NSApplication.IsActive.Override(() => true);
        h.Main.Loop.Run(() => { center.Show("update-1.7.1", "Title", "Body", "1.7.1"); return Task.CompletedTask; });
        h.Main.Loop.RunUntil(() => platform.Posts.Count == 1, AppModelHarness.Bound);
        var post = Assert.Single(platform.Posts);
        Assert.Contains("silent=\"true\"", post.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("ms-winsoundevent", post.Xml, StringComparison.Ordinal);
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:67-74 as? String and UUID parse must both succeed
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-005: a missing, unparsable or non-string threadID gets banner plus sound
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:63-75
    [InlineData("malformed")]
    [InlineData("uuid object")]
    [InlineData("missing")]
    public void A_zero_selected_UUID_does_not_make_invalid_or_non_string_payloads_valid(string kind) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        h.Main.OnMain(() => model.SelectedThreadID = Guid.Empty); // valid UUID, deliberately distinguish it from failed parsing
        using var active = NSApplication.IsActive.Override(() => true);
        var info = new Dictionary<string, object>();
        if (kind == "malformed") info["threadID"] = "not-a-uuid"; // test input, source :68 must reject
        if (kind == "uuid object") info["threadID"] = Guid.Empty; // source :67 does not stringify Any
        var response = h.Main.OnMain(() => new AppDelegate { Model = model }.UserNotificationCenter(new UNNotification(info)));
        h.Main.Loop.RunUntil(() => response.IsCompleted, AppModelHarness.Bound);
        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, response.GetAwaiter().GetResult()); // :74
    });
}

// STOP opencode-App.AppDelegate #1 - RESOLVED 2026-10-02: the Acrylic hint and its boot hand-over landed (ED-236, WindowManager
// STOP #5 closed; GlassWindowNativeTests), and a clean-data boot capture of the real app was taken under OD-0055
// (porting/verification/native/launch-2026-10-02/RESULT.md). The tests carry the Req.
// Kind: missing external composition evidence/material dependency.
// Swift: App/AppDelegate.swift:22; ED-103 (5), (7), ED-180, OD-0018.
// Records: PR-LAUNCH-028, UI-LAUNCH-001.
// Site: ApplicationDidFinishLaunching -> StartBoot. The ED-103 stage and hand-over landed 2026-10-01
// (Port/BootStageWindow.cs, BootStageWindowTests; probes P12, P13). The main window's Acrylic hint (WindowManager STOP #5)
// landed with ED-236; P13's ~150-200 ms blur lag at the hand-over is what the native glass tests cover (STOP Boot #1).
// OD-0030's withdrawn lensing probe is a different issue. Stand-in main content is not a full clean-data boot capture.

// STOP opencode-App.AppDelegate #2 - RESOLVED: the shell binding is Port/AppShell.cs (WM_ACTIVATEAPP on the main window calls
// UpdateChecker.HandleActivation; AppShellActivationTests) and the launch activation counts by OD-0040 (STOP Boot #2).
// Kind: missing shell activation binding and launch-activation ruling.
// Swift: App/AppDelegate.swift:28; App/UpdateChecker.swift:141-149.
// Records: PR-LAUNCH-046.
// Site: StartBackgroundChecks -> UpdateChecker.HandleActivation. It had no production caller; the shell now calls it.
// The hook itself is proved: FinishLaunching puts exactly one procedure hook on the main window, before the boot shows it
// (AppShell.AddWindowProcedureHook records it; AppShellActivationTests), and the recorded hook is what the tests call. Windows'
// own delivery of WM_ACTIVATEAPP and Avalonia's call of the hook are the platform's and the framework's.

// STOP opencode-App.AppDelegate #3 - RESOLVED 2026-10-01 by OD-0036 (no reason text); the tests carry the Req.
// Kind: missing session-end reason ruling/binding.
// Swift: App/AppDelegate.swift:45-61; ED-004 (2).
// Records: PR-LAUNCH-050, PR-WIN-073.
// Site: ApplicationShouldTerminate -> Port/AppShell.ShutdownRequested. Ordinary 250 ms/240 s quit and the three
// shutdown steps are wired and tested. ShutdownBlockReasonCreate is absent; no source/ruling gives its user-facing
// reason text. Need that text from the owner/ED and the shell binding. WM_ENDSESSION has limited OS grace.

// STOP opencode-App.AppDelegate #4 - RESOLVED 2026-10-01 by OD-0038 (Present restores a minimised window to its prior
// state; AppDelegateThirdTests A_reopen_restores_a_minimised_main_window..., Showing_a_minimised_Settings_window...).
// Kind: unresolved minimized-window reading in another unit.
// Swift: App/AppDelegate.swift:37; App/Window/WindowManager.swift:57-60.
// Records: PR-WIN-072.
// Site: ApplicationShouldHandleReopen -> WindowManager.Present. Transport and hidden/visible cached-window reuse
// landed. Present only calls Show/Activate and does not restore WindowState.Minimized. Need the source/oracle ruling
// on makeKeyAndOrderFront restoring a minimized window, then the WindowManager owner's restore binding.

// STOP opencode-App.AppDelegate #5 - RESOLVED: the native-close half by OD-0039 (follow the Swift); the reopen paths are
// clicked in SettingsViewTests (the About page's "Welcome tour" Show button) and CommandPaletteTests (the palette row,
// OD-0053); the first-launch tour seen natively in porting/verification/native/launch-2026-10-02/RESULT.md.
// Kind: record/source scope ambiguity and missing whole-record pointer proof.
// Swift: App/AppDelegate.swift:24-26; TourPages.swift:27; TourWindowController.swift:103-107,145-148.
// Records: UI-LAUNCH-002.
// Site: tour launch gate -> TourWindowController createdWindow.Closed. Native windowWillClose only clears the
// controller; TourView's close action invokes onClose. The record broadly says every close marks hasSeenTour=true.
// Need a ruling distinguishing native close from the tour close control, plus pointer proofs for the reopen paths.
// The existing native-close false assertion and all 700 ms/setting-gate assertions are retained.

// STOP opencode-App.AppDelegate #6 - RESOLVED 2026-10-01 by OD-0037 (callback proof; one manual click of each kind
// stays on the Windows verification list). WindowsToastRoutingTests drives the production window adapter.
// Kind: missing safe native-notification hit-testing fixture or proof-method ruling.
// Swift: App/AppDelegate.swift:77-92; WindowManager.swift:41-45; ED-220 (7), ED-225.
// Records: PR-LAUNCH-064, PR-LAUNCH-065, PR-UPDATE-024, PR-WIN-076, UI-LAUNCH-003.
// Site: WindowsToastPlatform.Show / WindowsToastLaunchTransport ingress -> UserNotificationCenter(didReceive).
// Routing, payload transport and About centering landed. Recording callbacks do not prove a pointer hitting a
// system-drawn toast. Mutation standard requires real hit testing; ED-220 (7) forbids real toast/registry/URI use in
// tests. Need a safe native-toast hit-testing seam or a ruling accepting callback proof for this system UI. No fake
// clickable toast is substituted. Partial callback assertions remain without these whole-record Req claims.

// STOP App.AppDelegate #7 - OPEN: PR-LAUNCH-071 is not claimed.
// Kind: owner ruling not built.
// Swift: App/AppDelegate.swift:22-33.
// Records: PR-LAUNCH-071.
// OD-0096 (owner_decisions.md, 2026-10-03, with its 2026-10-04 amendments: look I, words W1, download D3) changes the first run: the
// window first shows a "Getting ready" sheet, and the Welcome screen then asks whether the user wants voice typing. The record's
// "no first-run onboarding beyond the welcome tour" and "no Windows-only first-run wizard" are what that ruling replaces, and it is
// not built (nothing settles whether the 700 ms tour shows over the sheet). The tests that pin today's sequence (main window,
// bootstrap, the 700 ms tour; AppDelegateFourthTests, AppDelegateThirdTests) carry no Req for it: they are rewritten with OD-0096.
// The clean-data capture of the real exe (porting/verification/native/launch-2026-10-02/RESULT.md) was taken before OD-0096.

// STOP App.AppDelegate #8 - RESOLVED: UI-LAUNCH-005 and PR-UPDATE-025 are claimed again (ED-271).
// Kind: record conflicted with the pinned source; the records are amended.
// Swift: App/AppDelegate.swift:63-75; App/UpdateChecker.swift:249-253; App/AppModel.swift:1224.
// Records: UI-LAUNCH-005, PR-UPDATE-025 (their "with sound" wording is amended to the source's: the update banner is silent, a chat's
// banner carries the default sound; porting/spec/LAUNCH.json, UPDATE.json, regenerated by merge_spec.mjs).
// willPresent returns [.banner, .sound] for an update, but the update's content sets no sound (UpdateChecker.swift:249-253), so the
// port's update toast posts `<audio silent="true"/>` and a chat's toast keeps the default sound. Proved by AppDelegateProofTests
// Update_delivery_in_an_active_app_is_still_posted_and_is_silent_..., WindowsToastTests Background_update_posts_..., the presentation
// table (AppDelegateTests, WindowsToastTests) and the taps (WindowsToastRoutingTests), which carry the Req traits again.
