using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: Ignis unit U2 (OD-0134, ED-NEW-ignis-providers; work/ignis/PLAN.md). Codex's CacheUsage event from
// thread/tokenUsage/updated's tokenUsage.last. No process is started: the notification handler is driven directly.
public sealed class CodexSessionIgnisTests
{
    private static SessionConfiguration MinimalConfig() => new()
    {
        Provider = ProviderKind.Codex,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:" + Path.GetTempPath().Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static JSONValue TokenUsage(JSONValue last) => JSONValue.FromDictionaryLiteral(
        ("last", last),
        ("modelContextWindow", 272_000L),
        ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 90_000L))));

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void CacheUsageOf_splits_the_cached_part_out_of_input_tokens_and_has_no_write_premium()
    {
        var usage = CodexSession.CacheUsageOf(TokenUsage(JSONValue.FromDictionaryLiteral(
            ("totalTokens", 41_200L), ("inputTokens", 41_000L), ("cachedInputTokens", 38_400L), ("outputTokens", 200L), ("reasoningOutputTokens", 80L))),
            "gpt-5.1-codex", "head-7");

        Assert.Equal(new IgnisTurnUsage(ProviderKind.Codex, "gpt-5.1-codex", "head-7", 2_600, 38_400, 0, 0, 0, 200, 41_200, 272_000, true), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void CacheUsageOf_without_cached_input_tokens_is_not_reported()
    {
        var usage = CodexSession.CacheUsageOf(TokenUsage(JSONValue.FromDictionaryLiteral(("totalTokens", 100L), ("inputTokens", 90L), ("outputTokens", 10L))), "m", null);

        Assert.Equal(IgnisTurnUsage.NotReported(ProviderKind.Codex, "m", null, 100, 272_000), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void A_lead_token_usage_notification_emits_cache_usage_beside_usage_and_spend()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var events = new List<ProviderEvent>();
        var session = new CodexSession(MinimalConfig()) { OnEvent = events.Add };

        runtime.OnMain(() => typeof(CodexSession).GetMethod("HandleNotification", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, ["thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(("tokenUsage", TokenUsage(JSONValue.FromDictionaryLiteral(
                ("totalTokens", 1_000L), ("inputTokens", 950L), ("cachedInputTokens", 900L), ("outputTokens", 50L)))))]));

        Assert.Single(events.OfType<ProviderEvent.Usage>());
        Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        var usage = Assert.Single(events.OfType<ProviderEvent.CacheUsage>()).Value;
        Assert.Null(usage.AgentID);
        Assert.Equal(50, usage.UncachedInput);
        Assert.Equal(900, usage.CacheRead);
        Assert.Equal(0, usage.CacheWrite);
        Assert.True(usage.Reported);
    }
}
