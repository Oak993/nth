using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.ModelsPalette;

// PORT-ONLY: RR-19's catalogue/search prerequisite, OD-0049; ED-612.
// Exercises the real API parser and the source's Settings model search, with no socket or key store.
// This does not credit the absent approved picker UI, animation, placement or keyboard flow.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-234")]
public sealed class OpenRouterPickerCatalogueTests
{
    [Theory]
    [InlineData("kimi", "moonshotai/kimi-k2")]
    [InlineData("MOONSHOTAI", "moonshotai/kimi-k2")]
    [InlineData("moonshotai/kimi-k2", "moonshotai/kimi-k2")]
    [InlineData("claude", "anthropic/claude-sonnet-4.5")]
    public async Task API_rows_are_searchable_by_name_maker_and_exact_id(string query, string expected)
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler
        {
            Answer = _ => InProcessHttpHandler.Reply(200, """
                {"data":[
                  {"id":"moonshotai/kimi-k2","name":"MoonshotAI: Kimi K2"},
                  {"id":"anthropic/claude-sonnet-4.5","name":"Anthropic: Claude Sonnet 4.5"}
                ]}
                """)
        };
        using var transport = URLSession.Transport.Override(handler);
        var rows = await OpenRouterAPI.ListModels("fixture-key");
        var found = rows.Where(row => ModelsSettingsPage.Matches(row, row.Id, ProviderKind.OpenRouter, query));
        Assert.Equal(expected, Assert.Single(found).Id);
        Assert.Equal(OpenRouterAPI.ModelsURL, Assert.Single(handler.Received).Url);
    }

    [Fact]
    public async Task A_failed_API_refresh_does_not_manufacture_picker_models()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(503, "{}") };
        using var transport = URLSession.Transport.Override(handler);
        Assert.Empty(await OpenRouterAPI.ListModels("fixture-key"));
    }
}
