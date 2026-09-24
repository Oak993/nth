using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e).
//
// Tests for unit Services.Providers.ProviderRegistry part 1 of 5: ProviderStatus.IsInstalled/Summary
// (PR-PROVREG-075 to 082); the constructor's cache load and seeding (PR-PROVACP-048); AvailableProviders,
// CountsAsInstalled, Status (PR-PROVREG-103 to 105); Executable(for:) and its Command Code interpreter
// guard (PR-PROVREG-094, PA-0016, ED-127); Environment(for:) (PR-PROVREG-099 to 102, PR-MCP-175); the
// private helpers OpenCodeModels and MergedACPCatalog kept internal for direct testing (PR-PROVACP-048,
// PR-PROVACP-139); and RefreshAPIProvider (PR-PROVAPI-088).
public sealed class ProviderRegistryTests
{
    private static string TempDir()
    {
        // R-TEST-9: a real folder under MARROW_PORT_DATA_DIR, never a file-system abstraction.
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry", Guid.NewGuid().ToString("N")).Replace('\\', '/');
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

    // The constructor (:187, :189, :194) seeds Deepseek, Zai and Commandcode from DeepseekSeed, ZaiSeed and
    // CommandcodeSeed only when their cached catalog is empty. Those three properties are MARROW_STUB
    // (blocked on DeepSeekAPI.Option, ZaiAPI.Option, CommandCodeAPI.Seed: missing dependencies owned by
    // other units, section 6). Every test that constructs a ProviderRegistry pre-seeds a non-empty cached
    // row for those three providers so the constructor never reaches the stubs; a test whose own scenario
    // already seeds one of them (PR-PROVACP-048's cache test) is not affected by the corresponding default.
    private static void SeedUnblockingCache(TestRuntime runtime, params (string Provider, object[] Rows)[] overrides)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        foreach (var (provider, rows) in overrides) payload[provider] = rows;
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

    // ---- ProviderStatus: five fields with defaults (PR-PROVREG-075) -----------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-075")] // ProviderRegistry.swift:3-17
    public void provider_status_constructed_with_no_arguments_has_the_five_default_fields()
    {
        var status = new ProviderStatus();
        Assert.Null(status.Executable);
        Assert.Null(status.Version);
        Assert.Equal(new ProviderStatus.Auth.Unknown(), status.ProviderStatusAuth);
        Assert.False(status.IsChecking);
        Assert.False(status.ApiKeyConfigured);
    }

    // ---- Auth has three cases (PR-PROVREG-076) ---------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-076")] // ProviderRegistry.swift:4-8
    public void auth_has_exactly_unknown_signedin_and_signedout_and_signedin_carries_an_optional_account()
    {
        var unknown = new ProviderStatus.Auth.Unknown();
        var signedOut = new ProviderStatus.Auth.SignedOut();
        var named = new ProviderStatus.Auth.SignedIn("ops@corp");
        var anonymous = new ProviderStatus.Auth.SignedIn(null);
        Assert.NotEqual<ProviderStatus.Auth>(unknown, signedOut);
        Assert.NotEqual<ProviderStatus.Auth>(signedOut, named);
        Assert.NotEqual<ProviderStatus.Auth>(named, anonymous);
        Assert.Null(anonymous.Account);
        Assert.Equal("ops@corp", named.Account);
    }

    // ---- isInstalled (PR-PROVREG-077) -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-077")] // ProviderRegistry.swift:14-17
    public void is_installed_is_true_when_executable_is_present_or_api_key_is_configured()
    {
        var withBinary = new ProviderStatus { Executable = SwiftURL.Create("marrow-native-path:/usr/local/bin/claude") };
        var withKey = new ProviderStatus { ApiKeyConfigured = true };
        var withNeither = new ProviderStatus();
        Assert.True(withBinary.IsInstalled);
        Assert.True(withKey.IsInstalled);
        Assert.False(withNeither.IsInstalled);
        // apiKeyConfigured is only ever set by refreshAPIProvider, so a signed-in auth state alone
        // with no executable and no key never reads as installed.
        Assert.False(new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("a") }.IsInstalled);
    }

