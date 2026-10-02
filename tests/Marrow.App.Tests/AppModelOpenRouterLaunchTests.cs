using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). AppModel.bootstrap() checks every provider (AppModel.swift:263) and
// then loads the catalogue of each key provider that is configured (:267-274); OpenRouter joins them, so a configured key
// has its model list in the registry when the app has started, ready for the composer's picker, and no key means no
// request. https://openrouter.ai is answered in this process through the HTTP client's test seam (URLSession.Transport,
// OD-0057), with a handler that refuses every other host; the key is made up and lives in AppModelHarness's in-memory
// Credential Manager. Nothing leaves the process (R-TEST-9, R-TEST-10).
public sealed class AppModelOpenRouterLaunchTests
{
    private static readonly string[] Listed =
        ["openrouter/auto", "anthropic/claude-sonnet-4.5", "deepseek/deepseek-r1", "meta-llama/llama-3.3-70b-instruct:free"];

    private static string Models(AppModel model, AppModelHarness h) =>
        string.Join(",", h.Main.OnMain(() => model.Providers.Models(ProviderKind.OpenRouter)).Select(option => option.Id));

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_configured_key_has_its_model_list_loaded_when_the_app_starts()
    {
        using var h = new AppModelHarness();
        using var handler = new InProcessHttpHandler
        {
            Answer = request => request.Url.AbsolutePath switch
            {
                "/api/v1/key" => InProcessHttpHandler.Reply(200, "{}"),
                "/api/v1/models" => InProcessHttpHandler.Reply(200, """{"data":[{"id":"openrouter/auto","name":"Auto Router","context_length":2000000},{"id":"anthropic/claude-sonnet-4.5","name":"Anthropic: Claude Sonnet 4.5","context_length":1000000},{"id":"deepseek/deepseek-r1","name":"DeepSeek: R1","context_length":163840},{"id":"meta-llama/llama-3.3-70b-instruct:free","name":"Meta: Llama 3.3 70B Instruct (free)","context_length":65536}]}"""),
                _ => throw new HttpRequestException("refused"),
            },
        };
        using var transport = URLSession.Transport.Override(handler);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.SetAPIKeyInput("sk-or-made-up-key", ProviderKind.OpenRouter));
        Assert.Equal("openrouter/auto", Models(model, h)); // the offline seed until the list has loaded

        h.Bootstrap(model);

        Assert.Equal(string.Join(",", Listed), Models(model, h));
        var toOpenRouter = handler.Received.Where(request => request.Url.Host == "openrouter.ai").ToList();
        Assert.Equal(["/api/v1/key", "/api/v1/models"], toOpenRouter.Select(request => request.Url.AbsolutePath).Order(StringComparer.Ordinal));
        Assert.All(toOpenRouter, request => Assert.Equal("Bearer sk-or-made-up-key", request.Header("Authorization")));
        Assert.Equal("Signed in", h.Main.OnMain(() => model.Providers.Status(ProviderKind.OpenRouter).Summary));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void With_no_key_the_app_starts_without_asking_OpenRouter_anything()
    {
        using var h = new AppModelHarness();
        using var handler = new InProcessHttpHandler { Answer = _ => throw new HttpRequestException("refused") };
        using var transport = URLSession.Transport.Override(handler);
        var model = h.MakeModel();

        h.Bootstrap(model);

        Assert.DoesNotContain(handler.Received, request => request.Url.Host == "openrouter.ai");
        Assert.Equal("openrouter/auto", Models(model, h)); // still the seed
        Assert.Equal("Not installed", h.Main.OnMain(() => model.Providers.Status(ProviderKind.OpenRouter).Summary));
    }
}
