using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/PiPlanLimits.swift (B2 1fbf81e), :1-115.
public sealed class PiPlanLimitsTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private static int sequence;

    // R-TEST-9: a real folder under MARROW_PORT_DATA_DIR, used as PI_CODING_AGENT_DIR so no read ever
    // reaches the real user profile or %LOCALAPPDATA%.
    private static string NewConfigDirectory()
    {
        var root = TestDataDirectory.Read() ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var dir = Path.Combine(root, "pi-plan-limits-tests", "cfg-" + Interlocked.Increment(ref sequence).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Dictionary<string, string> EnvironmentFor(string configDirectory) =>
        new() { ["PI_CODING_AGENT_DIR"] = configDirectory };

    private static void WriteAuthJson(string configDirectory, string json)
    {
        var file = SwiftPath.AppendingPathComponent(configDirectory, "auth.json");
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes(json));
    }

    // ---- PR-PROVCLI-313: read(environment:) ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:12-15 - no auth.json at all
    public async Task Read_returns_null_when_auth_json_is_missing()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:13-15 - must parse to an object, else nil
    public async Task Read_returns_null_when_auth_json_is_an_array()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "[1,2,3]");
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:13-15 - must parse to an object, else nil
    public async Task Read_returns_null_when_auth_json_is_a_bare_string()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "\"hello\"");
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:13-14 - must parse at all, else nil
    public async Task Read_returns_null_when_auth_json_does_not_parse()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "this is not json");
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:20 - "type" must be exactly "oauth"
    public async Task Read_skips_an_entry_whose_type_is_not_exactly_oauth()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"OAuth\",\"access\":\"token\"}}");
        await using var server = new LoopbackHttpServer();
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex")).WaitAsync(Guard));
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:20 - a missing "type" is not "oauth"
    public async Task Read_skips_an_entry_with_no_type_field()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"access\":\"token\"}}");
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:21 - the access token must be a non-empty string
    public async Task Read_skips_an_entry_with_an_empty_access_token()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"\"}}");
        await using var server = new LoopbackHttpServer();
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex")).WaitAsync(Guard));
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:21 - the access token must be present
    public async Task Read_skips_an_entry_with_a_missing_access_token()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\"}}");
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:22-26 - "expires" is epoch MILLISECONDS; 0.0 is always past
    public async Task Read_skips_an_entry_whose_expires_milliseconds_are_in_the_past()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"token\",\"expires\":0.0}}");
        await using var server = new LoopbackHttpServer();
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex")).WaitAsync(Guard));
        Assert.Equal(0, server.ConnectionCount); // an expired token never reaches the network
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:23 - a non-double "expires" does not trigger the skip
    public async Task Read_does_not_skip_for_expiry_when_expires_is_not_a_double()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        // "expires" as a string fails entry["expires"]?.double, so the expiry skip never fires; the entry
        // reaches the network instead, proving the skip is double-typed only.
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"token\",\"expires\":\"0\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex"));
        var exchange = await server.NextExchange();
        Assert.Equal("/anthropic", exchange.Target);
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        Assert.Null(await pending.WaitAsync(Guard)); // an empty body yields no windows
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:30-34 - every key other than anthropic/openai-codex/openai is skipped
    public async Task Read_skips_keys_other_than_anthropic_openai_codex_and_openai()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        // Far in the future so the entry survives the expiry check and is dropped only by key dispatch.
        WriteAuthJson(configDir, "{\"gemini\":{\"type\":\"oauth\",\"access\":\"token\",\"expires\":4102444800000.0}}");
        await using var server = new LoopbackHttpServer();
        Assert.Null(await PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex")).WaitAsync(Guard));
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:18 - keys are walked SORTED; :36-40 windows/names appended in that order
    public async Task Read_visits_keys_in_sorted_order_and_joins_plan_names_with_middle_dot()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir,
            "{\"openai\":{\"type\":\"oauth\",\"access\":\"a.b.c\"},\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex"));
        // Sorted key order: "anthropic" before "openai".
        var first = await server.NextExchange();
        Assert.Equal("/anthropic", first.Target);
        await first.Respond(200, "OK", "{\"limits\":[{\"kind\":\"session\",\"percent\":10.0}]}"u8.ToArray());
        var second = await server.NextExchange();
        Assert.Equal("/codex", second.Target);
        await second.Respond(200, "OK",
            Encoding.UTF8.GetBytes("{\"primary_window\":{\"used_percent\":5.0,\"limit_window_seconds\":300}}"));
        var result = await pending.WaitAsync(Guard);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Windows.Count);
        Assert.Equal("Claude · Codex", result.PlanName); // U+00B7 MIDDLE DOT, no subscription/plan type given
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // PiPlanLimits.swift:35, :39-40 - an entry whose reader answers nil is skipped
    public async Task Read_returns_null_when_the_only_entry_reader_answers_nil()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex"));
        var exchange = await server.NextExchange();
        await exchange.Respond(500, "Server Error", []);
        Assert.Null(await pending.WaitAsync(Guard));
    }

    // ---- PR-PROVCLI-314: readAnthropic ---------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:45-48 - exact GET, three headers, 15s timeout
    public async Task Read_sends_the_anthropic_request_with_the_three_exact_headers()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok-xyz\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/api/oauth/usage"), null);
        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer tok-xyz"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["oauth-2025-04-20"], exchange.HeaderValues("anthropic-beta"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:49-53 - non-200 yields nil for this provider
    public async Task Read_returns_null_when_anthropic_answers_a_non_200()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/x"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(401, "Unauthorized", []);
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:51 - an unparseable body yields nil
    public async Task Read_returns_null_when_anthropic_body_does_not_parse()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/x"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "not json"u8.ToArray());
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:52 - limits parsed from json.limits with titlePrefix "Claude"
    public async Task Read_parses_anthropic_limits_with_the_claude_title_prefix()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/x"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{\"limits\":[{\"kind\":\"session\",\"percent\":33.0}]}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.NotNull(result);
        Assert.Equal("Claude · 5-hour limit", result!.Windows[0].Title);
        Assert.Equal(33.0, result.Windows[0].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:55 - plan name overwritten from subscription_type, prefixed "Claude "
    public async Task Read_prefixes_the_mapped_subscription_type_with_claude()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/x"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK",
            "{\"limits\":[{\"kind\":\"session\",\"percent\":1.0}],\"subscription_type\":\"max\"}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.Equal("Claude Max", result!.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // PiPlanLimits.swift:55 - missing/unmapped subscription_type yields the bare literal "Claude"
    public async Task Read_uses_bare_claude_when_subscription_type_is_absent()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/x"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{\"limits\":[{\"kind\":\"session\",\"percent\":1.0}]}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.Equal("Claude", result!.PlanName);
    }

    // ---- PR-PROVCLI-315: readCodex --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:61-63 - Authorization and Accept headers
    public async Task Read_sends_the_codex_request_with_authorization_and_accept()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok-codex\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer tok-codex"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        Assert.Empty(exchange.HeaderValues("ChatGPT-Account-Id")); // a single-part token has no claim to extract
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:30 - key "openai-codex" also routes to readCodex
    public async Task Read_routes_the_openai_codex_key_to_readCodex()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai-codex\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        Assert.Equal(["Bearer tok"], exchange.HeaderValues("Authorization"));
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:65-67, :102-113 - the account id header from the token's own claim
    public async Task Read_adds_the_ChatGPT_Account_Id_header_from_the_tokens_own_claim()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        var payload = Base64Url("{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"acct_123\"}}");
        var token = "header." + payload + ".sig";
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"" + token + "\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        Assert.Equal(["acct_123"], exchange.HeaderValues("ChatGPT-Account-Id"));
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:68-70 - non-200 yields nil
    public async Task Read_returns_null_when_codex_answers_a_non_200()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(503, "Unavailable", []);
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:70 - an unparseable body yields nil
    public async Task Read_returns_null_when_codex_body_does_not_parse()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "not json"u8.ToArray());
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:71 - "rate_limit" object is used when present
    public async Task Read_uses_the_rate_limit_object_when_present()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes(
            "{\"rate_limit\":{\"primary_window\":{\"used_percent\":9.0,\"limit_window_seconds\":300}}," +
            "\"primary_window\":{\"used_percent\":99.0,\"limit_window_seconds\":300}}"));
        var result = await pending.WaitAsync(Guard);
        Assert.Equal(9.0, result!.Windows[0].Percent); // the top-level primary_window (outside rate_limit) is ignored
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:71 - falls back to the whole body when "rate_limit" is absent
    public async Task Read_falls_back_to_the_whole_body_when_rate_limit_is_absent()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK",
            "{\"primary_window\":{\"used_percent\":4.0,\"limit_window_seconds\":300}}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.Equal(4.0, result!.Windows[0].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:74-75, :39-40 - no windows at all yields nil
    public async Task Read_returns_null_when_codex_has_no_windows()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{}"u8.ToArray());
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:77 - plan name "Codex " + codexPlanName(plan_type)
    public async Task Read_prefixes_the_mapped_plan_type_with_codex()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes(
            "{\"primary_window\":{\"used_percent\":1.0,\"limit_window_seconds\":300},\"plan_type\":\"pro\"}"));
        var result = await pending.WaitAsync(Guard);
        Assert.Equal("Codex Pro 20x", result!.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:77 - a missing plan_type yields the bare literal "Codex"
    public async Task Read_uses_bare_codex_when_plan_type_is_absent()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai\":{\"type\":\"oauth\",\"access\":\"tok\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), null, server.Url("/x"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK",
            "{\"primary_window\":{\"used_percent\":1.0,\"limit_window_seconds\":300}}"u8.ToArray());
        var result = await pending.WaitAsync(Guard);
        Assert.Equal("Codex", result!.PlanName);
    }

    // ---- PR-PROVCLI-315: codexAccountID (direct, offline) ----------------------------------------------------

    private static string Base64Url(string json)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    [Theory]
    [Trait("Req", "PR-PROVCLI-315")] // Services/Providers/PiPlanLimits.swift:106-112: Data(base64Encoded:) has no ignoreUnknownCharacters option.
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void CodexAccountID_rejects_whitespace_in_the_base64_payload(string whitespace)
    {
        var payload = Base64Url("{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"acct_fixture\"}}");
        // Four inserted Characters leave :107-110's padding unchanged. .NET wrongly ignores all four.
        var invalid = payload.Insert(4, string.Concat(Enumerable.Repeat(whitespace, 4)));
        Assert.Equal("acct_fixture", JSONValue.Parse(Convert.FromBase64String(
            invalid + new string('=', (4 - invalid.Length % 4) % 4)))!["https://api.openai.com/auth"]!["chatgpt_account_id"]!.JSONValueString);
        Assert.Null(PiPlanLimits.CodexAccountID("h." + invalid + ".s"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-313")] // Services/Providers/PiPlanLimits.swift:18,28-40: a nil provider does not discard subsequent windows.
    public async Task Read_keeps_both_openai_logins_in_sorted_order_after_anthropic_fails()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"openai-codex\":{\"type\":\"oauth\",\"access\":\"fixture-second\"},\"openai\":{\"type\":\"oauth\",\"access\":\"fixture-first\"},\"anthropic\":{\"type\":\"oauth\",\"access\":\"fixture-failed\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), server.Url("/codex"));
        var failed = await server.NextExchange();
        Assert.Equal("/anthropic", failed.Target);
        await failed.Respond(503, "Unavailable", []);
        foreach (var token in new[] { "fixture-first", "fixture-second" })
        {
            var exchange = await server.NextExchange();
            Assert.Equal("/codex", exchange.Target);
            Assert.Equal(["Bearer " + token], exchange.HeaderValues("Authorization"));
            await exchange.Respond(200, "OK", "{\"primary_window\":{\"used_percent\":12,\"limit_window_seconds\":300}}"u8.ToArray());
        }
        var result = await pending.WaitAsync(Guard);
        Assert.NotNull(result);
        Assert.Equal("Codex \U000000B7 Codex", result.PlanName); // U+00B7 MIDDLE DOT
        Assert.Equal(new[] { "codex-primary", "codex-primary" }, result.Windows.Select(w => w.Id));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-314")] // Services/Providers/PiPlanLimits.swift:52-56: no mapped subscription still uses the bare Claude literal.
    public async Task Read_uses_bare_claude_for_an_empty_subscription()
    {
        using var runtime = new TestRuntime();
        var configDir = NewConfigDirectory();
        WriteAuthJson(configDir, "{\"anthropic\":{\"type\":\"oauth\",\"access\":\"fixture\"}}");
        await using var server = new LoopbackHttpServer();
        var pending = PiPlanLimits.Read(EnvironmentFor(configDir), server.Url("/anthropic"), null);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{\"limits\":[{\"kind\":\"session\",\"percent\":7}],\"subscription_type\":\"\"}"u8.ToArray());
        Assert.Equal("Claude", (await pending.WaitAsync(Guard))!.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // Services/Providers/PiPlanLimits.swift:86-90,97: reset_at wins, and percent has no clamp.
    public void CodexWindow_prefers_absolute_reset_and_keeps_a_negative_percent()
    {
        using var runtime = new TestRuntime();
        var window = PiPlanLimits.CodexWindow("codex-secondary", JSONValue.Parse(
            "{\"used_percent\":-5,\"limit_window_seconds\":59,\"reset_at\":1893456000,\"reset_after_seconds\":3600}"));
        Assert.NotNull(window);
        Assert.Equal(SwiftDate.From1970Seconds(1893456000), window.ResetsAt);
        Assert.Equal(-5, window.Percent);
        Assert.Equal("codex-secondary", window.Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:105 - fewer than two dot-separated parts yields no id
    public void CodexAccountID_returns_null_for_a_token_with_fewer_than_two_parts()
    {
        Assert.Null(PiPlanLimits.CodexAccountID("onlyone"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:106-110 - hyphen/underscore mapping and equals padding
    public void CodexAccountID_decodes_a_base64url_second_part_with_padding_applied()
    {
        var payload = Base64Url("{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"acct_abc\"}}");
        var token = "h." + payload + ".s";
        Assert.Equal("acct_abc", PiPlanLimits.CodexAccountID(token));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:111-112 - a part that fails to decode yields no id
    public void CodexAccountID_returns_null_when_the_second_part_fails_to_decode()
    {
        Assert.Null(PiPlanLimits.CodexAccountID("h.not-valid-base64!!!.s"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:112-113 - a part that decodes but fails to parse as JSON yields no id
    public void CodexAccountID_returns_null_when_the_decoded_payload_is_not_json()
    {
        var payload = Base64Url("not json");
        Assert.Null(PiPlanLimits.CodexAccountID("h." + payload + ".s"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-315")] // PiPlanLimits.swift:113 - the claim path is "https://api.openai.com/auth" then "chatgpt_account_id"
    public void CodexAccountID_returns_null_when_the_auth_claim_is_missing()
    {
        var payload = Base64Url("{\"other\":1}");
        Assert.Null(PiPlanLimits.CodexAccountID("h." + payload + ".s"));
    }

    // ---- PR-PROVCLI-316: codexWindow (direct, offline) ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:82 - nil raw yields nil
    public void CodexWindow_returns_null_when_raw_is_null()
    {
        Assert.Null(PiPlanLimits.CodexWindow("codex-primary", null));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:83 - missing "used_percent" yields nil
    public void CodexWindow_returns_null_when_used_percent_is_missing()
    {
        var raw = JSONValue.Parse("{\"limit_window_seconds\":300}");
        Assert.Null(PiPlanLimits.CodexWindow("codex-primary", raw));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:84 - missing "limit_window_seconds" yields nil
    public void CodexWindow_returns_null_when_limit_window_seconds_is_missing()
    {
        var raw = JSONValue.Parse("{\"used_percent\":50.0}");
        Assert.Null(PiPlanLimits.CodexWindow("codex-primary", raw));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:86-87 - "reset_at" as a double is seconds since 1970
    public void CodexWindow_takes_reset_time_from_reset_at_when_present()
    {
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":300,\"reset_at\":1893456000.0}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.NotNull(window);
        Assert.Equal(SwiftDate.From1970Seconds(1893456000.0), window!.ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:89-90 - "reset_after_seconds" as an int is seconds from now
    public void CodexWindow_takes_reset_time_from_reset_after_seconds_when_reset_at_is_absent()
    {
        using var runtime = new TestRuntime();
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":300,\"reset_after_seconds\":3600}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.NotNull(window);
        Assert.Equal(SwiftDate.AddingTimeInterval(SwiftDate.Now, 3600), window!.ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:92 - neither field present yields a nil reset time
    public void CodexWindow_reset_time_is_null_when_neither_field_is_present()
    {
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":300}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.NotNull(window);
        Assert.Null(window!.ResetsAt);
    }

    [Theory]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:95 - the id is the literal passed in, verbatim
    [InlineData("codex-primary")]
    [InlineData("codex-secondary")]
    public void CodexWindow_id_is_the_literal_passed_in(string id)
    {
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":300}");
        Assert.Equal(id, PiPlanLimits.CodexWindow(id, raw)!.Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:96 - "Codex · " + windowTitle(minutes: seconds / 60), INTEGER division
    public void CodexWindow_title_uses_integer_division_for_minutes()
    {
        // 299 / 60 = 4 (integer division), never 4.98...
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":299}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.Equal("Codex · " + PlanLimitsReader.WindowTitle(4), window!.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:98 - a window shorter than 60 seconds titles as zero minutes
    public void CodexWindow_title_is_zero_minutes_for_a_window_shorter_than_sixty_seconds()
    {
        var raw = JSONValue.Parse("{\"used_percent\":50.0,\"limit_window_seconds\":30}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.Equal("Codex · " + PlanLimitsReader.WindowTitle(0), window!.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-316")] // PiPlanLimits.swift:97 - percent is used_percent unchanged, with no clamping
    public void CodexWindow_percent_is_unclamped()
    {
        var raw = JSONValue.Parse("{\"used_percent\":150.0,\"limit_window_seconds\":300}");
        var window = PiPlanLimits.CodexWindow("codex-primary", raw);
        Assert.Equal(150.0, window!.Percent);
    }
}
