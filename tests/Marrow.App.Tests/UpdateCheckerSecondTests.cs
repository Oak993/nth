using System.Reflection;
using System.Text.Json;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/UpdateChecker.swift (B2 1fbf81e), :1-327. Part 1 of 2, the checker side:
// PR-SETTINGS-105, PR-UPDATE-005 to PR-UPDATE-014, PR-UPDATE-018 to PR-UPDATE-023 (the network session,
// persistence, check(), the schedule and the notification). UpdateCheckerTests holds the pure parts (AppVersion,
// newestRelease, the asset predicate). Every expected value is transcribed from the Swift line cited on the test
// or from the record, never from a C# implementation. Non-ASCII text is written as \u escapes (R-STR-9). HTTP in
// a test is a loopback server (R-TEST-10), never the internet.
//
// Every real HTTP call goes through runtime.Loop.Run/RunUntil (never a plain await on TestRuntime.OnMain's
// result): OnMain's body runs synchronously on the manual loop, so an async continuation captured on that
// loop's SynchronizationContext needs the loop pumped AGAIN to resume - RunUntilIdle alone (what Advance calls)
// only drains what is already queued. Loop.Run/RunUntil poll and pump for real elapsed time until the awaited
// Task actually completes, which is what a real loopback round trip needs.
public sealed class UpdateCheckerSecondTests
{
    private static string Suffix => GitLabRelease.Link.ArtifactSuffix;

    private static AppInfo Running(string version = "1.0.0", bool isDevelopment = false) =>
        ReleaseFeed.Running(version, isDevelopment);

    // ---- PR-SETTINGS-105: the three defaults keys ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-105")] // UpdateChecker.swift:77-81
    public void The_checker_persists_under_its_own_three_keys_outside_any_settings_store()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(request => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        Assert.True(runtime.Loop.Run(() => checker.Check(background: false)));
        Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
        Assert.NotNull(runtime.Defaults.Object("updateChecker_lastChecked"));
        Assert.Null(runtime.Defaults.Object("updateChecker_notifiedVersion")); // never written by a foreground check
    }

    // ---- PR-UPDATE-005 / PR-UPDATE-007 / PR-UPDATE-009 / PR-UPDATE-014 / PR-UPDATE-019: the empty feed (OD-0012 I1) ----

