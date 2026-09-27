using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B3 1.7.0), the "Keeping the CLIs current"
// section, :467-536 in the pristine B3 tree (E:/Droppy/reference/droppy-code-v1.7.0).
//
// Tests for the B3 CLI self-updater: UpdatesOwnCLI (PR-PROVREG-251), NeedsNewerCLI (PR-PROVREG-252),
// UpdateCLIIfDue's gates (PR-PROVREG-253) and run (PR-PROVREG-254), the persisted attempt dates
// (PR-PROVREG-255), the before/after version comparison that decides the reload and the result
// (PR-PROVREG-256), the in-flight UpdatingCLIs set (PR-PROVREG-257), and InstalledCLIVersion/CliVersion
// (PR-PROVREG-258).
//
// R-TEST-10 / the lane brief for A23: this file never launches a real "claude" or "codex" binary and never
// runs a real "update". EVERY call to UpdateCLIIfDue/InstalledCLIVersion in this file, without exception,
// runs under either ExecutableSeam (points Executable(for:) at the offline ProviderProbeStandIn,
// tests/fixtures/provider-probe-cli) or NoExecutableSeam (points it at nothing), never bare: Claude and
// Codex's ExecutableName is the real CLI name, and this machine may have (and by a prior version of this
// same seam, once did: see 96f7f157's commit message) the real CLI installed on PATH, which
// LoginEnvironment.Which's real search would then reach. This applies even to UpdateCLIIfDue's own gates
// (UpdatesOwnCLI, the custom binary path check, the 12-hour date check), which the production code proves
// return early before Executable(for:) is ever consulted (:498, :500-501, :506-507) - the seam around them
// is defense in depth, so a regression in one of those gates traps on NoExecutableSeam's own guarantee
// instead of silently reaching this machine's real PATH.
public sealed class ProviderRegistryCLIUpdateTests
{
    private static string TempDir()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-cli-update", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SeedUnblockingCache(TestRuntime runtime)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));
    }

    private static ProviderRegistry MakeRegistry(TestRuntime runtime, FakeProviderSettings settings)
    {
        SeedUnblockingCache(runtime);
        return new ProviderRegistry(settings);
    }

    // Matches the production FileUrl helper (a "file://" URL's .Path prepends a slash before a Windows
    // drive letter that CreateProcessW rejects; a non-"file" scheme round trips it).
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    // Points Executable(for:) at the given script for exactly the given provider, and at nothing (null) for
    // every other provider (matching "no executable resolves" unless the test says otherwise).
    private static IDisposable ExecutableSeam(ProviderKind provider, ProviderProbeScript script) =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(p => p == provider ? FileUrl(script.ExecutablePath) : null);

    private static IDisposable NoExecutableSeam() => ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);

    // A pure-resolution regression proof for the seam itself: Executable(for:) never spawns a process on its
    // own, so this is safe to run even if the seam were broken (unlike anything that goes on to call
    // CliVersion/UpdateCLIIfDue). Proves the seam's answer is final even when it is null (see Executable's
    // own comment): a delegate that answers null must not fall through to the real PATH search.
    [Fact]
    [Trait("Req", "PR-PROVREG-253")]
    public void the_no_executable_seam_makes_executable_return_null_without_falling_through_to_the_real_path()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        using (NoExecutableSeam())
        {
            Assert.Null(registry.Executable(ProviderKind.Claude));
            Assert.Null(registry.Executable(ProviderKind.Codex));
        }
    }

    // ---- UpdatesOwnCLI (PR-PROVREG-251) ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-251")] // ProviderRegistry.swift:476-479
    public void updates_own_cli_is_true_only_for_claude_and_codex()
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
        {
            var expected = provider is ProviderKind.Claude or ProviderKind.Codex;
            Assert.Equal(expected, ProviderRegistry.UpdatesOwnCLI(provider));
        }
    }

    // ---- NeedsNewerCLI (PR-PROVREG-252) -----------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-252")] // ProviderRegistry.swift:482-490
    [InlineData("Version 2.1.280 or newer is required")]
    [InlineData("REQUIRES A NEWER VERSION of the CLI")]
    [InlineData("Run Claude Update to continue")]
    [InlineData("please run codex UPDATE now")]
    [InlineData("Upgrade Codex first")]
    [InlineData("You must Update Codex")]
    public void needs_newer_cli_matches_each_of_the_six_phrases_case_insensitively(string message) =>
        Assert.True(ProviderRegistry.NeedsNewerCLI(message));

    [Fact]
    [Trait("Req", "PR-PROVREG-252")] // ProviderRegistry.swift:469-473 (doc comment example)
    public void needs_newer_cli_matches_the_doc_comments_own_example()
    {
        Assert.True(ProviderRegistry.NeedsNewerCLI(
            "does not support this model; version 2.1.280 or newer is required"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-252")] // ProviderRegistry.swift:482-490
    public void needs_newer_cli_is_false_for_an_unrelated_message_and_for_the_empty_string()
    {
        Assert.False(ProviderRegistry.NeedsNewerCLI("network timed out"));
        Assert.False(ProviderRegistry.NeedsNewerCLI(""));
    }

    // ---- UpdateCLIIfDue's gates, no executable ever reached (PR-PROVREG-253) ----------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:498
    public async Task update_cli_if_due_returns_false_at_once_for_a_provider_that_does_not_update_its_own_cli()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // Review fix (finding #2, HIGH): wrapped in NoExecutableSeam even though UpdatesOwnCLI(Cursor) is
        // proven false and should return before Executable(for:) is ever consulted - if that gate regressed,
        // Cursor's own ExecutableName could resolve the real Cursor CLI on this machine's PATH.
        using var seam = NoExecutableSeam();
        var calls = new List<ShellInvocation>();
        using (ShellObserver.Observe(calls.Add))
        {
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Cursor))));
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Cursor, force: true))));
        }
        Assert.Empty(calls); // no process is ever spawned for a provider that is not Claude or Codex
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:498
    public async Task update_cli_if_due_returns_false_when_a_custom_binary_path_is_set()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = TempDir() + "/my-own-claude-build.exe"; // a hand-set custom build
        var registry = MakeRegistry(runtime, settings);
        // Review fix (finding #2, HIGH, applied here too though not one of the cited lines): the custom-path
        // gate (:498) is proven to return before Executable(for:) is reached, but a regression there would
        // let Claude's real ExecutableName fall through to this machine's real PATH the same way.
        using var seam = NoExecutableSeam();
        var calls = new List<ShellInvocation>();
        using (ShellObserver.Observe(calls.Add))
        {
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude))));
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)))); // force does not override this gate
        }
        Assert.Empty(calls);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:506-507
    public async Task update_cli_if_due_skips_an_unforced_call_whose_last_attempt_was_under_12_hours_ago()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        runtime.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object>
        {
            ["claude"] = SwiftDate.Seconds1970(SwiftDate.Now) - 11 * 3600d, // 11 hours ago: under the 12-hour gate
        });
        // Review fix (finding #2, HIGH): wrapped in NoExecutableSeam - the 12-hour date gate (:506-507) is
        // proven to return before Executable(for:) is reached, but a regression there would fall through to
        // this machine's real PATH the same way.
        using var seam = NoExecutableSeam();
        var calls = new List<ShellInvocation>();
        using (ShellObserver.Observe(calls.Add))
        {
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude))));
        }
        Assert.Empty(calls); // never even reaches executable resolution
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:506-507, :509
    public async Task update_cli_if_due_returns_false_and_writes_no_date_when_no_executable_resolves()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        using var seam = NoExecutableSeam();
        var calls = new List<ShellInvocation>();
        bool result;
        using (ShellObserver.Observe(calls.Add))
        {
            result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true))); // force bypasses the date gate too
        }
        Assert.False(result);
        Assert.Empty(calls); // no --version, no update
        Assert.Null(runtime.Defaults.Object("providerCLIUpdateDates")); // PR-PROVREG-255: no write on this early return
    }

    // ---- The run itself: an 11/13-hour date gate and a forced run past it (PR-PROVREG-253, 254, 255) -----

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:506-507
    public async Task update_cli_if_due_runs_once_the_last_attempt_is_13_hours_old()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        runtime.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object>
        {
            ["claude"] = SwiftDate.Seconds1970(SwiftDate.Now) - 13 * 3600d, // past the 12-hour gate
        });
        var script = ProviderProbeStandIns.CopyFor("thirteen-hours").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude)));

        Assert.False(result); // same version before and after: no reload, but the run did happen
        Assert.Equal(3, script.RecordedCalls().Count); // --version, update, --version
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-254")] // ProviderRegistry.swift:512-514
    public async Task update_cli_if_due_runs_version_then_update_then_version_with_the_documented_shape()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var script = ProviderProbeStandIns.CopyFor("shape").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var invocations = new List<ShellInvocation>();
        bool result;
        using (ShellObserver.Observe(invocations.Add))
        {
            result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));
        }

        Assert.False(result); // stand-in reports the same version both times
        var calls = script.RecordedCalls();
        Assert.Equal(3, calls.Count);
        Assert.Equal(["--version"], calls[0]);
        Assert.Equal(["update"], calls[1]);
        Assert.Equal(["--version"], calls[2]);

        Assert.Equal(3, invocations.Count);
        Assert.Equal(30, invocations[0].Timeout); // :531-532
        Assert.Equal(300, invocations[1].Timeout); // :514
        Assert.Equal(Path.GetTempPath(), invocations[1].Directory); // :514 temp directory
        Assert.Equal(30, invocations[2].Timeout);
        // The same provider environment computation is used for all three runs (:512). Compared sorted by
        // key: two separately built dictionaries need not enumerate in the same order to be the same map.
        var expectedEnvironment = registry.Environment(ProviderKind.Claude)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray();
        foreach (var invocation in invocations)
            Assert.Equal(expectedEnvironment, invocation.Environment!.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-254")] // ProviderRegistry.swift:512-518
    public async Task update_cli_if_due_ignores_a_failing_update_and_still_writes_the_attempt_date()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // The update call itself fails (nonzero exit, via SetUpdateExitCode - the review fix for finding #6:
        // previously exit.txt was shared by every call, so nothing here actually failed): both --version
        // reads still succeed (exit.txt stays 0) and report the same text, so a failing update still counts
        // as an attempt. Swift discards the update call's result unconditionally (`_ = try? await Shell.run`,
        // :514), so production takes the exact same path whether the update succeeded or failed; this proves
        // that discarding, not merely a same-version comparison the pre-fix version already exercised.
        var script = ProviderProbeStandIns.CopyFor("failing-update").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetUpdateExitCode(1);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        Assert.False(result);
        var dates = (IReadOnlyDictionary<string, object>)runtime.Defaults.Object("providerCLIUpdateDates")!;
        Assert.Equal(SwiftDate.Seconds1970(SwiftDate.Now), Assert.IsType<double>(dates["claude"]));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-255")] // ProviderRegistry.swift:505, Swift's `as? [String: Double]` per-entry semantics
    public async Task update_cli_if_due_treats_one_unparseable_entry_as_no_dates_at_all_even_when_the_rest_parse()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // A well-typed dictionary whose "claude" entry is fresh (would normally gate an unforced call) but
        // whose "codex" entry is a string, not a number: Swift's `as? [String: Double]` fails for the WHOLE
        // dictionary when any one value cannot bridge to Double, not just that entry (ReadCliUpdateDates'
        // own comment), so "claude" must read as due too, not gated by its own fresh timestamp.
        runtime.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object>
        {
            ["claude"] = SwiftDate.Seconds1970(SwiftDate.Now), // fresh: would gate an unforced call on its own
            ["codex"] = "x", // unparseable: invalidates the whole dictionary
        });
        var script = ProviderProbeStandIns.CopyFor("malformed-entry").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude))); // unforced: only runs because the dates read as empty

        Assert.Equal(3, script.RecordedCalls().Count); // it ran, so claude's own fresh entry did not gate it
    }

    // ---- Persisted attempt dates (PR-PROVREG-255) -------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-255")] // ProviderRegistry.swift:515-516
    public async Task update_cli_if_due_keeps_the_other_providers_entry_when_it_writes_this_ones()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        runtime.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object> { ["codex"] = 12345d });
        var script = ProviderProbeStandIns.CopyFor("keep-other").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        var dates = (IReadOnlyDictionary<string, object>)runtime.Defaults.Object("providerCLIUpdateDates")!;
        Assert.Equal(12345d, dates["codex"]);
        Assert.Equal(SwiftDate.Seconds1970(SwiftDate.Now), dates["claude"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-255")] // ProviderRegistry.swift:505
    public async Task update_cli_if_due_treats_a_malformed_stored_value_as_no_dates_at_all()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // A string, not a [String: Double] dictionary: Swift's `as? [String: Double]` fails for the whole
        // value, not just one entry, so both CLIs read as due.
        runtime.Defaults.Set("providerCLIUpdateDates", "not-a-dictionary");
        var script = ProviderProbeStandIns.CopyFor("malformed").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude))); // unforced: only runs because the stored dates read as empty

        Assert.Equal(3, script.RecordedCalls().Count); // it ran, so the malformed value did not gate it
    }

    // ---- The before/after version comparison decides the reload and the result (PR-PROVREG-256) ---------

    [Fact]
    [Trait("Req", "PR-PROVREG-256")] // ProviderRegistry.swift:517-518
    public async Task update_cli_if_due_returns_false_and_does_not_reload_when_the_version_is_unchanged()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var script = ProviderProbeStandIns.CopyFor("same-version").SetStdout("1.0.0\n").SetExitCode(0);
        // No post-update-stdout.txt: the "update" call does not change what --version reports afterwards.
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var before = registry.Status(ProviderKind.Claude);
        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        Assert.False(result);
        Assert.Equal(before, registry.Status(ProviderKind.Claude)); // Refresh never ran
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-256")] // ProviderRegistry.swift:517-518
    public async Task update_cli_if_due_returns_false_when_the_version_cannot_be_read_afterwards()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // The first --version succeeds ("before" is "1.0.0"); once "update" has run, the exit code flips to
        // nonzero, so the second --version fails and "after" reads nil.
        var script = ProviderProbeStandIns.CopyFor("after-fails").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetPostUpdateExitCode(1);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true))));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-256")] // ProviderRegistry.swift:517-521
    public async Task update_cli_if_due_returns_true_and_reloads_when_the_version_changed()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var script = ProviderProbeStandIns.CopyFor("changed-version").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetPostUpdateStdout("2.0.0\n"); // the "update" call installs a newer version
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        Assert.True(result); // :521
        // Refresh(provider) and LoadCatalog(provider, force: true) both ran (:519-520): Refresh assigns a
        // fresh status from the same seam-resolved executable, reporting the post-update version.
        Assert.Equal("2.0.0", registry.Status(ProviderKind.Claude).Version);

        // Review fix (finding #5, MEDIUM): the assertion above is also produced by Refresh alone (LoadCatalog
        // deleted, or run before Refresh, still passes it), so it does not by itself prove LoadCatalog ran, or
        // that it ran after Refresh. Assert the full call sequence instead: UpdateCLIIfDue's own --version /
        // update / --version (:513-517), then Refresh(Claude)'s --version and "auth status" probes (:375-376,
        // both against the already-updated executable, so both report "2.0.0"), then LoadCatalog's
        // LoadClaudeCatalog -> ClaudeSession.Handshake, whose bare process invocation (the stand-in speaks no
        // stream-json) is exactly the documented control-request argv (ClaudeSession.cs:270-274). The
        // handshake fails to find its response in a non-JSON stream and is swallowed the same way every other
        // catalog probe's failure is (SwiftTry.OptionalAsync), so it changes nothing else this test checks.
        var calls = script.RecordedCalls();
        Assert.Equal(6, calls.Count);
        Assert.Equal(["--version"], calls[0]); // UpdateCLIIfDue's "before" read
        Assert.Equal(["update"], calls[1]);
        Assert.Equal(["--version"], calls[2]); // UpdateCLIIfDue's "after" read: :517-518 sees the change
        Assert.Equal(["--version"], calls[3]); // Refresh(Claude): :375
        Assert.Equal(["auth", "status"], calls[4]); // Refresh(Claude): :376
        Assert.Equal(
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                "--no-session-persistence", "--setting-sources", "user,project,local"],
            calls[5]); // LoadCatalog(force: true) -> LoadClaudeCatalog -> ClaudeSession.Handshake
        Assert.Empty(registry.UpdatingCLIs); // :511 defer removed it after the true return too
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-256")] // ProviderRegistry.swift:518
    public async Task update_cli_if_due_returns_true_when_the_version_could_not_be_read_before_but_can_be_read_after()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // "before" --version fails (exit 1): nil. Once "update" has run, the exit code flips to 0 and
        // stdout carries a version, so "after" reads a real value: nil != "9.9.9", so the guard's `let
        // after` unwraps and the comparison takes the true branch.
        var script = ProviderProbeStandIns.CopyFor("nil-before").SetStdout("9.9.9\n").SetExitCode(1);
        script.SetPostUpdateStdout("9.9.9\n");
        script.SetPostUpdateExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        Assert.True(result);
        Assert.Equal("9.9.9", registry.Status(ProviderKind.Claude).Version);
    }

    // ---- UpdatingCLIs (PR-PROVREG-257) ------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-257")] // ProviderRegistry.swift:510-511
    public async Task updating_c_l_is_holds_the_provider_only_while_its_updater_runs()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        Assert.False(registry.UpdatingCLIs.Contains(ProviderKind.Claude));

        var markerDir = TempDir();
        var marker = Path.Combine(markerDir, "in-flight.marker");
        var script = ProviderProbeStandIns.CopyFor("in-flight").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetConcurrencyProbe(marker, sleepMs: 400);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var task = runtime.Loop.Run(() => Task.FromResult(registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));
