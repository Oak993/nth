using System.Reflection;
using System.Xml.Linq;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B3 52bc792), :1158-1270;
// App/AppDelegate.swift :13-17, :63-92; App/UpdateChecker.swift :245-261.
// ED-220 supplies the Windows identity, registration and payload mechanism. All platform boundaries are fake.
[Collection("Headless")]
public sealed class WindowsToastTests(HeadlessSession headless)
{
    private static readonly ToastBuildIdentity ReleaseIdentity = new("Nth.App", "nth-release");
    private static ToastRequest Request(string identifier = "thread", bool sound = true) =>
        new(identifier, "A <title> & \"quoted\"", "Body \u203A \u2019", sound,
            new Dictionary<string, object> { ["threadID"] = "00112233-4455-6677-8899-AABBCCDDEEFF" });

    [Theory]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; PLATFORM BOUNDARY ED-220 (2)
    [InlineData(false, false, "Nth.App", "nth-release")]
    [InlineData(true, false, "Nth.App.Dev", "nth-dev")]
    [InlineData(false, true, "Nth.App.Dev", "nth-dev")]
    [InlineData(true, true, "Nth.App.Dev", "nth-dev")]
    public void Build_identity_separates_release_from_debug_and_dev(bool debug, bool dev, string appId, string scheme) =>
        Assert.Equal(new ToastBuildIdentity(appId, scheme), ToastBuildIdentity.ForBuild(debug, dev));

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; PLATFORM BOUNDARY ED-220 (2), (6)
    public void Registration_sets_process_identity_and_only_different_HKCU_values()
    {
        var platform = new RecordingToastPlatform();
        const string executable = "C:\\Program Files\\Marrow\\Marrow.exe";
        WindowsToastRegistration.Register(platform, ReleaseIdentity, executable, "C:\\Marrow\\icon.ico");
        Assert.Equal(["Nth.App"], platform.AppIds);
        Assert.Equal("Nth", platform.Registry[("Software\\Classes\\AppUserModelId\\Nth.App", "DisplayName")]);
        Assert.Equal("URL:Nth", platform.Registry[("Software\\Classes\\nth-release", "")]);
        Assert.Equal("C:\\Marrow\\icon.ico", platform.Registry[("Software\\Classes\\AppUserModelId\\Nth.App", "IconUri")]);
        Assert.Equal("", platform.Registry[("Software\\Classes\\nth-release", "URL Protocol")]);
        Assert.Equal("\"C:\\Program Files\\Marrow\\Marrow.exe\" \"%1\"", platform.Registry[("Software\\Classes\\nth-release\\shell\\open\\command", "")]);
        var count = platform.Writes.Count;
        WindowsToastRegistration.Register(platform, ReleaseIdentity, executable, "C:\\Marrow\\icon.ico");
        Assert.Equal(count, platform.Writes.Count);
        WindowsToastRegistration.Register(platform, ReleaseIdentity, executable, "C:\\Marrow\\changed.ico");
        Assert.Equal(count + 1, platform.Writes.Count);
        Assert.All(platform.Writes, w => Assert.StartsWith("Software\\Classes\\", w.Key, StringComparison.Ordinal));
        Assert.DoesNotContain(platform.Writes, w => w.Key.Contains("Electron", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Decision", "OD-0135")] // the product is named Nth: the old name's own HKCU keys go, nothing else is deleted
    public void Registration_removes_exactly_the_old_names_own_keys_and_keeps_the_new_ones()
    {
        var platform = new RecordingToastPlatform();
        platform.Registry[("Software\\Classes\\AppUserModelId\\MarrowCode.Marrow", "DisplayName")] = "Marrow";
        platform.Registry[("Software\\Classes\\marrow-code-release\\shell\\open\\command", "")] = "\"C:\\Old\\Marrow.App.exe\" \"%1\"";
        platform.Registry[("Software\\Classes\\marrow-code-dev", "URL Protocol")] = "";
        platform.Registry[("Software\\Classes\\marrow-release", "")] = "URL:Someone else";
        WindowsToastRegistration.Register(platform, ReleaseIdentity, "C:\\Nth\\Marrow.App.exe", "icon");
        Assert.Equal(
            ["Software\\Classes\\AppUserModelId\\MarrowCode.Marrow", "Software\\Classes\\AppUserModelId\\MarrowCode.Marrow.Dev",
             "Software\\Classes\\marrow-code-release", "Software\\Classes\\marrow-code-dev"],
            platform.DeletedTrees);
        Assert.DoesNotContain(platform.Registry.Keys, entry => entry.Key.Contains("MarrowCode", StringComparison.Ordinal)
            || entry.Key.Contains("marrow-code-", StringComparison.Ordinal));
        Assert.Equal("URL:Someone else", platform.Registry[("Software\\Classes\\marrow-release", "")]);
        Assert.Equal("Nth", platform.Registry[("Software\\Classes\\AppUserModelId\\Nth.App", "DisplayName")]);
        Assert.Equal("\"C:\\Nth\\Marrow.App.exe\" \"%1\"", platform.Registry[("Software\\Classes\\nth-release\\shell\\open\\command", "")]);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; PLATFORM BOUNDARY ED-002, ED-212, ED-220 (6)
    public void Dev_protocol_command_preserves_its_isolated_data_root()
    {
        var platform = new RecordingToastPlatform();
        WindowsToastRegistration.Register(platform, new("Nth.App.Dev", "nth-dev"), "C:\\Marrow.exe", "icon", "C:\\Dev Data");
        Assert.Equal("\"C:\\Marrow.exe\" --notification-data-root \"C:\\Dev Data\" \"%1\"",
            platform.Registry[("Software\\Classes\\nth-dev\\shell\\open\\command", "")]);
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppModel.swift:1261-1267; App/AppDelegate.swift:63-75; ED-220 (3)
    [InlineData(true)]
    [InlineData(false)]
    public void ToastGeneric_preserves_text_sound_and_activation_userInfo(bool sound)
    {
        var request = Request(sound: sound);
        var xml = XDocument.Parse(WindowsToastCenter.Payload(request, ReleaseIdentity));
        var toast = xml.Root!;
        Assert.Equal("protocol", (string?)toast.Attribute("activationType"));
        Assert.Equal("ToastGeneric", (string?)toast.Descendants("binding").Single().Attribute("template"));
        Assert.Equal(new[] { request.Title, request.Body }, toast.Descendants("text").Select(t => t.Value));
        var audio = toast.Element("audio")!;
        if (sound) Assert.Equal("ms-winsoundevent:Notification.Default", (string?)audio.Attribute("src"));
        else Assert.Equal("true", (string?)audio.Attribute("silent"));
        var decoded = ToastActivation.Decode(ReleaseIdentity, (string)toast.Attribute("launch")!)!;
        Assert.Equal(request.UserInfo["threadID"], decoded.UserInfo["threadID"]);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppModel.swift:1265-1268; ED-220 (3)
    public void Same_thread_replaces_the_same_Tag_without_changing_the_request_identifier()
    {
        using var runtime = new TestRuntime();
        var platform = new RecordingToastPlatform();
        var center = new WindowsToastCenter(platform, ReleaseIdentity);
        center.InstallDelegate(new AppDelegate());
        const string id = "00112233-4455-6677-8899-AABBCCDDEEFF";
        runtime.Loop.Run(() => center.Add(Request(id)));
        runtime.Loop.Run(() => center.Add(Request(id) with { Body = "Next" }));
        Assert.Equal(2, platform.Posts.Count);
        Assert.All(platform.Posts, p => Assert.Equal(id, p.Tag));
        Assert.Single(platform.Delivered);
        Assert.Contains("Next", platform.Delivered[("Nth.App", id)], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-189's notification side effect only (App/AppModel+HydraMerge.swift:919-929); the coordinator clauses are in the HydraMerge tests
    public void Attention_turn_finish_Hydra_note_and_usage_limit_share_AppModel_Notify() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var center = new RecordingAppToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => false);
        h.Main.OnMain(() =>
        {
            model.Settings.NotifyWhenFinished = true;
            model.Settings.ChimeWhenFinished = false;
            model.ThreadNeedsAttention(thread.Id); // AppModel.swift:1176
            model.TurnFinished(thread.Id, TurnStatus.Completed, continues: false); // AppModel.swift:1224
            typeof(AppModel).GetMethod("Note", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [thread.Id, "Hydra has no files left to merge.", "Details"]); // HydraMerge.swift:928
            typeof(Marrow.App.Runtime.AutoContinue).GetMethod("Notify", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Guid), typeof(string)])!
                .Invoke(model.AutoContinue, [thread.Id, "Usage limit reached. Send a message to continue."]); // AutoContinue.swift:154
        });
        h.Main.Loop.RunUntilIdle();
        Assert.Equal(new[] { "Waiting for your decision.", "Finished.", "Hydra has no files left to merge.", "Usage limit reached. Send a message to continue." }, center.Requests.Select(r => r.Body));
        Assert.All(center.Requests, r => { Assert.Equal(thread.Title, r.Title); Assert.Equal(SwiftUUID.UuidString(thread.Id), r.Identifier); });
    });

    // PR-CHAT-189's coordinator clauses (settlement record, no held record, legacy held read) are proved in
    // AppModelHydraMergeCoordinatorTests, AppModelHydraMergeTests and AppModelHydraMergeAlertTests; this test covers the
    // notification side effect at :923-929 only. The earlier STOP #1 ("coordinator still writes held records") was stale at B3:
    // the C# writes no Held record (grep Outcome.Held src: only the read at AppModel+HydraMerge.cs:156).
    // STOP toasts #2. Kind: protected caller. App/AppModel.swift:1158-1165, :1180-1182, :1215-1224.
    // The older private IsOnScreen and unread/chime decisions outside Notify do not include all inline helpers.
    // Notify uses the exact B3 screen gate; updating the callers' read/chime behavior requires their owner's changes.

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppDelegate.swift:67-74
    [Trait("Req", "PR-UPDATE-025")] // App/AppDelegate.swift:67-74
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-005: the gate decides whether the Windows toast is posted at all
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:63-75; ED-220 (5)
    [InlineData(true, "selected", 0)]
    [InlineData(false, "selected", 1)]
    [InlineData(true, "other", 1)]
    [InlineData(true, "invalid", 1)]
    [InlineData(true, "update", 1)]
    public void willPresent_runs_before_the_platform_post(bool active, string kind, int posts) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var selected = Guid.NewGuid();
        h.Main.OnMain(() => model.SelectedThreadID = selected);
        using var activity = NSApplication.IsActive.Override(() => active);
        var platform = new RecordingToastPlatform();
        var center = new WindowsToastCenter(platform, ReleaseIdentity);
        center.InstallDelegate(new AppDelegate { Model = model });
        var info = kind == "update" ? new Dictionary<string, object> { ["update"] = "2.0" }
            : new Dictionary<string, object> { ["threadID"] = kind switch { "selected" => SwiftUUID.UuidString(selected), "other" => SwiftUUID.UuidString(Guid.NewGuid()), _ => "invalid" } };
        h.Main.Loop.Run(() => center.Add(Request() with { UserInfo = info }));
        Assert.Equal(posts, platform.Posts.Count);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:13-17
    [Trait("Req", "PR-LAUNCH-084")] // App/AppDelegate.swift:14-15
    public void Launch_installs_the_delegate_after_cache_and_markdown_before_windows() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var installed = false;
        var center = new RecordingAppToastCenter { Installed = value =>
        {
            Assert.Same(launch.Delegate, value);
            Assert.Equal(8 * 1024 * 1024, URLCache.Shared.MemoryCapacity);
            Assert.Equal(0, URLCache.Shared.DiskCapacity);
            Assert.NotNull(MarkdownDelegationCard.Render);
            Assert.Empty(launch.Events);
            installed = true;
        } };
        using var binding = ToastNotifications.Center.Override(center);
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        Assert.True(installed);
        Dispatcher.UIThread.RunJobs();
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppModel.swift:1249-1268
    [InlineData(false)]
    [InlineData(true)]
    public void AppModel_permission_is_requested_once_even_after_failure_and_Notify_preserves_payload(bool denied) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var center = new RecordingAppToastCenter { FailPermission = denied };
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => false);
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        h.Main.OnMain(() => { model.Notify(id, "Title", "Body"); model.Notify(id, "Quiet", "Body", false); model.RequestNotificationPermission(); });
        h.Main.Loop.RunUntilIdle();
        Assert.Equal(1, center.Permissions);
        Assert.Equal(2, center.Requests.Count);
        Assert.Equal("00112233-4455-6677-8899-AABBCCDDEEFF", center.Requests[0].Identifier);
        Assert.Equal(center.Requests[0].Identifier, center.Requests[0].UserInfo["threadID"]);
        Assert.True(center.Requests[0].Sound);
        Assert.False(center.Requests[1].Sound);
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-063")] // App/AppModel.swift:1158-1165, :1259
    [InlineData("selected", true)]
    [InlineData("panel", true)]
    [InlineData("settled-panel", true)]
    [InlineData("archived-panel", false)]
    [InlineData("inline", true)]
    [InlineData("folded", false)]
    [InlineData("settled-parent", false)]
    [InlineData("archived-parent", false)]
    [InlineData("panel-parent", false)]
    [InlineData("archived-inline", false)]
    public void Notify_uses_the_full_Swift_on_screen_gate(string kind, bool suppressed) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var parent = AppModelSamples.Thread(project) with { ExpandsHelpers = kind != "folded", IsArchived = kind == "archived-parent", IsInPanel = kind == "panel-parent" };
        if (kind == "settled-parent") parent = parent with { IsSettled = true };
        var child = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = kind.Contains("panel", StringComparison.Ordinal) && kind != "panel-parent", IsArchived = kind.StartsWith("archived-", StringComparison.Ordinal) && kind != "archived-parent" };
        if (kind == "settled-panel") child = child with { IsSettled = true };
        h.WriteLibrary([project], [parent, child]);
        var model = h.MakeModel();
        var center = new RecordingAppToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => true);
        h.Main.OnMain(() => { model.SelectedThreadID = parent.Id; model.Notify(kind == "selected" ? parent.Id : child.Id, "title", "body"); });
        h.Main.Loop.RunUntilIdle();
        Assert.Equal(suppressed ? 0 : 1, center.Requests.Count);
        Assert.Equal(suppressed ? 0 : 1, center.Permissions);
    });

    [Theory]
    [Trait("Rule", "R-TEST-4")] // Source-derived; OD-0037 accepts the activation callback as the proof of a toast click.
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; owner_decisions.md OD-0037
    [Trait("Req", "PR-LAUNCH-065")] // App/AppDelegate.swift:77-92; owner_decisions.md OD-0037
    [Trait("Req", "PR-WIN-076")] // App/AppDelegate.swift:77-92
    [Trait("Req", "PR-UPDATE-024")] // App/AppDelegate.swift:77-92; App/Window/WindowManager.swift:32-38
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:84-90 the update tap opens About
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:77-92 the taps in the documented order
    [InlineData(false, "valid")]
    [InlineData(false, "unknown")]
    [InlineData(false, "invalid")]
    [InlineData(true, "none")]
    [InlineData(true, "valid")]
    public void Activation_follows_didReceive_order_for_running_and_second_launch_paths(bool update, string threadKind) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var known = AppModelSamples.Thread(project) with { HasUnread = true };
        h.WriteLibrary([project], [known]);
        var model = h.MakeModel();
        var original = Guid.NewGuid();
        var requested = threadKind == "unknown" ? Guid.NewGuid() : known.Id;
        var info = new Dictionary<string, object>();
        if (threadKind != "none") info["threadID"] = threadKind == "invalid" ? "invalid" : SwiftUUID.UuidString(requested);
        if (update) info["update"] = ""; // presence, not a nonempty version, decides the update branch (:83)
        var expectedSelection = threadKind is "valid" or "unknown" ? requested : original;
        var windows = new RecordingToastWindows(e =>
        {
            if (e == "main") Assert.Equal(original, model.SelectedThreadID);
            if (e == "activate") Assert.Equal(expectedSelection, model.SelectedThreadID);
        });
        using var ui = ToastActivationWindows.Current.Override(windows);
        var appDelegate = new AppDelegate { Model = model };
        h.Main.OnMain(() => model.SelectedThreadID = original);
        // The method did not exist before this unit: invoke its fixed contract so the tests compile red first.
        var method = typeof(AppDelegate).GetMethod("UserNotificationCenter", [typeof(UNNotificationResponse)]);
        Assert.NotNull(method);
        h.Main.Loop.Run(() => (Task)method.Invoke(appDelegate, [new UNNotificationResponse(new UNNotification(info))])!);
        Assert.Equal(update ? new[] { "activate", "about" } : ["main", "activate"], windows.Events);
        if (threadKind == "valid") Assert.False(h.Main.OnMain(() => model.Thread(known.Id)!.HasUnread));
        windows.Events.Clear();
        h.Main.OnMain(() => model.SelectedThreadID = original);
        var transport = new RecordingToastTransport();
        Task<bool>? routed = null;
        using var listener = transport.Listen("isolated-instance", p => routed = ToastSecondLaunch.Route(appDelegate, ReleaseIdentity, p));
        var uri = ToastActivation.Encode(ReleaseIdentity, info);
        h.Main.OnMain(() => Assert.True(ToastSecondLaunch.Forward(transport, "isolated-instance", ReleaseIdentity, [uri])));
        Assert.Equal("isolated-instance", Assert.Single(transport.Sent).Name);
        Assert.NotNull(routed);
        h.Main.Loop.RunUntil(() => routed.IsCompleted, AppModelHarness.Bound);
        Assert.True(routed.GetAwaiter().GetResult());
        Assert.Equal(update ? new[] { "activate", "about" } : ["main", "activate"], windows.Events);
    });

    [Theory]
    [Trait("Rule", "R-TEST-4")] // Source-derived; OD-0037 accepts the activation callback as the proof of a toast click.
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; ED-220 (6); owner_decisions.md OD-0037
    [InlineData("https://example.com/?payload=x")]
    [InlineData("nth-dev://notification?payload=%7B%7D")]
    [InlineData("nth-release://notification?payload=bad")]
    [InlineData("nth-release://other?payload=%7B%7D")]
    public void Foreign_or_malformed_arguments_never_forward_or_activate(string argument)
    {
        var transport = new RecordingToastTransport();
        Assert.Null(ToastActivation.Decode(ReleaseIdentity, argument));
        Assert.False(ToastSecondLaunch.Forward(transport, "test", ReleaseIdentity, [argument]));
        Assert.Empty(transport.Sent);
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-021")] // App/UpdateChecker.swift:178-180, :245-261
    [Trait("Req", "PR-UPDATE-023")] // App/UpdateChecker.swift:254-261; PLATFORM BOUNDARY ED-220 (4)
    [Trait("Req", "PR-UPDATE-025")] // App/UpdateChecker.swift:249-253 no content.sound: the update banner is silent; ED-271
    [Trait("Req", "UI-LAUNCH-005")] // App/UpdateChecker.swift:249-253; App/AppDelegate.swift:74; ED-271 (records amended, STOP App.AppDelegate #8)
    // The notification side effect of the background check, App/UpdateChecker.swift:178-180, :245-261. The update banner is
    // silent: UpdateChecker.swift:249-253 sets title, body and userInfo and no content.sound, so willPresent's .sound (:74) has
    // nothing to play (ED-271).
    [InlineData(false)]
    [InlineData(true)]
    public void Background_update_posts_through_the_same_poster_after_persisting_once_even_if_delivery_fails(bool denied)
    {
        using var runtime = new TestRuntime();
        using var active = NSApplication.IsActive.Override(() => true);
        var platform = new RecordingToastPlatform { FailShow = denied };
        platform.BeforeShow = () => Assert.Equal("2.0.0", runtime.Defaults.String("updateChecker_notifiedVersion"));
        var center = new WindowsToastCenter(platform, ReleaseIdentity);
        center.InstallDelegate(new AppDelegate());
        using var updates = UpdateToastCenter.Ambient.Override(center);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed(Release("v2.0.0", [Link("Marrow" + GitLabRelease.Link.ArtifactSuffix, "https://example.com/Marrow" + GitLabRelease.Link.ArtifactSuffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        Assert.True(runtime.Loop.Run(() => checker.Check(background: false)));
        Assert.Empty(platform.Posts);
        Assert.True(runtime.Loop.Run(() => checker.Check(background: true)));
        runtime.Loop.RunUntil(() => platform.Posts.Count == 1, TimeSpan.FromSeconds(10));
        Assert.True(runtime.Loop.Run(() => checker.Check(background: true)));
        runtime.Loop.RunUntilIdle();
        var posted = Assert.Single(platform.Posts);
        Assert.Equal("update-2.0.0", posted.Tag);
        var xml = XDocument.Parse(posted.Xml);
        Assert.Equal(new[] { "Nth 2.0.0 is available", "Open Settings \u203A About to update and restart." }, xml.Descendants("text").Select(t => t.Value));
        var audio = xml.Root!.Element("audio")!;
        Assert.Equal("true", (string?)audio.Attribute("silent")); // :249-252 no content.sound
        Assert.Null(audio.Attribute("src"));
        Assert.Equal("2.0.0", ToastActivation.Decode(ReleaseIdentity, (string)xml.Root.Attribute("launch")!)!.UserInfo["update"]);
        Assert.Empty(platform.Writes); // posting and permission never register or prompt
    }
}
