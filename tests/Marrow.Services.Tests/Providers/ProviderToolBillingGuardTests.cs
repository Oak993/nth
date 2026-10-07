using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// RR-50 round 4: inspect real heads/builders; transport is always an in-process fake.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Rule", "R-TEST-4")]
public sealed class ProviderToolBillingGuardTests
{
    private static OpenAICompatibleSession Head(ProviderKind provider, string directory, bool hydra = false)
    {
        var configuration = new SessionConfiguration
        {
            Provider = provider, WorkingDirectory = SwiftURL.Create("file:" + directory.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build, ApiKey = "fixture-never-sent",
            IsHydraHead = hydra,
        };
        return provider == ProviderKind.Meta ? new MetaSession(configuration) : new ZaiSession(configuration);
    }

    [Fact]
    public void Every_known_Meta_and_Zai_model_effort_and_tool_mode_builds_only_local_function_tools()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        foreach (var provider in new[] { ProviderKind.Meta, ProviderKind.Zai })
        foreach (var hydra in new[] { false, true })
        {
            var session = Head(provider, storage.Root, hydra);
            session.SkillTools = new NativeSkillTools(null, offer: new FakeToolOffer(true, "fixture"));
            session.SeedMcpToolsForTests([new MCPHub.Tool("fixture", "web_search", "Local function; never executed", JSONValue.FromDictionaryLiteral(("type", "object")))]);
            var models = provider == ProviderKind.Meta
                ? new[] { "muse-spark-1.3-contributor", "muse-spark-1.3", "muse-spark-1.2-contributor", "muse-spark-1.2", "muse-spark-1.1" }
                : new[] { "glm-5.3", "glm-5.3-flash" };
            foreach (var model in models)
            foreach (var effort in new string?[] { null, "minimal", "low", "medium", "high", "xhigh", "extra-high", "max", "unknown" })
            foreach (var withTools in new[] { false, true })
            {
                var payload = session.RequestPayloadForTests(model, effort, withTools);
                var allowed = new[] { "model", "messages", "stream", "tools", "tool_choice", "stream_options", "reasoning_effort", "max_tokens" }
                    .Concat(provider == ProviderKind.Zai ? ["thinking", "tool_stream"] : Array.Empty<string>());
                Assert.All(payload.Keys, key => Assert.Contains(key, allowed));
                Assert.Equal(withTools, payload.ContainsKey("tools"));
                if (withTools)
                {
                    Assert.All(payload["tools"].JSONValueArray!, tool =>
                    {
                        Assert.Equal("function", tool["type"]!.JSONValueString);
                        Assert.All(tool.JSONValueObject!.Keys, key => Assert.Contains(key, new[] { "type", "function" }));
                    });
                    Assert.Contains(payload["tools"].JSONValueArray!, tool => tool["function"]!["name"]!.JSONValueString!.Contains("web_search", StringComparison.Ordinal));
                    Assert.Contains(payload["tools"].JSONValueArray!, tool => tool["function"]!["name"]!.JSONValueString == "request_tool");
                }
                ProviderToolBillingGuard.Validate(provider, payload);
            }
        }
    }

    [Theory]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    public void A_future_provider_tool_or_unknown_enablement_field_fails_closed_before_transport(ProviderKind provider)
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No dispatch authorized") };
        using var transport = URLSession.Transport.Override(handler);
        var session = Head(provider, storage.Root);
        foreach (var field in new[] { "web_search_options", "web_search", "search", "plugins", "future_provider_tool" })
        {
            var payload = session.RequestPayloadForTests("fixture");
            payload[field] = JSONValue.FromDictionaryLiteral(("enabled", true));
            Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.PerformPayloadForTests(payload)));
        }
        foreach (var toolType in new[] { "web_search", "web_search_preview", "code_interpreter", "future_provider_tool" })
        {
            var payload = session.RequestPayloadForTests("fixture");
            payload["tools"] = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", toolType)));
            Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.PerformPayloadForTests(payload)));
        }
        var hidden = session.RequestPayloadForTests("fixture");
        hidden["stream_options"] = JSONValue.FromDictionaryLiteral(("include_usage", true), ("web_search", true));
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.PerformPayloadForTests(hidden)));
        Assert.Empty(handler.Received);
    }
}
