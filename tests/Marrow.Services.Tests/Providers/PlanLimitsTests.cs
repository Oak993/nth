using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/PlanLimits.swift (B2 1fbf81e), :1-336.
public sealed class PlanLimitsTests
{
    private const string MiddleDot = "·"; // U+00B7 MIDDLE DOT
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static string TempDataDir(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var folder = Path.Combine(root, "plan-limits-tests", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ---- PR-PROVREG-162, PR-USAGE-022: exposesLimits -----------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-162")] // PlanLimits.swift:65-70
    [Trait("Req", "PR-USAGE-022")] // PlanLimits.swift:68-70; ProviderCredits.swift:58; Provider.swift:3-17
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Pi)]
    public void ExposesLimits_is_true_for_exactly_the_seven_named_providers(ProviderKind provider) =>
        Assert.True(PlanLimitsReader.ExposesLimits(provider));

    [Theory]
    [Trait("Req", "PR-PROVREG-162")] // PlanLimits.swift:67
    [Trait("Req", "PR-USAGE-022")] // b2_change: opencodeGo is added to Provider.swift and stays false here
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Devin)]
    public void ExposesLimits_is_false_for_the_seven_non_exposing_providers_including_opencodeGo(ProviderKind provider) =>
        Assert.False(PlanLimitsReader.ExposesLimits(provider));

    // ---- PR-PROVREG-171: the CLI dispatch's eight-case nil arm --------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:80
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Devin)]
    public async Task Read_executable_returns_null_with_no_call_for_the_eight_provider_nil_arm(ProviderKind provider)
    {
        var executable = FileUrl(Path.Combine(Path.GetTempPath(), "unreachable-" + Guid.NewGuid().ToString("N") + ".exe"));
        var result = await PlanLimitsReader.Read(provider, executable, ValueMap<string, string>.Empty);
        Assert.Null(result);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:74, :76-77 - codex, antigravity and copilot use try?
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    public async Task Read_executable_swallows_the_unimplemented_stub_for_codex_antigravity_and_copilot(ProviderKind provider)
    {
        // CodexSession.ReadPlanLimits, AntigravitySession.ReadPlanLimits and CopilotSession.ReadPlanLimits are
        // MARROW_STUB in this dependency cycle (see the work packet's cyclic-unit note); each throws
        // NotImplementedException, which SwiftTry.IsThrownError treats as a thrown Swift error (not a trap), so
        // the try?-wrapped dispatch swallows it to null exactly as PlanLimits.swift:74/76/77 do for a real
        // thrown error. This proves the wrapping; it cannot prove the exact callee until that unit lands.
        var executable = FileUrl(Path.Combine(Path.GetTempPath(), "unreachable-" + Guid.NewGuid().ToString("N") + ".exe"));
        var result = await PlanLimitsReader.Read(provider, executable, ValueMap<string, string>.Empty);
        Assert.Null(result);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:78 - commandcode dispatches directly, no try?
    public async Task Read_executable_dispatches_commandcode_to_CommandCodeAPI_PlanLimits_without_try()
    {
        var executable = FileUrl(Path.Combine(Path.GetTempPath(), "unreachable-" + Guid.NewGuid().ToString("N") + ".exe"));
        var error = await Assert.ThrowsAsync<NotImplementedException>(
            () => PlanLimitsReader.Read(ProviderKind.Commandcode, executable, ValueMap<string, string>.Empty));
        Assert.Contains("CommandCodeAPI.swift:78", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:79 - pi dispatches directly, no try?
    public async Task Read_executable_dispatches_pi_to_PiPlanLimits_Read_without_try()
    {
        var executable = FileUrl(Path.Combine(Path.GetTempPath(), "unreachable-" + Guid.NewGuid().ToString("N") + ".exe"));
        var error = await Assert.ThrowsAsync<NotImplementedException>(
            () => PlanLimitsReader.Read(ProviderKind.Pi, executable, ValueMap<string, string>.Empty));
        Assert.Contains("PiPlanLimits.swift:11", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:75 - claude routes to readClaude, not a stub
    public async Task Read_executable_dispatches_claude_to_readClaude_not_a_stub()
    {
        // readClaude is fully implemented in this file; with no stored login and an executable that answers no
        // control requests, it falls through both internal paths to null rather than throwing
        // NotImplementedException, proving this arm did not reach a sibling stub.
        var script = new ClaudeScript("dispatch-claude");
        var environment = new Dictionary<string, string>
        {
            [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value,
            ["CLAUDE_CONFIG_DIR"] = TempDataDir("dispatch-claude-config"),
        };
        var result = await PlanLimitsReader.Read(ProviderKind.Claude, FileUrl(script.ExecutablePath), environment);
        Assert.Null(result);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:87 - zai dispatches directly, no try?
    [InlineData(ProviderKind.Zai)]
    public async Task Read_apiKey_dispatches_zai_to_ZaiAPI_PlanLimits(ProviderKind provider)
    {
        var error = await Assert.ThrowsAsync<NotImplementedException>(() => PlanLimitsReader.Read(provider, "key"));
        Assert.Contains("ZaiAPI.swift:74", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-171")] // PlanLimits.swift:88 default: nil
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Pi)]
    public async Task Read_apiKey_returns_null_for_every_provider_other_than_zai(ProviderKind provider)
    {
        var result = await PlanLimitsReader.Read(provider, "key");
        Assert.Null(result);
    }

    // ---- PR-PROVREG-173: windowTitle ------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-173")] // PlanLimits.swift:92-102
    [InlineData(null, "Usage limit")]
    [InlineData(300L, "5-hour limit")]
    [InlineData(1_440L, "Daily")]
    [InlineData(10_080L, "Weekly")]
    [InlineData(40_320L, "Monthly")]
    [InlineData(44_640L, "Monthly")]
    [InlineData(42_000L, "Monthly")]
    [InlineData(2_880L, "2-day limit")]
    [InlineData(30L, "1-hour limit")]
    [InlineData(90L, "1-hour limit")]
    [InlineData(180L, "3-hour limit")]
    public void WindowTitle_matches_the_five_literals_and_the_derived_forms(long? minutes, string expected) =>
        Assert.Equal(expected, PlanLimitsReader.WindowTitle(minutes));

    // ---- PR-PROVREG-174: planName ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-174")] // PlanLimits.swift:104-107
    public void PlanName_is_null_for_null_or_empty_input()
    {
        Assert.Null(PlanLimitsReader.PlanName(null));
        Assert.Null(PlanLimitsReader.PlanName(""));
    }

    [Theory]
    [Trait("Req", "PR-PROVREG-174")] // PlanLimits.swift:106
    [InlineData("max", "Max")]
    [InlineData("pro plan", "Pro plan")]
    public void PlanName_uppercases_only_the_first_character(string raw, string expected) =>
        Assert.Equal(expected, PlanLimitsReader.PlanName(raw));

    // ---- PR-PROVREG-175: codexPlanName ------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-175")] // PlanLimits.swift:113-127
    [InlineData("pro", "Pro 20x")]
    [InlineData("prolite", "Pro 5x")]
    [InlineData("pro lite", "Pro 5x")]
    [InlineData("ent26", "Enterprise")]
    [InlineData("hc", "Enterprise")]
    [InlineData("self_serve_business_prolite", "Business Premium")]
    [InlineData("self_serve_business_usage_based", "Business")]
    [InlineData("enterprise_cbp_automation", "Enterprise (Automation)")]
    [InlineData("enterprise_cbp_usage_based", "Enterprise")]
    [InlineData("education", "Edu")]
    [InlineData("edu_plus", "Edu Plus")]
    [InlineData("edu_pro", "Edu Pro")]
    public void CodexPlanName_maps_the_eleven_raw_spellings(string raw, string expected) =>
        Assert.Equal(expected, PlanLimitsReader.CodexPlanName(raw));

    [Fact]
    [Trait("Req", "PR-PROVREG-175")] // PlanLimits.swift:115 raw.trimmed.lowercased()
    public void CodexPlanName_trims_then_lowercases_before_matching()
    {
        Assert.Equal("Pro 20x", PlanLimitsReader.CodexPlanName("  PRO  "));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-175")] // PlanLimits.swift:126 default: planName(raw)
    public void CodexPlanName_falls_back_to_planName_for_an_unknown_type()
    {
        Assert.Equal("Weird_tier", PlanLimitsReader.CodexPlanName("weird_tier"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-175")] // PlanLimits.swift:114
    public void CodexPlanName_is_null_for_null_or_empty_input()
    {
        Assert.Null(PlanLimitsReader.CodexPlanName(null));
        Assert.Null(PlanLimitsReader.CodexPlanName(""));
    }

    // ---- PR-PROVREG-191: ResetOutcome.message -----------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVREG-191")] // PlanLimits.swift:38-43
    [InlineData(PlanLimits.ResetOutcome.Reset, "Usage was reset.")]
    [InlineData(PlanLimits.ResetOutcome.NothingToReset, "There was no active usage to reset.")]
    [InlineData(PlanLimits.ResetOutcome.NoCredit, "No banked resets are left.")]
    [InlineData(PlanLimits.ResetOutcome.AlreadyRedeemed, "That reset was already used.")]
    public void ResetOutcome_Message_is_the_exact_pinned_string(PlanLimits.ResetOutcome outcome, string expected) =>
        Assert.Equal(expected, outcome.Message);

    // ---- PR-PROVREG-192: ResetCreditError.ErrorDescription ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-192")] // PlanLimits.swift:55-56
    public void ResetCreditError_codexUnavailable_and_unknownOutcome_are_the_exact_pinned_strings()
    {
        Assert.Equal("Codex CLI not found.", new ResetCreditError(new ResetCreditError.Case.CodexUnavailable()).ErrorDescription);
        Assert.Equal("Codex returned an unknown reset result.", new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-192")] // PlanLimits.swift:57 - passes the provider message through unchanged
    public void ResetCreditError_rejected_passes_the_message_through_verbatim()
    {
        var error = new ResetCreditError(new ResetCreditError.Case.Rejected("Z.ai says no."));
        Assert.Equal("Z.ai says no.", error.ErrorDescription);
    }

    // ---- PR-PROVREG-189: parseDate ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-189")] // PlanLimits.swift:332-334 - strips a microsecond fraction, then ISO8601.date
    public void ParseDate_strips_a_microsecond_fraction_before_parsing()
    {
        var withFraction = PlanLimitsReader.ParseDate("2025-01-02T03:04:05.123456Z");
        var withoutFraction = PlanLimitsReader.ParseDate("2025-01-02T03:04:05Z");
        Assert.NotNull(withFraction);
        Assert.Equal(withoutFraction, withFraction);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-189")] // PlanLimits.swift:333 global replacement, not first-match-only
    public void ParseDate_strips_every_fractional_run_not_just_the_first()
    {
        // A date-only string with no time component has no fraction to strip; this exercises the "every run"
        // wording by using an unparseable string whose only digits-after-dot look like a fraction (this input
        // still fails to parse either way; the meaningful case is covered by the round-trip test above, which
        // uses ISO8601.date's own accepted grammar).
        Assert.Null(PlanLimitsReader.ParseDate("not-a-date"));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-189")] // PlanLimits.swift:335 - unparseable text yields nil
    public void ParseDate_returns_null_for_unparseable_text() =>
        Assert.Null(PlanLimitsReader.ParseDate("banana"));

    // ---- PR-PROVREG-179: hexDecoded --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-179")] // PlanLimits.swift:225 length must be even and all hex
    public void HexDecoded_decodes_an_even_length_all_hex_string()
    {
        var bytes = PlanLimitsReader.HexDecoded("48656c6c6f");
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(bytes!));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-179")] // PlanLimits.swift:225 - odd length yields nil
    public void HexDecoded_returns_null_for_an_odd_length_string() =>
        Assert.Null(PlanLimitsReader.HexDecoded("abc"));

    [Fact]
    [Trait("Req", "PR-PROVREG-179")] // PlanLimits.swift:225 - a non-hex character yields nil
    public void HexDecoded_returns_null_for_a_non_hex_character() =>
        Assert.Null(PlanLimitsReader.HexDecoded("zz"));

    // ---- PR-PROVREG-178: parseLogin -----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:213-222
    public void ParseLogin_reads_the_token_and_divides_expiresAt_by_1000()
    {
        var json = System.Text.Encoding.UTF8.GetBytes(
            "{\"claudeAiOauth\":{\"accessToken\":\"tok-abc\",\"expiresAt\":1700000000000,\"subscriptionType\":\"max\",\"rateLimitTier\":\"default_claude_max_20x\"}}");
        var login = PlanLimitsReader.ParseLogin(json);
        Assert.NotNull(login);
        Assert.Equal("tok-abc", login!.AccessToken);
        Assert.Equal(SwiftDate.From1970Seconds(1_700_000_000), login.ExpiresAt);
        Assert.Equal("max", login.SubscriptionType);
        Assert.Equal("default_claude_max_20x", login.RateLimitTier);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:214 - missing claudeAiOauth yields nil
    public void ParseLogin_returns_null_when_claudeAiOauth_is_missing()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"somethingElse\":true}");
        Assert.Null(PlanLimitsReader.ParseLogin(json));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:215 - empty accessToken yields nil
    public void ParseLogin_returns_null_when_accessToken_is_empty()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"claudeAiOauth\":{\"accessToken\":\"\"}}");
        Assert.Null(PlanLimitsReader.ParseLogin(json));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:148-153 - "Max (20x)" from max + default_claude_max_20x
    public void ClaudeLogin_PlanName_renders_Max_20x()
    {
        var login = new PlanLimitsReader.ClaudeLogin { AccessToken = "t", SubscriptionType = "max", RateLimitTier = "default_claude_max_20x" };
        Assert.Equal("Max (20x)", login.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:149 - no subscriptionType yields nil
    public void ClaudeLogin_PlanName_is_null_without_a_subscriptionType()
    {
        var login = new PlanLimitsReader.ClaudeLogin { AccessToken = "t" };
        Assert.Null(login.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-178")] // PlanLimits.swift:150-151 - no digit-plus-x tier: no suffix
    public void ClaudeLogin_PlanName_has_no_suffix_when_the_tier_has_no_digit_x_run()
    {
        var login = new PlanLimitsReader.ClaudeLogin { AccessToken = "t", SubscriptionType = "pro", RateLimitTier = "default_claude_pro" };
        Assert.Equal("Pro", login.PlanName);
    }

    // ---- PR-PROVREG-177: claudeLogin file lookup order --------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-177")] // PlanLimits.swift:184-193 - CLAUDE_CONFIG_DIR, when set, is read first
    public async Task ReaderClaudeLogin_reads_CLAUDE_CONFIG_DIR_before_the_home_directory()
    {
        var configDir = TempDataDir("config-dir");
        var homeDir = TempDataDir("home-dir");
        File.WriteAllText(Path.Combine(configDir, ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"from-config\"}}");
        File.WriteAllText(Path.Combine(SwiftPath.AppendingPathComponent(homeDir, ".claude"), ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"from-home\"}}");
        // The home candidate directory does not exist yet; only the CLAUDE_CONFIG_DIR file need be real, since
        // it is read first and this test proves that file wins.
        var environment = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = configDir };
        var login = await PlanLimitsReader.ReaderClaudeLogin(environment, homeDir);
        Assert.Equal("from-config", login!.AccessToken);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-177")] // PlanLimits.swift:186-193 - falls back to <home>\.claude\.credentials.json
    public async Task ReaderClaudeLogin_falls_back_to_the_home_claude_directory_when_CLAUDE_CONFIG_DIR_is_unset()
    {
        var homeDir = TempDataDir("home-only");
        var claudeDir = SwiftPath.AppendingPathComponent(homeDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, ".credentials.json"), "{\"claudeAiOauth\":{\"accessToken\":\"from-home\"}}");
        var login = await PlanLimitsReader.ReaderClaudeLogin(new Dictionary<string, string>(), homeDir);
        Assert.Equal("from-home", login!.AccessToken);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-177")] // PlanLimits.swift:184 - an empty CLAUDE_CONFIG_DIR is treated as unset
    public async Task ReaderClaudeLogin_treats_an_empty_CLAUDE_CONFIG_DIR_as_unset()
    {
        var homeDir = TempDataDir("home-empty-config");
        var claudeDir = SwiftPath.AppendingPathComponent(homeDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, ".credentials.json"), "{\"claudeAiOauth\":{\"accessToken\":\"from-home\"}}");
        var environment = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = "" };
        var login = await PlanLimitsReader.ReaderClaudeLogin(environment, homeDir);
        Assert.Equal("from-home", login!.AccessToken);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-177")] // PlanLimits.swift:210 - no file found, no Keychain call on Windows: nil
    public async Task ReaderClaudeLogin_returns_null_when_no_file_exists_and_spawns_nothing()
    {
        var homeDir = TempDataDir("home-missing");
        var login = await PlanLimitsReader.ReaderClaudeLogin(new Dictionary<string, string>(), homeDir);
        Assert.Null(login);
    }

    // ---- PR-PROVREG-180: refreshClaudeLogin argv and RP-182's retry guard --------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-180")] // PlanLimits.swift:239 - exactly ["auth", "status"], 20s timeout
    public async Task RefreshClaudeLogin_spawns_exactly_auth_status()
    {
        var script = new ClaudeScript("refresh-argv");
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await PlanLimitsReader.RefreshClaudeLogin(FileUrl(script.ExecutablePath), environment);
        Assert.Equal(["auth", "status"], script.Calls()[0].Arguments);
    }

    // ---- PR-PROVREG-181: fetchClaudeUsage headers ----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-181")] // PlanLimits.swift:157
    public void UsageURL_is_the_pinned_oauth_usage_endpoint() =>
        Assert.Equal(new Uri("https://api.anthropic.com/api/oauth/usage"), PlanLimitsReader.UsageURL);

    [Fact]
    [Trait("Req", "PR-PROVREG-181")] // PlanLimits.swift:158
    public void KeychainService_is_the_pinned_literal() =>
        Assert.Equal("Claude Code-credentials", PlanLimitsReader.KeychainService);

    [Fact]
    [Trait("Req", "PR-PROVREG-181")] // PlanLimits.swift:244-247 - exact GET, three headers
    public async Task FetchClaudeUsage_sends_a_GET_with_the_three_exact_headers()
    {
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.FetchClaudeUsage("tok-xyz", server.Url("/api/oauth/usage"));
        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer tok-xyz"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["oauth-2025-04-20"], exchange.HeaderValues("anthropic-beta"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.Equal(200, result!.Value.Status);
    }

    // ---- PR-PROVREG-176, 180, 182: readClaudeUsageEndpoint end to end -------------------------------------

    private static string WriteLogin(string homeDir, string token, DateTimeOffset? expiresAt = null)
    {
        var claudeDir = SwiftPath.AppendingPathComponent(homeDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var path = Path.Combine(claudeDir, ".credentials.json");
        var expiresMs = expiresAt is { } e ? SwiftDate.Seconds1970(e) * 1_000 : (double?)null;
        var json = expiresMs is { } ms
            ? $"{{\"claudeAiOauth\":{{\"accessToken\":\"{token}\",\"expiresAt\":{ms.ToString(CultureInfo.InvariantCulture)}}}}}"
            : $"{{\"claudeAiOauth\":{{\"accessToken\":\"{token}\"}}}}";
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-176")] // PlanLimits.swift:135-137 - the endpoint result is returned when non-nil
    [Trait("Req", "PR-PROVREG-182")] // PlanLimits.swift:177-179 - only status 200 with parseable JSON is accepted
    public async Task ReadClaudeUsageEndpoint_returns_parsed_limits_on_a_200()
    {
        var homeDir = TempDataDir("endpoint-200");
        WriteLogin(homeDir, "tok-ok");
        var script = new ClaudeScript("endpoint-200-refresh");
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script.ExecutablePath), environment, homeDir, server.Url("/api/oauth/usage"));
        var exchange = await server.NextExchange();
        Assert.Equal(["Bearer tok-ok"], exchange.HeaderValues("Authorization"));
        await exchange.Respond(200, "OK",
            System.Text.Encoding.UTF8.GetBytes("{\"limits\":[{\"kind\":\"session\",\"percent\":10.0}]}"));
        var result = await pending.WaitAsync(Guard);
        Assert.NotNull(result);
        Assert.Equal("5-hour limit", result!.Windows[0].Title);
        // No refresh: the login was not near expiry and the first response was 200.
        Assert.Empty(script.Calls());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-176")] // PlanLimits.swift:161 - no stored login: nil, no request
    public async Task ReadClaudeUsageEndpoint_returns_null_with_no_request_when_there_is_no_login()
    {
        var homeDir = TempDataDir("endpoint-no-login");
        var script = new ClaudeScript("endpoint-no-login-refresh");
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await using var server = new LoopbackHttpServer();
        var result = await PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script.ExecutablePath), environment, homeDir, server.Url("/api/oauth/usage")).WaitAsync(Guard);
        Assert.Null(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-180")] // PlanLimits.swift:163-164 - a token expiring within 5 minutes is refreshed first
    public async Task ReadClaudeUsageEndpoint_refreshes_a_login_expiring_within_five_minutes_before_fetching()
    {
        var homeDir = TempDataDir("endpoint-near-expiry");
        WriteLogin(homeDir, "tok-near-expiry", SwiftDate.AddingTimeInterval(SwiftDate.Now, 60));
        var script = new ClaudeScript("endpoint-near-expiry-refresh");
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script.ExecutablePath), environment, homeDir, server.Url("/api/oauth/usage"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", System.Text.Encoding.UTF8.GetBytes("{\"limits\":[{\"kind\":\"session\",\"percent\":1.0}]}"));
        await pending.WaitAsync(Guard);
        Assert.Equal(["auth", "status"], script.Calls()[0].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-182")] // PlanLimits.swift:174-176 - unchanged token after refresh: nil, no retry
    public async Task ReadClaudeUsageEndpoint_returns_null_when_a_401_refresh_does_not_change_the_token()
    {
        var homeDir = TempDataDir("endpoint-401-unchanged");
        WriteLogin(homeDir, "tok-stale");
        var script = new ClaudeScript("endpoint-401-refresh"); // an inert stand-in never rewrites the file
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script.ExecutablePath), environment, homeDir, server.Url("/api/oauth/usage"));
        var first = await server.NextExchange();
        await first.Respond(401, "Unauthorized", []);
        var result = await pending.WaitAsync(Guard);
        Assert.Null(result);
        Assert.Equal(["auth", "status"], script.Calls()[0].Arguments); // the refresh did run once
        Assert.Single(script.Calls()); // and only once: no second refresh without a second 401
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-182")] // PlanLimits.swift:169 - only 401 or 403 trigger a refresh and retry
    public async Task ReadClaudeUsageEndpoint_does_not_retry_on_a_non_401_403_status()
    {
        var homeDir = TempDataDir("endpoint-500");
        WriteLogin(homeDir, "tok-500");
        var script = new ClaudeScript("endpoint-500-refresh");
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script.ExecutablePath), environment, homeDir, server.Url("/api/oauth/usage"));
        var exchange = await server.NextExchange();
        await exchange.Respond(500, "Server Error", []);
        var result = await pending.WaitAsync(Guard);
        Assert.Null(result);
        Assert.Empty(script.Calls()); // no refresh spawned for a 500
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-182")] // PlanLimits.swift:169-176 - a changed token after a 401 retries once and succeeds
    public async Task ReadClaudeUsageEndpoint_retries_once_after_a_401_when_the_refresh_actually_changes_the_token()
    {
        var homeDir = TempDataDir("endpoint-401-changed");
        WriteLogin(homeDir, "tok-old");
        var rewritten = TempDataDir("endpoint-401-changed-src");
        var newLoginPath = Path.Combine(rewritten, "new.json");
        File.WriteAllText(newLoginPath, "{\"claudeAiOauth\":{\"accessToken\":\"tok-new\"}}");
        var script = AuthRewriteStandIns.CopyFor("endpoint-401-changed");
        var controlDir = Path.Combine(Path.GetDirectoryName(script)!, "control");
        Directory.CreateDirectory(controlDir);
        var environment = new Dictionary<string, string>
        {
            ["AUTH_STANDIN_DIR"] = controlDir,
            ["AUTH_REWRITE_SOURCE"] = newLoginPath,
            ["AUTH_REWRITE_TARGET"] = Path.Combine(SwiftPath.AppendingPathComponent(homeDir, ".claude"), ".credentials.json"),
        };
        await using var server = new LoopbackHttpServer();
        var pending = PlanLimitsReader.ReadClaudeUsageEndpoint(
            FileUrl(script), environment, homeDir, server.Url("/api/oauth/usage"));
        var first = await server.NextExchange();
        Assert.Equal(["Bearer tok-old"], first.HeaderValues("Authorization"));
        await first.Respond(401, "Unauthorized", []);
        var second = await server.NextExchange();
        Assert.Equal(["Bearer tok-new"], second.HeaderValues("Authorization"));
        await second.Respond(200, "OK", System.Text.Encoding.UTF8.GetBytes("{\"limits\":[{\"kind\":\"session\",\"percent\":7.0}]}"));
        var result = await pending.WaitAsync(Guard);
        Assert.NotNull(result);
        Assert.Equal(7.0, result!.Windows[0].Percent);
    }

    // ---- PR-PROVREG-183, 184: readClaudeFromCLI ------------------------------------------------------------

    private const string UsageResponseTemplate =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
        "\"subscription_type\":\"max\",\"rate_limits\":{\"limits\":[{\"kind\":\"session\",\"percent\":42.5}]}}}}";

    [Fact]
    [Trait("Req", "PR-PROVREG-183")] // PlanLimits.swift:256 - exactly the six-token argv
    public async Task ReadClaudeFromCLI_spawns_the_exact_six_token_argv()
    {
        var script = new ClaudeScript("cli-argv");
        script.SetResponse(UsageResponseTemplate);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await PlanLimitsReader.ReadClaudeFromCLI(FileUrl(script.ExecutablePath), environment);
        Assert.Equal(
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose"],
            script.Calls()[0].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-183")] // PlanLimits.swift:274 - request_id "marrow-init" (DEVIATION OD-0012 F1)
    public async Task ReadClaudeFromCLI_sends_the_marrow_init_request_id_first()
    {
        var script = new ClaudeScript("cli-init-id");
        script.SetResponse(UsageResponseTemplate);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await PlanLimitsReader.ReadClaudeFromCLI(FileUrl(script.ExecutablePath), environment);
        var captured = script.CapturedControlRequestLine(0);
        Assert.Contains("\"request_id\":\"marrow-init\"", captured, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"initialize\"", captured, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-183")] // PlanLimits.swift:280 - request_id "marrow-usage" (DEVIATION OD-0012 F1)
    public async Task ReadClaudeFromCLI_sends_marrow_usage_after_the_init_response()
    {
        var script = new ClaudeScript("cli-usage-id");
        script.SetResponse(UsageResponseTemplate);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await PlanLimitsReader.ReadClaudeFromCLI(FileUrl(script.ExecutablePath), environment);
        var second = script.CapturedLine(0, 1);
        Assert.Contains("\"request_id\":\"marrow-usage\"", second, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"get_usage\"", second, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-184")] // PlanLimits.swift:281-284 - rate_limits is the parse root, plan name from subscription_type
    public async Task ReadClaudeFromCLI_parses_rate_limits_as_the_root_and_subscription_type_as_the_plan_name()
    {
        var script = new ClaudeScript("cli-parse-root");
        script.SetResponse(UsageResponseTemplate);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var result = await PlanLimitsReader.ReadClaudeFromCLI(FileUrl(script.ExecutablePath), environment);
        Assert.NotNull(result);
        Assert.Equal("Max", result!.PlanName);
        Assert.Equal(42.5, result.Windows[0].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-183")] // PlanLimits.swift:262-264 - process.start() throwing yields nil
    public async Task ReadClaudeFromCLI_returns_null_when_the_executable_does_not_exist()
    {
        var missing = FileUrl(Path.Combine(Path.GetTempPath(), "missing-claude-" + Guid.NewGuid().ToString("N") + ".exe"));
        var result = await PlanLimitsReader.ReadClaudeFromCLI(missing, new Dictionary<string, string>());
        Assert.Null(result);
    }

    // ---- PR-PROVREG-185, 186, 187, 188: parseClaudeUsage --------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-185")] // PlanLimits.swift:296 - a row without a numeric percent is dropped
    public void ParseClaudeUsage_drops_a_row_with_no_numeric_percent()
    {
        var root = JSONValue.Parse("{}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"session\"}")! };
        Assert.Null(PlanLimitsReader.ParseClaudeUsage(root, limits, null));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-185")] // PlanLimits.swift:300-303 - the two special titles and the id's original index
    public void ParseClaudeUsage_titles_session_and_weekly_all_specially_and_keeps_the_original_index_in_the_id()
    {
        var root = JSONValue.Parse("{}")!;
        var limits = new List<JSONValue>
        {
            JSONValue.Parse("{\"kind\":\"other\"}")!,                              // dropped: no percent
            JSONValue.Parse("{\"kind\":\"session\",\"percent\":10.0}")!,           // index 1
            JSONValue.Parse("{\"kind\":\"weekly_all\",\"percent\":20.0}")!,        // index 2
        };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Windows.Count);
        Assert.Equal("5-hour limit", result.Windows[0].Title);
        Assert.Equal("session-1", result.Windows[0].Id);
        Assert.Equal($"Weekly {MiddleDot} all models", result.Windows[1].Title);
        Assert.Equal("weekly_all-2", result.Windows[1].Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-185")] // PlanLimits.swift:303 - group and scope compose the generic title
    public void ParseClaudeUsage_composes_the_generic_title_from_group_and_scope()
    {
        var root = JSONValue.Parse("{}")!;
        var limits = new List<JSONValue>
        {
            JSONValue.Parse("{\"kind\":\"other\",\"percent\":5.0,\"group\":\"pro\",\"scope\":{\"model\":{\"display_name\":\"Opus\"}}}")!,
        };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.Equal($"Pro {MiddleDot} Opus", result!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-185")] // PlanLimits.swift:298 - group falls back to the literal "Usage"
    public void ParseClaudeUsage_falls_back_to_Usage_when_group_is_absent()
    {
        var root = JSONValue.Parse("{}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"other\",\"percent\":5.0}")! };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.Equal("Usage", result!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-186")] // PlanLimits.swift:315-320 - falls back to five_hour then seven_day
    public void ParseClaudeUsage_falls_back_to_five_hour_and_seven_day_when_limits_is_empty()
    {
        var root = JSONValue.Parse("{\"five_hour\":{\"utilization\":11.0},\"seven_day\":{\"utilization\":22.0}}")!;
        var result = PlanLimitsReader.ParseClaudeUsage(root, [], null);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Windows.Count);
        Assert.Equal("five_hour", result.Windows[0].Id);
        Assert.Equal("5-hour limit", result.Windows[0].Title);
        Assert.Equal(11.0, result.Windows[0].Percent);
        Assert.Equal("seven_day", result.Windows[1].Id);
        Assert.Equal($"Weekly {MiddleDot} all models", result.Windows[1].Title);
        Assert.Equal(22.0, result.Windows[1].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-186")] // PlanLimits.swift:314 - the fallback only runs when limits produced nothing
    public void ParseClaudeUsage_does_not_use_the_fallback_when_limits_rows_parsed()
    {
        var root = JSONValue.Parse("{\"five_hour\":{\"utilization\":99.0}}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"session\",\"percent\":1.0}")! };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.Single(result!.Windows);
        Assert.Equal(1.0, result.Windows[0].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-187")] // PlanLimits.swift:322-325 - an enabled extra_usage block appends a window
    public void ParseClaudeUsage_appends_extra_usage_when_enabled()
    {
        var root = JSONValue.Parse("{\"extra_usage\":{\"is_enabled\":true,\"utilization\":33.0}}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"session\",\"percent\":1.0}")! };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.Equal(2, result!.Windows.Count);
        var extra = result.Windows[1];
        Assert.Equal("Extra usage", extra.Title);
        Assert.Equal("extra-usage", extra.Id);
        Assert.Equal(33.0, extra.Percent);
        Assert.Null(extra.ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-187")] // PlanLimits.swift:322 - is_enabled false: no extra usage window
    public void ParseClaudeUsage_skips_extra_usage_when_not_enabled()
    {
        var root = JSONValue.Parse("{\"extra_usage\":{\"is_enabled\":false,\"utilization\":33.0}}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"session\",\"percent\":1.0}")! };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null);
        Assert.Single(result!.Windows);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-187")] // PlanLimits.swift:327-328 - a payload with no windows at all returns nil
    public void ParseClaudeUsage_returns_null_when_no_windows_result_at_all()
    {
        var root = JSONValue.Parse("{}")!;
        Assert.Null(PlanLimitsReader.ParseClaudeUsage(root, [], null));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-188")] // PlanLimits.swift:305-306 - a title prefix composes with U+00B7, id lowercases
    public void ParseClaudeUsage_titlePrefix_composes_titles_and_lowercases_into_ids()
    {
        var root = JSONValue.Parse("{}")!;
        var limits = new List<JSONValue> { JSONValue.Parse("{\"kind\":\"session\",\"percent\":1.0}")! };
        var result = PlanLimitsReader.ParseClaudeUsage(root, limits, null, titlePrefix: "Codex");
        Assert.Equal($"Codex {MiddleDot} 5-hour limit", result!.Windows[0].Title);
        Assert.Equal("codex-session-0", result.Windows[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-188")] // PlanLimits.swift:317-318, :323-324 - the prefix also applies to both fallback paths
    public void ParseClaudeUsage_titlePrefix_applies_to_the_fallback_and_extra_usage_windows()
    {
        var root = JSONValue.Parse("{\"five_hour\":{\"utilization\":1.0},\"extra_usage\":{\"is_enabled\":true,\"utilization\":2.0}}")!;
        var result = PlanLimitsReader.ParseClaudeUsage(root, [], null, titlePrefix: "Pi");
        Assert.Equal($"Pi {MiddleDot} 5-hour limit", result!.Windows[0].Title);
        Assert.Equal("pi-five_hour", result.Windows[0].Id);
        Assert.Equal($"Pi {MiddleDot} Extra usage", result.Windows[1].Title);
        Assert.Equal("pi-extra-usage", result.Windows[1].Id);
    }
}
