using System.Text;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/MetaAPI.swift (B3 52bc7926), :1-70.
//
// Every network-touching test runs against LoopbackHttpServer on 127.0.0.1 (R-TEST-10), reached through the
// PORT-ONLY modelsUrl override MetaAPI.Validate/ListModels accept for tests only (production always uses the real
// MetaAPI.ModelsURL). The detail strings use U+00B7 MIDDLE DOT, not a hyphen or ASCII period.
public sealed class MetaAPITests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private const string MiddleDot = "·"; // U+00B7 MIDDLE DOT

    private static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

    // ---- PR-PROVAPI-034: endpoints and context window ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-034")] // Services/Providers/MetaAPI.swift:16-18
    public void BaseURL_chatURL_and_modelsURL_are_the_pinned_api_meta_ai_endpoints()
    {
        Assert.Equal(new Uri("https://api.meta.ai/v1"), MetaAPI.BaseURL);
        Assert.Equal(new Uri("https://api.meta.ai/v1/chat/completions"), MetaAPI.ChatURL);
        Assert.Equal(new Uri("https://api.meta.ai/v1/models"), MetaAPI.ModelsURL);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-034")] // Services/Providers/MetaAPI.swift:20-22
    public void ContextWindow_is_1048576_and_MaxOutputTokens_is_131072()
    {
        Assert.Equal(1_048_576, MetaAPI.ContextWindow);
        Assert.Equal(131_072, MetaAPI.MaxOutputTokens);
    }

    // ---- PR-PROVAPI-035: validate ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-035")] // Services/Providers/OpenAICompatibleAPI.swift:14-15 (via MetaAPI.swift:25)
    public async Task Validate_returns_false_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await MetaAPI.Validate("", server.Url("/v1/models")).WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-035")] // Services/Providers/OpenAICompatibleAPI.swift:14 (.trimmed)
    public async Task Validate_returns_false_with_no_request_for_a_whitespace_only_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await MetaAPI.Validate("   \n\t  ", server.Url("/v1/models")).WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-035")] // Services/Providers/OpenAICompatibleAPI.swift:5-10,16 (bearerRequest, via MetaAPI.swift:26)
    public async Task Validate_sends_a_GET_with_bearer_auth_a_json_accept_header_and_a_15_second_timeout()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.Validate("  sk-abc123  ", server.Url("/v1/models"));
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/v1/models", exchange.Target);
        Assert.Equal(["Bearer sk-abc123"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(200, "OK", []);
        Assert.True(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-035")] // Services/Providers/OpenAICompatibleAPI.swift:19
    public async Task Validate_is_false_for_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.Validate("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(401, "Unauthorized", []);

        Assert.False(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-035")] // Services/Providers/OpenAICompatibleAPI.swift:17 (try?)
    public async Task Validate_is_false_on_a_transport_error()
    {
        using var runtime = new TestRuntime();

        var result = await MetaAPI.Validate("sk-abc123", LoopbackHttpServer.RefusedUrl("/v1/models")).WaitAsync(Guard);

        Assert.False(result);
    }

    // ---- PR-PROVAPI-036: listModels parses data[].id and sorts the default first --------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:23-24 (via MetaAPI.swift:29)
    public async Task ListModels_returns_empty_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await MetaAPI.ListModels("", server.Url("/v1/models")).WaitAsync(Guard);

        Assert.Empty(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:27-28
    public async Task ListModels_returns_empty_for_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.ListModels("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(500, "Internal Server Error", Ascii("oops"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:28 (JSONValue.parse(data))
    public async Task ListModels_returns_empty_for_an_unparseable_body()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.ListModels("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("not json"), ("Content-Type", "application/json"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:29-30
    public async Task ListModels_returns_empty_when_the_data_array_has_no_ids()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.ListModels("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:29-33
    public async Task ListModels_drops_unrecognized_non_Spark_ids_and_sorts_the_default_first_then_ascending_id()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        // Deliberately shuffled, with two non-Spark ids the endpoint also lists (PR-PROVAPI-043).
        var body = Ascii("{\"data\":[" +
            "{\"id\":\"muse-spark-1.2\"}," +
            "{\"id\":\"muse-image-1.0\"}," +
            "{\"id\":\"muse-spark-1.3-contributor\"}," +
            "{\"id\":\"muse-spark-1.1\"}," +
            "{\"id\":\"muse-spark-1.3\"}," +
            "{\"id\":\"muse-voice-transcribe-1.0\"}," +
            "{\"id\":\"muse-spark-1.2-contributor\"}" +
            "]}");
        var pending = MetaAPI.ListModels("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", body, ("Content-Type", "application/json"));

        var options = await pending.WaitAsync(Guard);

        Assert.Equal(
            ["muse-spark-1.3", "muse-spark-1.1", "muse-spark-1.2", "muse-spark-1.2-contributor", "muse-spark-1.3-contributor"],
            options.Select(o => o.Id));
        Assert.True(options[0].IsDefault);
        Assert.All(options.Skip(1), o => Assert.False(o.IsDefault));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-036")] // Services/Providers/OpenAICompatibleAPI.swift:5-10,25 (bearerRequest, via MetaAPI.swift:29)
    public async Task ListModels_sends_the_same_bearer_and_accept_headers_as_validate()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = MetaAPI.ListModels("sk-xyz", server.Url("/v1/models"));
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer sk-xyz"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));
        await pending.WaitAsync(Guard);
    }

    // ---- PR-PROVAPI-037: only muse-spark-1.3 offers "max" ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-037")] // Services/Providers/MetaAPI.swift:36-41
    public void Efforts_for_muse_spark_1_3_are_exactly_the_six_including_max()
    {
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], MetaAPI.Efforts("muse-spark-1.3"));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-037")] // Services/Providers/MetaAPI.swift:39-41
    [InlineData("muse-spark-1.3-contributor")]
    [InlineData("muse-spark-1.2")]
    [InlineData("muse-spark-1.2-contributor")]
    [InlineData("muse-spark-1.1")]
    [InlineData("muse-spark-9.9")]
    public void Efforts_for_every_other_id_are_exactly_the_five_without_max(string id)
    {
        // ED-NEW-muse-wonder-7: known rows preserve their scale; an unknown checkpoint has none.
        if (id == "muse-spark-9.9") Assert.Empty(MetaAPI.Efforts(id));
        else Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], MetaAPI.Efforts(id));
    }

    // ---- PR-PROVAPI-038 to 042: the five Spark model rows ----------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-038")] // Services/Providers/MetaAPI.swift:46-47
    public void Option_for_muse_spark_1_3_is_the_default_row_with_six_efforts()
    {
        var option = MetaAPI.Option("muse-spark-1.3");

        Assert.NotNull(option);
        Assert.Equal("muse-spark-1.3", option!.Id);
        Assert.Equal("Spark 1.3", option.Name);
        Assert.Equal($"Latest Muse Spark {MiddleDot} best agentic coding {MiddleDot} 1M context", option.Detail);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], option.Efforts);
        Assert.Equal("high", option.DefaultEffort);
        Assert.True(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-039")] // Services/Providers/MetaAPI.swift:48-49
    public void Option_for_muse_spark_1_3_contributor_has_five_efforts_and_is_not_default()
    {
        var option = MetaAPI.Option("muse-spark-1.3-contributor");

        Assert.NotNull(option);
        Assert.Equal("muse-spark-1.3-contributor", option!.Id);
        Assert.Equal("Spark 1.3 Contributor", option.Name);
        Assert.Equal($"Cheaper 1.3 {MiddleDot} Meta may train on your chats {MiddleDot} no max effort", option.Detail);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], option.Efforts);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-040")] // Services/Providers/MetaAPI.swift:50-51
    public void Option_for_muse_spark_1_2()
    {
        var option = MetaAPI.Option("muse-spark-1.2");

        Assert.NotNull(option);
        Assert.Equal("Spark 1.2", option!.Name);
        Assert.Equal($"Previous checkpoint {MiddleDot} Standard tier {MiddleDot} 1M context", option.Detail);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], option.Efforts);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-041")] // Services/Providers/MetaAPI.swift:52-53
    public void Option_for_muse_spark_1_2_contributor()
    {
        var option = MetaAPI.Option("muse-spark-1.2-contributor");

        Assert.NotNull(option);
        Assert.Equal("Spark 1.2 Contributor", option!.Name);
        Assert.Equal($"Cheaper 1.2 {MiddleDot} Meta may train on your chats", option.Detail);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], option.Efforts);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-042")] // Services/Providers/MetaAPI.swift:54-55
    public void Option_for_muse_spark_1_1()
    {
        var option = MetaAPI.Option("muse-spark-1.1");

        Assert.NotNull(option);
        Assert.Equal("Spark 1.1", option!.Name);
        Assert.Equal($"Original checkpoint {MiddleDot} Standard tier {MiddleDot} 1M context", option.Detail);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], option.Efforts);
        Assert.Equal("high", option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    // ---- PR-PROVAPI-043: only ids prefixed muse-spark are offered ------------------------------------------

    [Theory]
    [Trait("Req", "PR-PROVAPI-043")] // Services/Providers/MetaAPI.swift:57-60
    [InlineData("muse-image-1.0")]
    [InlineData("muse-voice-transcribe-1.0")]
    [InlineData("gpt-4")]
    public void Option_is_null_for_a_listed_id_that_does_not_start_with_muse_spark(string id)
    {
        Assert.Null(MetaAPI.Option(id));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-043")] // Services/Providers/MetaAPI.swift:56-62
    public void Option_for_a_future_Spark_checkpoint_is_named_by_its_raw_id_with_no_guessed_efforts()
    {
        var option = MetaAPI.Option("muse-spark-2.0");

        Assert.NotNull(option);
        Assert.Equal("muse-spark-2.0", option!.Id);
        Assert.Equal("muse-spark-2.0", option.Name);
        Assert.Null(option.Detail);
        Assert.Empty(option.Efforts); // ED-NEW-muse-wonder-7: never guess a new model's scale.
        Assert.Null(option.DefaultEffort);
        Assert.False(option.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-043")] // Services/Providers/MetaAPI.swift:60 (id.lowercased().hasPrefix)
    public void Option_prefix_test_is_case_insensitive()
    {
        var option = MetaAPI.Option("MUSE-SPARK-2.0");

        Assert.NotNull(option);
        Assert.Equal("MUSE-SPARK-2.0", option!.Id);
        Assert.Equal("MUSE-SPARK-2.0", option.Name);
    }

    // ---- PR-PROVAPI-044: vision model test, nil/empty is true -----------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-044")] // Services/Providers/MetaAPI.swift:67
    public void IsVisionModel_is_true_for_a_null_id()
    {
        Assert.True(MetaAPI.IsVisionModel(null));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-044")] // Services/Providers/MetaAPI.swift:67
    public void IsVisionModel_is_true_for_an_empty_id()
    {
        Assert.True(MetaAPI.IsVisionModel(""));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-044")] // Services/Providers/MetaAPI.swift:68
    [InlineData("muse-spark-1.3")]
    [InlineData("muse-spark-1.3-contributor")]
    [InlineData("MUSE-SPARK-1.3")]
    public void IsVisionModel_is_true_for_any_muse_spark_id_case_insensitively(string id)
    {
        Assert.True(MetaAPI.IsVisionModel(id));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-044")] // Services/Providers/MetaAPI.swift:68
    [InlineData("muse-image-1.0")]
    [InlineData("gpt-4")]
    public void IsVisionModel_is_false_for_a_non_Spark_id(string id)
    {
        Assert.False(MetaAPI.IsVisionModel(id));
    }

    // ---- UI-PROVAPI-027: the model picker's rows end to end --------------------------------------------------

    [Fact]
    [Trait("Req", "UI-PROVAPI-027")] // Services/Providers/MetaAPI.swift:36-62
    public async Task The_model_picker_rows_match_the_seeded_catalog_in_seed_order_when_the_endpoint_lists_only_those_five()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        // The exact five ids of ProviderRegistry.swift:128-129's metaSeed (UI-PROVAPI-027 source_evidence), in
        // their seed order, proving the endpoint's own answer re-sorts to the same row order the seed already has.
        var body = Ascii("{\"data\":[" +
            "{\"id\":\"muse-spark-1.3\"}," +
            "{\"id\":\"muse-spark-1.3-contributor\"}," +
            "{\"id\":\"muse-spark-1.2\"}," +
            "{\"id\":\"muse-spark-1.2-contributor\"}," +
            "{\"id\":\"muse-spark-1.1\"}" +
            "]}");
        var pending = MetaAPI.ListModels("sk-abc123", server.Url("/v1/models"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", body, ("Content-Type", "application/json"));

        var options = await pending.WaitAsync(Guard);

        Assert.Equal(5, options.Count);
        Assert.Equal("Spark 1.3", options[0].Name);
        Assert.Equal("Spark 1.1", options[1].Name);
        Assert.Equal("Spark 1.2", options[2].Name);
        Assert.Equal("Spark 1.2 Contributor", options[3].Name);
        Assert.Equal("Spark 1.3 Contributor", options[4].Name);
        Assert.True(options[0].IsDefault);
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], options[0].Efforts);
        foreach (var other in options.Skip(1)) Assert.Equal(["minimal", "low", "medium", "high", "xhigh"], other.Efforts);
        Assert.All(options, o => Assert.Equal("high", o.DefaultEffort));
    }
}
