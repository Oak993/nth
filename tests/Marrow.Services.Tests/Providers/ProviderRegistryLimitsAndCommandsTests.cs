using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e).
//
// Tests for unit Services.Providers.ProviderRegistry part 4 of 5: RefreshPlanLimits (PR-PROVREG-163 to
// 170), ConsumeResetCredit (PR-PROVREG-193 to 196), RefreshCredits/InvalidateCredits/InvalidatePlanLimits
// (PR-PROVREG-198 to 202, 205, 206), UpdateCatalog's citation-sharing records (PR-PROVREG-154 to 156,
// proven where UpdateCatalog/LoadClaudeCatalog/LoadAPICatalog already have coverage: see
// ProviderRegistryCatalogTests.cs's PR-PROVREG-149 to 153 tests, which exercise the same call sites this
// part's records describe; no new test is added here for an already-covered site) and CommandKey/
// UpdateCommands (PR-PROVREG-213, 214). A fourth class in the file this unit's tests mirror (R-TEST-3):
// parts 1 to 3 live in ProviderRegistryTests.cs, ProviderRegistryRefreshTests.cs and
// ProviderRegistryCatalogTests.cs.
//
// UNTESTABLE IN THIS PART (section 6: reported, not invented):
//  - PR-PROVREG-198 to 202, 205: RefreshCredits and InvalidateCredits stay MARROW_STUB. Their sole
//    dependency, CreditsReader (ProviderCredits.cs, unit Services.Providers.ProviderCredits, a sibling in
//    this file's dependency cycle), has ExposesCredits, Read and TopUpURL all left as unconditional
//    NotImplementedException throws at the time of this unit's work. RefreshCredits' very first statement
//    calls ExposesCredits, so no branch for any provider can be exercised, or even reached, without a real
//    implementation there; this matches the file's own precedent of leaving a member unimplemented when
//    its sole callee is another unit's stub (DeepseekSeed/ZaiSeed/CommandcodeSeed, ProviderRegistry.cs
//    :140-179), rather than shipping code that can never observably pass. See the STOP report in this
//    unit's final report.
//  - PR-PROVREG-164 (the queued read actually re-running once the first completes), 165 (served from an
//    actual prior success), 167, 168 (a read that outlives or survives past its 25s watchdog), 170 (a
//    later nil read leaving a real prior success untouched), the re-read half of 193 and 194: each needs a
//    plan-limits read that actually SUCCEEDS to set up its precondition or to time against. Every CLI-based
//    ExposesLimits provider's session reader (CodexSession.ReadPlanLimits, AntigravitySession.ReadPlanLimits,
//    CopilotSession.ReadPlanLimits, CommandCodeAPI.PlanLimits, PiPlanLimits.Read) is itself MARROW_STUB
//    (unconditional throw), and letting one of those throws escape a MainActor.Spawn(throwing: false) task
//    would end the process per R-TASK-1/ED-078, not just fail one test; Claude's path (PlanLimitsReader.
//    ReadClaude) is implemented but reads real Claude Code login files, which is forbidden (never read a
//    credential file) and has no override reachable through ProviderRegistry's public API. Z.ai's API-key
//    path (PlanLimitsReader.Read(provider, apiKey) -> ZaiAPI.PlanLimits) IS fully implemented but has no URL
//    override at ProviderRegistry's call site (matching the Swift, which has none either), so exercising it
//    would reach the real internet, forbidden by R-TEST-10 (same rationale ProviderRegistryRefreshTests.cs
//    already records for PR-PROVREG-126 to 129). The failure-only halves of 164, 165, 169, 170, 193, 194,
//    195, 196 that a guard reaches before any reader call ARE tested below.
//  - PR-PROVREG-206: InvalidatePlanLimits' guard logic is exercised for both eligible and ineligible
//    providers below, but proving it actually clears a REAL fetch timestamp needs a prior successful read
//    (see above) and the timestamp is private state with no public reader; untestable for the same reason.
public sealed class ProviderRegistryLimitsAndCommandsTests
{
    private static string TempDir()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-limits", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FakeProviderSettings Settings() => new();

    // The constructor seeds Deepseek/Zai/Commandcode from DeepSeekAPI.Option/ZaiAPI.Option/CommandCodeAPI.Seed
    // (ProviderRegistry.cs :140-179) only when the persisted catalog for that provider is empty; those three
    // statics are MARROW_STUB (missing dependencies owned by other units), so every registry in this part's
    // tests is built over a pre-seeded cache blob, the same technique ProviderRegistryCatalogTests.cs uses.
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

