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
    [Trait("Req", "PR-PROVREG-216")] // ProviderRegistry.swift:753-759
    public void recording_a_command_moves_it_to_front_with_no_duplicate_left_behind()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());

        registry.RecordCommand("alpha", ProviderKind.Claude);
        registry.RecordCommand("beta", ProviderKind.Claude);
        Assert.Equal(new[] { "beta", "alpha" }, registry.RecentCommands["claude"].ToArray());

        registry.RecordCommand("alpha", ProviderKind.Claude); // moves back to front, no duplicate left
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

        for (var i = 0; i < 105; i++) registry.RecordCommand("cmd" + i, ProviderKind.Codex);

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

        registry.RecordCommand("only-claude", ProviderKind.Claude);
        registry.RecordCommand("only-codex", ProviderKind.Codex);

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

        await registry.LoadCommands(provider, dir);

        Assert.Empty(registry.Commands);
        Assert.Empty(registry.CommandErrors);
    }

    // ---- PR-PROVREG-217: an unresolved executable returns silently, no error recorded -----------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-217")] // ProviderRegistry.swift:767
    public async Task an_unresolved_executable_returns_silently_with_no_error_recorded()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings()); // no binary path configured for Codex anywhere on PATH

        await registry.LoadCommands(ProviderKind.Codex, TempDir());

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
        registry.UpdateCommands(ValueList<SlashCommand>.Empty, ProviderKind.Codex, dir); // sets the fetch timestamp to "now"

        await registry.LoadCommands(ProviderKind.Codex, dir, force: false);

        // No error was recorded: the freshness gate returned before ever reaching CodexSession.
        Assert.Empty(registry.CommandErrors);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-217")] // ProviderRegistry.swift:766
    public async Task force_bypasses_the_freshness_window_and_reaches_the_provider_call()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        var dir = TempDir();
        settings.Paths[ProviderKind.Codex] = WriteExecutable(dir, "codex.exe");
        var registry = MakeRegistry(runtime, settings);
        registry.UpdateCommands(ValueList<SlashCommand>.Empty, ProviderKind.Codex, dir); // fresh timestamp

        // Forced: bypasses the freshness gate, reaches CodexSession.ListCommands (still MARROW_STUB, an
        // unconditional throw), and the catch stores its localized description (PR-PROVREG-218).
        await registry.LoadCommands(ProviderKind.Codex, dir, force: true);

        var key = ProviderRegistry.CommandKey(ProviderKind.Codex, dir);
        Assert.True(registry.CommandErrors.ContainsKey(key));
    }

    // ---- PR-PROVREG-218: a failed load leaves a previously cached list for that key untouched ---------------

    [Fact]
    [Trait("Req", "PR-PROVREG-218")] // ProviderRegistry.swift:776-779, 750
    public async Task a_failed_load_stores_the_error_and_leaves_any_previously_cached_list_untouched()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        var dir = TempDir();
        settings.Paths[ProviderKind.Codex] = WriteExecutable(dir, "codex.exe");
        var registry = MakeRegistry(runtime, settings);
        var key = ProviderRegistry.CommandKey(ProviderKind.Codex, dir);
        var seeded = new ValueList<SlashCommand>([new SlashCommand { Name = "already-here", Detail = "" }]);
        registry.UpdateCommands(seeded, ProviderKind.Codex, dir);
        // Roll the clock past the freshness window so the next call reaches the provider call unforced.
        runtime.Advance(TimeSpan.FromSeconds(601));

        await registry.LoadCommands(ProviderKind.Codex, dir, force: false);

        Assert.True(registry.CommandErrors.ContainsKey(key));
        Assert.Equal(seeded, registry.Commands[key]); // the stale list survives alongside the error
    }

    // ---- PR-PROVREG-215: the command error map is cleared wholesale once it reaches thirty-two entries ------

    [Fact]
    [Trait("Req", "PR-PROVREG-215")] // ProviderRegistry.swift:776-779
    public async Task the_command_error_map_is_cleared_wholesale_once_it_reaches_thirty_two_entries()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        var dir = TempDir();
        settings.Paths[ProviderKind.Codex] = WriteExecutable(dir, "codex.exe");
        var registry = MakeRegistry(runtime, settings);

        for (var i = 0; i < 32; i++)
        {
            await registry.LoadCommands(ProviderKind.Codex, Path.Combine(dir, "d" + i).Replace('\\', '/'), force: true);
        }
        Assert.Equal(32, registry.CommandErrors.Count);
        var firstKey = ProviderRegistry.CommandKey(ProviderKind.Codex, Path.Combine(dir, "d0").Replace('\\', '/'));
        Assert.True(registry.CommandErrors.ContainsKey(firstKey));

        // The 33rd failure clears every previous entry wholesale before storing only the new one -- an
        // eviction of one, like UpdateCommands' command cache, would leave 32 entries; this leaves exactly 1.
        var freshDir = Path.Combine(dir, "fresh").Replace('\\', '/');
        await registry.LoadCommands(ProviderKind.Codex, freshDir, force: true);

        Assert.Single(registry.CommandErrors);
        Assert.False(registry.CommandErrors.ContainsKey(firstKey));
        var freshKey = ProviderRegistry.CommandKey(ProviderKind.Codex, freshDir);
        Assert.True(registry.CommandErrors.ContainsKey(freshKey));
    }
}
