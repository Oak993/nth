using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). The OpenAI-compatible API client of OpenRouter, in the manner of
// DeepSeekAPI/MetaAPI/ZaiAPI (OpenAICompatibleAPI.swift:5-34 for the bearer request and the id-ordered list). Every
// request goes to https://openrouter.ai through the HTTP client's test seam (URLSession.Transport, OD-0057), answered
// in this process: no socket is opened and no real key or credential store is read (R-TEST-9, R-TEST-10).
// The detail strings join with U+00B7 MIDDLE DOT, as the source's model details do (MetaAPI.swift:46-53).
[Collection(OpenAICompatibleSessionCollection.Name)] // the model facts are process-wide
public sealed class OpenRouterAPITests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private const string MiddleDot = "·"; // U+00B7 MIDDLE DOT

    private static ModelOption Row(ValueList<ModelOption> list, string id) => Assert.Single(list, option => option.Id == id);

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_endpoints_are_the_OpenRouter_v1_API()
    {
        Assert.Equal(new Uri("https://openrouter.ai/api/v1"), OpenRouterAPI.BaseURL);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), OpenRouterAPI.ChatURL);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/models"), OpenRouterAPI.ModelsURL);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/key"), OpenRouterAPI.KeyURL);
    }

    // ---- the model catalogue: the request ------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_catalogue_is_one_GET_of_the_models_endpoint_with_the_bearer_key_and_a_json_accept()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        await OpenRouterAPI.ListModels("  " + OpenRouterBodies.Key + " \n").WaitAsync(Guard);

        var seen = Assert.Single(handler.Received);
        Assert.Equal("GET", seen.Method);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/models"), seen.Url);
        Assert.Equal(OpenRouterBodies.Bearer, seen.Header("Authorization")); // the key arrives trimmed
        Assert.Equal("application/json", seen.Header("Accept"));
        Assert.Empty(seen.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    [Trait("Decision", "ED-234")]
    public async Task An_empty_key_lists_nothing_and_sends_nothing(string key)
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        Assert.Empty(await OpenRouterAPI.ListModels(key).WaitAsync(Guard));
        Assert.False(await OpenRouterAPI.Validate(key).WaitAsync(Guard));
        Assert.Empty(handler.Received);
    }

    // ---- the model catalogue: the parse --------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_catalogue_has_one_row_per_listed_id_with_the_default_first_and_the_rest_in_id_order()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        var list = await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        // The row without an id is dropped; the router row is the default and leads.
        Assert.Equal(
            ["openrouter/auto", "anthropic/claude-sonnet-4.5", "deepseek/deepseek-r1", "meta-llama/llama-3.3-70b-instruct:free"],
            list.Select(option => option.Id));
        Assert.Equal(["openrouter/auto"], list.Where(option => option.IsDefault).Select(option => option.Id));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_row_takes_its_name_without_the_vendor_lead_and_a_detail_of_vendor_context_and_price()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        var list = await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        var sonnet = Row(list, "anthropic/claude-sonnet-4.5");
        Assert.Equal("Claude Sonnet 4.5", sonnet.Name); // "Anthropic: " is dropped: the id names the vendor
        Assert.Equal($"anthropic {MiddleDot} 1M context {MiddleDot} $3 / $15 per 1M", sonnet.Detail);

        var deepseek = Row(list, "deepseek/deepseek-r1");
        Assert.Equal("R1", deepseek.Name);
        Assert.Equal($"deepseek {MiddleDot} 164K context {MiddleDot} $0.5 / $2.18 per 1M", deepseek.Detail); // per token x one million

        var free = Row(list, "meta-llama/llama-3.3-70b-instruct:free");
        Assert.Equal("Llama 3.3 70B Instruct (free)", free.Name);
        Assert.Equal($"meta-llama {MiddleDot} 66K context {MiddleDot} free", free.Detail); // 0 and 0 is free

        var auto = Row(list, "openrouter/auto");
        Assert.Equal("Auto Router", auto.Name); // no colon: the name stands as listed
        Assert.Equal($"openrouter {MiddleDot} 2M context", auto.Detail); // "-1" is not a price, so none is shown
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_model_that_reasons_offers_low_medium_and_high_and_one_that_does_not_offers_no_scale()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        var list = await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        var sonnet = Row(list, "anthropic/claude-sonnet-4.5"); // supported_parameters has "reasoning"
        Assert.Equal(["low", "medium", "high"], sonnet.Efforts);
        Assert.Equal("medium", sonnet.DefaultEffort);
        var deepseek = Row(list, "deepseek/deepseek-r1"); // supported_parameters has "include_reasoning"
        Assert.Equal(["low", "medium", "high"], deepseek.Efforts);
        var free = Row(list, "meta-llama/llama-3.3-70b-instruct:free");
        Assert.Empty(free.Efforts);
        Assert.Null(free.DefaultEffort);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_models_the_list_describes_say_their_context_window_and_whether_they_take_images()
    {
        using var runtime = new TestRuntime();
        OpenRouterAPI.ForgetModelsForTests();
        Assert.Equal(OpenRouterAPI.DefaultContextWindow, OpenRouterAPI.ContextWindow("anthropic/claude-sonnet-4.5")); // not listed yet
        Assert.False(OpenRouterAPI.IsVisionModel("anthropic/claude-sonnet-4.5"));
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);

        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        Assert.Equal(1_000_000, OpenRouterAPI.ContextWindow("anthropic/claude-sonnet-4.5"));
        Assert.Equal(65_536, OpenRouterAPI.ContextWindow("meta-llama/llama-3.3-70b-instruct:free"));
        Assert.Equal(OpenRouterAPI.DefaultContextWindow, OpenRouterAPI.ContextWindow("not/listed"));
        Assert.True(OpenRouterAPI.IsVisionModel("anthropic/claude-sonnet-4.5")); // input_modalities has "image"
        Assert.False(OpenRouterAPI.IsVisionModel("meta-llama/llama-3.3-70b-instruct:free")); // text only
        Assert.False(OpenRouterAPI.IsVisionModel("openrouter/auto")); // no architecture at all
        Assert.False(OpenRouterAPI.IsVisionModel(null));
        OpenRouterAPI.ForgetModelsForTests();
    }

    [Theory]
    [InlineData("""{"id":"x/y"}""", "x/y", "x")]
    [InlineData("""{"id":"x/y","name":"","context_length":0}""", "x/y", "x")]
    [InlineData("""{"id":"x/y","name":"Lab: Why: This","context_length":8192,"pricing":{"prompt":"0.000001","completion":"0.000002"}}""", "Why: This", $"x {MiddleDot} 8K context {MiddleDot} $1 / $2 per 1M")]
    [InlineData("""{"id":"x/y","name":"Num","context_length":8192,"pricing":{"prompt":0.000001,"completion":0.000002}}""", "Num", $"x {MiddleDot} 8K context {MiddleDot} $1 / $2 per 1M")]
    [InlineData("""{"id":"plain","name":"Plain","context_length":512,"pricing":{"prompt":"0.00000125","completion":"abc"}}""", "Plain", "512 context")]
    [InlineData("""{"id":"plain","name":"Lab:"}""", "Lab:", null)]
    [InlineData("""{"id":"plain","name":"Lab: ","context_length":300000,"pricing":{"prompt":"0.00000125","completion":"0.00001"}}""", "Lab: ", $"300K context {MiddleDot} $1.25 / $10 per 1M")]
    [InlineData("""{"id":"a/b","name":"N","context_length":1500000}""", "N", $"a {MiddleDot} 1.5M context")]
    [InlineData("""{"id":"a/b","name":"N","context_length":1000}""", "N", $"a {MiddleDot} 1K context")]
    [InlineData("""{"id":"a/b","name":"N","context_length":999}""", "N", $"a {MiddleDot} 999 context")]
    [InlineData("""{"id":"a/b","name":"N","pricing":{"prompt":"0","completion":"0.000001"}}""", "N", $"a {MiddleDot} $0 / $1 per 1M")]
    [InlineData("""{"id":"a/b","name":"N","pricing":{"prompt":"0.000001","completion":"0"}}""", "N", $"a {MiddleDot} $1 / $0 per 1M")]
    [InlineData("""{"id":"a/b","name":"N","pricing":{"prompt":"-1","completion":"0.000001"}}""", "N", "a")]
    [Trait("Decision", "ED-234")]
    public void The_edge_cases_of_a_row_fall_back_without_inventing_fields(string json, string name, string? detail)
    {
        var option = OpenRouterAPI.Option(OpenRouterAPI.Parse(JSONValue.Parse(json)!)!);

        Assert.Equal(name, option.Name);
        Assert.Equal(detail, option.Detail);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void An_item_without_a_string_id_is_not_a_model()
    {
        Assert.Null(OpenRouterAPI.Parse(JSONValue.Parse("""{"name":"No id"}""")!));
        Assert.Null(OpenRouterAPI.Parse(JSONValue.Parse("""{"id":7,"name":"Numeric id"}""")!));
        Assert.Null(OpenRouterAPI.Parse(JSONValue.Parse("""{"id":""}""")!));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_offline_seed_is_the_router_alone_and_the_default()
    {
        var seed = OpenRouterAPI.SeedOption;
        Assert.Equal("openrouter/auto", seed.Id);
        Assert.Equal("Auto Router", seed.Name);
        Assert.True(seed.IsDefault);
        Assert.Equal($"openrouter {MiddleDot} 2M context", seed.Detail);
        Assert.Empty(seed.Efforts);
        Assert.Equal("openrouter/auto", OpenRouterAPI.AutoModelId);
    }

    // ---- the model catalogue: answers that are not a list --------------------------------------------------------

    [Theory]
    [InlineData(401, """{"error":{"message":"No auth credentials found"}}""")]
    [InlineData(500, "oops")]
    [InlineData(200, "not json")]
    [InlineData(200, """{"data":[]}""")]
    [InlineData(200, """{"data":[{"name":"no id"}]}""")]
    [InlineData(200, """{"models":[]}""")]
    [Trait("Decision", "ED-234")]
    public async Task A_non_200_answer_or_a_body_without_rows_is_an_empty_catalogue(int status, string body)
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(status, body) };
        using var scope = URLSession.Transport.Override(handler);

        Assert.Empty(await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_failed_answer_leaves_the_facts_of_the_last_good_list_in_place()
    {
        using var runtime = new TestRuntime();
        OpenRouterAPI.ForgetModelsForTests();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);
        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        handler.Answer = _ => InProcessHttpHandler.Reply(500, "oops");
        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        Assert.Equal(1_000_000, OpenRouterAPI.ContextWindow("anthropic/claude-sonnet-4.5"));
        OpenRouterAPI.ForgetModelsForTests();
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_list_with_no_usable_rows_leaves_the_facts_of_the_last_good_list_in_place()
    {
        using var runtime = new TestRuntime();
        OpenRouterAPI.ForgetModelsForTests();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);
        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        handler.Answer = _ => InProcessHttpHandler.Reply(200, """{"data":[{"name":"no id"}]}""");
        Assert.Empty(await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard));

        Assert.Equal(1_000_000, OpenRouterAPI.ContextWindow("anthropic/claude-sonnet-4.5"));
        Assert.True(OpenRouterAPI.IsVisionModel("anthropic/claude-sonnet-4.5"));
        OpenRouterAPI.ForgetModelsForTests();
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_new_list_replaces_the_facts_of_the_old_one()
    {
        using var runtime = new TestRuntime();
        OpenRouterAPI.ForgetModelsForTests();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson) };
        using var scope = URLSession.Transport.Override(handler);
        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        handler.Answer = _ => InProcessHttpHandler.Reply(200, """{"data":[{"id":"x/new","name":"New","context_length":4096}]}""");
        await OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard);

        Assert.Equal(4096, OpenRouterAPI.ContextWindow("x/new"));
        Assert.Equal(OpenRouterAPI.DefaultContextWindow, OpenRouterAPI.ContextWindow("anthropic/claude-sonnet-4.5")); // no longer listed
        OpenRouterAPI.ForgetModelsForTests();
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_transport_failure_reaches_the_caller_as_the_other_API_providers_lists_do()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler(); // nothing scripted: the connection is refused, no socket is opened
        using var scope = URLSession.Transport.Override(handler);

        var error = await Assert.ThrowsAsync<URLError>(() => OpenRouterAPI.ListModels(OpenRouterBodies.Key).WaitAsync(Guard));

        Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);
    }

    // ---- the key check -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_key_is_checked_by_one_GET_of_the_key_endpoint_with_the_bearer_key()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, """{"data":{"label":"sk-or-v1-abc"}}""") };
        using var scope = URLSession.Transport.Override(handler);

        Assert.True(await OpenRouterAPI.Validate(" " + OpenRouterBodies.Key + " ").WaitAsync(Guard));

        var seen = Assert.Single(handler.Received);
        Assert.Equal("GET", seen.Method);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/key"), seen.Url);
        Assert.Equal(OpenRouterBodies.Bearer, seen.Header("Authorization"));
        Assert.Equal("application/json", seen.Header("Accept"));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [Trait("Decision", "ED-234")]
    public async Task A_key_the_endpoint_does_not_accept_is_not_valid(int status)
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(status, """{"error":{"message":"No auth credentials found"}}""") };
        using var scope = URLSession.Transport.Override(handler);

        Assert.False(await OpenRouterAPI.Validate(OpenRouterBodies.Key).WaitAsync(Guard));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_key_check_that_cannot_reach_the_host_is_not_valid_and_does_not_throw()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler(); // refused
        using var scope = URLSession.Transport.Override(handler);

        Assert.False(await OpenRouterAPI.Validate(OpenRouterBodies.Key).WaitAsync(Guard));
        Assert.Single(handler.Received);
    }
}
