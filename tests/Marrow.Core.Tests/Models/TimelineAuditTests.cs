using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/Timeline.swift (B3 52bc792), :285-369.
public sealed class TimelineAuditTests
{
    private static readonly Guid ID = new("ABCDEF12-3456-7890-ABCD-EF1234567890");
    private static readonly DateTimeOffset Date = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:285-330
    [Trait("Req", "PR-RUNTIME-251")] // Core/Models/Timeline.swift:306,329
    [Trait("Req", "PR-USAGE-086")] // Core/Models/TokenSpend.swift:15-22,56-65
    public void Turn_spends_round_trip_with_declaration_order_and_nil_fields_omitted()
    {
        var turn = new TurnRecord { Id = ID, Index = 3, StartedAt = Date,
            TokenSpends = new[] { new TokenSpend(ProviderKind.Claude, 1200, 800, 300, 100, "opus", "head-1"),
                new TokenSpend(ProviderKind.Codex, 9) } };
        var document = new ThreadDocument(ID) { Turns = new[] { turn } };
        var restored = Coding.Decode<ThreadDocument>(Coding.Encode(document))!;
        Assert.Equal(document, restored);
        using var shape = JsonDocument.Parse(SwiftJson.Encode(document, Coding.StorageOptions, SwiftJsonStyle.PlainEncoder));
        var encodedTurn = shape.RootElement.GetProperty("turns")[0];
        Assert.Equal(new[] { "id", "index", "startedAt", "status", "tokenSpends", "hydraMerged" },
            encodedTurn.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "provider", "totalTokens", "inputTokens", "outputTokens", "cachedInputTokens", "model", "agentID" },
            encodedTurn.GetProperty("tokenSpends")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "provider", "totalTokens" }, encodedTurn.GetProperty("tokenSpends")[1].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-017")] // Core/Models/Timeline.swift:344-367
    [Trait("Req", "PR-RUNTIME-160")] // Core/Models/Timeline.swift:353,367
    public void Continuation_round_trips_all_fields_with_exact_names_order_dates_and_UUIDs()
    {
        var attachment = new Attachment { Id = ID, Name = "a.txt", Path = "C:\\files\\a.txt", MimeType = "text/plain" };
        var continuation = new ThreadContinuation(ID, "Forwarded", "User: request", Date, new[] { attachment }, true);
        var document = new ThreadDocument(ID) { Continuation = continuation };
        Assert.Equal(document, Coding.Decode<ThreadDocument>(Coding.Encode(document)));
        Assert.Equal(continuation, Coding.Decode<ThreadContinuation>(Coding.Encode(continuation)));
        using var shape = JsonDocument.Parse(SwiftJson.Encode(document, Coding.StorageOptions, SwiftJsonStyle.PlainEncoder));
        Assert.Equal(new[] { "threadID", "items", "turns", "followUps", "hydraMerges", "continuation" },
            shape.RootElement.EnumerateObject().Select(p => p.Name));
        var payload = shape.RootElement.GetProperty("continuation");
        Assert.Equal(new[] { "sourceThreadID", "sourceTitle", "createdAt", "context", "attachments", "isTruncated" },
            payload.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ABCDEF12-3456-7890-ABCD-EF1234567890", payload.GetProperty("sourceThreadID").GetString());
        Assert.Equal("2026-01-02T03:04:05Z", payload.GetProperty("createdAt").GetString());
        Assert.Equal(new[] { "id", "name", "path", "mimeType" }, payload.GetProperty("attachments")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:329
    [Trait("Req", "PR-CHAT-017")] // Core/Models/Timeline.swift:367
    public void Older_documents_without_either_key_decode_and_omit_nil_optionals()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"threadID":"ABCDEF12-3456-7890-ABCD-EF1234567890","turns":[{"id":"ABCDEF12-3456-7890-ABCD-EF1234567890","startedAt":"2026-01-02T03:04:05Z"}]}""");
        var document = Coding.Decode<ThreadDocument>(bytes)!;
        Assert.Null(document.Continuation);
        Assert.Null(Assert.Single(document.Turns).TokenSpends);
        using var shape = JsonDocument.Parse(Coding.Encode(document));
        Assert.False(shape.RootElement.TryGetProperty("continuation", out _));
        Assert.False(shape.RootElement.GetProperty("turns")[0].TryGetProperty("tokenSpends", out _));
    }

    [Theory]
    [InlineData("sourceThreadID")]
    [InlineData("sourceTitle")]
    [InlineData("createdAt")]
    [InlineData("context")]
    [InlineData("attachments")]
    [InlineData("isTruncated")]
    [Trait("Req", "PR-CHAT-017")] // Core/Models/ThreadContinuation.swift:4-10 synthesized required fields; Timeline.swift:367 lenient caller
    public void Missing_required_continuation_field_throws_standalone_and_defaults_in_document(string key)
    {
        var fields = new Dictionary<string, object> { ["sourceThreadID"] = "ABCDEF12-3456-7890-ABCD-EF1234567890",
            ["sourceTitle"] = "title", ["createdAt"] = "2026-01-02T03:04:05Z", ["context"] = "history",
            ["attachments"] = Array.Empty<object>(), ["isTruncated"] = false };
        fields.Remove(key);
        var bytes = SwiftJson.Encode(fields, Coding.StorageOptions, SwiftJsonStyle.Storage);
        Assert.Throws<DecodingError>(() => Coding.Decode<ThreadContinuation>(bytes));
        var documentFields = new Dictionary<string, object> { ["threadID"] = "ABCDEF12-3456-7890-ABCD-EF1234567890", ["continuation"] = fields };
        Assert.Null(Coding.Decode<ThreadDocument>(SwiftJson.Encode(documentFields, Coding.StorageOptions, SwiftJsonStyle.Storage))!.Continuation);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:329 container.value defaults the entire array if any spend fails
    public void Malformed_spend_defaults_the_whole_optional_list_to_nil()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"threadID":"ABCDEF12-3456-7890-ABCD-EF1234567890","turns":[{"tokenSpends":[{"provider":"claude","totalTokens":2},{"provider":"unknown"}]}]}""");
        Assert.Null(Assert.Single(Coding.Decode<ThreadDocument>(bytes)!.Turns).TokenSpends);
    }
}