    [Fact]
    [Trait("Req", "PR-UPDATE-005")] // UpdateChecker.swift:66-70, DEVIATION OD-0012 I1
    [Trait("Req", "PR-UPDATE-014")]
    [Trait("Req", "PR-UPDATE-019")]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: while the feed is empty a scheduled or activation check makes no request (OD-0012 I1)
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:134-149; owner_decisions.md OD-0012 I1
    public void An_empty_feed_makes_no_request_and_fails_with_the_exact_no_feed_message()
    {
        using var runtime = new TestRuntime();
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], ""));
        var available = runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()); // no real await on this path
        Assert.False(available);
        Assert.Equal("No update feed is set.", runtime.OnMain(() => checker.LastError));
        Assert.NotNull(runtime.OnMain(() => checker.LastCheckedAt));
        Assert.Null(runtime.OnMain(() => checker.Update));
        Assert.Contains("updateChecker_lastChecked", runtime.Defaults.Writes);
        Assert.DoesNotContain("updateChecker_release", runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-005")] // UpdateChecker.swift:70, DefaultFeed constant
    [Trait("Decision", "ED-235")] // AMENDED by UE-1: OD-0012 I1 made the constant empty; OD-0100 (2026-10-04) answers it with the owner's public GitHub repository
    public void The_default_feed_constant_is_the_owners_release_repository()
    {
        Assert.Equal("https://github.com/Oak993/marrow-releases", UpdateChecker.DefaultFeed);
    }

    // ---- PR-UPDATE-006: session configuration and headers ----

    [Fact]
    [Trait("Req", "PR-UPDATE-006")] // UpdateChecker.swift:101-110
    public void A_check_sends_the_marrow_user_agent_and_the_json_accept_header()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(request => CannedResponse.Json(Feed()));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("2.4.6"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        var request = Assert.Single(server.Requests);
        Assert.Equal(["Nth/2.4.6"], request.HeaderValues("User-Agent")); // DEVIATION OD-0012 F1
        Assert.Equal(["application/json"], request.HeaderValues("Accept"));
    }

    // ---- PR-UPDATE-008: background-check guards ----

    [Fact]
    [Trait("Req", "PR-UPDATE-008")] // UpdateChecker.swift:133
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: the development-build guard
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:133
    public void A_development_build_never_starts_the_background_schedule()
    {
        using var runtime = new TestRuntime();
        var checker = runtime.OnMain(() => new UpdateChecker(Running(isDevelopment: true), [], ""));
        runtime.OnMain(checker.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromHours(1));
        Assert.Null(runtime.OnMain(() => checker.LastCheckedAt)); // no launch check ever ran
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-012")] // UpdateChecker.swift:157-158
    public void Check_is_a_no_op_in_a_development_build_and_touches_no_state()
    {
        using var runtime = new TestRuntime();
        var checker = runtime.OnMain(() => new UpdateChecker(Running(isDevelopment: true), [], "http://127.0.0.1:1/x"));
        var available = runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()); // :158 returns before any await
        Assert.False(available);
        Assert.Null(runtime.OnMain(() => checker.LastCheckedAt));
        Assert.Null(runtime.OnMain(() => checker.LastError));
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-012")] // UpdateChecker.swift:159 guard !isChecking else { return updateAvailable }
    public void A_check_already_in_flight_makes_a_second_call_return_immediately()
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        using var server = new LoopbackFeedServer(async _ => { await gate.Task; return CannedResponse.Json(Feed()); });
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/hangs")));

        Task<bool>? firstTask = null;
        runtime.OnMain(() => firstTask = checker.Check()); // starts it; the server holds the response back
        runtime.Loop.RunUntil(() => server.Requests.Count > 0, TimeSpan.FromSeconds(20));
        Assert.True(runtime.OnMain(() => checker.IsChecking));

        var second = runtime.Loop.Run(() => checker.Check());
        Assert.False(second); // updateAvailable, still false, returned without a second request
        Assert.Single(server.Requests); // no second request went out

        gate.SetResult();
        var first = runtime.Loop.Run(() => firstTask!);
        Assert.False(first); // the feed answers an empty array: NoRelease, but the request did complete
    }

    // ---- PR-UPDATE-011: restored at construction ----

    [Fact]
    [Trait("Req", "PR-UPDATE-011")] // UpdateChecker.swift:112-118
    public void A_stored_release_and_last_checked_date_are_restored_at_construction()
    {
        using var runtime = new TestRuntime();
        var stored = new AvailableUpdate
        {
            Version = "9.9.9",
            Tag = "v9.9.9",
            DownloadURL = SwiftURL.Create("https://example.com/Marrow" + Suffix)!,
            Size = null,
            Notes = "notes",
            ReleasedAt = null,
            PageURL = SwiftURL.Create("https://example.com/page")!,
        };
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(stored));
        var when = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        runtime.Defaults.Set("updateChecker_lastChecked", when);
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], ""));
        Assert.Equal("9.9.9", runtime.OnMain(() => checker.Update)!.Version);
        Assert.Equal(when, runtime.OnMain(() => checker.LastCheckedAt));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-011")] // UpdateChecker.swift:114-116 try? decode: a corrupt blob leaves update nil
    public void A_corrupt_stored_blob_leaves_update_nil_without_an_error()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_release", new byte[] { 1, 2, 3 });
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], ""));
        Assert.Null(runtime.OnMain(() => checker.Update));
        Assert.Null(runtime.OnMain(() => checker.LastError));
    }

    // ---- PR-UPDATE-013: the success path ----

    [Fact]
    [Trait("Req", "PR-UPDATE-013")] // UpdateChecker.swift:162-180
    public void A_successful_check_stores_the_release_clears_the_error_and_stamps_the_time()
    {
        using var runtime = new TestRuntime();
        LoopbackFeedServer server = null!;
        server = new LoopbackFeedServer(request => request.Target.StartsWith("/releases", StringComparison.Ordinal)
            ? CannedResponse.Json(Feed(Release("v3.0.0", [Link("Marrow" + Suffix, server.Url("/download/Marrow" + Suffix))])))
            : CannedResponse.HeadWithLength(200, 555));
        using var disposeServer = server;
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        var available = runtime.Loop.Run(() => checker.Check());
        Assert.True(available);
        Assert.Equal("3.0.0", runtime.OnMain(() => checker.Update)!.Version);
        Assert.Null(runtime.OnMain(() => checker.LastError));
        Assert.NotNull(runtime.OnMain(() => checker.LastCheckedAt));
        Assert.Contains("updateChecker_release", runtime.Defaults.Writes);
        Assert.Contains("updateChecker_lastChecked", runtime.Defaults.Writes);
    }

    // ---- PR-UPDATE-018: the size HEAD ----

    [Fact]
    [Trait("Req", "PR-UPDATE-018")] // UpdateChecker.swift:171-173, :221-229
    public void A_release_with_no_size_gets_one_from_a_head_request_when_it_answers_2xx_with_a_positive_length()
    {
        using var runtime = new TestRuntime();
        LoopbackFeedServer server = null!;
        server = new LoopbackFeedServer(request => request.Method == "HEAD"
            ? CannedResponse.HeadWithLength(200, 4096)
            : CannedResponse.Json(Feed(Release("v1.0.0", [Link("Marrow" + Suffix, server.Url("/f" + Suffix))]))));
        using var disposeServer = server;
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.Equal(4096, runtime.OnMain(() => checker.Update)!.Size);
        Assert.Equal("HEAD", server.Requests.Last().Method);
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-018")] // UpdateChecker.swift:226-227 non-2xx or length <= 0 leaves size nil
    [InlineData(404, 4096)]
    [InlineData(200, 0)]
    public void A_head_that_fails_or_reports_no_length_leaves_the_size_nil_and_never_fails_the_check(int status, long length)
    {
        using var runtime = new TestRuntime();
        LoopbackFeedServer server = null!;
        server = new LoopbackFeedServer(request => request.Method == "HEAD"
            ? CannedResponse.HeadWithLength(status, length)
            : CannedResponse.Json(Feed(Release("v1.0.0", [Link("Marrow" + Suffix, server.Url("/f" + Suffix))]))));
        using var disposeServer = server;
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.Null(runtime.OnMain(() => checker.Update)!.Size);
        Assert.Null(runtime.OnMain(() => checker.LastError));
    }

    // ---- PR-UPDATE-014 / PR-UPDATE-020: failure branches keep the previous release ----

    [Fact]
    [Trait("Req", "PR-UPDATE-014")] // UpdateChecker.swift:164-166, :271
    public void A_feed_with_no_qualifying_release_fails_with_the_exact_message_and_keeps_the_previous_release()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed(Release("v1.0.0", [Link("notes.txt", "https://example.com/notes.txt")]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.Equal("No release with a disk image was found.", runtime.OnMain(() => checker.LastError));
        Assert.Null(runtime.OnMain(() => checker.Update));
        Assert.DoesNotContain("updateChecker_release", runtime.Defaults.Writes);
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-020")] // UpdateChecker.swift:191-193, :272
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(429)]
    public void A_non_2xx_status_fails_with_the_exact_http_status_message(int status)
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Plain(status, "Status"));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.Equal("GitHub answered with HTTP " + status + ".", runtime.OnMain(() => checker.LastError));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-019")] // UpdateChecker.swift:181-185, ASCII apostrophe (byte 0x27 at :182)
    public void An_unreachable_server_fails_with_the_exact_could_not_reach_message()
    {
        using var runtime = new TestRuntime();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens here now
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], "http://127.0.0.1:" + port + "/releases"));
        runtime.Loop.Run(() => checker.Check());
        Assert.Equal("Couldn't reach GitHub.", runtime.OnMain(() => checker.LastError)); // ASCII U+0027: UpdateChecker.swift:182 byte 0x27
    }

    // ---- PR-UPDATE-021 / PR-UPDATE-022 / PR-UPDATE-023: the notification ----

    [Fact]
    [Trait("Req", "PR-UPDATE-021")] // UpdateChecker.swift:178-180, :245-248
    [Trait("Req", "PR-UPDATE-022")] // UpdateChecker.swift:249-253, DEVIATION OD-0012 F1
    public void A_background_check_that_finds_a_newer_version_shows_exactly_one_toast_with_the_fixed_strings()
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        var available = runtime.Loop.Run(() => checker.Check(background: true));
        Assert.True(available);
        var shown = Assert.Single(toasts.Shown);
        Assert.Equal("update-2.0.0", shown.Identifier);
        Assert.Equal("Nth 2.0.0 is available", shown.Title); // DEVIATION OD-0012 F1
        Assert.Equal("Open Settings › About to update and restart.", shown.Body); // U+203A
        Assert.Equal("2.0.0", shown.Version);
        Assert.Contains("updateChecker_notifiedVersion", runtime.Defaults.Writes);
        Assert.Equal("2.0.0", runtime.Defaults.Object("updateChecker_notifiedVersion"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-021")] // UpdateChecker.swift:157 background: false default -> never notifies
    public void A_manual_check_never_notifies_even_when_it_finds_an_update()
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check(background: false));
        Assert.Empty(toasts.Shown);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-021")] // UpdateChecker.swift:247-248 write-then-send: one banner per version, ever
    public void A_version_already_marked_notified_never_shows_a_second_toast()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_notifiedVersion", "2.0.0");
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check(background: true));
        Assert.Empty(toasts.Shown);
    }

    // PR-UPDATE-021 precondition "updateAvailable is true": a background check that finds nothing newer posts nothing and
    // writes no marker (UpdateChecker.swift:178 `if background, updateAvailable`), while the check itself persists
    // (:175-177). The asset link points at the loopback server so the size HEAD (:171-173) never leaves this PC.
    [Theory]
    [Trait("Req", "PR-UPDATE-021")] // UpdateChecker.swift:178-180
    [InlineData("1.0.0", "v1.0.0")] // the feed's newest release is the running version
    [InlineData("3.0.0", "v2.0.0")] // the feed's newest release is older than the running version
    public void A_background_check_that_finds_nothing_newer_posts_no_toast_and_writes_no_marker(string running, string tag)
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        LoopbackFeedServer? server = null;
        server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release(tag, [Link("Marrow" + Suffix, server!.Url("/asset" + Suffix))]))));
        using var serverScope = server;
        var checker = runtime.OnMain(() => new UpdateChecker(Running(running), [], server.Url("/releases")));
        var available = runtime.Loop.Run(() => checker.Check(background: true));
        Assert.False(available);
        Assert.Empty(toasts.Shown);
        Assert.DoesNotContain("updateChecker_notifiedVersion", runtime.Defaults.Writes);
        Assert.Null(runtime.Defaults.Object("updateChecker_notifiedVersion"));
        // The check itself did persist (:175-177): only the notification was skipped.
        Assert.Contains("updateChecker_release", runtime.Defaults.Writes);
        Assert.Contains("updateChecker_lastChecked", runtime.Defaults.Writes);
    }

    // The shipped marker is ONE string compared with `!=` (UpdateChecker.swift:247), so "one notification per version, ever"
    // holds while the same version stays the last one notified: a different newer version notifies and moves the key, and a
    // version returning after another one notifies again. Nothing pins `!=` against a set or `>=` otherwise.
    [Fact]
    [Trait("Req", "PR-UPDATE-021")] // UpdateChecker.swift:245-248 (one stored string, `!=`, written before the toast)
    public void The_marker_holds_only_the_last_notified_version_so_a_newer_one_notifies_and_an_older_one_returns()
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        var tag = "v2.0.0";
        LoopbackFeedServer? server = null;
        server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release(tag, [Link("Marrow" + Suffix, server!.Url("/asset" + Suffix))]))));
        using var serverScope = server;
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));

        runtime.Loop.Run(() => checker.Check(background: true));
        Assert.Equal(["update-2.0.0"], toasts.Shown.Select(t => t.Identifier).ToArray());
        Assert.Equal("2.0.0", runtime.Defaults.Object("updateChecker_notifiedVersion"));

        runtime.Loop.Run(() => checker.Check(background: true)); // the same version again: nothing
        Assert.Single(toasts.Shown);

        tag = "v2.1.0";
        runtime.Loop.Run(() => checker.Check(background: true)); // a different newer version notifies and moves the key
        Assert.Equal(["update-2.0.0", "update-2.1.0"], toasts.Shown.Select(t => t.Identifier).ToArray());
        Assert.Equal("2.1.0", runtime.Defaults.Object("updateChecker_notifiedVersion"));

        tag = "v2.0.0";
        runtime.Loop.Run(() => checker.Check(background: true)); // the earlier version, after another: `!=` holds, so it notifies again
        Assert.Equal(["update-2.0.0", "update-2.1.0", "update-2.0.0"], toasts.Shown.Select(t => t.Identifier).ToArray());
        Assert.Equal("2.0.0", runtime.Defaults.Object("updateChecker_notifiedVersion"));

        runtime.Loop.Run(() => checker.Check(background: true)); // and once more the same: nothing new
        Assert.Equal(3, toasts.Shown.Count);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-023")] // windows_path: no runtime permission prompt, the port sends the toast directly
    public void The_default_toast_binding_is_a_safe_no_op_until_the_toast_unit_exists()
    {
        // No override installed: production defaults to NoUpdateToastCenter, which must not throw.
        NoUpdateToastCenter.Instance.Show("update-1.0.0", "Nth 1.0.0 is available", "body", "1.0.0");
    }

    // ---- PR-UPDATE-007 / PR-UPDATE-009 / PR-UPDATE-010: the schedule ----

    // Test-only cleanup: the loop StartBackgroundChecks() spawns has no stop method in production (PR-UPDATE-010
    // reproduces upstream having none), so a test that starts it must cancel the TaskHandle directly before its
    // TestRuntime is disposed, or the loop's next tick throws against an unbound Ambient and crashes the host.
    private static void StopBackgroundChecksForTest(UpdateChecker checker)
    {
        var field = typeof(UpdateChecker).GetField("backgroundTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        (field.GetValue(checker) as TaskHandle)?.Cancel();
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-007")] // UpdateChecker.swift:132-140, :74-75 launchDelay = 8, checkInterval = 6h
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: the 8 s delay and the 6 h interval
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:74-75, :134-140
    public void The_first_background_check_runs_after_8_seconds_and_repeats_every_6_hours()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed()));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        runtime.OnMain(checker.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromSeconds(7));
        Assert.Empty(server.Requests);
        runtime.Advance(TimeSpan.FromSeconds(1));
        // Wait for the check to actually finish (LastCheckedAt set), not just for the server to have seen the
        // request: the loop's own sequencing is `await Check(...); await TrySleep(checkInterval)` - while Check()
        // is still awaiting the real network round trip, the code has not reached TrySleep yet, so the very next
        // Advance(~6 hours) below cannot arm that timer. Losing this race left the second sleep starting only
        // after the almost-6-hour jump had already passed, needing another full interval from there, so the
        // 1ms Advance meant to trigger the second check never did - reproduced as the second RunUntil timing out
        // at its 20s bound under a loaded full-suite run.
        runtime.Loop.RunUntil(() => checker.LastCheckedAt is not null, TimeSpan.FromSeconds(20));
        Assert.Single(server.Requests);
        runtime.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMilliseconds(1));
        Assert.Single(server.Requests);
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        runtime.Loop.RunUntil(() => server.Requests.Count >= 2, TimeSpan.FromSeconds(20));
        Assert.Equal(2, server.Requests.Count);
        StopBackgroundChecksForTest(checker);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-009")] // UpdateChecker.swift:141-149 stale = now - lastCheckedAt > checkInterval
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: the stale-on-activation rule
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:141-149
    public void Activation_runs_a_catch_up_check_only_when_the_last_check_is_older_than_6_hours_or_never_happened()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed()));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));

        var notInstalled = runtime.OnMain(checker.HandleActivation); // never started: no observer installed
        Assert.Null(notInstalled); // no handle spawned, no request
        Assert.Empty(server.Requests);

        runtime.OnMain(checker.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromSeconds(8)); // the launch check
        // Wait for the check to actually finish (LastCheckedAt set), not just for the server to have seen the
        // request: server.Requests.Count >= 1 only means the request arrived, which can still be a real network
        // hop ahead of Check() finishing and setting LastCheckedAt (:176/:183). HandleActivation's staleness test
        // reads LastCheckedAt right after this, so on that narrower, real race this test asserted "not stale" (or
        // even "still not stale" at exactly the 6-hour mark) while the launch check's own LastCheckedAt write was
        // still in flight, seeing it as null (never checked: always stale) instead - reproduced 3 of 8 solo runs
        // with no load at all before this fix.
        runtime.Loop.RunUntil(() => checker.LastCheckedAt is not null, TimeSpan.FromSeconds(20));
        Assert.Single(server.Requests);
        // Stop the loop here so only HandleActivation drives every further request: otherwise the loop's own
        // 6-hourly tick (PR-UPDATE-007) resets lastCheckedAt at the same instants this test advances to.
        StopBackgroundChecksForTest(checker);

        var notStale = runtime.OnMain(checker.HandleActivation); // exactly 0s since the last check: not stale
        Assert.Null(notStale);
        Assert.Single(server.Requests);

        runtime.Advance(TimeSpan.FromHours(6)); // exactly checkInterval elapsed: not strictly stale
        var stillNotStale = runtime.OnMain(checker.HandleActivation);
        Assert.Null(stillNotStale); // still not > checkInterval: no extra request
        Assert.Single(server.Requests);

        runtime.Advance(TimeSpan.FromMilliseconds(1));
        var stale = runtime.OnMain(checker.HandleActivation);
        Assert.NotNull(stale); // now strictly stale: a check was spawned
        runtime.Loop.Run(() => stale!.Completion);
        Assert.Equal(2, server.Requests.Count); // the launch check, plus this one
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-010")] // UpdateChecker.swift:99, :141: no removeObserver anywhere in the file
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: the activation observer is never removed
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:99, :141
    public void There_is_no_way_to_uninstall_the_activation_hook_once_started()
    {
        Assert.DoesNotContain("Stop", typeof(UpdateChecker).GetMethods().Select(m => m.Name));
        Assert.DoesNotContain("RemoveActivation", typeof(UpdateChecker).GetMethods().Select(m => m.Name));
    }

    // ---- PR-UPDATE-004: the seven fields, round-tripped through persistence ----

    [Fact]
    [Trait("Req", "PR-UPDATE-004")] // UpdateChecker.swift:41-51, :233-237 persisted JSON round-trips
    public void AvailableUpdate_round_trips_through_the_persisted_bytes()
    {
        var update = new AvailableUpdate
        {
            Version = "1.4.0",
            Tag = "v1.4.0",
            DownloadURL = SwiftURL.Create("https://example.com/Marrow" + Suffix)!,
            Size = 12345,
            Notes = "## Notes\nSome *markdown*.",
            ReleasedAt = new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero),
            PageURL = SwiftURL.Create("https://gitlab.com/droppyformac1/droppy-code/-/releases/v1.4.0")!,
        };
        var bytes = AvailableUpdate.EncodeToBytes(update);
        var decoded = AvailableUpdate.DecodeFromBytes(bytes)!;
        Assert.Equal(update.Version, decoded.Version);
        Assert.Equal(update.Tag, decoded.Tag);
        Assert.Equal(update.DownloadURL.AbsoluteString, decoded.DownloadURL.AbsoluteString);
        Assert.Equal(update.Size, decoded.Size);
        Assert.Equal(update.Notes, decoded.Notes);
        Assert.Equal(update.ReleasedAt, decoded.ReleasedAt);
        Assert.Equal(update.PageURL.AbsoluteString, decoded.PageURL.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-004")] // UpdateChecker.swift:47 notes: release.description ?? "" (empty, not omitted)
    public void A_release_with_no_size_or_date_omits_those_keys_but_not_notes()
    {
        var update = new AvailableUpdate
        {
            Version = "1.0.0",
            Tag = "v1.0.0",
            DownloadURL = SwiftURL.Create("https://example.com/Marrow" + Suffix)!,
            Size = null,
            Notes = "",
            ReleasedAt = null,
            PageURL = SwiftURL.Create("https://example.com/page")!,
        };
        var bytes = AvailableUpdate.EncodeToBytes(update);
        using var document = JsonDocument.Parse(bytes);
        Assert.False(document.RootElement.TryGetProperty("size", out _));
        Assert.False(document.RootElement.TryGetProperty("releasedAt", out _));
        Assert.True(document.RootElement.TryGetProperty("notes", out var notes));
        Assert.Equal("", notes.GetString());
    }
}