    // ---- PR-PROVREG-163: no-limits providers return immediately, no task, no loading flag ------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-163")] // ProviderRegistry.swift:606-607
    public void refresh_plan_limits_returns_immediately_for_every_provider_that_exposes_no_limits()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());
        // The seven no-limits providers as of B2 (PlanLimits.swift; PlanLimitsReader.ExposesLimits).
        foreach (var provider in new[]
                 {
                     ProviderKind.Cursor, ProviderKind.Opencode, ProviderKind.OpencodeGo, ProviderKind.Grok,
                     ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Devin,
                 })
        {
            registry.RefreshPlanLimits(provider);
            registry.RefreshPlanLimits(provider, force: true);
        }
        Assert.Empty(registry.LoadingLimits);
        Assert.Empty(registry.PlanLimits);
    }

    // ---- PR-PROVREG-166: an API-key provider with an empty key clears cached limits and starts no read -----

    [Fact]
    [Trait("Req", "PR-PROVREG-166")] // ProviderRegistry.swift:610-615
    public void an_api_key_provider_with_no_key_clears_cached_limits_and_starts_no_read()
    {
        using var runtime = new TestRuntime();
        var settings = Settings(); // no key for Zai
        var registry = MakeRegistry(runtime, settings);
        registry.RefreshPlanLimits(ProviderKind.Zai);
        // The guard for an empty key returns before loadingLimits.insert(provider) (:612-615), so no task
        // is ever spawned and the loop needs no pumping.
        Assert.Empty(registry.LoadingLimits);
        Assert.DoesNotContain(ProviderKind.Zai, registry.PlanLimits.Keys);
        registry.RefreshPlanLimits(ProviderKind.Zai, force: true);
        Assert.Empty(registry.LoadingLimits);
    }

    // ---- PR-PROVREG-169: an unresolvable CLI executable silently does nothing (ED-031) -----------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-169")] // ProviderRegistry.swift:632-636
    [Trait("Decision", "ED-031")]
    public void a_cli_limits_read_silently_does_nothing_when_the_executable_cannot_be_resolved()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        // A non-empty custom path that does not resolve: Executable(for:) returns nil with no fallback (:219-226).
        settings.Paths[ProviderKind.Codex] = Path.Combine(TempDir(), "missing-codex.exe");
        var registry = MakeRegistry(runtime, settings);

        registry.RefreshPlanLimits(ProviderKind.Codex);
        Assert.Contains(ProviderKind.Codex, registry.LoadingLimits); // the flag is set synchronously before the task runs
        runtime.Loop.RunUntil(() => registry.LoadingLimits.Count == 0, TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(ProviderKind.Codex, registry.LoadingLimits); // the defer cleared the flag
        Assert.Empty(registry.PlanLimits); // no limits, no message, no crash
    }

    // ---- PR-PROVREG-193: an unresolvable Codex CLI throws codexUnavailable before touching CodexSession -----

    [Fact]
    [Trait("Req", "PR-PROVREG-193")] // ProviderRegistry.swift:653-656
    [Trait("Req", "PR-PROVREG-196")] // ProviderRegistry.swift:650-652
    public async Task consume_reset_credit_for_codex_throws_codex_unavailable_when_the_executable_does_not_resolve_and_clears_redeeming_reset()
    {
        using var runtime = new TestRuntime();
        var settings = Settings();
        settings.Paths[ProviderKind.Codex] = Path.Combine(TempDir(), "missing-codex.exe");
        var registry = MakeRegistry(runtime, settings);

        Assert.Null(registry.RedeemingReset);
        var error = await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(ProviderKind.Codex, "credit-1"));
        Assert.Equal("Codex CLI not found.", error.ErrorDescription); // ResetCreditError.Case.CodexUnavailable
        // The defer (:652) clears redeemingReset even on the throwing path.
        Assert.Null(registry.RedeemingReset);
    }

    // ---- PR-PROVREG-194: Z.ai needs both a non-empty key and a credit id, or unknownOutcome (named Codex) ----

    [Fact]
    [Trait("Req", "PR-PROVREG-194")] // ProviderRegistry.swift:664-666
    public async Task consume_reset_credit_for_zai_throws_unknown_outcome_without_a_key_or_without_a_credit_id()
    {
        using var runtime = new TestRuntime();
        var settings = Settings(); // no key
        var registry = MakeRegistry(runtime, settings);

        var noKey = await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(ProviderKind.Zai, "credit-1"));
        Assert.Equal("Codex returned an unknown reset result.", noKey.ErrorDescription); // named Codex even for Z.ai

        settings.Keys[ProviderKind.Zai] = "zai-key";
        var noCredit = await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(ProviderKind.Zai, null));
        Assert.Equal("Codex returned an unknown reset result.", noCredit.ErrorDescription);
    }

    // ---- PR-PROVREG-195: any other provider throws unknownOutcome, defensively --------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-195")] // ProviderRegistry.swift:673-675
    public async Task consume_reset_credit_for_any_other_provider_throws_unknown_outcome()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());
        foreach (var provider in new[] { ProviderKind.Cursor, ProviderKind.Opencode, ProviderKind.Grok, ProviderKind.Deepseek })
        {
            var error = await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(provider, "some-id"));
            Assert.Equal("Codex returned an unknown reset result.", error.ErrorDescription);
        }
    }

    // ---- PR-PROVREG-196: redeemingReset holds the credit id, or the provider rawValue when nil ----------------

    [Fact]
    [Trait("Req", "PR-PROVREG-196")] // ProviderRegistry.swift:58-60, :650-652
    public async Task redeeming_reset_carries_the_credit_id_or_the_provider_rawvalue_and_is_always_cleared()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());
        // A nil credit id (an unlisted placeholder row) still throws for a non-Codex/Zai provider, but
        // redeemingReset is set to the provider's own rawValue while the call is out (unobservable mid-call
        // here without a hook into the throwing call itself, so this asserts the post-condition: cleared by
        // the defer on every exit, throwing included, for both a named id and a nil one).
        await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(ProviderKind.Cursor, "credit-9"));
        Assert.Null(registry.RedeemingReset);
        await Assert.ThrowsAsync<ResetCreditError>(() => registry.ConsumeResetCredit(ProviderKind.Cursor, null));
        Assert.Null(registry.RedeemingReset);
    }

    // ---- PR-PROVREG-213: command keys are rawValue + U+000A + a normalized directory (ED-033) -----------------

    [Fact]
    [Trait("Req", "PR-PROVREG-213")] // ProviderRegistry.swift:736-738
    [Trait("Decision", "ED-033")]
    public void command_keys_compose_ravlue_newline_directory_and_fold_two_spellings_of_one_directory_together()
    {
        var key = ProviderRegistry.CommandKey(ProviderKind.Claude, "C:\\proj");
        Assert.Equal("claude\nc:/proj", key); // single separator form, invariant lowercase (ED-033)
        // Two different directories for the same provider are distinct entries.
        Assert.NotEqual(
            ProviderRegistry.CommandKey(ProviderKind.Claude, "C:\\proj"),
            ProviderRegistry.CommandKey(ProviderKind.Claude, "C:\\other"));
        // ED-033: two spellings of the same directory (case, trailing separator, slash direction) fold to
        // one identity, so they are one cache entry instead of two.
        Assert.Equal(
            ProviderRegistry.CommandKey(ProviderKind.Claude, "C:\\Proj\\"),
            ProviderRegistry.CommandKey(ProviderKind.Claude, "c:/proj"));
        // Different providers never share a key even for the same directory.
        Assert.NotEqual(
            ProviderRegistry.CommandKey(ProviderKind.Claude, "C:\\proj"),
            ProviderRegistry.CommandKey(ProviderKind.Codex, "C:\\proj"));
    }

    // ---- PR-PROVREG-214: the command cache evicts the single oldest entry at 32, never on an existing key ----

    [Fact]
    [Trait("Req", "PR-PROVREG-214")] // ProviderRegistry.swift:740-751
    public void the_command_cache_evicts_only_the_oldest_entry_once_it_reaches_thirty_two_and_never_on_an_existing_key()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings());
        for (var i = 0; i < 32; i++)
        {
            runtime.Advance(TimeSpan.FromSeconds(1)); // distinct, increasing fetch times
            registry.UpdateCommands(
                new ValueList<SlashCommand>([new SlashCommand { Name = "c" + i, Detail = "" }]),
                ProviderKind.Codex, "C:\\dir" + i);
        }
        Assert.Equal(32, registry.Commands.Count);

        // A NEW key at 32 entries evicts exactly one entry: the one with the oldest fetch time.
        runtime.Advance(TimeSpan.FromSeconds(1));
        registry.UpdateCommands(
            new ValueList<SlashCommand>([new SlashCommand { Name = "new", Detail = "" }]),
            ProviderKind.Codex, "C:\\fresh");
        Assert.Equal(32, registry.Commands.Count);
        var evicted = ProviderRegistry.CommandKey(ProviderKind.Codex, "C:\\dir0");
        var survivor = ProviderRegistry.CommandKey(ProviderKind.Codex, "C:\\dir1");
        var fresh = ProviderRegistry.CommandKey(ProviderKind.Codex, "C:\\fresh");
        Assert.False(registry.Commands.ContainsKey(evicted)); // oldest gone
        Assert.True(registry.Commands.ContainsKey(survivor)); // the rest survive
        Assert.False(registry.CommandErrors.ContainsKey(evicted));
        Assert.True(registry.Commands.ContainsKey(fresh));
        Assert.False(registry.CommandErrors.ContainsKey(fresh)); // its error is cleared

        // Updating an EXISTING key never evicts, however full the cache is.
        var dir5 = ProviderRegistry.CommandKey(ProviderKind.Codex, "C:\\dir5");
        registry.UpdateCommands(
            new ValueList<SlashCommand>([new SlashCommand { Name = "again", Detail = "" }]),
            ProviderKind.Codex, "C:\\dir5");
        Assert.Equal(32, registry.Commands.Count);
        Assert.True(registry.Commands.ContainsKey(survivor));
        Assert.True(registry.Commands.ContainsKey(fresh));
        Assert.Equal("again", registry.Commands[dir5][0].Name);
    }
}