<        // Swift :30 @MainActor, :508 await LoginEnvironment.load(), :510-511 insert/defer remove.
        // Keep pumping the actor until the offline probe starts: a fresh environment load suspends
        // before insertion, so waiting off the loop alone never lets its continuation reach the probe.
        await Task.Run(() => runtime.Loop.RunUntil(() => File.Exists(marker), TimeSpan.FromSeconds(5)));
        Assert.True(File.Exists(marker), "the stand-in never started (or finished too fast to observe)");
        Assert.True(registry.UpdatingCLIs.Contains(ProviderKind.Claude));

        await Task.Run(() => runtime.Loop.Run(() => task));
        Assert.False(registry.UpdatingCLIs.Contains(ProviderKind.Claude)); // removed on return, unchanged-version path
    }

    // Review fix (finding #3, MEDIUM): PR-PROVREG-257's side_effects is "Observation change notifications on
    // insert and remove". Before this fix, updatingCLIs was mutated as a bare backing field with no Notify
    // call at all, so this test would have failed (zero notifications) against the reviewed commit.
    [Fact]
    [Trait("Req", "PR-PROVREG-257")]
    public async Task updating_c_l_is_notifies_on_both_the_insert_and_the_remove()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var script = ProviderProbeStandIns.CopyFor("notify").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var notifications = new List<string>();
        registry.PropertyChanged += (_, e) => { if (e.PropertyName is { } name) notifications.Add(name); };

        await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));

        // At least one notification for the insert and one for the remove: other properties this same call
        // touches (Statuses is untouched here - Refresh never runs on the unchanged-version path) may also
        // notify, so this counts occurrences of "UpdatingCLIs" rather than asserting the list exactly.
        Assert.True(notifications.Count(n => n == nameof(ProviderRegistry.UpdatingCLIs)) >= 2,
            "expected at least one notification for the insert and one for the remove, got: " + string.Join(", ", notifications));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-257")] // ProviderRegistry.swift:509-511
    public async Task updating_c_l_is_is_never_inserted_when_a_gate_returns_early()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // Review fix (finding #2, HIGH): the Cursor call is also wrapped now, matching every other call in
        // this file - UpdatesOwnCLI(Cursor) is proven false elsewhere, but a regression here would otherwise
        // reach the real Cursor CLI on this machine's PATH with no seam standing in the way.
        using (NoExecutableSeam())
        {
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Cursor)))); // wrong provider
        }
        Assert.Empty(registry.UpdatingCLIs);
        using (NoExecutableSeam())
        {
            Assert.False(await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)))); // no executable
        }
        Assert.Empty(registry.UpdatingCLIs);
    }

    // ---- Single-flight: an unforced call while one is already in flight (PR-PROVREG-253) ------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:500-501
    public async Task update_cli_if_due_unforced_returns_false_immediately_while_one_is_already_in_flight()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var marker = Path.Combine(TempDir(), "unforced.marker");
        var script = ProviderProbeStandIns.CopyFor("unforced-blocked").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetConcurrencyProbe(marker, sleepMs: 400);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var first = runtime.Loop.Run(() => Task.FromResult(registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!registry.UpdatingCLIs.Contains(ProviderKind.Claude) && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(registry.UpdatingCLIs.Contains(ProviderKind.Claude), "the first call never became in-flight");

        var calls = new List<ShellInvocation>();
        bool second;
        using (ShellObserver.Observe(calls.Add))
        {
            second = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLIIfDue(ProviderKind.Claude))); // unforced: must not wait
        }
        Assert.False(second);
        Assert.Empty(calls); // the unforced caller never itself touches the CLI

        await Task.Run(() => runtime.Loop.Run(() => first));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-253")] // ProviderRegistry.swift:501-503
    public async Task update_cli_if_due_forced_waits_for_an_in_flight_update_then_runs_again()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var marker = Path.Combine(TempDir(), "forced.marker");
        var script = ProviderProbeStandIns.CopyFor("forced-wait").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetConcurrencyProbe(marker, sleepMs: 300);
        using var seam = ExecutableSeam(ProviderKind.Claude, script);

        var first = runtime.Loop.Run(() => Task.FromResult(registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!registry.UpdatingCLIs.Contains(ProviderKind.Claude) && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(registry.UpdatingCLIs.Contains(ProviderKind.Claude), "the first call never became in-flight");

        // The second, forced call waits in a 500ms-polling loop driven by SwiftClock.Provider, which
        // TestRuntime fixes: pump it forward in small real-time steps until the first call has finished,
        // so the wait resolves without a real half-second sleep per iteration.
        var second = runtime.Loop.Run(() => Task.FromResult(registry.UpdateCLIIfDue(ProviderKind.Claude, force: true)));
        while (!first.IsCompleted)
        {
            runtime.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(10);
        }
        await Task.Run(() => runtime.Loop.Run(() => first));
        // Give the pump a few more turns so the second call's own wait loop (which may still be mid-sleep)
        // observes the first call's completion and proceeds to its own run.
        for (var i = 0; i < 20 && !second.IsCompleted; i++)
        {
            runtime.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(10);
        }
        await Task.Run(() => runtime.Loop.Run(() => second)); // both calls ran the updater; neither throws
        Assert.Equal(6, script.RecordedCalls().Count); // two full --version/update/--version sequences
    }

    // ---- InstalledCLIVersion / CliVersion (PR-PROVREG-258) ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-258")] // ProviderRegistry.swift:531-536
    public async Task cli_version_returns_the_trimmed_stdout_on_success_and_nil_otherwise()
    {
        var ok = ProviderProbeStandIns.CopyFor("cliversion-ok").SetStdout("  2.3.4  \n").SetExitCode(0);
        Assert.Equal("2.3.4", await ProviderRegistry.CliVersion(ok.ExecutablePath, new Dictionary<string, string>()));

        var nonzero = ProviderProbeStandIns.CopyFor("cliversion-nonzero").SetStdout("2.3.4\n").SetExitCode(1);
        Assert.Null(await ProviderRegistry.CliVersion(nonzero.ExecutablePath, new Dictionary<string, string>()));

        var empty = ProviderProbeStandIns.CopyFor("cliversion-empty").SetStdout("   \n").SetExitCode(0);
        Assert.Null(await ProviderRegistry.CliVersion(empty.ExecutablePath, new Dictionary<string, string>()));

        var missing = Path.Combine(TempDir(), "does-not-exist.exe");
        Assert.Null(await ProviderRegistry.CliVersion(missing, new Dictionary<string, string>())); // a launch error
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-258")] // ProviderRegistry.swift:524-529
    public async Task installed_cli_version_resolves_the_executable_then_reads_its_version()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        var script = ProviderProbeStandIns.CopyFor("installed-version").SetStdout("4.5.6\n").SetExitCode(0);
        using (ExecutableSeam(ProviderKind.Claude, script))
        {
            Assert.Equal("4.5.6", await registry.InstalledCLIVersion(ProviderKind.Claude));
        }
        Assert.Equal(["--version"], script.RecordedCalls()[0]);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-258")] // ProviderRegistry.swift:526-527
    public async Task installed_cli_version_is_nil_when_no_executable_resolves()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        using (NoExecutableSeam())
        {
            Assert.Null(await registry.InstalledCLIVersion(ProviderKind.Claude));
        }
    }
}