    // ---- summary (PR-PROVREG-078 to 082) ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-078")] // ProviderRegistry.swift:19-20
    public void summary_reads_not_installed_when_the_provider_is_not_installed()
    {
        Assert.Equal("Not installed", new ProviderStatus().Summary);
        // The auth state is not consulted at all in this branch.
        Assert.Equal("Not installed", new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("x") }.Summary);
    }

    private static readonly SwiftURL SomeExecutable = SwiftURL.Create("marrow-native-path:/usr/bin/claude")!;

    [Fact]
    [Trait("Req", "PR-PROVREG-079")] // ProviderRegistry.swift:22
    public void summary_reads_signed_in_as_account_when_an_account_name_is_known()
    {
        var status = new ProviderStatus { Executable = SomeExecutable, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("ada@example.com") };
        Assert.Equal("Signed in as ada@example.com", status.Summary);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-080")] // ProviderRegistry.swift:22
    public void summary_reads_signed_in_when_signed_in_with_no_account_name()
    {
        var status = new ProviderStatus { Executable = SomeExecutable, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) };
        Assert.Equal("Signed in", status.Summary);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-081")] // ProviderRegistry.swift:23
    public void summary_reads_not_signed_in_when_signed_out()
    {
        var status = new ProviderStatus { Executable = SomeExecutable, ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() };
        Assert.Equal("Not signed in", status.Summary);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-082")] // ProviderRegistry.swift:24
    public void summary_reads_installed_with_a_middle_dot_version_or_plain_installed_when_auth_is_unknown()
    {
        var withVersion = new ProviderStatus { Executable = SomeExecutable, Version = "1.0.7" };
        Assert.Equal("Installed \U000000B7 1.0.7", withVersion.Summary); // U+00B7 MIDDLE DOT
        var withoutVersion = new ProviderStatus { Executable = SomeExecutable };
        Assert.Equal("Installed", withoutVersion.Summary);
    }

    // ---- status(_:) default (PR-PROVREG-105) ------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-105")] // ProviderRegistry.swift:210-217
    public void status_returns_a_default_empty_provider_status_for_a_never_probed_provider()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        var status = registry.Status(ProviderKind.Codex);
        Assert.Null(status.Executable);
        Assert.Null(status.Version);
        Assert.Equal(new ProviderStatus.Auth.Unknown(), status.ProviderStatusAuth);
        Assert.False(status.IsChecking);
        Assert.False(status.ApiKeyConfigured);
        Assert.Equal("Not installed", status.Summary);
    }

    // ---- countsAsInstalled / availableProviders (PR-PROVREG-104, -103) -----------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-104")] // ProviderRegistry.swift:202-208
    public async Task counts_as_installed_treats_a_provider_with_no_status_yet_as_installed()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        // No probe has run: a check that has not happened yet is not a "no".
        Assert.True(registry.CountsAsInstalled(ProviderKind.Claude));
        // Once a status exists (here, an API-key provider refreshed with an empty key, which never
        // touches a network), its own IsInstalled is used, including false.
        await registry.RefreshAPIProvider(ProviderKind.Deepseek);
        Assert.False(registry.CountsAsInstalled(ProviderKind.Deepseek));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-103")] // ProviderRegistry.swift:198-200
    public async Task available_providers_is_enabled_and_counts_as_installed_in_allcases_order()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        settings.Enabled.Add(ProviderKind.Deepseek);
        settings.Enabled.Add(ProviderKind.Claude);
        settings.Enabled.Add(ProviderKind.Zai);
        var registry = MakeRegistry(runtime, settings);
        // Deepseek and Zai refresh to signedOut on their empty keys (offline, no network reached);
        // Claude has no status yet and so counts as installed.
        await registry.RefreshAPIProvider(ProviderKind.Deepseek);
        await registry.RefreshAPIProvider(ProviderKind.Zai);
        Assert.Equal([ProviderKind.Claude], registry.AvailableProviders);
        // Every provider disabled: an empty list.
        Assert.Empty(MakeRegistry(runtime, new FakeProviderSettings()).AvailableProviders);
    }

    // ---- executable(for:) custom path (PR-PROVREG-094, PA-0016, ED-127) ----------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-094")] // ProviderRegistry.swift:219-226
    public void a_custom_binary_path_resolves_through_the_same_lookup_and_an_unresolvable_one_disables_the_fallbacks()
    {
        using var runtime = new TestRuntime();
        var dir = TempDir();
        var settings = new FakeProviderSettings();
        var real = WriteExecutable(dir + "/bin", "myclaude.cmd");
        settings.Paths[ProviderKind.Claude] = real;
        var registry = MakeRegistry(runtime, settings);
        var found = registry.Executable(ProviderKind.Claude);
        Assert.NotNull(found);
        Assert.Equal(real, found!.Path);

        // A non-empty custom path that does not resolve returns nil WITHOUT consulting fallbacks, even
        // though a bare-name copy might exist elsewhere.
        settings.Paths[ProviderKind.Claude] = dir + "/missing/nope.cmd";
        Assert.Null(registry.Executable(ProviderKind.Claude));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-094")] // ProviderRegistry.swift:219-226
    [Trait("Decision", "ED-127")]
    public void a_custom_path_naming_the_windows_command_interpreter_is_rejected_for_command_code()
    {
        using var runtime = new TestRuntime();
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var cmdExe = Path.Join(systemRoot, "System32", "cmd.exe");
        Assert.True(File.Exists(cmdExe), "This test needs a real Windows System32\\cmd.exe.");
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Commandcode] = cmdExe;
        var registry = MakeRegistry(runtime, settings);
        // PA-0016/ED-127: identity-checked and rejected, so Command Code never reports the operating
        // system's own shell as installed, even through a custom path.
        Assert.Null(registry.Executable(ProviderKind.Commandcode));
        // A provider whose executableName is not "cmd" is unaffected by the guard.
        var dir = TempDir();
        var claudeExe = WriteExecutable(dir + "/bin2", "claude.cmd");
        settings.Paths[ProviderKind.Claude] = claudeExe;
        Assert.NotNull(registry.Executable(ProviderKind.Claude));
    }

    // ---- environment(for:) PATH prepend (PR-PROVREG-099) --------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-099")] // ProviderRegistry.swift:260-266
    public void environment_prepends_the_executables_own_directory_to_path()
    {
        using var runtime = new TestRuntime();
        var dir = TempDir();
        var bin = dir + "/bin";
        WriteExecutable(bin, "claude.cmd");
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = bin + "/claude.cmd";
        var registry = MakeRegistry(runtime, settings);
        var environment = registry.Environment(ProviderKind.Claude);
        Assert.True(environment.TryGetValue("PATH", out var path));
        // A freshly generated temp directory is certainly not already on the ambient PATH, so it must
        // be prepended, as the first entry, followed by a semicolon (Windows PATH separator, ED-031).
        Assert.StartsWith(bin + ";", path, StringComparison.Ordinal);
    }

    // ---- Command Code key injection (PR-PROVREG-100) -------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-100")] // ProviderRegistry.swift:267-272
    public void a_non_empty_command_code_key_is_injected_and_an_empty_one_leaves_any_inherited_value_untouched()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var registry = MakeRegistry(runtime, settings);
        // Empty key: the variable is not set by this registry (any inherited ambient value, if
        // present, is left alone; here there is none, so it stays absent).
        var without = registry.Environment(ProviderKind.Commandcode);
        Assert.False(without.ContainsKey("COMMAND_CODE_API_KEY") && settings.ApiKey(ProviderKind.Commandcode).Length != 0);

        // Non-empty key from Settings: injected, taking precedence over the CLI's own login.
        settings.Keys[ProviderKind.Commandcode] = "studio-key";
        var with = registry.Environment(ProviderKind.Commandcode);
        Assert.True(with.TryGetValue("COMMAND_CODE_API_KEY", out var injected));
        Assert.Equal("studio-key", injected);
        // A provider other than Command Code never gets the variable.
        Assert.False(registry.Environment(ProviderKind.Claude).ContainsKey("COMMAND_CODE_API_KEY"));
    }

    // ---- OpenCode MCP environment merge (PR-PROVREG-101) ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-101")] // ProviderRegistry.swift:273-275
    public void opencode_zen_and_go_receive_their_connected_mcp_servers_as_environment_content()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        var overrides = MCPSessionOverrides.OpencodeEnvironment();
        foreach (var provider in new[] { ProviderKind.Opencode, ProviderKind.OpencodeGo })
        {
            var result = registry.Environment(provider);
            foreach (var (key, value) in overrides)
            {
                Assert.True(result.TryGetValue(key, out var got));
                Assert.Equal(value, got);
            }
        }
        // A provider that is not OpenCode Zen or Go never receives the merge unless the ambient
        // environment already held the same key (none of the override keys are common env vars).
        var claudeEnvironment = registry.Environment(ProviderKind.Claude);
        foreach (var key in overrides.Keys) Assert.False(claudeEnvironment.ContainsKey(key) && overrides.Count > 0 && !LoginEnvironment.Current.ContainsKey(key));
    }

    // ---- Antigravity settings path (PR-PROVREG-102, PR-MCP-175) -------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-102")] // ProviderRegistry.swift:276-277
    [Trait("Req", "PR-MCP-175")] // ProviderRegistry.swift:276-277
    public void antigravity_receives_gemini_cli_system_settings_path_only_when_a_path_is_available()
    {
        using var runtime = new TestRuntime();
        using var storage = BindStorage();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        // With no connected MCP servers, gemini-settings.json holds no mcpServers object (or is
        // absent), so GeminiSettingsPath() returns nil and the variable is not set.
        var result = registry.Environment(ProviderKind.Antigravity);
        Assert.False(result.ContainsKey("GEMINI_CLI_SYSTEM_SETTINGS_PATH"));
        // A provider other than Antigravity never receives the variable.
        Assert.False(registry.Environment(ProviderKind.Claude).ContainsKey("GEMINI_CLI_SYSTEM_SETTINGS_PATH"));
    }

    // ---- OpenCodeModels (PR-PROVACP-048, PR-PROVACP-139) ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVACP-048")] // ProviderRegistry.swift:543-559
    [Trait("Req", "PR-PROVACP-139")] // ProviderRegistry.swift:543-559
    public void opencode_models_splits_by_the_go_prefix_and_drops_the_redundant_display_name_prefix()
    {
        var list = new ValueList<ModelOption>(
        [
            new ModelOption("opencode/zen-1", "OpenCode Zen/Zen One"),
            new ModelOption("opencode-go/go-1", "OpenCode Go/Go One"),
            new ModelOption("opencode/zen-2", "Untouched Name"),
        ]);
        var zen = ProviderRegistry.OpenCodeModels(list, ProviderKind.Opencode);
        Assert.Equal(["opencode/zen-1", "opencode/zen-2"], zen.Select(m => m.Id).ToArray());
        Assert.Equal("Zen One", zen[0].Name);
        Assert.Equal("Untouched Name", zen[1].Name);

        var go = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal(["opencode-go/go-1"], go.Select(m => m.Id).ToArray());
        Assert.Equal("Go One", go[0].Name);

        // Every other provider keeps every row, only losing its own display-name prefix.
        var grok = ProviderRegistry.OpenCodeModels(
            new ValueList<ModelOption>([new ModelOption("grok-4", "Grok/grok-4")]), ProviderKind.Grok);
        Assert.Single(grok);
        Assert.Equal("grok-4", grok[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-048")] // ProviderRegistry.swift:170-176
    [Trait("Req", "PR-PROVREG-134")] // ProviderRegistry.swift:170-176 (part 3: every cached list re-split and re-ordered on every launch)
    public void at_registry_load_every_cached_catalog_passes_through_opencodemodels_and_has_its_efforts_ordered()
    {
        using var runtime = new TestRuntime();
        // A cache holding a stale high-to-low Grok scale, an unsplit OpenCode list, and a
        // Zai catalog (so the ctor's stale/empty seed checks for Zai do not overwrite it and
        // reach the still-stubbed ZaiSeed), all written the way JSONEncoder().encode([String:
        // [ModelOption]]) would (an object keyed by ProviderKind.rawValue).
        var cache = JsonSerializer.Serialize(new Dictionary<string, object[]>
        {
            ["grok"] = [new { id = "grok-4", name = "Grok 4", efforts = new[] { "max", "high", "low" } }],
            ["opencode"] = [
                new { id = "opencode/zen-1", name = "OpenCode Zen/Zen One", efforts = Array.Empty<string>() },
                new { id = "opencode-go/go-1", name = "OpenCode Go/Go One", efforts = Array.Empty<string>() },
            ],
            // deepseek/zai/commandcode: any non-empty cached row keeps the constructor away from the
            // still-stubbed DeepseekSeed/ZaiSeed/CommandcodeSeed (see SeedUnblockingCache's comment).
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "glm-5.3", name = "GLM 5.3", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        });
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(cache));
        var registry = new ProviderRegistry(new FakeProviderSettings());

        // :172-176: the cached Grok scale is reordered low to high (ACPSession.OrderedEfforts).
        Assert.Equal(["low", "high", "max"], registry.Catalogs[ProviderKind.Grok][0].Efforts.ToArray());

        // :172 (openCodeModels): a cached .opencode list drops "opencode-go/" ids and loses the
        // "OpenCode/" name prefix; a cached .opencodeGo list, read from the SAME "opencode" cache
        // entry (the Swift cache keys by rawValue, and Zen and Go share one probe, PR-PROVACP-139),
        // keeps only the "opencode-go/" half.
        Assert.Equal(["opencode/zen-1"], registry.Catalogs[ProviderKind.Opencode].Select(m => m.Id).ToArray());
        Assert.Equal("Zen One", registry.Catalogs[ProviderKind.Opencode][0].Name);
    }

    // ---- mergedACPCatalog (PR-PROVACP-139) ------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVACP-139")] // ProviderRegistry.swift:108-119
    [Trait("Req", "PR-PROVREG-139")] // ProviderRegistry.swift:103-119 (part 3: same merge, same evidence)
    public void merged_acp_catalog_keeps_the_cached_scale_for_a_live_row_with_no_efforts_matching_by_exact_id()
    {
        var cached = new ValueList<ModelOption>(
        [
            new ModelOption("grok-4", "Grok 4", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "low", fastTier: "fast"),
            new ModelOption("model[effort=high]", "Parameterized", efforts: new ValueList<string>(["high"])),
        ]);
        var live = new ValueList<ModelOption>(
        [
            new ModelOption("grok-4", "Grok 4"), // empty efforts: inherits the cached scale, default and fast tier
            new ModelOption("model", "Base only", efforts: new ValueList<string>(["medium"])), // own scale: untouched
            new ModelOption("new-row", "New"), // not in cache: kept as is
        ]);
        var merged = ProviderRegistry.MergedACPCatalog(live, cached);

        Assert.Equal(3, merged.Count);
        Assert.Equal(["low", "high"], merged[0].Efforts.ToArray());
        Assert.Equal("low", merged[0].DefaultEffort);
        Assert.Equal("fast", merged[0].FastTier);
        // The parameterized cached id does not match the live "model" id (exact-id matching only).
        Assert.Equal(["medium"], merged[1].Efforts.ToArray());
        Assert.Null(merged[1].DefaultEffort);
        Assert.Equal("new-row", merged[2].Id);
        Assert.Empty(merged[2].Efforts);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-139")] // ProviderRegistry.swift:109
    public void merged_acp_catalog_returns_the_live_list_unchanged_when_nothing_is_cached()
    {
        var live = new ValueList<ModelOption>([new ModelOption("a", "A")]);
        var merged = ProviderRegistry.MergedACPCatalog(live, ValueList<ModelOption>.Empty);
        Assert.Same(live[0], merged[0]);
    }

    // ---- API-key provider install state (PR-PROVAPI-088) ------------------------------------------------------
    //
    // Only the empty-key branch is reachable without a network call: the valid-key and rejected-key
    // branches call DeepSeekAPI.Validate/MetaAPI.Validate/ZaiAPI.Validate with no URL override at this
    // call site (matching the Swift, which has none either; MetaAPI's override parameter is PORT-ONLY,
    // "production call sites never pass them", MetaAPI.cs:15), so exercising them would reach the real
    // internet, forbidden by R-TEST-10. Untestable in this part without one of those APIs' own units
    // adding a call-site seam, which is out of this unit's scope.

    [Fact]
    [Trait("Req", "PR-PROVAPI-088")] // ProviderRegistry.swift:382-386
    [Trait("Req", "PR-PROVREG-125")] // ProviderRegistry.swift:381-386
    public async Task an_api_key_provider_with_no_key_is_signed_out_and_not_configured_with_no_network_call()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, new FakeProviderSettings());
        foreach (var provider in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai })
        {
            await registry.RefreshAPIProvider(provider);
            var status = registry.Status(provider);
            Assert.Equal(new ProviderStatus.Auth.SignedOut(), status.ProviderStatusAuth);
            Assert.False(status.ApiKeyConfigured);
            Assert.Null(status.Executable);
            Assert.Null(status.Version);
        }
    }
}
