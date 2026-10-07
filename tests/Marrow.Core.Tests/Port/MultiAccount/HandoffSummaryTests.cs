using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.MultiAccount;

// OD-0114; multi-account-NOTES: Marrow's own digest / Nothing missed. No model judges fidelity.
[Trait("Feature", "MA-4")]
[Trait("Rule", "R-TEST-4")]
public sealed class HandoffSummaryTests
{
    [Theory]
    [InlineData("claude-chat.json")]
    [InlineData("codex-chat.json")]
    public void Recorded_chats_keep_every_message_byte_exact_and_every_detail_is_reachable(string fixture)
    {
        var history = HandoffFixtures.Read(fixture);
        var input = new HandoffContext(history, "MA-4: finish the port", "diff --git a/Config.cs b/Config.cs\n+timeout = 17;");
        var result = HandoffSummary.Build(input, Path.GetFullPath("handoff.json"));
        foreach (var item in history.Items)
        {
            var text = item.TimelineItemContent switch
            {
                TimelineItem.Content.User u => u.Value.Text,
                TimelineItem.Content.Assistant a => a.Value.Text,
                _ => null,
            };
            if (text is not null) Assert.True(Encoding.UTF8.GetBytes(result.Text).AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0);
        }
        var restored = JsonSerializer.Deserialize<HandoffContext>(result.FullConversation)!;
        HandoffFixtures.AssertFidelity(history, result.Text, restored.History);
        Assert.Contains(result.HandoffFile, result.Text, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(result.FullConversation), result.Size.FullBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(result.Text), result.Size.SummaryBytes);
    }

    [Fact]
    public void Older_reads_and_searches_shorten_but_latest_turns_commands_errors_and_approvals_survive()
    {
        var history = HandoffFixtures.Read("claude-chat.json");
        var input = new HandoffContext(history, "Finish MA-4", "current diff")
        {
            Approvals = new HandoffApproval[] { new("approval-17", "Run dotnet test?", "ApproveAlways: just this thread", "Do not push main") },
            ChangedFiles = new[] { "Config.cs", "tests/Case17.cs" },
        };
        var result = HandoffSummary.Build(input, Path.GetFullPath("handoff.json"), new(1, 64, 128));
        Assert.Contains("re-readable output shortened", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(HandoffFixtures.ReadMiddle, result.Text, StringComparison.Ordinal);
        Assert.Contains("COMMAND-HEAD", result.Text, StringComparison.Ordinal);
        Assert.Contains("COMMAND-TAIL", result.Text, StringComparison.Ordinal);
        Assert.Contains("Last error", result.Text, StringComparison.Ordinal);
        Assert.Contains("ERROR-TAIL", result.Text, StringComparison.Ordinal);
        Assert.Contains("PASS", result.Text, StringComparison.Ordinal);
        Assert.Contains("FAIL", result.Text, StringComparison.Ordinal);
        Assert.Contains("exit 17", result.Text, StringComparison.Ordinal);
        Assert.Contains("ApproveAlways: just this thread", result.Text, StringComparison.Ordinal);
        Assert.Contains("latest read detail 98765", result.Text, StringComparison.Ordinal);
        Assert.Contains("current diff", result.Text, StringComparison.Ordinal);
        Assert.Contains("tests/Case17.cs", result.Text, StringComparison.Ordinal);
        Assert.Contains("Accepted", result.Text, StringComparison.Ordinal);
        Assert.Contains("Active", result.Text, StringComparison.Ordinal);
        Assert.True(result.Size.FullBytes > result.Size.SummaryBytes);
        Assert.Equal(result, HandoffSummary.Build(input, result.HandoffFile, new(1, 64, 128)));
    }

    [Fact]
    public void Fidelity_checker_detects_missing_files_decisions_numbers_and_instructions()
    {
        var history = HandoffFixtures.Read("codex-chat.json");
        Assert.ThrowsAny<Exception>(() => HandoffFixtures.AssertFidelity(history, "empty", new ThreadDocument(history.ThreadID)));
    }

    [Fact]
    public void Full_recent_turns_are_selected_from_history_even_without_turn_records_and_unicode_is_not_split()
    {
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        var history = new ThreadDocument(Guid.NewGuid()) { Items = new TimelineItem[] {
            new(old, new TimelineItem.Content.Tool(new() { ToolCallKind = ToolCall.Kind.Web, Title = "page", Output = new string('x', 200) + "🙂TAIL" })),
            new(recent, new TimelineItem.Content.Tool(new() { ToolCallKind = ToolCall.Kind.Read, Title = "recent", Output = new string('y', 300) })) } };
        var result = HandoffSummary.Build(new(history, "", ""), Path.GetFullPath("handoff.json"), new(1, 8, 9));
        Assert.Contains(new string('y', 300), result.Text, StringComparison.Ordinal);
        Assert.Contains("🙂TAIL", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("�", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.Text)), StringComparison.Ordinal);
    }
}
