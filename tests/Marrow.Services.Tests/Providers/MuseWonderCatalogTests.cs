using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Owner's MUSE-WONDER order, 2026-10-13. Every HTTP/CLI result is an offline fake.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Rule", "R-TEST-4")] // ED-NEW-muse-wonder-1 through -7: owner order and cited official docs.
public sealed class MuseWonderCatalogTests
{
    private static async Task<ValueList<ModelOption>> Meta(string body)
    {
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, body) };
        using var scope = URLSession.Transport.Override(handler);
        return await MetaAPI.ListModels("fixture-key");
    }

    [Fact]
    public async Task Wonder_leads_the_chat_catalog_and_Spark_13_follows_it()
    {
        using var runtime = new TestRuntime();
        var list = await Meta("""{"data":[{"id":"muse-spark-1.1"},{"id":"muse-wonder-full-1.0"},{"id":"muse-wonder-1.0"},{"id":"muse-image-1.0"},{"id":"muse-spark-1.3"},{"id":"muse-voice-transcribe-1.0"},{"id":"sam-3.1"}]}""");
        Assert.Equal(["muse-wonder-1.0", "muse-spark-1.3", "muse-spark-1.1"], list.Select(row => row.Id));
        Assert.Equal(["muse-wonder-1.0"], list.Where(row => row.IsDefault).Select(row => row.Id));
        Assert.Equal("Wonder 1.0", list[0].Name);
        Assert.Equal("Most capable Muse model \U000000B7 complex coding and agents \U000000B7 Standard tier \U000000B7 1M context", list[0].Detail);
    }

    [Fact]
    public void Wonder_has_the_documented_six_efforts_and_vision_and_context()
    {
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], MetaAPI.Efforts("muse-wonder-1.0"));
        Assert.Equal("max", MetaSession.ReasoningEffort("max", "muse-wonder-1.0"));
        Assert.True(MetaAPI.IsVisionModel("muse-wonder-1.0"));
        Assert.Equal(1_048_576, MetaAPI.ContextWindow);
        Assert.Null(MetaAPI.Option("muse-wonder-full-1.0"));
    }

    [Fact]
    public async Task Future_chat_ids_are_raw_rows_with_no_guessed_efforts()
    {
        using var runtime = new TestRuntime();
        var list = await Meta("""{"data":[{"id":"muse-next-2.0"},{"id":"totally-new-chat"},{"id":"muse-wonder-2.0"},{"id":"muse-wonder-full-2.0"},{"id":"muse-wonder-1.0-contributor"}]}""");
        Assert.Equal(["muse-next-2.0", "muse-wonder-2.0", "totally-new-chat"], list.Select(row => row.Id));
        Assert.All(list, row => { Assert.Equal(row.Id, row.Name); Assert.Empty(row.Efforts); Assert.Null(row.DefaultEffort); });
    }

    [Fact]
    public async Task Endpoint_chat_metadata_wins_over_nonchat_looking_names()
    {
        using var runtime = new TestRuntime();
        var list = await Meta("""{"data":[{"id":"muse-image-chat-2","architecture":{"output_modalities":["text"],"input_modalities":["text","image"]}},{"id":"future-chat-looking","architecture":{"output_modalities":["image"]}},{"id":"future-generator","type":"image"},{"id":"future-embedding","task":"embedding","architecture":{"output_modalities":["text"]}},{"id":"muse-image-explicit-chat","supported_endpoints":["/v1/chat/completions"]},{"id":"future-opaque","supported_endpoints":["/v1/embeddings"]},{"id":"muse-opaque","capabilities":{"chat":true}},{"id":"muse-text-only","capabilities":{"chat":false}}]}""");
        Assert.Equal(["muse-image-chat-2", "muse-image-explicit-chat", "muse-opaque"], list.Select(row => row.Id));
        Assert.True(MetaAPI.IsVisionModel("muse-image-chat-2"));
        Assert.False(MetaAPI.IsVisionModel("muse-opaque"));
    }

    [Fact]
    public async Task Name_fallback_excludes_nonchat_outputs_without_a_chat_allowlist()
    {
        using var runtime = new TestRuntime();
        var list = await Meta("""{"data":[{"id":"muse-new-chat"},{"id":"muse-video-1"},{"id":"muse-voice-1"},{"id":"muse-audio-1"},{"id":"muse-transcribe-1"},{"id":"muse-embedding-1"},{"id":"muse-rerank-1"},{"id":"muse-glimmer-1"}]}""");
        Assert.Equal("muse-new-chat", Assert.Single(list).Id);
    }

    [Fact]
    public void Unknown_Meta_efforts_are_omitted_from_the_request()
    {
        Assert.Null(MetaSession.ReasoningEffort("high", "muse-next-2.0"));
        Assert.Null(MetaSession.ReasoningEffort("max", "muse-wonder-2.0"));
    }

    [Fact]
    public void Wonder_is_the_seed_and_session_default_with_the_existing_Spark_rows_kept()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var seed = ProviderRegistry.MetaSeed;
        Assert.Equal(["muse-wonder-1.0", "muse-spark-1.3", "muse-spark-1.3-contributor", "muse-spark-1.2", "muse-spark-1.2-contributor", "muse-spark-1.1"], seed.Select(row => row.Id));
        Assert.Equal(["muse-wonder-1.0"], seed.Where(row => row.IsDefault).Select(row => row.Id));
        var session = new MetaSession(new SessionConfiguration { Provider = ProviderKind.Meta, WorkingDirectory = SwiftURL.Create("file:///C:/fixture")!, Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        Assert.Equal("muse-wonder-1.0", session.OpenAICompatibleSessionConfig.DefaultModel);
        Assert.Equal("Meta coding agent (Muse Wonder muse-wonder-1.0)", session.OpenAICompatibleSessionConfig.AgentIdentity("muse-wonder-1.0"));
        foreach (var effort in MetaAPI.MaxEfforts)
        foreach (var withTools in new[] { false, true })
        {
            var payload = session.RequestPayloadForTests("muse-wonder-1.0", effort, withTools);
            Assert.Equal("muse-wonder-1.0", payload["model"].JSONValueString);
            Assert.Equal(effort, payload["reasoning_effort"].JSONValueString);
            if (withTools) Assert.All(payload["tools"].JSONValueArray!, tool => Assert.Equal("function", tool["type"]!.JSONValueString));
            ProviderToolBillingGuard.Validate(ProviderKind.Meta, payload);
        }
    }

    [Fact]
    public async Task Zai_lists_new_chat_families_with_safe_efforts_and_keeps_unavailable_FlashX_out()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, """{"data":[{"id":"glm-6-future"},{"id":"glm-5.3"},{"id":"glm-5.3-flashx"},{"id":"future-image","type":"image"}]}""") };
        using var scope = URLSession.Transport.Override(handler);
        var list = await ZaiAPI.ListModels("fixture-key");
        Assert.Equal(["glm-5.3", "glm-6-future"], list.Select(row => row.Id));
        Assert.Empty(list[1].Efforts);
        Assert.Null(list[1].DefaultEffort);
    }

    [Fact]
    public void Command_Code_accepts_a_new_chat_name_without_a_hyphen_or_slash()
    {
        var rows = CommandCodeAPI.ParseModels("Available models\nFuture vendor\n  nova  Future chat (default)\n", null, null);
        Assert.Equal("nova", Assert.Single(rows).Id);
        Assert.True(rows[0].IsDefault);
    }

    [Fact]
    public void Argon_is_first_in_the_seed_with_documented_efforts_and_Flash_stays_default()
    {
        var seed = ProviderRegistry.AntigravitySeed;
        Assert.Equal("gemini-4-argon", seed[0].Id);
        Assert.Equal("Gemini 4 Argon", seed[0].Name);
        Assert.Equal(["low", "medium", "high"], seed[0].Efforts);
        Assert.Equal("medium", seed[0].DefaultEffort);
        Assert.Equal(["gemini-3.8-flash"], seed.Where(row => row.IsDefault).Select(row => row.Id));
    }

    [Fact]
    public void Wonder_has_its_own_dated_price_instead_of_Spark_rates()
    {
        var row = ModelPriceCatalog.FromJson(ModelPriceData.Json).Find(ProviderKind.Meta, "muse-wonder-1.0", DateTimeOffset.Parse("2026-10-13T23:59:59Z", CultureInfo.InvariantCulture));
        Assert.NotNull(row);
        Assert.Equal(4, row.InputPerMillion);
        Assert.Equal(20, row.OutputPerMillion);
        Assert.Equal(0.20, row.CachedInputPerMillion);
        Assert.Equal(new Uri("https://dev.meta.ai/docs/pricing-rate-limits"), row.Source);
    }

    [Fact]
    public void A_fake_agy_list_preserves_Argon_effort_suffixes_and_an_older_account_default()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("argon-catalog");
        script.Set("models.stdout", "gemini-4-argon-low\tGemini 4 Argon (Low)\ngemini-4-argon-high\tGemini 4 Argon (High)\ngemini-3.8-flash\tGemini 3.8 Flash\n");
        script.Set("model.stdout", """{"command":{"data":{"id":"gemini-3.8-flash"}}}""");
        var url = SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;
        var rows = runtime.Loop.Run(() => AntigravitySession.ListModels(url, new ValueMap<string, string>([script.EnvironmentEntry])), TimeSpan.FromSeconds(60));
        Assert.Equal(["gemini-4-argon", "gemini-3.8-flash"], rows.Select(row => row.Id));
        Assert.Equal(["low", "high"], rows[0].Efforts);
        Assert.Equal("Gemini 4 Argon", rows[0].Name);
        Assert.Equal([false, true], rows.Select(row => row.IsDefault));
    }

    [Fact]
    public async Task OpenRouter_lists_Argon_without_a_known_name_filter()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, """{"data":[{"id":"google/gemini-4-argon","name":"Google: Gemini 4 Argon","supported_parameters":["reasoning"],"architecture":{"input_modalities":["text","image"],"output_modalities":["text"]}}]}""") };
        using var scope = URLSession.Transport.Override(handler);
        var row = Assert.Single(await OpenRouterAPI.ListModels("fixture-key"));
        Assert.Equal("google/gemini-4-argon", row.Id);
        Assert.Equal("Gemini 4 Argon", row.Name);
        Assert.Equal(OpenRouterAPI.ReasoningEfforts, row.Efforts);
        Assert.True(OpenRouterAPI.IsVisionModel(row.Id));
    }

    [Fact]
    public void Meta_errors_recommend_Wonder_and_describe_both_reasoning_families()
    {
        var session = new MetaSession(new SessionConfiguration { Provider = ProviderKind.Meta, WorkingDirectory = SwiftURL.Create("file:///C:/fixture")!, Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        Assert.Equal("Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker.", session.FriendlyError("model_not_found", 404));
        Assert.Equal("Muse Wonder and Muse Spark always reason; \"none\" is rejected. Pick Minimal or higher in the model picker.", session.FriendlyError("reasoning_effort none", 400));
    }

    [Fact]
    public void Argon_normalization_uses_medium_when_neither_a_suffix_nor_an_effort_is_given()
    {
        Assert.Equal(("gemini-4-argon", "medium"), AntigravitySession.Normalize("gemini-4-argon", null));
        Assert.Equal(("gemini-4-argon", "low"), AntigravitySession.Normalize("gemini-4-argon-low", null));
        Assert.Equal(("gemini-4-argon", "high"), AntigravitySession.Normalize("gemini-4-argon", "high"));
    }
    [Fact]
    public async Task Future_Spark_chat_ids_have_raw_names_and_no_inherited_effort_scale()
    {
        using var runtime = new TestRuntime();
        var row = Assert.Single(await Meta("""{"data":[{"id":"muse-spark-9.0"}]}"""));
        Assert.Equal(row.Id, row.Name);
        Assert.Empty(row.Efforts);
        Assert.Null(row.DefaultEffort);
        Assert.Null(MetaSession.ReasoningEffort("high", row.Id));
    }

}
