using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleAPI.swift (B3 52bc7926), :1-35.
//
// Every network-touching test runs against LoopbackHttpServer on 127.0.0.1 (R-TEST-10). This is the shared
// implementation MetaAPI, DeepSeekAPI (via its ModelsURL only -- DeepSeekAPI has no port-only URL override, so
// its own tests exercise only the pre-network empty/whitespace-key path) and ZaiAPI delegate to; the network
// paths (bearer headers, non-200, transport error, parse failure, sort order) are proven once here and are the
// same behaviour MetaAPITests.cs and ZaiAPITests.cs re-cite to this file (PR-PROVAPI-035/036 and -057/058).
public sealed class OpenAICompatibleAPITests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    private static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

    private static ModelOption? Option(string id) => id == "b"
        ? new ModelOption(id: id, name: id, isDefault: true)
        : new ModelOption(id: id, name: id);

    // ---- BearerRequest: :5-10 -----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:5-10
    public void BearerRequest_sets_a_15_second_timeout_bearer_auth_and_json_accept()
    {
        var url = new Uri("https://example.invalid/models");

        var request = OpenAICompatibleAPI.BearerRequest(url, "key-abc");

        Assert.Equal(url, request.Url);
        Assert.Equal(15, request.TimeoutInterval);
        Assert.Equal("Bearer key-abc", request.Value("Authorization"));
        Assert.Equal("application/json", request.Value("Accept"));
    }

    // ---- Validate: :13-19 ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:14-15
    public async Task Validate_returns_false_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await OpenAICompatibleAPI.Validate(server.Url("/models"), "").WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:14 (.trimmed)
    public async Task Validate_returns_false_with_no_request_for_a_whitespace_only_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await OpenAICompatibleAPI.Validate(server.Url("/models"), "   \n\t  ").WaitAsync(Guard);

        Assert.False(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:16,19
    public async Task Validate_sends_a_GET_with_bearer_auth_and_a_json_accept_header_then_true_on_200()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = OpenAICompatibleAPI.Validate(server.Url("/models"), "  key-abc  ");
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/models", exchange.Target);
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

        var pending = OpenAICompatibleAPI.Validate(server.Url("/models"), "key-abc");
        var exchange = await server.NextExchange();
        await exchange.Respond(401, "Unauthorized", []);

        Assert.False(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-057")] // Services/Providers/OpenAICompatibleAPI.swift:17 (try?)
    public async Task Validate_is_false_on_a_transport_error()
    {
        using var runtime = new TestRuntime();

        var result = await OpenAICompatibleAPI.Validate(LoopbackHttpServer.RefusedUrl("/models"), "key-abc").WaitAsync(Guard);

        Assert.False(result);
    }

    // ---- ListModels: :22-34 --------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:23-24
    public async Task ListModels_returns_empty_with_no_request_for_an_empty_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var result = await OpenAICompatibleAPI.ListModels(server.Url("/models"), "", Option).WaitAsync(Guard);

        Assert.Empty(result);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:27-28
    public async Task ListModels_returns_empty_for_a_non_200_status()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = OpenAICompatibleAPI.ListModels(server.Url("/models"), "key-abc", Option);
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

        var pending = OpenAICompatibleAPI.ListModels(server.Url("/models"), "key-abc", Option);
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

        var pending = OpenAICompatibleAPI.ListModels(server.Url("/models"), "key-abc", Option);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));

        Assert.Empty(await pending.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:29,31-33
    public async Task ListModels_drops_ids_the_option_callback_rejects_and_sorts_the_default_first_then_ascending_id()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var body = Ascii("{\"data\":[{\"id\":\"c\"},{\"id\":\"rejected\"},{\"id\":\"b\"},{\"id\":\"a\"}]}");
        var pending = OpenAICompatibleAPI.ListModels(server.Url("/models"), "key-abc",
            id => id == "rejected" ? null : Option(id));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", body, ("Content-Type", "application/json"));

        var options = await pending.WaitAsync(Guard);

        Assert.Equal(["b", "a", "c"], options.Select(o => o.Id));
        Assert.True(options[0].IsDefault);
        Assert.All(options.Skip(1), o => Assert.False(o.IsDefault));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-058")] // Services/Providers/OpenAICompatibleAPI.swift:5-10,25 (bearerRequest)
    public async Task ListModels_sends_the_same_bearer_and_accept_headers_as_validate()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = OpenAICompatibleAPI.ListModels(server.Url("/models"), "key-xyz", Option);
        var exchange = await server.NextExchange();

        Assert.Equal("GET", exchange.Method);
        Assert.Equal(["Bearer key-xyz"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));

        await exchange.Respond(200, "OK", Ascii("{\"data\":[]}"), ("Content-Type", "application/json"));
        await pending.WaitAsync(Guard);
    }
}
