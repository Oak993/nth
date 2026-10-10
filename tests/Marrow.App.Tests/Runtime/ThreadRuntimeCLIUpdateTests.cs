using System.Reflection;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Runtime;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B3 1.7.0), :2255-2293 (finishTurnUpdatingCLI,
// resendAfterCLIUpdate) and their three callers (:1378, :2209, :2226). Unit App.Runtime.ThreadRuntime, lane A33.
//
// R-TEST-10 (matching Marrow.Services.Tests.Providers.ProviderRegistryCLIUpdateTests): every path in this file
// that could reach ProviderRegistry.Executable/UpdateCLIIfDue/InstalledCLIVersion runs under
// ProviderRegistry.SetCLIUpdaterExecutableForTesting, never bare - either pointed at the offline
// ProviderProbeStandIn (this project's own compiled copy, Fixtures/ProviderProbeStandIns.cs) or answering null
// for every provider (NoExecutableSeam). No test in this file launches a real "claude" or "codex" binary or
// runs a real "update"; the seam's answer is final and never falls through to the real PATH (ProviderRegistry.
// Executable's own comment). These tests exercise ThreadRuntime's ORCHESTRATION of UpdateCLIIfDue/
// InstalledCLIVersion (already fully gated and tested at ProviderRegistryCLIUpdateTests), not those methods
// again.
public sealed class ThreadRuntimeCLIUpdateTests
{
    private static IDisposable NoExecutableSeam() => ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);

    private static IDisposable ExecutableSeam(ProviderKind provider, ProviderProbeScript script) =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(p => p == provider ? FileUrl(script.ExecutablePath) : null);

    // Matches ProviderRegistryCLIUpdateTests.FileUrl: a "file://" URL's .Path prepends a slash before a
    // Windows drive letter that CreateProcessW rejects; a non-"file" scheme round trips it.
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    // ---- FinishTurnUpdatingCLI routing from the three callers (PR-RUNTIME-077/102/103/265, UI-RUNTIME-039) ---

    [Fact]
    [Trait("Req", "PR-RUNTIME-077")] // ThreadRuntime.swift:1376-1381 (B3): finishTurnUpdatingCLI consulted first.
    [Trait("Req", "PR-RUNTIME-265")] // ThreadRuntime.swift:2255-2270
    [Trait("Req", "UI-RUNTIME-039")] // ThreadRuntime.swift:2263
    public void StartTurn_catch_takes_a_needs_newer_CLI_error_to_the_update_path_for_Claude_or_Codex()
    {
        using var seam = NoExecutableSeam();
        using var h = new AppModelHarness();
        var (_, thread, model, runtime) = CreateRuntime(h, ProviderKind.Claude);
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        const string error = "does not support this model; version 2.1.280 or newer is required";

        h.Main.OnMain(() =>
        {
            var signature = InvokePrivateStatic("MakeSessionSignature", model.Thread(thread.Id)!, model.Project(thread.ProjectID)!.Path,
                model.Settings.AntiSlopEnabled, model.HydraLaunch(model.Thread(thread.Id)!));
            SetPrivateField(runtime, "session", new MessageFailingProviderSession(error));
            SetPrivateField(runtime, "sessionSignature", signature);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "StartTurn", "Start request.",
            Array.Empty<Attachment>(), null, false), AppModelHarness.Bound);
        var turnID = h.Main.OnMain(() => runtime.Turns.Single().Id);
        // FinishTurnUpdatingCLI's own follow-up (finishTurn, then resendAfterCLIUpdate) is an un-awaited
        // MainActor.Spawn: wait for its terminal notice instead of assuming RunUntilIdle drains it.
        h.Main.Loop.RunUntil(() => NoticeCount(runtime) >= 2, AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notices = Notices(runtime);
            var info = Assert.Single(notices, n => n.NoticeLevel == Notice.Level.Info);
            Assert.Equal("This model needs a newer Claude. Updating it, then sending your message again.", info.Message);
            // The raw error is never posted unwrapped: only the CLI-update path's own notices appear.
            Assert.DoesNotContain(notices, n => n.NoticeLevel == Notice.Level.Error && n.Message == error);
            var failure = Assert.Single(notices, n => n.NoticeLevel == Notice.Level.Error);
            Assert.Equal(error + "\n\nNth could not bring Claude up to date. Run `claude update` in Terminal, then send the message again.",
                failure.Message);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
            // The info notice is part of the failing turn (:2263 comment: "so the resend's rewind takes it
            // away with the failed attempt"), which requires its Item.TurnID to be the failing turn's id.
            var infoEntry = Assert.Single(runtime.Entries, entry =>
                entry.Item.TimelineItemContent is TimelineItem.Content.Notice n && n.Value.NoticeLevel == Notice.Level.Info);
            Assert.Equal(turnID, infoEntry.Item.TurnID);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-077")] // ThreadRuntime.swift:1376-1381: providers other than Claude/Codex are unaffected.
    public void StartTurn_catch_posts_the_raw_error_when_the_provider_does_not_update_its_own_CLI()
    {
        using var seam = NoExecutableSeam();
        using var h = new AppModelHarness();
        var (_, thread, model, runtime) = CreateRuntime(h, ProviderKind.Copilot);
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        const string error = "version 2.1.280 or newer is required"; // matches NeedsNewerCLI's text but Copilot does not updatesOwnCLI

        h.Main.OnMain(() =>
        {
            var signature = InvokePrivateStatic("MakeSessionSignature", model.Thread(thread.Id)!, model.Project(thread.ProjectID)!.Path,
                model.Settings.AntiSlopEnabled, model.HydraLaunch(model.Thread(thread.Id)!));
            SetPrivateField(runtime, "session", new MessageFailingProviderSession(error));
            SetPrivateField(runtime, "sessionSignature", signature);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "StartTurn", "Start request.",
            Array.Empty<Attachment>(), null, false), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal(error, notice.Message);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-102")] // ThreadRuntime.swift:2206-2211 (B3)
    [Trait("Req", "PR-RUNTIME-265")]
    [Trait("Req", "UI-RUNTIME-039")]
    public void TurnCompleted_takes_a_needs_newer_CLI_error_before_appending_its_own_notice()
    {
        using var seam = NoExecutableSeam();
        using var h = new ThreadRuntimePart12Harness(ProviderKind.Codex);
        const string error = "please run codex UPDATE now";

        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("Hello there.");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, error));
        });
        h.Main.Loop.RunUntil(() => NoticeCount(h.Runtime) >= 2, AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notices = Notices(h.Runtime);
            var info = Assert.Single(notices, n => n.NoticeLevel == Notice.Level.Info);
            Assert.Equal("This model needs a newer Codex. Updating it, then sending your message again.", info.Message);
            Assert.DoesNotContain(notices, n => n.NoticeLevel == Notice.Level.Error && n.Message == error);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-103")] // ThreadRuntime.swift:2212-2229 (B3)
    [Trait("Req", "PR-RUNTIME-265")]
    [Trait("Req", "UI-RUNTIME-039")]
    public void Exited_takes_a_needs_newer_CLI_error_before_the_stopped_unexpectedly_notice()
    {
        using var seam = NoExecutableSeam();
        using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
        const string error = "Run Claude Update to continue";

        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("Hello there.");
            h.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            h.Runtime.Rehearse(new ProviderEvent.Exited(error));
        });
        h.Main.Loop.RunUntil(() => NoticeCount(h.Runtime) >= 2, AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notices = Notices(h.Runtime);
            var info = Assert.Single(notices, n => n.NoticeLevel == Notice.Level.Info);
            Assert.Equal("This model needs a newer Claude. Updating it, then sending your message again.", info.Message);
            Assert.DoesNotContain(notices, n => n.Message == "Claude stopped unexpectedly.");
            Assert.DoesNotContain(notices, n => n.NoticeLevel == Notice.Level.Error && n.Message == error);
        });
    }

    // ---- ResendAfterCLIUpdate's own branches, invoked directly (PR-RUNTIME-266, UI-RUNTIME-040/041/042) ------

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-040")] // ThreadRuntime.swift:2276-2278
    public void ResendAfterCLIUpdate_reports_it_could_not_bring_the_CLI_up_to_date_when_no_version_can_be_read()
    {
        using var seam = NoExecutableSeam();
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Claude);
        var turnID = Guid.NewGuid();

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Claude, "boom"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal("boom\n\nNth could not bring Claude up to date. Run `claude update` in Terminal, then send the message again.",
                notice.Message);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")] // ThreadRuntime.swift:2275: updateCLIIfDue(provider, force: true).
    // ProviderRegistry.UpdateCLIIfDue's own 12-hour freshness gate (:506-507; proven at
    // ProviderRegistryCLIUpdateTests.update_cli_if_due_skips_an_unforced_call_whose_last_attempt_was_under_12_hours_ago)
    // would skip an unforced call outright, with no --version and no update call at all. Only force: true
    // reaches the stand-in here, so a call recorded on it proves ThreadRuntime passes force, not just that
    // an update happened to be due.
    public void ResendAfterCLIUpdate_forces_the_update_past_the_stored_dates_12_hour_gate()
    {
        var script = ProviderProbeStandIns.CopyFor("force-flag").SetStdout("4.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Codex, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Codex);
        var turnID = Guid.NewGuid();
        h.Main.OnMain(() => h.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object>
        {
            [SwiftEnum<ProviderKind>.RawValue(ProviderKind.Codex)] = SwiftDate.Seconds1970(SwiftDate.Now) - 3600d, // 1 hour ago
        }));

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Codex, "boom-force"),
            AppModelHarness.Bound);

        Assert.Contains(script.RecordedCalls(), call => call.SequenceEqual(["update"]));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-040")] // ThreadRuntime.swift:2276-2278: at most one resend per CLI version.
    public void ResendAfterCLIUpdate_reports_it_could_not_bring_the_CLI_up_to_date_when_that_version_was_already_resent_at()
    {
        var script = ProviderProbeStandIns.CopyFor("already-resent").SetStdout("2.5.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Codex, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Codex);
        var turnID = Guid.NewGuid();
        h.Main.OnMain(() => SetPrivateField(runtime, "cliResendVersions",
            new Dictionary<ProviderKind, string> { [ProviderKind.Codex] = "2.5.0" }));

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Codex, "boom2"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal("boom2\n\nNth could not bring Codex up to date. Run `codex update` in Terminal, then send the message again.",
                notice.Message);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-041")] // ThreadRuntime.swift:2283-2287
    public void ResendAfterCLIUpdate_reports_up_to_date_and_asks_to_resend_when_the_runtime_is_not_idle()
    {
        var script = ProviderProbeStandIns.CopyFor("not-idle").SetStdout("3.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Claude);
        var turnID = Guid.NewGuid();
        h.Main.OnMain(() => SetPrivateField(runtime, "phase", RuntimePhase.Running));

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Claude, "boom3"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal("boom3\n\nClaude is up to date now (3.0.0). Send the message again.", notice.Message);
            // Recorded before the idle/turn guard even though this branch does not resend (PR-RUNTIME-266).
            var recorded = Assert.IsType<Dictionary<ProviderKind, string>>(GetPrivateField(runtime, "cliResendVersions"));
            Assert.Equal("3.0.0", recorded[ProviderKind.Claude]);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-041")] // ThreadRuntime.swift:2283-2287: the turn or its user entry is gone.
    public void ResendAfterCLIUpdate_reports_up_to_date_and_asks_to_resend_when_the_turn_no_longer_exists()
    {
        var script = ProviderProbeStandIns.CopyFor("turn-gone").SetStdout("3.1.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Claude);
        var turnID = Guid.NewGuid(); // never added via RehearseTurn: turns.first(where:) finds nothing.

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Claude, "boom4"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal("boom4\n\nClaude is up to date now (3.1.0). Send the message again.", notice.Message);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-041")] // ThreadRuntime.swift:2283-2287: the entry's message is from Hydra.
    public void ResendAfterCLIUpdate_reports_up_to_date_and_asks_to_resend_when_the_message_is_from_hydra()
    {
        var script = ProviderProbeStandIns.CopyFor("hydra-message").SetStdout("3.2.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Claude);
        Guid turnID = default;

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Delegated message.");
            turnID = runtime.Turns.Single().Id;
            var entry = runtime.Entries.Single(e => e.TimelineEntryKind == TimelineEntry.Kind.User);
            var user = (TimelineItem.Content.User)entry.Item.TimelineItemContent;
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.User(user.Value with { HydraHeads = new ValueList<long>([1]) }),
            };
            // ResendAfterCLIUpdate expects the runtime idle (FinishTurn normally reaches that first).
            SetPrivateField(runtime, "phase", RuntimePhase.Idle);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Claude, "boom5"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            Assert.Equal("boom5\n\nClaude is up to date now (3.2.0). Send the message again.", notice.Message);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-266")]
    [Trait("Req", "UI-RUNTIME-042")] // ThreadRuntime.swift:2288-2292: resend(...) itself throws.
    // Review fix: this branch previously had no test, and its catch had no R-ERR-1 filter and passed any
    // exception straight to SwiftErrors.LocalizedDescription, which throws NotSupportedException for a type
    // with no recorded per-site decision. A ProviderError (a SwiftError, ILocalizedError) is the exact
    // exception Revert throws for this case, so it also proves the catch's ordinary (non-Win32Exception) arm.
    public void ResendAfterCLIUpdate_reports_the_resend_failure_when_resend_itself_throws()
    {
        var script = ProviderProbeStandIns.CopyFor("resend-throws").SetStdout("5.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Codex, script);
        using var h = new AppModelHarness();
        var (_, _, _, runtime) = CreateRuntime(h, ProviderKind.Codex);
        Guid turnID = default;

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Message to resend.");
            turnID = runtime.Turns.Single().Id;
            // ResendAfterCLIUpdate expects the runtime idle (FinishTurn normally reaches that first).
            SetPrivateField(runtime, "phase", RuntimePhase.Idle);
        });

        h.Main.Loop.Run(() => (Task)InvokePrivate(runtime, "ResendAfterCLIUpdate", turnID, ProviderKind.Codex, "boom6"),
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var notice = Assert.IsType<TimelineItem.Content.Notice>(Assert.Single(runtime.Entries,
                entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
            // Resend -> Revert throws because the turn has no providerTurnID and is not Failed
            // (ThreadRuntime.swift:1866-1867's "no Codex turn ID" case), caught by the :2288-2292 catch.
            Assert.Equal("Codex is up to date now (5.0.0), but the message could not be sent again: " +
                "The selected message has no Codex turn ID, so its history cannot be safely reverted. Send it again.",
                notice.Message);
        });
    }

    private static Notice[] Notices(ThreadRuntime runtime) =>
        runtime.Entries.Where(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice)
            .Select(entry => ((TimelineItem.Content.Notice)entry.Item.TimelineItemContent).Value).ToArray();

    private static int NoticeCount(ThreadRuntime runtime) => Notices(runtime).Length;

    private static (Project Project, ChatThread Thread, AppModel Model, ThreadRuntime Runtime) CreateRuntime(
        AppModelHarness h, ProviderKind provider)
    {
        var project = AppModelSamples.Project("P", h.NewFolder("thread runtime cli update"));
        var thread = AppModelSamples.Thread(project) with { Provider = provider, Model = "thread-runtime-cli-test" };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        return (project, thread, model, runtime);
    }

    private static object InvokePrivate(ThreadRuntime runtime, string method, params object?[] arguments)
    {
        var target = typeof(ThreadRuntime).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(target);
        return target.Invoke(runtime, arguments)!;
    }

    private static object InvokePrivateStatic(string method, params object?[] arguments)
    {
        var target = typeof(ThreadRuntime).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(target);
        return target.Invoke(null, arguments)!;
    }

    private static void SetPrivateField(ThreadRuntime runtime, string fieldName, object value)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(runtime, value);
    }

    private static object GetPrivateField(ThreadRuntime runtime, string fieldName)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(runtime)!;
    }
}
