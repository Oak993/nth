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

    // ---- ED-NEW-ignis-runtime ----

    [Theory]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    [InlineData("cache_write_input_tokens")]
    [InlineData("cacheWriteInputTokens")]
    public void CacheUsageOf_reads_the_cache_write_field_into_the_unknown_lifetime_bucket(string field)
    {
        var usage = CodexSession.CacheUsageOf(TokenUsage(JSONValue.FromDictionaryLiteral(
            ("totalTokens", 41_200L), ("inputTokens", 41_000L), ("cachedInputTokens", 30_000L), (field, 6_000L), ("outputTokens", 200L))),
            "gpt-5.6-codex", null);

        // 41000 input = 30000 read + 6000 written + 5000 uncached; the stream names no lifetime for the writes.
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Codex, "gpt-5.6-codex", null, 5_000, 30_000, 0, 0, 6_000, 200, 41_200, 272_000, true), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Nested_helpers_at_every_depth_reach_ignis_once_each_and_nothing_else_of_them_reaches_the_chat()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var events = new List<ProviderEvent>();
        var session = new CodexSession(MinimalConfig()) { OnEvent = events.Add };
        typeof(CodexSession).GetField("threadID", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, "lead");
        void Notify(string method, JSONValue parameters) => typeof(CodexSession).GetMethod("HandleNotification", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [method, parameters]);
        JSONValue Started(string id, string parent, string? nickname = null) => JSONValue.FromDictionaryLiteral(("thread", nickname is null
            ? JSONValue.FromDictionaryLiteral(("id", id), ("parentThreadId", parent), ("model", "gpt-5.6-codex-mini"))
            : JSONValue.FromDictionaryLiteral(("id", id), ("parentThreadId", parent), ("model", "gpt-5.6-codex-mini"), ("agentNickname", nickname))));
        JSONValue Used(string thread, long cached) => JSONValue.FromDictionaryLiteral(("threadId", thread), ("tokenUsage", TokenUsage(JSONValue.FromDictionaryLiteral(
            ("totalTokens", 10_000L), ("inputTokens", 9_000L), ("cachedInputTokens", cached), ("outputTokens", 1_000L)))));

        runtime.OnMain(() =>
        {
            Notify("thread/started", Started("head", "lead", "Scout"));          // level 1: a head of this lead
            Notify("thread/started", Started("nested", "head"));                 // level 2: the head's own sub-thread
            Notify("thread/started", Started("nested2", "nested"));              // level 3
            Notify("thread/started", Started("stranger", "someone-else"));       // not under this lead
            Notify("thread/tokenUsage/updated", Used("head", 8_000));
            Notify("thread/tokenUsage/updated", Used("nested", 7_000));
            Notify("thread/tokenUsage/updated", Used("nested2", 6_000));
            Notify("thread/tokenUsage/updated", Used("stranger", 5_000));
            Notify("thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(("threadId", "lead"), ("tokenUsage", TokenUsage(JSONValue.FromDictionaryLiteral(
                ("totalTokens", 10_000L), ("inputTokens", 9_000L), ("cachedInputTokens", 4_000L), ("outputTokens", 1_000L))))));
        });

        var usages = events.OfType<ProviderEvent.CacheUsage>().Select(e => e.Value).ToList();
        Assert.Equal(["head", "nested", "nested2", null], usages.Select(u => u.AgentID));
        Assert.Equal([8_000L, 7_000L, 6_000L, 4_000L], usages.Select(u => u.CacheRead));
        Assert.Equal("gpt-5.6-codex-mini", usages[1].Model);
        // Only the real head is an agent in the chat; the nested threads add no agent, progress or spend events.
        Assert.Equal(["head"], events.OfType<ProviderEvent.AgentStarted>().Select(e => e.Value.Id));
        Assert.Equal(["head"], events.OfType<ProviderEvent.AgentProgress>().Select(e => e.AgentID).Distinct());
    }
}
