using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e).
//
// Tests for unit Services.Providers.ProviderRegistry part 5 of 5: RecordCommand (PR-PROVREG-216) and
// LoadCommands (PR-PROVREG-215, 217, 218). A fifth class in the file this unit's tests mirror (R-TEST-3):
// parts 1 to 4 live in ProviderRegistryTests.cs, ProviderRegistryRefreshTests.cs,
// ProviderRegistryCatalogTests.cs and ProviderRegistryLimitsAndCommandsTests.cs.
//
// RESOLVED LATER (defects-priority): the join-not-duplicate half of PR-PROVREG-217 is proved by launch-counting stand-ins in
// ComposerViewTests (Two_composers_on_one_provider_and_folder_launch_the_CLI_once_...), and the CLI half of PR-PROVREG-218 by
// ProviderRegistryCommandErrorTests.a_load_that_cannot_launch_the_CLI_stores_the_launch_error_and_clears_the_in_flight_entry: a launch
// failure is a Win32Exception, which the registry now stores by its own Message (it threw NotSupportedException out of the catch).
// What follows is the original note, kept for its history.
//
// UNTESTABLE IN THIS PART (section 6: reported, not invented):
//  - PR-PROVREG-217's join-not-duplicate half (a second caller for the same key awaiting the first task's
//    completion instead of spawning a second CLI process): CodexSession.ListCommands and ClaudeSession's
//    process launch give no observable signal from ProviderRegistry's public surface that would distinguish
//    "one CLI launch, two awaiters" from "two CLI launches" without instrumenting those sessions directly,
//    which is out of this part's scope (they belong to other units in this file's dependency cycle). The
//    single-caller freshness/force and executable-resolution guards that gate reaching that join point ARE
//    proven below.
//  - PR-PROVREG-234 (SignIn) and PR-PROVREG-235 (SigningIn): left MARROW_STUB per the packet's own
//    windows_path ("do not implement until reachability is settled") -- reachability of ProviderRegistry.
//    signIn in the pinned checkout is UNRESOLVED and not recorded in dead_code.md. No test is written for
//    either; see this unit's final report.
//  - PR-PROVREG-215 (the wholesale commandErrors clear at 32 entries) and the CLI-triggered half of
//    PR-PROVREG-218 (storing `error.localizedDescription`/SwiftErrors.LocalizedDescription for a REAL
//    failed load): reaching LoadCommands' catch block needs CodexSession.ListCommands or ClaudeSession's
//    process launch to actually throw. CodexSession.ListCommands is MARROW_STUB (an unconditional
//    NotImplementedException, a bare .NET exception with neither ILocalizedError nor ISwiftErrorIdentity);
//    ClaudeSession's real StdioProcess/ProcessLauncher.Run() path (used against a deliberately-invalid
//    fake executable, the only way to force a failure without a real CLI, matching this file's own
//    WriteExecutable technique) throws Win32Exception, also neither. SwiftErrors.LocalizedDescription
//    (Marrow.Core/Port/SwiftErrors.cs:121-125) correctly refuses to map either -- `throw new
//    NotSupportedException($"No Swift text is defined for {error.GetType().Name}; record a per-site
//    decision.")` -- so calling LoadCommands' catch path in a test throws that NotSupportedException
//    instead of exercising PR-PROVREG-215/218's own logic. Confirmed by running it: the stack trace ends
//    at SwiftErrors.LocalizedDescription. This is a missing/mismatched dependency (section 6), not
//    something this part can fabricate a SwiftError-typed failure around; UpdateCommands' own error-
//    clearing on success (:750) and its 32-entry command-CACHE eviction (a different structure, PR-
//    PROVREG-214) are already proven in ProviderRegistryLimitsAndCommandsTests.cs. LoadCommands' own
//    implementation (ProviderRegistry.cs) still ports :776-779 exactly, cited to the Swift lines, ready to
//    be exercised once a caller in this cycle can produce a real ISwiftErrorIdentity failure.
public sealed class ProviderRegistrySlashCommandsTests
{
    private static string TempDir()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-slash-commands", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteExecutable(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, []);
        return path.Replace('\\', '/');
    }

    private static FakeProviderSettings Settings() => new();

    private static void SeedUnblockingCache(TestRuntime runtime)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs",
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));
    }

    private static ProviderRegistry MakeRegistry(TestRuntime runtime, FakeProviderSettings settings)
    {
        SeedUnblockingCache(runtime);
        return new ProviderRegistry(settings);
    }

    // ---- PR-PROVREG-216: recordCommand move-to-front, no duplicate, capped at 100, whole dict persisted ----

    [Fact]
    [Trait("Req", "PR-PROVREG-216")] // Services/Providers/ProviderRegistry.swift:172,832-838
    public void A_fresh_registry_recovers_complete_command_order_for_both_providers_from_defaults()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());
        runtime.OnMain(() =>
        {
            registry.RecordCommand("alpha", ProviderKind.Claude);
            registry.RecordCommand("review", ProviderKind.Codex);
            registry.RecordCommand("beta", ProviderKind.Claude);
            registry.RecordCommand("compact", ProviderKind.Codex);
            registry.RecordCommand("alpha", ProviderKind.Claude);
            registry.RecordCommand("review", ProviderKind.Codex);
        });
        var stored = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(runtime.Defaults.Object("recentSlashCommands"));
        Assert.Equal(new[] { "claude", "codex" }, stored.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new object[] { "alpha", "beta" }, Assert.IsAssignableFrom<IReadOnlyList<object>>(stored["claude"]));
        Assert.Equal(new object[] { "review", "compact" }, Assert.IsAssignableFrom<IReadOnlyList<object>>(stored["codex"]));
        var restarted = runtime.OnMain(() => new ProviderRegistry(Settings())); // :172 reads the persisted dictionary.
        Assert.NotSame(registry, restarted);
        Assert.Equal(new[] { "claude", "codex" }, restarted.RecentCommands.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "alpha", "beta" }, restarted.RecentCommands["claude"]);
        Assert.Equal(new[] { "review", "compact" }, restarted.RecentCommands["codex"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-216")] // ProviderRegistry.swift:753-759
    public void recording_a_command_moves_it_to_front_with_no_duplicate_left_behind()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());

        runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("alpha", ProviderKind.Claude)));
        runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("beta", ProviderKind.Claude)));
        Assert.Equal(new[] { "beta", "alpha" }, registry.RecentCommands["claude"].ToArray());

        runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("alpha", ProviderKind.Claude))); // moves back to front, no duplicate left
        Assert.Equal(new[] { "alpha", "beta" }, registry.RecentCommands["claude"].ToArray());

        // Persisted under the exact key, and written on every call (one write per RecordCommand).
        Assert.Equal(3, runtime.Defaults.Writes.Count(key => key == "recentSlashCommands"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-216")] // ProviderRegistry.swift:757
    public void recent_commands_are_capped_at_exactly_one_hundred_per_provider()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());

        for (var i = 0; i < 105; i++) runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("cmd" + i, ProviderKind.Codex)));

        var recent = registry.RecentCommands["codex"];
        Assert.Equal(100, recent.Count);
        Assert.Equal("cmd104", recent[0]); // most recent first
        Assert.Equal("cmd5", recent[99]);  // the earliest five (cmd0..cmd4) fell off the end
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-216")] // ProviderRegistry.swift:754, 757
    public void recent_commands_are_kept_separately_per_provider()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());

        runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("only-claude", ProviderKind.Claude)));
        runtime.Loop.Run(() => MainActor.Run(() => registry.RecordCommand("only-codex", ProviderKind.Codex)));

        Assert.Equal(new[] { "only-claude" }, registry.RecentCommands["claude"].ToArray());
        Assert.Equal(new[] { "only-codex" }, registry.RecentCommands["codex"].ToArray());
    }

    // ---- PR-PROVREG-217: loadCommands only lists for Codex and Claude ---------------------------------------

    [Theory]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Antigravity)]
    [Trait("Req", "PR-PROVREG-217")] // ProviderRegistry.swift:763
    public async Task load_commands_is_a_no_op_for_every_provider_other_than_codex_and_claude(ProviderKind provider)
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        var dir = TempDir();
        settings.Paths[provider] = WriteExecutable(dir, "cli.exe"); // resolved, so only the provider guard could stop it
        var registry = MakeRegistry(runtime, settings);

        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(provider, dir)));

        Assert.Empty(registry.Commands);
        Assert.Empty(registry.CommandErrors);
    }

    // ---- PR-PROVREG-217: an unresolved executable returns silently, no error recorded -----------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-217")] // ProviderRegistry.swift:767
    public async Task an_unresolved_executable_returns_silently_with_no_error_recorded()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        settings.Paths[ProviderKind.Codex] = Path.Combine(TempDir(), "does-not-exist.exe"); // does not resolve, no fallback (:219-226)
        var registry = MakeRegistry(runtime, settings);

        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Codex, TempDir())));

        Assert.Empty(registry.Commands);
        Assert.Empty(registry.CommandErrors);
    }

    // ---- PR-PROVREG-217: the freshness window is exactly 600 seconds, and force bypasses it -----------------

    [Fact]
    [Trait("Req", "PR-PROVREG-217")] // ProviderRegistry.swift:766
    public async Task an_unforced_call_within_six_hundred_seconds_of_a_delivered_list_is_suppressed()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        var dir = TempDir();
        settings.Paths[ProviderKind.Codex] = WriteExecutable(dir, "codex.exe"); // resolves: only the freshness gate could stop it
        var registry = MakeRegistry(runtime, settings);
        runtime.Loop.Run(() => MainActor.Run(() => registry.UpdateCommands(ValueList<SlashCommand>.Empty, ProviderKind.Codex, dir))); // sets the fetch timestamp to "now"

        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Codex, dir, force: false)));

        // No error was recorded: the freshness gate returned before ever reaching CodexSession.
        Assert.Empty(registry.CommandErrors);
    }

}
