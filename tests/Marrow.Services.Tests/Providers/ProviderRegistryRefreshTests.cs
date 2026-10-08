using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e).
//
// Tests for unit Services.Providers.ProviderRegistry part 2 of 5: RefreshAll/probeWidth (PR-PROVREG-106),
// Refresh (PR-PROVREG-107 to 110), the Version probe (PR-PROVREG-111, 112) and the Auth probe's per-provider
// cases (PR-PROVREG-113 to 124). A second class in the file this unit's tests mirror (R-TEST-3): the first
// part's tests live in ProviderRegistryTests.cs.
//
// PR-PROVREG-125 is proven by ProviderRegistryTests.an_api_key_provider_with_no_key_is_signed_out_and_not_
// configured_with_no_network_call (an added Req trait on that existing test: :381-386 is exactly the branch
// PR-PROVAPI-088 already exercises). PR-PROVREG-126 to 129 (a valid or rejected key's follow-up reads) are
// untestable in this part: DeepSeekAPI.Validate, MetaAPI.Validate and ZaiAPI.Validate are called at
// RefreshAPIProvider's one call site with no URL override (matching the Swift, which has none either), so
// exercising the valid/invalid branches would reach the real internet, forbidden by R-TEST-10. See
// ProviderRegistryTests.cs's comment above that test for the same rationale (PR-PROVAPI-088).
public sealed class ProviderRegistryRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-PROVREG-109")]
    public async Task Disabling_or_removing_accounts_during_discovery_finishes_signed_out_without_a_probe(bool remove)
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        SeedUnblockingCache(runtime);
        var accounts = new Marrow.Core.Port.Accounts.AccountStore(UserDefaults.Standard.Current);
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, TempDir());
        var script = ProviderProbeStandIns.CopyFor("codex");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release discovery");
            return FileUrl(script.ExecutablePath);
        });
        var refresh = Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex), TimeSpan.FromSeconds(30)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await MainActor.Invoke(() =>
            {
                foreach (var account in accounts.ForService(ProviderKind.Codex))
                    if (remove) accounts.Remove(account.Id); else accounts.SetEnabled(account.Id, false);
                return Task.CompletedTask;
            });
        }
        finally { release.Set(); }
        await refresh;
        Assert.Empty(script.RecordedCalls());
        Assert.False(registry.Status(ProviderKind.Codex).IsChecking);
        Assert.IsType<ProviderStatus.Auth.SignedOut>(registry.Status(ProviderKind.Codex).ProviderStatusAuth);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-109")]
    public async Task Refresh_resolves_files_off_the_main_actor_and_publishes_status_on_it()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        bool? resolverOnMain = null;
        var writesOnMain = new List<bool>();
        registry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProviderRegistry.Statuses)) writesOnMain.Add(MainActor.IsIsolated);
        };
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ =>
        {
            resolverOnMain = MainActor.IsIsolated;
            return null; // fail closed: no installed provider or process can be reached
        });
        await Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex)));
        Assert.Equal(false, resolverOnMain);
        Assert.Equal(new[] { true, true }, writesOnMain);
        Assert.Equal(new ProviderStatus(), registry.Status(ProviderKind.Codex));
    }

    private static string TempDir()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-refresh", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Matches the production FileUrl helper in ProviderRegistry.cs (a "file://" URL's .Path prepends a slash
    // before a Windows drive letter that CreateProcessW rejects; a non-"file" scheme round trips it).
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static void SeedUnblockingCache(TestRuntime runtime)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
    }

    private static ProviderRegistry MakeRegistry(TestRuntime runtime, FakeProviderSettings settings)
    {
        SeedUnblockingCache(runtime);
        return new ProviderRegistry(settings);
    }

    // Environment(for:) reads MCPProviderConfig.GeminiSettingsPath() and MCPSessionOverrides.OpencodeEnvironment(),
    // which both resolve Storage.Root; R-TEST-9's throwaway folder under MARROW_PORT_DATA_DIR.
    private static IDisposable BindStorage()
    {
        var dir = TempDir();
        return Storage.Locations.Override(StorageLocations.Create(dir, dir));
    }

    // ---- RefreshAll: probeWidth = 4, refills as each finishes, completes every provider (PR-PROVREG-106) ----
    //
    // AntigravitySession.AuthStatus, CopilotSession.AuthStatus, CommandCodeAPI.AuthStatus and PiCLI.AuthStatus
    // are MARROW_STUB (missing dependencies owned by other units in this file's cycle, section 6), so this
    // test points those four providers at a Binary path that cannot resolve (a custom path always short
    // circuits the PATH fallback, :224), so Refresh returns at the "executable not found" branch (:367-370)
    // before ever reaching Auth's delegate cases. The other seven CLI providers, and the three API-key
    // providers on an empty key, are all fully implemented in this file and safely probed.

    [Fact]
    [Trait("Req", "PR-PROVREG-106")] // ProviderRegistry.swift:335-356
    public async Task refresh_all_probes_at_most_four_providers_at_once_and_completes_every_one()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var settings = new FakeProviderSettings();
        var cliProviders = new[]
        {
            ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Cursor, ProviderKind.Opencode,
            ProviderKind.OpencodeGo, ProviderKind.Grok, ProviderKind.Devin,
        };
        var markerDir = Path.Combine(TempDir(), "markers");
        Directory.CreateDirectory(markerDir);
        var scripts = new Dictionary<ProviderKind, ProviderProbeScript>();
        foreach (var provider in cliProviders)
        {
            var script = ProviderProbeStandIns.CopyFor(provider.Id).SetExitCode(0);
            script.SetConcurrencyProbe(Path.Combine(markerDir, provider.Id + ".marker"), sleepMs: 150);
            scripts[provider] = script;
            settings.Paths[provider] = script.ExecutablePath;
        }
        var missing = Path.Combine(TempDir(), "does-not-exist.exe");
        foreach (var provider in new[] { ProviderKind.Antigravity, ProviderKind.Copilot, ProviderKind.Commandcode, ProviderKind.Pi })
            settings.Paths[provider] = missing;
        var registry = MakeRegistry(runtime, settings);

        // RefreshAll's child probes hop onto the main actor for their own state writes (R-TASK-5, R-ISO-4);
        // TestRuntime's main actor is a ManualMainLoop that only runs work while something pumps it, so this
        // drives RefreshAll from a background thread (runtime.Loop.Run pumps until it completes) while this
        // thread polls the marker folder, matching R-TEST-5's "advance the manual clock and pump" for a call
        // that genuinely spans real process launches.
        using var stop = new CancellationTokenSource();
        var maxConcurrent = 0;
        var poll = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var active = Directory.GetFiles(markerDir).Length;
                if (active > maxConcurrent) maxConcurrent = active;
                try { await Task.Delay(5, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        var pumpTask = Task.Run(() => runtime.Loop.Run(() => registry.RefreshAll(), TimeSpan.FromSeconds(30)));
        await pumpTask;
        stop.Cancel();
        await poll;

        Assert.True(maxConcurrent <= 4, $"observed {maxConcurrent} concurrent probes, expected at most 4 (probeWidth, :338)");
        Assert.True(maxConcurrent >= 2, $"observed only {maxConcurrent} concurrent probes; expected real overlap, not serialization");

        // Every one of the 14 providers has a final, non-checking status (:349's group drains completely).
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases) Assert.False(registry.Status(provider).IsChecking);
        // The seven probed CLI providers actually ran (their status carries the resolved executable).
        foreach (var provider in cliProviders) Assert.NotNull(registry.Status(provider).Executable);
        // The four delegate-blocked providers safely came back empty, never reaching their MARROW_STUB.
        foreach (var provider in new[] { ProviderKind.Antigravity, ProviderKind.Copilot, ProviderKind.Commandcode, ProviderKind.Pi })
            Assert.Null(registry.Status(provider).Executable);
        // The three API-key providers, with no key configured, resolved through RefreshAPIProvider.
        foreach (var provider in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai })
            Assert.Equal(new ProviderStatus.Auth.SignedOut(), registry.Status(provider).ProviderStatusAuth);
        // OpenCode Zen and OpenCode Go each got their own call: the opencode CLI is probed twice.
        Assert.Equal(2, scripts[ProviderKind.Opencode].RecordedCalls().Count);
        Assert.Equal(2, scripts[ProviderKind.OpencodeGo].RecordedCalls().Count);
    }

    // ---- Refresh: checking flag, API-key routing, unresolved reset, probe sequencing (PR-PROVREG-107 to 110) --

    [Fact]
    [Trait("Req", "PR-PROVREG-107")] // ProviderRegistry.swift:358-361, 368, 377
    public async Task refresh_marks_checking_immediately_and_only_the_replacing_status_clears_it()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var settings = new FakeProviderSettings();
        var script = ProviderProbeStandIns.CopyFor("checking").SetExitCode(0);
        script.SetSleep(200);
        settings.Paths[ProviderKind.Codex] = script.ExecutablePath;
        var registry = MakeRegistry(runtime, settings);

        Assert.False(registry.Status(ProviderKind.Codex).IsChecking); // no probe has run yet: no entry at all
        var task = runtime.Loop.Run(() => Task.FromResult(registry.Refresh(ProviderKind.Codex)));
        await Task.Delay(30); // the synchronous set (:360-361) happens before the first awaited probe returns
        Assert.True(registry.Status(ProviderKind.Codex).IsChecking);
        await Task.Run(() => runtime.Loop.Run(() => task));
        Assert.False(registry.Status(ProviderKind.Codex).IsChecking); // cleared only by the replacing status (:377)
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-108")] // ProviderRegistry.swift:362-365
    public async Task refresh_routes_an_api_key_provider_straight_to_refreshapiprovider()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        await Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Deepseek)));
        var status = registry.Status(ProviderKind.Deepseek);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), status.ProviderStatusAuth);
        Assert.False(status.ApiKeyConfigured);
        Assert.Null(status.Executable);
        Assert.Null(status.Version);
        Assert.False(status.IsChecking);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-109")] // ProviderRegistry.swift:367-370
    public async Task refresh_resets_a_stale_status_to_empty_when_the_executable_cannot_be_resolved()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var settings = new FakeProviderSettings();
        var script = ProviderProbeStandIns.CopyFor("stale").SetStdout("codex-cli 1.4.0\n").SetExitCode(0);
        settings.Paths[ProviderKind.Codex] = script.ExecutablePath;
        var registry = MakeRegistry(runtime, settings);
        await Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex)));
        Assert.NotNull(registry.Status(ProviderKind.Codex).Executable);
        Assert.Equal("1.4.0", registry.Status(ProviderKind.Codex).Version);

        settings.Paths[ProviderKind.Codex] = Path.Combine(TempDir(), "gone.exe"); // now unresolvable
        await Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex)));
        var status = registry.Status(ProviderKind.Codex);
        Assert.Equal(new ProviderStatus(), status); // fully default: version and auth are discarded, not kept
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-110")] // ProviderRegistry.swift:371-377
    public async Task refresh_runs_the_version_probe_then_the_auth_probe_and_assigns_one_status()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var settings = new FakeProviderSettings();
        var script = ProviderProbeStandIns.CopyFor("sequence").SetStdout("codex-cli 2.0.0, logged in\n").SetExitCode(0);
        settings.Paths[ProviderKind.Codex] = script.ExecutablePath;
        var registry = MakeRegistry(runtime, settings);
        await Task.Run(() => runtime.Loop.Run(() => registry.Refresh(ProviderKind.Codex)));

        var calls = script.RecordedCalls();
        Assert.Equal(2, calls.Count); // exactly two launches
        Assert.Equal(["--version"], calls[0]); // version first
        Assert.Equal(["login", "status"], calls[1]); // then auth
        var status = registry.Status(ProviderKind.Codex);
        Assert.Equal("2.0.0", status.Version);
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), status.ProviderStatusAuth);
        Assert.NotNull(status.Executable);
    }

    // ---- Version probe (PR-PROVREG-111, 112) --------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-111")] // ProviderRegistry.swift:788-796
    public async Task version_probe_runs_with_a_single_dash_dash_version_argument_and_returns_null_on_nonzero_exit()
    {
        var script = ProviderProbeStandIns.CopyFor("version-argv").SetStdout("cli 1.0.0\n").SetExitCode(1);
        var result = await ProviderRegistry.Version(script.ExecutablePath, new Dictionary<string, string>());
        Assert.Null(result); // non-success result yields nil
        Assert.Equal([["--version"]], script.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-112")] // ProviderRegistry.swift:791-795
    public async Task version_string_is_the_first_match_over_stdout_then_stderr_with_ansi_stripped()
    {
        var script = ProviderProbeStandIns.CopyFor("version-parse")
            .SetStdout("\u001B[1mtool\u001B[0m ")
            .SetStderr("build 3.10.2-rc1 extra 9.9.9\n")
            .SetExitCode(0);
        var result = await ProviderRegistry.Version(script.ExecutablePath, new Dictionary<string, string>());
        Assert.Equal("3.10.2-rc1", result); // stdout has no digits: the first match is in stderr, appended after stdout

        var none = ProviderProbeStandIns.CopyFor("version-no-match").SetStdout("no numbers here\n").SetExitCode(0);
        Assert.Null(await ProviderRegistry.Version(none.ExecutablePath, new Dictionary<string, string>()));
    }

    // ---- Auth probe: codex, claude, cursor, devin (PR-PROVREG-113 to 117) --------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-113")] // ProviderRegistry.swift:799-805
    public async Task codex_auth_requires_success_and_a_case_insensitive_logged_in()
    {
        var signedIn = ProviderProbeStandIns.CopyFor("codex-in").SetStdout("You are LOGGED IN as x\n").SetExitCode(0);
        var result = await ProviderRegistry.Auth(ProviderKind.Codex, FileUrl(signedIn.ExecutablePath), new Dictionary<string, string>());
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), result);
        Assert.Equal([["login", "status"]], signedIn.RecordedCalls());

        // Success but no "logged in" text: signed out.
        var noText = ProviderProbeStandIns.CopyFor("codex-out").SetStdout("ok\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Codex, FileUrl(noText.ExecutablePath), new Dictionary<string, string>()));

        // "logged in" text but a nonzero exit: still signed out, never unknown, since the process did not throw.
        var failed = ProviderProbeStandIns.CopyFor("codex-failed").SetStdout("logged in\n").SetExitCode(1);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Codex, FileUrl(failed.ExecutablePath), new Dictionary<string, string>()));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-114")] // ProviderRegistry.swift:806-809
    public async Task claude_auth_parses_stdout_json_and_ignores_the_exit_status()
    {
        // Nonzero exit, garbage on stderr: only stdout is parsed, and the exit status is not consulted.
        var signedIn = ProviderProbeStandIns.CopyFor("claude-in")
            .SetStdout("{\"loggedIn\":true,\"email\":\"a@b.com\"}")
            .SetStderr("not json")
            .SetExitCode(1);
        var result = await ProviderRegistry.Auth(ProviderKind.Claude, FileUrl(signedIn.ExecutablePath), new Dictionary<string, string>());
        Assert.Equal(new ProviderStatus.Auth.SignedIn("a@b.com"), result);
        Assert.Equal([["auth", "status"]], signedIn.RecordedCalls());

        var signedOut = ProviderProbeStandIns.CopyFor("claude-out").SetStdout("{\"loggedIn\":false}").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Claude, FileUrl(signedOut.ExecutablePath), new Dictionary<string, string>()));

        var badJson = ProviderProbeStandIns.CopyFor("claude-bad").SetStdout("not json").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            await ProviderRegistry.Auth(ProviderKind.Claude, FileUrl(badJson.ExecutablePath), new Dictionary<string, string>()));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-115")] // ProviderRegistry.swift:810-816
    public async Task cursor_auth_extracts_the_account_or_distinguishes_not_logged_in_from_unknown()
    {
        var signedIn = ProviderProbeStandIns.CopyFor("cursor-in").SetStdout("\u001B[2mHi\u001B[0m Logged in as alice@example.com\n").SetExitCode(0);
        var result = await ProviderRegistry.Auth(ProviderKind.Cursor, FileUrl(signedIn.ExecutablePath), new Dictionary<string, string>());
        Assert.Equal(new ProviderStatus.Auth.SignedIn("alice@example.com"), result);
        Assert.Equal([["status"]], signedIn.RecordedCalls());

        var signedOut = ProviderProbeStandIns.CopyFor("cursor-out").SetStdout("You are NOT LOGGED IN.\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Cursor, FileUrl(signedOut.ExecutablePath), new Dictionary<string, string>()));

        var unknown = ProviderProbeStandIns.CopyFor("cursor-unknown").SetStdout("garbage\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            await ProviderRegistry.Auth(ProviderKind.Cursor, FileUrl(unknown.ExecutablePath), new Dictionary<string, string>()));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-116")] // ProviderRegistry.swift:817-824
    public async Task devin_auth_tests_not_logged_in_before_logged_in()
    {
        // Contains both "not logged in" and, as a substring, "logged in": the negative test must win.
        var signedOut = ProviderProbeStandIns.CopyFor("devin-out").SetStdout("You are NOT LOGGED IN right now.\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Devin, FileUrl(signedOut.ExecutablePath), new Dictionary<string, string>()));

        var signedIn = ProviderProbeStandIns.CopyFor("devin-in").SetStdout("Logged in.\nName: Bob Jones\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn("Bob Jones"),
            await ProviderRegistry.Auth(ProviderKind.Devin, FileUrl(signedIn.ExecutablePath), new Dictionary<string, string>()));

        var unknown = ProviderProbeStandIns.CopyFor("devin-unknown").SetStdout("nothing relevant\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            await ProviderRegistry.Auth(ProviderKind.Devin, FileUrl(unknown.ExecutablePath), new Dictionary<string, string>()));
        Assert.Equal([["auth", "status"]], unknown.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-117")] // ProviderRegistry.swift:869-878
    public async Task devin_account_name_is_case_insensitive_name_prefix_and_empty_remainder_is_nil()
    {
        // "NAME:" (uppercase) still matches; the fixed-length drop of "name:" leaves the value intact.
        var upper = ProviderProbeStandIns.CopyFor("devin-upper").SetStdout("Logged in\nNAME: Carol\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn("Carol"),
            await ProviderRegistry.Auth(ProviderKind.Devin, FileUrl(upper.ExecutablePath), new Dictionary<string, string>()));

        // An empty remainder after "Name:" yields no account, not an empty string.
        var empty = ProviderProbeStandIns.CopyFor("devin-empty").SetStdout("Logged in\nName:   \n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null),
            await ProviderRegistry.Auth(ProviderKind.Devin, FileUrl(empty.ExecutablePath), new Dictionary<string, string>()));
    }

    // ---- OpenCode Zen / Go auth (PR-PROVREG-118, 119, 120) -------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-118")] // ProviderRegistry.swift:833-845
    public async Task opencode_auth_cuts_at_the_first_environment_line_and_reads_bullet_credentials()
    {
        var script = ProviderProbeStandIns.CopyFor("opencode-cut")
            .SetStdout("\U000025CF alice api\nEnvironment: dev\n\U000025CF ignored\n")
            .SetExitCode(0);
        var result = await ProviderRegistry.Auth(ProviderKind.Opencode, FileUrl(script.ExecutablePath), new Dictionary<string, string>());
        Assert.Equal(new ProviderStatus.Auth.SignedIn("alice"), result); // "ignored" never reached: cut before it
        Assert.Equal([["auth", "list"]], script.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-119")] // ProviderRegistry.swift:840-850
    public async Task opencode_strips_suffixes_in_order_drops_empty_names_and_go_picks_its_own_credential()
    {
        var stdout = "\U000025CF  \n\U000025CF bob oauth\n\U000025CF opencode go account api\n";
        var zen = ProviderProbeStandIns.CopyFor("opencode-zen").SetStdout(stdout).SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn("bob"), // empty first bullet dropped; first remaining credential wins
            await ProviderRegistry.Auth(ProviderKind.Opencode, FileUrl(zen.ExecutablePath), new Dictionary<string, string>()));

        var go = ProviderProbeStandIns.CopyFor("opencode-go").SetStdout(stdout).SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn("opencode go account"), // Go: the credential naming it, case-insensitively
            await ProviderRegistry.Auth(ProviderKind.OpencodeGo, FileUrl(go.ExecutablePath), new Dictionary<string, string>()));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-120")] // ProviderRegistry.swift:851
    public async Task opencode_with_no_selected_credential_is_signed_out_on_success_or_unknown_on_failure()
    {
        var noneSucceeded = ProviderProbeStandIns.CopyFor("opencode-none-ok").SetStdout("\U000025CF  \n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.Opencode, FileUrl(noneSucceeded.ExecutablePath), new Dictionary<string, string>()));

        var noneFailed = ProviderProbeStandIns.CopyFor("opencode-none-fail").SetStdout("\U000025CF  \n").SetExitCode(1);
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            await ProviderRegistry.Auth(ProviderKind.Opencode, FileUrl(noneFailed.ExecutablePath), new Dictionary<string, string>()));

        // Only a Zen credential exists: Go, on the same output, reads signed out on a zero exit.
        var zenOnly = ProviderProbeStandIns.CopyFor("opencode-zen-only").SetStdout("\U000025CF zen-account\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedIn("zen-account"),
            await ProviderRegistry.Auth(ProviderKind.Opencode, FileUrl(zenOnly.ExecutablePath), new Dictionary<string, string>()));
        var zenOnlyForGo = ProviderProbeStandIns.CopyFor("opencode-zen-only-go").SetStdout("\U000025CF zen-account\n").SetExitCode(0);
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            await ProviderRegistry.Auth(ProviderKind.OpencodeGo, FileUrl(zenOnlyForGo.ExecutablePath), new Dictionary<string, string>()));
    }

    // ---- Grok auth: file read, no process (PR-PROVREG-121, 122) --------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-121")] // ProviderRegistry.swift:852-854
    public async Task grok_auth_reads_a_json_file_directly_and_launches_no_process()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "auth.json");
        ProviderRegistry.SetGrokAuthPathForTesting(path);
        try
        {
            // The executable points nowhere real: if Grok's probe ever spawned a process, this would throw or
            // hang rather than reach a JSON-derived result, so a clean result proves no process was launched.
            var bogus = FileUrl(Path.Combine(dir, "unreachable-if-invoked.exe"));

            File.WriteAllText(path, "{\"a\":{\"email\":\"x@y.com\"}}");
            Assert.Equal(new ProviderStatus.Auth.SignedIn("x@y.com"), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));

            File.Delete(path); // unreadable file: unknown
            Assert.Equal(new ProviderStatus.Auth.Unknown(), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));

            File.WriteAllText(path, "[1,2,3]"); // not a JSON object at the root: unknown
            Assert.Equal(new ProviderStatus.Auth.Unknown(), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));

            File.WriteAllText(path, "{}"); // empty object: signed out
            Assert.Equal(new ProviderStatus.Auth.SignedOut(), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));
        }
        finally
        {
            ProviderRegistry.ResetGrokAuthPathForTesting();
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-122")] // ProviderRegistry.swift:855-863
    public async Task grok_account_prefers_email_then_first_name_across_entries_then_nil()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "auth.json");
        ProviderRegistry.SetGrokAuthPathForTesting(path);
        try
        {
            var bogus = FileUrl(Path.Combine(dir, "unreachable.exe"));

            // acct1's email is empty (skipped); acct2's is the first non-empty one found across entries.
            File.WriteAllText(path, "{\"acct1\":{\"email\":\"\",\"first_name\":\"Ann\"},\"acct2\":{\"email\":\"bob@x.com\"}}");
            Assert.Equal(new ProviderStatus.Auth.SignedIn("bob@x.com"), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));

            // No email anywhere: falls back to the first non-empty first_name.
            File.WriteAllText(path, "{\"a\":{\"first_name\":\"\"},\"b\":{\"first_name\":\"Zoe\"}}");
            Assert.Equal(new ProviderStatus.Auth.SignedIn("Zoe"), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));

            // Non-empty root, no readable account fields anywhere: signed in with no account.
            File.WriteAllText(path, "{\"a\":{}}");
            Assert.Equal(new ProviderStatus.Auth.SignedIn(null), await ProviderRegistry.Auth(ProviderKind.Grok, bogus, new Dictionary<string, string>()));
        }
        finally
        {
            ProviderRegistry.ResetGrokAuthPathForTesting();
        }
    }

    // ---- Delegated providers and the unreachable API-key arm (PR-PROVREG-123, 124) -------------------------
    //
    // Antigravity's auth parsing belongs to AntigravitySession (PR-PROVCLI-339); this registry-side
    // check proves its arm invokes `agy models` through the supplied executable and returns the delegate's
    // Auth directly, as PR-PROVREG-123 requires. The detailed sign-in mapping is covered by
    // AntigravitySessionTests. Copilot, Command Code and Pi auth values are covered by their owning tests.

    [Fact]
    [Trait("Req", "PR-PROVREG-123")] // ProviderRegistry.swift:825-832
    public async Task antigravity_auth_dispatches_to_its_session_and_returns_its_status()
    {
        var script = ProviderProbeStandIns.CopyFor("antigravity-auth")
            .SetStdout("gemini-3-flash\tGemini\n")
            .SetExitCode(0);
        var result = await ProviderRegistry.Auth(ProviderKind.Antigravity, FileUrl(script.ExecutablePath), new Dictionary<string, string>());
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), result);
        Assert.Equal([["models"]], script.RecordedCalls());
    }

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [Trait("Req", "PR-PROVREG-124")] // ProviderRegistry.swift:864-865
    public async Task auth_switchs_api_key_arm_returns_unknown(ProviderKind provider)
    {
        var executable = FileUrl(Path.Combine(TempDir(), "unused.exe"));
        Assert.Equal(new ProviderStatus.Auth.Unknown(), await ProviderRegistry.Auth(provider, executable, new Dictionary<string, string>()));
    }
}
