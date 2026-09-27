using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/TokenSpend.swift (B2 1fbf81e), unit
// Core.Models.TokenSpend, part 1 of 1. Every expected value is transcribed from the cited Swift lines or
// the record itself, never from the C# under test. Covers PR-USAGE-086 (the seven field names and the
// lenient, field-by-field decode with defaults), PR-USAGE-087 (totalTokens is the sole authoritative
// amount; cachedInputTokens is never added on top of inputTokens; an agentID belongs to that head alone,
// observable here as merge leaving provider/model/agentID untouched) and PR-USAGE-088 (merge adds
// totalTokens unconditionally and sums each optional breakdown only when both sides carry a value).
public sealed class TokenSpendTests
{
    [Theory]
    [InlineData(false, "{\"provider\":\"claude\",\"totalTokens\":1200}")]
    [InlineData(true, "{\"provider\":\"claude\",\"totalTokens\":1200,\"inputTokens\":800,\"outputTokens\":300,\"cachedInputTokens\":100,\"model\":\"opus\",\"agentID\":\"head-1\"}")]
    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:15-22 synthesized Codable writes seven names in declaration order, omitting nil
    public void Review_encoder_round_trips_exact_fields_and_omits_nil(bool populated, string expected)
    {
        var spend = populated ? new TokenSpend(ProviderKind.Claude, 1200, 800, 300, 100, "opus", "head-1") : new TokenSpend(ProviderKind.Claude, 1200);
        var method = typeof(TokenSpend).GetMethod("Write", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { method.Invoke(null, [writer, spend]); }
        using var encoded = JsonDocument.Parse(stream.ToArray());
        var bytes = SwiftJson.Write(encoded.RootElement, SwiftJsonStyle.PlainEncoder);
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(bytes));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(spend, TokenSpend.Decode(json.RootElement));
        var persisted = SwiftJson.Write(JsonSerializer.SerializeToElement(spend), SwiftJsonStyle.PlainEncoder);
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(persisted));
        Assert.Equal(spend, JsonSerializer.Deserialize<TokenSpend>(persisted));
    }
    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:15-22,:56-65
    [Fact]
    public void Decode_reads_all_seven_fields_by_their_exact_names_when_present()
    {
        var element = JsonDocument.Parse("""
            {
                "provider": "claude",
                "totalTokens": 1200,
                "inputTokens": 800,
                "outputTokens": 300,
                "cachedInputTokens": 100,
                "model": "claude-opus-4",
                "agentID": "agent-7"
            }
            """).RootElement;

        var spend = TokenSpend.Decode(element);

        Assert.Equal(ProviderKind.Claude, spend.Provider);
        Assert.Equal(1200L, spend.TotalTokens);
        Assert.Equal(800L, spend.InputTokens);
        Assert.Equal(300L, spend.OutputTokens);
        Assert.Equal(100L, spend.CachedInputTokens);
        Assert.Equal("claude-opus-4", spend.Model);
        Assert.Equal("agent-7", spend.AgentID);
    }

    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:57-65
    [Fact]
    public void Decode_defaults_totalTokens_to_zero_and_every_other_optional_to_null_when_absent()
    {
        var element = JsonDocument.Parse("""{"provider":"codex"}""").RootElement;

        var spend = TokenSpend.Decode(element);

        Assert.Equal(ProviderKind.Codex, spend.Provider);
        Assert.Equal(0L, spend.TotalTokens);
        Assert.Null(spend.InputTokens);
        Assert.Null(spend.OutputTokens);
        Assert.Null(spend.CachedInputTokens);
        Assert.Null(spend.Model);
        Assert.Null(spend.AgentID);
    }

    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:58, container.value's lenient fallback (:3-8)
    [Fact]
    public void Decode_treats_a_mistyped_totalTokens_as_the_zero_default()
    {
        var element = JsonDocument.Parse("""{"provider":"codex","totalTokens":"not-a-number"}""").RootElement;

        var spend = TokenSpend.Decode(element);

        Assert.Equal(0L, spend.TotalTokens);
    }

    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:60-62, container.value's lenient fallback (:3-8)
    [Fact]
    public void Decode_treats_a_mistyped_optional_breakdown_field_as_null()
    {
        var element = JsonDocument.Parse("""
            {"provider":"codex","inputTokens":"not-a-number","outputTokens":true,"cachedInputTokens":[1]}
            """).RootElement;

        var spend = TokenSpend.Decode(element);

        Assert.Null(spend.InputTokens);
        Assert.Null(spend.OutputTokens);
        Assert.Null(spend.CachedInputTokens);
    }

    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:58, only provider is required (:56-58)
    [Fact]
    public void Decode_throws_when_provider_is_missing()
    {
        var element = JsonDocument.Parse("""{"totalTokens":5}""").RootElement;

        Assert.Throws<DecodingError>(() => TokenSpend.Decode(element));
    }

    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:58, ProviderKind has no default
    [Fact]
    public void Decode_throws_when_provider_is_unrecognized()
    {
        var element = JsonDocument.Parse("""{"provider":"not-a-real-provider"}""").RootElement;

        Assert.Throws<DecodingError>(() => TokenSpend.Decode(element));
    }

    [Trait("Req", "PR-USAGE-088")] // Core/Models/TokenSpend.swift:42-43 totalTokens += other.totalTokens
    [Fact]
    public void Merge_always_adds_totalTokens()
    {
        var receiver = new TokenSpend(ProviderKind.Claude, totalTokens: 100);
        var other = new TokenSpend(ProviderKind.Codex, totalTokens: 50);

        var merged = receiver.Merge(other);

        Assert.Equal(150L, merged.TotalTokens);
    }

    [Trait("Req", "PR-USAGE-088")] // Core/Models/TokenSpend.swift:44-46,:49-52 sum(_:_:) adds when both present
    [Fact]
    public void Merge_sums_each_optional_breakdown_when_both_sides_carry_a_value()
    {
        var receiver = new TokenSpend(ProviderKind.Claude, totalTokens: 100,
            inputTokens: 60, outputTokens: 30, cachedInputTokens: 10);
        var other = new TokenSpend(ProviderKind.Claude, totalTokens: 50,
            inputTokens: 20, outputTokens: 15, cachedInputTokens: 5);

        var merged = receiver.Merge(other);

        Assert.Equal(80L, merged.InputTokens);
        Assert.Equal(45L, merged.OutputTokens);
        Assert.Equal(15L, merged.CachedInputTokens);
    }

    [Trait("Req", "PR-USAGE-088")] // Core/Models/TokenSpend.swift:49-51 guard let lhs, let rhs else { return nil }
    [Fact]
    public void Merge_erases_a_breakdown_field_to_null_when_either_side_lacks_it()
    {
        var receiverMissing = new TokenSpend(ProviderKind.Claude, totalTokens: 100, inputTokens: null, outputTokens: 30, cachedInputTokens: null);
        var otherMissing = new TokenSpend(ProviderKind.Claude, totalTokens: 50, inputTokens: 20, outputTokens: null, cachedInputTokens: 5);

        var merged = receiverMissing.Merge(otherMissing);

        Assert.Null(merged.InputTokens);
        Assert.Null(merged.OutputTokens);
        Assert.Null(merged.CachedInputTokens);
    }

    [Trait("Req", "PR-USAGE-088")] // Core/Models/TokenSpend.swift:42-47, provider/model/agentID are never assigned
    [Trait("Req", "PR-USAGE-087")] // Core/Models/TokenSpend.swift:11-14, an agentID belongs to that head alone
    [Fact]
    public void Merge_leaves_provider_model_and_agentID_untouched_so_the_receivers_identity_wins()
    {
        var receiver = new TokenSpend(ProviderKind.Claude, totalTokens: 100, model: "claude-opus-4", agentID: "lead");
        var other = new TokenSpend(ProviderKind.Codex, totalTokens: 50, model: "gpt-5.6", agentID: "sub-agent-1");

        var merged = receiver.Merge(other);

        Assert.Equal(ProviderKind.Claude, merged.Provider);
        Assert.Equal("claude-opus-4", merged.Model);
        Assert.Equal("lead", merged.AgentID);
    }

    [Trait("Req", "PR-USAGE-087")] // Core/Models/TokenSpend.swift:7-9, cachedInputTokens is a subset of totalTokens
    [Fact]
    public void TotalTokens_is_the_only_authoritative_amount_and_is_never_derived_from_the_breakdown_fields()
    {
        // No invariant ties totalTokens to inputTokens + outputTokens (cachedInputTokens is a subset of
        // totalTokens, not additional to it), so a spend may carry a totalTokens that does not equal the
        // sum of its breakdown fields; the type stores exactly what it was given, field by field.
        var spend = new TokenSpend(ProviderKind.Claude, totalTokens: 1000,
            inputTokens: 100, outputTokens: 50, cachedInputTokens: 40);

        Assert.Equal(1000L, spend.TotalTokens);
        Assert.Equal(100L, spend.InputTokens);
        Assert.Equal(50L, spend.OutputTokens);
        Assert.Equal(40L, spend.CachedInputTokens);
    }
}
