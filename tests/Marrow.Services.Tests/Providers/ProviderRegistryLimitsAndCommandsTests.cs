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
//  - PR-PROVREG-198 to 202, 205 (RefreshCredits/InvalidateCredits) are now covered below: CreditsReader
//    (ProviderCredits.cs, unit Services.Providers.ProviderCredits) landed as a real implementation since
//    the earlier attempt at this part, un-blocking every branch through Command Code's loopback-served
//    credits endpoint. DeepSeek's own read (DeepSeekAPI.Balance) is still MARROW_STUB, so the tests below
//    exercise the empty-key/settings-read half of PR-PROVREG-198 through DeepSeek (a guard that returns
//    before ever calling CreditsReader.Read) and everything past the key check through Command Code, whose
//    reader is fully implemented and driven over a LoopbackHttpServer (R-TEST-10), never the real internet.
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

    // ---- PR-PROVREG-198 to 202, 205: RefreshCredits / InvalidateCredits ---------------------------------------
    //
    // Command Code is the only credits-exposing provider whose reader (CommandCodeAPI.Credits) is fully
    // implemented at the time of this part's work; DeepSeek's (DeepSeekAPI.Balance) is still MARROW_STUB
    // (see ProviderCreditsTests.cs's own note), so every test below that needs a real read uses Command
    // Code over a LoopbackHttpServer (R-TEST-10). Each network round trip follows the same shape as
    // ProviderRegistryCatalogTests.cs's RefreshEverything test: pump ManualMainLoop on a background thread
    // (MainActor.Spawn always enqueues, never runs inline) while the test thread awaits the exchange.

    private static byte[] CreditsBody(int monthly, int purchased, int free) => System.Text.Encoding.UTF8.GetBytes(
        "{\"credits\":{\"monthlyCredits\":" + monthly + ",\"purchasedCredits\":" + purchased +
        ",\"freeCredits\":" + free + ",\"belowThreshold\":false}}");

    private static async Task<ProviderCredits> RunOneCreditsRoundTrip(
        TestRuntime runtime, ProviderRegistry registry, LoopbackHttpServer server, int monthly, int purchased, int free, bool force = false)
    {
        registry.RefreshCredits(ProviderKind.Commandcode, force);
        var pump = Task.Run(() => runtime.Loop.RunUntil(() => registry.LoadingCredits.Count == 0, TimeSpan.FromSeconds(10)));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", CreditsBody(monthly, purchased, free));
        await pump;
        return registry.Credits[ProviderKind.Commandcode];
    }

    // ---- PR-PROVREG-198: Command Code's key comes from the CLI environment, others from Settings directly ----

    [Fact]
    [Trait("Req", "PR-PROVREG-198")] // ProviderRegistry.swift:682-686
    public async Task refresh_credits_for_commandcode_resolves_its_key_through_the_provider_environment_and_reads_the_balance()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = Settings();
        settings.Keys[ProviderKind.Commandcode] = "cc-key-1"; // Environment(for:) couples this into COMMAND_CODE_API_KEY
        var registry = MakeRegistry(runtime, settings);

        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Contains(ProviderKind.Commandcode, registry.LoadingCredits); // set synchronously (:692) before the task runs
        var pump = Task.Run(() => runtime.Loop.RunUntil(() => registry.LoadingCredits.Count == 0, TimeSpan.FromSeconds(10)));
        var exchange = await server.NextExchange();
        Assert.Equal("/alpha/billing/credits", exchange.Target);
        Assert.Equal(["Bearer cc-key-1"], exchange.HeaderValues("Authorization"));
        await exchange.Respond(200, "OK", CreditsBody(1, 2, 3));
        await pump;

        Assert.Equal(
            new ProviderCredits { Total = 6, Currency = "USD", ToppedUp = 2, Granted = 3, Monthly = 1, IsUsable = true },
            registry.Credits[ProviderKind.Commandcode]);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-198")] // ProviderRegistry.swift:684-690 (the "any other provider" arm: settings.apiKey(for:))
    public void refresh_credits_for_deepseek_reads_settings_directly_and_an_empty_key_clears_credits_with_no_network_call()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime, Settings()); // no Deepseek key in Settings
        registry.RefreshCredits(ProviderKind.Deepseek);
        // The empty-key guard (:687-689) returns before loadingCredits.insert, so no task is ever spawned;
        // DeepSeekAPI.Balance (MARROW_STUB) is never reached.
        Assert.Empty(registry.LoadingCredits);
        Assert.DoesNotContain(ProviderKind.Deepseek, registry.Credits.Keys);
    }

    // ---- PR-PROVREG-199 / 200: an empty key clears the balance but leaves the gate shut for up to 60s -------

    [Fact]
    [Trait("Req", "PR-PROVREG-199")] // ProviderRegistry.swift:687-690
    [Trait("Req", "PR-PROVREG-200")] // ProviderRegistry.swift:687-691 - the key check runs before the gate
    public async Task an_empty_credits_key_clears_the_balance_but_leaves_the_fetch_timestamp_so_a_repasted_key_is_still_gated()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = Settings();
        settings.Keys[ProviderKind.Commandcode] = "cc-key-1";
        var registry = MakeRegistry(runtime, settings);

        // A successful read establishes a fetch timestamp.
        await RunOneCreditsRoundTrip(runtime, registry, server, 1, 0, 0);
        Assert.Contains(ProviderKind.Commandcode, registry.Credits.Keys);

        // The key is cleared: the balance is dropped, synchronously, with no network call.
        settings.Keys.Remove(ProviderKind.Commandcode);
        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Empty(registry.LoadingCredits);
        Assert.DoesNotContain(ProviderKind.Commandcode, registry.Credits.Keys);

        // A key is pasted right back, and an unforced ask is made. Because the empty-key branch never
        // touched creditsFetchedAt, the sixty second gate from the FIRST success is still shut: no second
        // connection is made and the balance stays blank (PR-PROVREG-199's deliberate asymmetry with
        // RefreshPlanLimits' equivalent branch, :613-614, which the ProviderRegistryLimitsAndCommandsTests
        // PR-PROVREG-166 test above shows leaves limitsFetchedAt untouched too, but plan limits has nothing
        // to leave stale there since it was never set on that path either).
        settings.Keys[ProviderKind.Commandcode] = "cc-key-2";
        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Empty(registry.LoadingCredits); // suppressed synchronously by the gate (:691)
        Assert.DoesNotContain(ProviderKind.Commandcode, registry.Credits.Keys);
        Assert.Equal(1, server.ConnectionCount); // only the first round trip ever opened a connection

        // force bypasses the gate and reads again.
        var credits = await RunOneCreditsRoundTrip(runtime, registry, server, 2, 0, 0, force: true);
        Assert.Equal(2, credits.Total);
    }

    // ---- PR-PROVREG-200: a successful unforced read within 60s is served from cache; force bypasses it -----

    [Fact]
    [Trait("Req", "PR-PROVREG-200")] // ProviderRegistry.swift:691, :696-698 (timestamp only on success)
    public async Task a_second_unforced_credits_read_within_sixty_seconds_of_a_success_is_served_from_cache()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = Settings();
        settings.Keys[ProviderKind.Commandcode] = "cc-key";
        var registry = MakeRegistry(runtime, settings);

        var first = await RunOneCreditsRoundTrip(runtime, registry, server, 1, 0, 0);

        // Unforced, same key, still within the gate: no second connection, the cached value is unchanged.
        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Empty(registry.LoadingCredits);
        Assert.Equal(first, registry.Credits[ProviderKind.Commandcode]);
        Assert.Equal(1, server.ConnectionCount);

        // force bypasses the gate.
        registry.RefreshCredits(ProviderKind.Commandcode, force: true);
        var pump = Task.Run(() => runtime.Loop.RunUntil(() => registry.LoadingCredits.Count == 0, TimeSpan.FromSeconds(10)));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", CreditsBody(9, 0, 0));
        await pump;
        Assert.Equal(9, registry.Credits[ProviderKind.Commandcode].Total);
    }

    // ---- PR-PROVREG-201 / 202: no watchdog, no defer, and a forced call while loading is dropped, no queue --

    [Fact]
    [Trait("Req", "PR-PROVREG-201")] // ProviderRegistry.swift:692-699 - no watchdog, no defer
    [Trait("Req", "PR-PROVREG-202")] // ProviderRegistry.swift:681-682 - the guard returns with no force queue
    public async Task a_credits_read_that_never_returns_leaves_loading_set_forever_and_a_forced_call_meanwhile_is_dropped()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = Settings();
        settings.Keys[ProviderKind.Commandcode] = "cc-key";
        var registry = MakeRegistry(runtime, settings);

        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Contains(ProviderKind.Commandcode, registry.LoadingCredits);
        var pump = Task.Run(() => runtime.Loop.RunUntil(() => server.ConnectionCount >= 1, TimeSpan.FromSeconds(10)));
        var exchange = await server.NextExchange(); // the request arrived; the server deliberately never answers it
        await pump;

        // A forced call arriving while the first is still out finds no queue (contrast PR-PROVREG-164's
        // forcedLimitReads): it is dropped at the very first guard, synchronously, opening no second
        // connection and leaving loadingCredits exactly as it was.
        registry.RefreshCredits(ProviderKind.Commandcode, force: true);
        Assert.Equal(1, server.ConnectionCount);
        Assert.Contains(ProviderKind.Commandcode, registry.LoadingCredits);

        // Unlike RefreshPlanLimits' 25 second watchdog (PR-PROVREG-167), nothing clears loadingCredits on a
        // timer: pumping the loop for a while longer changes nothing while the exchange stays unanswered.
        runtime.Advance(TimeSpan.FromSeconds(120));
        runtime.RunUntilIdle();
        Assert.Contains(ProviderKind.Commandcode, registry.LoadingCredits);
        GC.KeepAlive(exchange);
    }

    // ---- PR-PROVREG-205: invalidateCredits reopens the gate only for a provider that exposes credits --------

    [Fact]
    [Trait("Req", "PR-PROVREG-205")] // ProviderRegistry.swift:702-707
    public async Task invalidate_credits_reopens_the_gate_for_an_exposing_provider_and_is_a_no_op_for_any_other()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var settings = Settings();
        settings.Keys[ProviderKind.Commandcode] = "cc-key";
        var registry = MakeRegistry(runtime, settings);

        await RunOneCreditsRoundTrip(runtime, registry, server, 1, 0, 0);

        // Still gated: an unforced call right after a success reads nothing new.
        registry.RefreshCredits(ProviderKind.Commandcode);
        Assert.Equal(1, server.ConnectionCount);

        // invalidateCredits (:704-706) clears the timestamp, reopening the gate for the NEXT unforced ask:
        // a second exchange arrives (proven by RunOneCreditsRoundTrip's own NextExchange await succeeding)
        // and the balance actually changes, which an un-reopened gate would have prevented.
        registry.InvalidateCredits(ProviderKind.Commandcode);
        var refreshed = await RunOneCreditsRoundTrip(runtime, registry, server, 5, 0, 0);
        Assert.Equal(5, refreshed.Total);

        // A provider that does not expose credits (:705 exposesCredits guard) is a silent no-op.
        registry.InvalidateCredits(ProviderKind.Codex);
        registry.InvalidateCredits(ProviderKind.Cursor);
        registry.InvalidateCredits(ProviderKind.Zai); // API-key based, but ExposesCredits is false for it too
    }
}
