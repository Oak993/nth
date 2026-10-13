using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ZaiAPI.swift (B3 52bc7926), :1-207.
//
// Every network-touching test runs against LoopbackHttpServer on 127.0.0.1 (R-TEST-10), reached through the
// PORT-ONLY url-override parameters ZaiAPI's networked members accept for tests only (production always uses
// the real ZaiAPI URL constants), the same seam MetaAPI.cs already uses. Detail and title strings use
// U+00B7 MIDDLE DOT, never a hyphen or ASCII period.
public sealed class ZaiAPITests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private const string MiddleDot = "·"; // U+00B7 MIDDLE DOT

    private static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

    // ---- PR-PROVAPI-056: endpoints -----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-056")] // Services/Providers/ZaiAPI.swift:7-12
    public void BaseURL_chatURL_modelsURL_quotaURL_resetListURL_and_resetUseURL_are_the_pinned_api_z_ai_endpoints()
    {
        Assert.Equal(new Uri("https://api.z.ai/api/coding/paas/v4"), ZaiAPI.BaseURL);
        Assert.Equal(new Uri("https://api.z.ai/api/coding/paas/v4/chat/completions"), ZaiAPI.ChatURL);
        Assert.Equal(new Uri("https://api.z.ai/api/coding/paas/v4/models"), ZaiAPI.ModelsURL);
        Assert.Equal(new Uri("https://api.z.ai/api/monitor/usage/quota/limit"), ZaiAPI.QuotaURL);
        Assert.Equal(new Uri("https://api.z.ai/api/biz/customer-package-reset/list?targetType=PERSONAL"), ZaiAPI.ResetListURL);
        Assert.Equal(new Uri("https://api.z.ai/api/biz/customer-package-reset/use"), ZaiAPI.ResetUseURL);
    }

    // ---- PR-PROVAPI-063: context window and output cap ----------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-063")] // Services/Providers/ZaiAPI.swift:16-17
    public void ContextWindow_is_1000000_and_MaxOutputTokens_is_128000()
    {
        Assert.Equal(1_000_000, ZaiAPI.ContextWindow);
        Assert.Equal(128_000, ZaiAPI.MaxOutputTokens);
    }

    // ---- PR-PROVAPI-057: validate ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:14-15 (via ZaiAPI.swift:21)
    public async Task Validate_returns_false_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await ZaiAPI.Validate("", server.Url("/api/coding/paas/v4/models")).WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:14 (.trimmed)
    public async Task Validate_returns_false_with_no_request_for_a_whitespace_only_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await ZaiAPI.Validate("   \n\t  ", server.Url("/api/coding/paas/v4/models")).WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:5-10,16 (bearerRequest, via ZaiAPI.swift:21)
    public async Task Validate_sends_a_GET_with_bearer_auth_and_a_json_accept_header()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.Validate("  key-abc  ", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/api/coding/paas/v4/models", exchange.Target);
        Assert.Equal(["Bearer key-abc"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(200, "OK", []);
        Assert.True(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:19
    public async Task Validate_is_false_for_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.Validate("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(401, "Unauthorized", []);

        Assert.False(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:17 (try?)
    public async Task Validate_is_false_on_a_transport_error()
    {
        using var runtime = new TestRuntime();

        var result = await ZaiAPI.Validate("key-abc", LoopbackHttpServer.RefusedUrl("/api/coding/paas/v4/models")).WaitAsync(Guard);

        Assert.False(result);
    }

    // ---- PR-PROVAPI-058: listModels parses data[].id and sorts the default first -------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:23-24 (via ZaiAPI.swift:24)
    public async Task ListModels_returns_empty_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await ZaiAPI.ListModels("", server.Url("/api/coding/paas/v4/models")).WaitAsync(Guard);

        Assert.Empty(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:27-28
    public async Task ListModels_returns_empty_for_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ListModels("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(500, "Internal Server Error", Ascii("oops"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:28 (JSONValue.parse(data))
    public async Task ListModels_returns_empty_for_an_unparseable_body()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ListModels("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("not json"), ("Content-Type", "application/json"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:29-30
    public async Task ListModels_returns_empty_when_the_data_array_has_no_ids()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ListModels("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:29-33
    public async Task ListModels_drops_ineligible_ids_and_sorts_the_default_first_then_ascending_id()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        // Deliberately shuffled, with GLM-5.3-FlashX and an older GLM id the endpoint also lists.
        var body = Ascii("{\"data\":[" +
            "{\"id\":\"glm-5.3-flash\"}," +
            "{\"id\":\"glm-5.3-flashx\"}," +
            "{\"id\":\"glm-4.6\"}," +
            "{\"id\":\"glm-5.3\"}" +
            "]}");
        var pending = ZaiAPI.ListModels("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", body, ("Content-Type", "application/json"));

        var options = await pending.WaitAsync(Guard);

        // ED-NEW-muse-wonder-7: endpoint-listed chat ids are accepted regardless of family; FlashX stays excluded.
        Assert.Equal(["glm-5.3", "glm-4.6", "glm-5.3-flash"], options.Select(o => o.Id));
        Assert.True(options[0].IsDefault);
        Assert.False(options[1].IsDefault);
        Assert.Equal("glm-4.6", options[1].Name);
        Assert.Empty(options[1].Efforts);
        Assert.Null(options[1].DefaultEffort);
        Assert.False(options[2].IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:5-10,25 (bearerRequest, via ZaiAPI.swift:24)
    public async Task ListModels_sends_the_same_bearer_and_accept_headers_as_validate()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ListModels("key-xyz", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer key-xyz"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));
        await pending.WaitAsync(Guard);
    }

    // ---- PR-PROVAPI-059: glm-5.3 row ----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-059")] // Services/Providers/ZaiAPI.swift:32-33,14
    public void Option_for_glm_5_3_is_the_default_row_with_max_effort_and_the_three_efforts()
    {
        var option = ZaiAPI.Option("glm-5.3");

        Assert.NotNull(option);
        Assert.Equal("glm-5.3", option!.Id);
        Assert.Equal("GLM-5.3", option.Name);
        Assert.Equal($"Best reasoning and coding {MiddleDot} 1M context", option.Detail);
        Assert.Equal(["low", "high", "max"], option.Efforts);
        Assert.Equal("max", option.DefaultEffort);
        Assert.True(option.IsDefault);
    }

    // ---- PR-PROVAPI-060: glm-5.3-flash row -----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-060")] // Services/Providers/ZaiAPI.swift:34-35
    public void Option_for_glm_5_3_flash_has_max_effort_and_is_not_default()
    {
        var option = ZaiAPI.Option("glm-5.3-flash");

        Assert.NotNull(option);
        Assert.Equal("glm-5.3-flash", option!.Id);
        Assert.Equal("GLM-5.3 Flash", option.Name);
        Assert.Equal($"Fast, reads images {MiddleDot} 1M context", option.Detail);
        Assert.Equal(["low", "high", "max"], option.Efforts);
        Assert.Equal("max", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    // ---- PR-PROVAPI-061: only glm-5.3-prefixed ids, never FlashX -------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-061")] // Services/Providers/ZaiAPI.swift:42
    public void Option_is_null_for_glm_5_3_flashx()
    {
        Assert.Null(ZaiAPI.Option("glm-5.3-flashx"));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-061")] // Services/Providers/ZaiAPI.swift:42 (lowercased().contains)
    [InlineData("glm-5.3-FLASHX")]
    [InlineData("glm-5.3-FlashX")]
    [InlineData("glm-5.3-something-flashx-else")]
    public void Option_is_null_when_id_contains_flashx_in_any_casing(string id)
    {
        Assert.Null(ZaiAPI.Option(id));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-061")] // Services/Providers/ZaiAPI.swift:42 (hasPrefix, case sensitive)
    [InlineData("glm-4.6")]
    [InlineData("glm-5")]
    [InlineData("GLM-5.3")]
    [InlineData("gpt-4")]
    public void Option_is_null_for_an_id_that_does_not_start_with_the_lowercase_prefix_glm_5_3(string id)
    {
        Assert.Null(ZaiAPI.Option(id));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-061")] // Services/Providers/ZaiAPI.swift:43
    public void Option_for_a_qualifying_unknown_id_is_named_by_its_own_id_with_max_effort()
    {
        var option = ZaiAPI.Option("glm-5.3-turbo");

        Assert.NotNull(option);
        Assert.Equal("glm-5.3-turbo", option!.Id);
        Assert.Equal("glm-5.3-turbo", option.Name);
        Assert.Null(option.Detail);
        Assert.Equal(["low", "high", "max"], option.Efforts);
        Assert.Equal("max", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    // ---- PR-PROVAPI-062: vision model test -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-062")] // Services/Providers/ZaiAPI.swift:49
    public void IsVisionModel_is_false_for_a_null_id()
    {
        Assert.False(ZaiAPI.IsVisionModel(null));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-062")] // Services/Providers/ZaiAPI.swift:69
    [InlineData("glm-5.3-flash")]
    [InlineData("glm-flash-nano")]
    public void IsVisionModel_is_true_when_id_contains_flash(string id)
    {
        Assert.True(ZaiAPI.IsVisionModel(id));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-062")] // Services/Providers/ZaiAPI.swift:69
    [InlineData("glm-4v")]
    [InlineData("glm-5.3v")]
    public void IsVisionModel_is_true_when_id_ends_with_v(string id)
    {
        Assert.True(ZaiAPI.IsVisionModel(id));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-062")] // Services/Providers/ZaiAPI.swift:69
    public void IsVisionModel_is_false_for_glm_5_3()
    {
        Assert.False(ZaiAPI.IsVisionModel("glm-5.3"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-062")] // Services/Providers/ZaiAPI.swift:69 (case sensitive)
    public void IsVisionModel_case_sensitive_tests_reject_uppercase_Flash_and_V()
    {
        Assert.False(ZaiAPI.IsVisionModel("glm-5.3-FLASH"));
        Assert.False(ZaiAPI.IsVisionModel("glm-5.3V"));
    }

    // ---- PR-PROVAPI-064: planLimits sequencing -------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-064")] // Services/Providers/ZaiAPI.swift:56-57
    public async Task PlanLimits_returns_nil_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await ZaiAPI.PlanLimits("", server.Url("/quota"), server.Url("/reset")).WaitAsync(Guard);

        Assert.Null(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-064")] // Services/Providers/ZaiAPI.swift:58; OpenAICompatibleAPI.swift:5-10
    public async Task PlanLimits_sends_a_GET_to_the_quota_endpoint_with_bearer_auth_and_json_accept()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.PlanLimits("key-abc", server.Url("/quota"), server.Url("/reset"));
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/quota", exchange.Target);
        Assert.Equal(["Bearer key-abc"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(500, "Internal Server Error", []);
        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-064")] // Services/Providers/ZaiAPI.swift:62
    public async Task PlanLimits_is_nil_when_code_is_not_200_and_success_is_not_true()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.PlanLimits("key-abc", server.Url("/quota"), server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK",
            Ascii("{\"code\":400,\"success\":false,\"data\":{\"level\":\"pro\",\"limits\":[]}}"),
            ("Content-Type", "application/json"));

        Assert.Null(await pending.WaitAsync(Guard));
        Assert.Equal(1, server.ConnectionCount); // the reset-list call is never made
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-064")] // Services/Providers/ZaiAPI.swift:63
    public async Task PlanLimits_is_nil_when_no_windows_are_produced()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.PlanLimits("key-abc", server.Url("/quota"), server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK",
            Ascii("{\"code\":200,\"data\":{\"level\":\"pro\",\"limits\":[]}}"),
            ("Content-Type", "application/json"));

        Assert.Null(await pending.WaitAsync(Guard));
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-064")] // Services/Providers/ZaiAPI.swift:64-65
    public async Task PlanLimits_fetches_reset_cards_only_after_the_quota_call_succeeds_and_attaches_them()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.PlanLimits("key-abc", server.Url("/quota"), server.Url("/reset"));
        var quota = await server.NextExchange();
        Assert.Equal("/quota", quota.Target);
        await quota.Respond(200, "OK",
            Ascii("{\"code\":200,\"data\":{\"level\":\"pro\",\"limits\":[" +
                  "{\"type\":\"CREDIT_LIMIT\",\"usage\":100,\"currentValue\":50}" +
                  "]}}"),
            ("Content-Type", "application/json"));

        var reset = await server.NextExchange();
        Assert.Equal("/reset", reset.Target);
        Assert.Equal(["Bearer key-abc"], reset.HeaderValues("Authorization"));
        await reset.Respond(200, "OK",
            Ascii("{\"code\":200,\"data\":{\"fiveHourResets\":[{\"recordId\":9,\"expireTime\":\"bad\"}],\"weekResets\":[]}}"),
            ("Content-Type", "application/json"));

        var limits = await pending.WaitAsync(Guard);

        Assert.NotNull(limits);
        Assert.Equal("Coding Plan Pro", limits!.PlanName);
        Assert.Single(limits.Windows);
        Assert.NotNull(limits.PlanLimitsResetCredits);
        Assert.Equal(1, limits.PlanLimitsResetCredits!.AvailableCount);
    }

    // ---- PR-PROVAPI-065: plan name prefix ------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-065")] // Services/Providers/ZaiAPI.swift:69
    public void Limits_plan_name_is_Coding_Plan_space_then_the_reader_rendering_of_the_level()
    {
        var json = JSONValue.Parse("{\"data\":{\"level\":\"pro\",\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1}]}}")!;

        var limits = ZaiAPI.Limits(json);

        Assert.NotNull(limits);
        Assert.Equal("Coding Plan Pro", limits!.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-065")] // Services/Providers/ZaiAPI.swift:69 (level missing)
    public void Limits_plan_name_is_null_when_level_is_missing()
    {
        var json = JSONValue.Parse("{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1}]}}")!;

        var limits = ZaiAPI.Limits(json);

        Assert.NotNull(limits);
        Assert.Null(limits!.PlanName);
    }

    // ---- PR-PROVAPI-066: credit/token windows, percent computation and clamp --------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:78-86
    public void Limits_uses_the_reported_percentage_when_present()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":100,\"currentValue\":1,\"percentage\":42.5}]}}")!;

        var window = ZaiAPI.Limits(json)!.Windows[0];

        Assert.Equal(42.5, window.Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:81-82
    public void Limits_computes_the_percent_from_current_over_usage_when_not_reported()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":200,\"currentValue\":50}]}}")!;

        var window = ZaiAPI.Limits(json)!.Windows[0];

        Assert.Equal(25.0, window.Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:83-84
    public void Limits_percent_is_zero_when_usage_is_zero_and_not_reported()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":0,\"currentValue\":0}]}}")!;

        var window = ZaiAPI.Limits(json)!.Windows[0];

        Assert.Equal(0, window.Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:86
    public void Limits_percent_is_clamped_to_0_and_100()
    {
        var below = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":100,\"currentValue\":1,\"percentage\":-30}]}}")!;
        var above = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":100,\"currentValue\":1,\"percentage\":250}]}}")!;

        Assert.Equal(0, ZaiAPI.Limits(below)!.Windows[0].Percent);
        Assert.Equal(100, ZaiAPI.Limits(above)!.Windows[0].Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:87
    public void Limits_resetsAt_is_nextResetTime_milliseconds_converted_to_seconds()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"nextResetTime\":1700000000000}]}}")!;

        var window = ZaiAPI.Limits(json)!.Windows[0];

        Assert.Equal(SwiftDate.From1970Seconds(1700000000), window.ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:87 (missing)
    public void Limits_resetsAt_is_null_when_nextResetTime_is_missing()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1}]}}")!;

        Assert.Null(ZaiAPI.Limits(json)!.Windows[0].ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-066")] // Services/Providers/ZaiAPI.swift:107-113
    public void Limits_credit_window_ids_are_credit_0_credit_1_in_list_order_with_the_formatted_detail()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[" +
            "{\"type\":\"CREDIT_LIMIT\",\"usage\":2000000,\"currentValue\":1500000}," +
            "{\"type\":\"TOKENS_LIMIT\",\"usage\":3000,\"currentValue\":1000}" +
            "]}}")!;

        var windows = ZaiAPI.Limits(json)!.Windows;

        Assert.Equal("credit-0", windows[0].Id);
        Assert.Equal("1,500,000 of 2,000,000 credits", windows[0].Detail);
        Assert.Equal("credit-1", windows[1].Id);
        Assert.Equal("1,000 of 3,000 credits", windows[1].Detail);
    }

    // ---- PR-PROVAPI-067: window title from a unit code ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:92-93
    public void Limits_unit_3_hours_multiplies_number_by_60_minutes()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":3,\"number\":5}]}}")!;

        // 5 hours * 60 = 300 minutes -> PlanLimitsReader.WindowTitle(300) == "5-hour limit"
        Assert.Equal("5-hour limit", ZaiAPI.Limits(json)!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:94
    public void Limits_unit_4_days_multiplies_number_by_1440_minutes()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":4,\"number\":1}]}}")!;

        Assert.Equal("Daily", ZaiAPI.Limits(json)!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:95
    public void Limits_unit_6_weeks_multiplies_number_by_10080_minutes()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":6,\"number\":1}]}}")!;

        Assert.Equal("Weekly", ZaiAPI.Limits(json)!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:96
    public void Limits_unit_5_months_multiplies_number_by_43200_minutes()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":5,\"number\":1}]}}")!;

        Assert.Equal("Monthly", ZaiAPI.Limits(json)!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:92-97 (number not above zero)
    public void Limits_a_non_positive_number_leaves_minutes_nil_even_with_a_known_unit()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":3,\"number\":0}]}}")!;

        Assert.Equal("5-hour limit", ZaiAPI.Limits(json)!.Windows[0].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-067")] // Services/Providers/ZaiAPI.swift:102-106
    public void Limits_falls_back_to_5_hour_limit_for_the_first_credit_window_and_Weekly_for_later_ones_with_an_unknown_unit()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[" +
            "{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":99,\"number\":1}," +
            "{\"type\":\"TOKENS_LIMIT\",\"usage\":10,\"currentValue\":1,\"unit\":99,\"number\":1}" +
            "]}}")!;

        var windows = ZaiAPI.Limits(json)!.Windows;

        Assert.Equal("5-hour limit", windows[0].Title);
        Assert.Equal("Weekly", windows[1].Title);
    }

    // ---- PR-PROVAPI-068: TIME_LIMIT becomes an MCP tools window ----------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-068")] // Services/Providers/ZaiAPI.swift:116-123
    public void Limits_TIME_LIMIT_entries_become_tools_windows_with_the_fixed_title_and_detail()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[" +
            "{\"type\":\"TIME_LIMIT\",\"usage\":50,\"currentValue\":10}," +
            "{\"type\":\"TIME_LIMIT\",\"usage\":60,\"currentValue\":20}" +
            "]}}")!;

        var windows = ZaiAPI.Limits(json)!.Windows;

        Assert.Equal("tools-0", windows[0].Id);
        Assert.Equal($"Monthly {MiddleDot} MCP tools", windows[0].Title);
        Assert.Equal("10 of 50 calls", windows[0].Detail);
        Assert.Equal("tools-1", windows[1].Id);
        Assert.Equal("20 of 60 calls", windows[1].Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-068")] // Services/Providers/ZaiAPI.swift:124-125
    public void Limits_skips_entries_of_any_other_type()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[" +
            "{\"type\":\"SOMETHING_ELSE\",\"usage\":10,\"currentValue\":1}," +
            "{\"type\":\"CREDIT_LIMIT\",\"usage\":10,\"currentValue\":1}" +
            "]}}")!;

        var windows = ZaiAPI.Limits(json)!.Windows;

        Assert.Single(windows);
        Assert.Equal("credit-0", windows[0].Id);
    }

    // ---- PR-PROVAPI-069: US grouped counts, no fraction digits ---------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-069")] // Services/Providers/ZaiAPI.swift:196-206
    public void Limits_formats_counts_with_US_thousands_grouping_and_no_decimals()
    {
        var json = JSONValue.Parse(
            "{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"usage\":1234567,\"currentValue\":987654}]}}")!;

        Assert.Equal("987,654 of 1,234,567 credits", ZaiAPI.Limits(json)!.Windows[0].Detail);
    }

    // ---- PR-PROVAPI-070: reset cards, order and skip rules ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:138-141
    public async Task ResetCredits_is_nil_on_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(500, "Internal Server Error", []);

        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:140 (code != 200)
    public async Task ResetCredits_is_nil_when_code_is_not_200()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"code\":400,\"data\":{}}"), ("Content-Type", "application/json"));

        Assert.Null(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:143-153
    public async Task ResetCredits_lists_all_five_hour_cards_first_then_all_weekly_cards()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{" +
            "\"fiveHourResets\":[{\"recordId\":1},{\"recordId\":2}]," +
            "\"weekResets\":[{\"recordId\":3}]" +
            "}}"), ("Content-Type", "application/json"));

        var credits = (await pending.WaitAsync(Guard))!.Credits;

        Assert.Equal(["FIVE_HOUR:1", "FIVE_HOUR:2", "WEEK:3"], credits.Select(c => c.Id));
        Assert.Equal("Resets the 5-hour limit", credits[0].Title);
        Assert.Equal("Resets the 5-hour limit", credits[1].Title);
        Assert.Equal("Resets the weekly limit", credits[2].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:145 (available == false is skipped)
    public async Task ResetCredits_skips_a_card_whose_available_field_is_exactly_false()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{" +
            "\"fiveHourResets\":[{\"recordId\":1,\"available\":false},{\"recordId\":2,\"available\":true}]," +
            "\"weekResets\":[]" +
            "}}"), ("Content-Type", "application/json"));

        var result = await pending.WaitAsync(Guard);

        Assert.Equal(1, result!.AvailableCount);
        Assert.Equal("FIVE_HOUR:2", result.Credits[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:145 (missing available field is kept)
    public async Task ResetCredits_keeps_a_card_whose_available_field_is_missing_rather_than_false()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{\"fiveHourResets\":[{\"recordId\":7}],\"weekResets\":[]}}"),
            ("Content-Type", "application/json"));

        var result = await pending.WaitAsync(Guard);

        Assert.Equal(1, result!.AvailableCount);
        Assert.Equal("FIVE_HOUR:7", result.Credits[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-070")] // Services/Providers/ZaiAPI.swift:145 (missing recordId is skipped)
    public async Task ResetCredits_skips_a_card_with_no_recordId()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{\"fiveHourResets\":[{\"available\":true}],\"weekResets\":[]}}"),
            ("Content-Type", "application/json"));

        var result = await pending.WaitAsync(Guard);

        Assert.Equal(0, result!.AvailableCount);
        Assert.Empty(result.Credits);
    }

    // ---- PR-PROVAPI-071: consumeResetCredit -------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:159-160
    public async Task ConsumeResetCredit_throws_unknownOutcome_for_a_malformed_credit_id()
    {
        using var runtime = new TestRuntime();

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => ZaiAPI.ConsumeResetCredit("key-abc", "no-colon-here"));
        Assert.IsType<ResetCreditError.Case.UnknownOutcome>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:160 (Int(parts[1]))
    public async Task ConsumeResetCredit_throws_unknownOutcome_when_the_record_part_is_not_an_integer()
    {
        using var runtime = new TestRuntime();

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => ZaiAPI.ConsumeResetCredit("key-abc", "FIVE_HOUR:abc"));
        Assert.IsType<ResetCreditError.Case.UnknownOutcome>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:161-171
    public async Task ConsumeResetCredit_posts_the_four_body_fields_with_a_lowercased_UUID_requestId()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", "FIVE_HOUR:42", server.Url("/reset-use"));
        var exchange = await server.NextExchange();

        Assert.Equal("POST", exchange.Method);
        Assert.Equal(["Bearer key-abc"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Content-Type"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        var body = JSONValue.Parse(exchange.Body)!;
        Assert.Equal("PERSONAL", body["targetType"]!.JSONValueString);
        Assert.Equal("FIVE_HOUR", body["resetType"]!.JSONValueString);
        Assert.Equal(42, body["recordId"]!.JSONValueInt);
        var requestId = body["requestId"]!.JSONValueString!;
        Assert.Equal(SwiftString.Lowercased(requestId), requestId);
        Assert.True(Guid.TryParse(requestId, out _), "requestId must be a UUID string");

        await exchange.Respond(200, "OK", Ascii("{\"code\":200,\"success\":true}"), ("Content-Type", "application/json"));
        await AdvanceUntilCompleted(runtime, pending);
        Assert.Equal(PlanLimits.ResetOutcome.Reset, await pending.WaitAsync(Guard));
    }

    /// <summary>
    /// The HTTP round trip above completes over a real loopback socket, on the .NET thread pool's own
    /// schedule, so the moment <see cref="ZaiAPI.ConsumeResetCredit"/> reaches its <c>try? await
    /// Task.sleep(for: .seconds(2))</c> (:178-179) and arms a timer on the manual clock cannot be pinned to a
    /// single poll: advancing the virtual clock before that timer exists does nothing for it. This alternates
    /// a short real wait (to let the pending continuation actually run) with a virtual advance, so whenever
    /// the settle timer does get armed, the very next iteration's advance reaches and fires it.
    /// </summary>
    private static async Task AdvanceUntilCompleted(TestRuntime runtime, Task pending)
    {
        for (var iteration = 0; iteration < 400 && !pending.IsCompleted; iteration++)
        {
            await Task.Delay(5);
            runtime.Advance(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:174
    public async Task ConsumeResetCredit_throws_unknownOutcome_for_an_unparseable_response()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", "WEEK:1", server.Url("/reset-use"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("not json"), ("Content-Type", "application/json"));

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => pending);
        Assert.IsType<ResetCreditError.Case.UnknownOutcome>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:175-176
    public async Task ConsumeResetCredit_throws_rejected_carrying_the_msg_field()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", "WEEK:1", server.Url("/reset-use"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"code\":409,\"success\":false,\"msg\":\"Too soon.\"}"),
            ("Content-Type", "application/json"));

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => pending);
        var rejected = Assert.IsType<ResetCreditError.Case.Rejected>(error.ErrorCase);
        Assert.Equal("Too soon.", rejected.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:176 (msg absent)
    public async Task ConsumeResetCredit_rejected_falls_back_to_the_pinned_default_message_when_msg_is_absent()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", "WEEK:1", server.Url("/reset-use"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"code\":409,\"success\":false}"), ("Content-Type", "application/json"));

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => pending);
        var rejected = Assert.IsType<ResetCreditError.Case.Rejected>(error.ErrorCase);
        Assert.Equal("Z.ai declined the reset.", rejected.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:178-180 (the two second settle delay)
    public async Task ConsumeResetCredit_does_not_complete_until_the_two_second_delay_elapses()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", "FIVE_HOUR:1", server.Url("/reset-use"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"code\":200,\"success\":true}"), ("Content-Type", "application/json"));

        var before = runtime.Time.Elapsed;
        await AdvanceUntilCompleted(runtime, pending);
        var afterCompletion = await pending.WaitAsync(Guard);

        // The manual clock only ever moves when this test advances it (ED-080), so however many polls it took
        // to reach completion, the virtual time actually spent must be at least the pinned 2 second delay.
        Assert.True(runtime.Time.Elapsed - before >= TimeSpan.FromSeconds(2),
            $"resolved after only {runtime.Time.Elapsed - before} of virtual time; the 2 second settle delay must elapse first");
        Assert.Equal(PlanLimits.ResetOutcome.Reset, afterCompletion);
    }

    // ---- PR-PROVAPI-072: card expiry parsed as naive Shanghai local time ------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-072")] // Services/Providers/ZaiAPI.swift:183-194 (resetDate/resetDateFormatter)
    public async Task ResetCredits_expiresAt_parses_the_pattern_in_Asia_Shanghai_local_time()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{\"fiveHourResets\":[{\"recordId\":1,\"expireTime\":\"2026-01-15 08:00:00\"}],\"weekResets\":[]}}"),
            ("Content-Type", "application/json"));

        var credit = (await pending.WaitAsync(Guard))!.Credits[0];

        // Asia/Shanghai is UTC+8 year round (no DST), so 08:00 local is 00:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), credit.ExpiresAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-072")] // Services/Providers/ZaiAPI.swift:183-194 (unparseable; resetDate/resetDateFormatter)
    public async Task ResetCredits_expiresAt_is_null_for_an_unparseable_string()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ResetCredits("key-abc", server.Url("/reset"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii(
            "{\"code\":200,\"data\":{\"fiveHourResets\":[{\"recordId\":1,\"expireTime\":\"not a date\"}],\"weekResets\":[]}}"),
            ("Content-Type", "application/json"));

        var credit = (await pending.WaitAsync(Guard))!.Credits[0];

        Assert.Null(credit.ExpiresAt);
    }

    // ---- UI-PROVAPI-028: the model picker's rows end to end --------------------------------------------------

    [Fact]
    [Trait("Req", "UI-PROVAPI-028")] // Services/Providers/ZaiAPI.swift:14,30-45; ProviderRegistry.swift:130
    public async Task The_model_picker_shows_GLM_5_3_first_then_GLM_5_3_Flash_both_with_max_preselected()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var body = Ascii("{\"data\":[{\"id\":\"glm-5.3-flash\"},{\"id\":\"glm-5.3\"}]}");
        var pending = ZaiAPI.ListModels("key-abc", server.Url("/api/coding/paas/v4/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", body, ("Content-Type", "application/json"));

        var options = await pending.WaitAsync(Guard);

        Assert.Equal(2, options.Count);
        Assert.Equal("GLM-5.3", options[0].Name);
        Assert.True(options[0].IsDefault);
        Assert.Equal("GLM-5.3 Flash", options[1].Name);
        Assert.False(options[1].IsDefault);
        Assert.All(options, o => Assert.Equal(["low", "high", "max"], o.Efforts));
        Assert.All(options, o => Assert.Equal("max", o.DefaultEffort));
    }
}
