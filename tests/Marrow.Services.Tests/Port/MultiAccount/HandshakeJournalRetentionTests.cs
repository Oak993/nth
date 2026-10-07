using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeJournalRetentionTests
{
    [Fact]
    public void The_live_journal_is_bounded_and_rotated_events_are_still_recoverable_word_for_word()
    {
        using var folder = new AccountTestFolder();
        var all = new string('x', 300_000) + "END";
        HandoffJournal.Record(folder.Path, new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Command, Title = "offline" }));
        for (var i = 0; i < 24; i++) HandoffJournal.Record(folder.Path, new ProviderEvent.ToolOutput("tool", all));
        Assert.True(new FileInfo(HandoffJournal.PathIn(folder.Path)).Length <= 4 * 1024 * 1024);
        var history = new ThreadDocument(Guid.NewGuid()) { Items = new ValueList<TimelineItem>([new(null, new TimelineItem.Content.Tool(new() { ToolCallKind = ToolCall.Kind.Command, Title = "offline" }), "tool", DateTimeOffset.UtcNow)]) };
        var restored = HandoffJournal.Restore(history, HandoffJournal.Read(folder.Path));
        Assert.Equal(string.Concat(Enumerable.Repeat(all, 24)), Assert.IsType<TimelineItem.Content.Tool>(restored.Items[0].TimelineItemContent).Value.Output);
    }

    [Fact]
    public void An_empty_final_message_preserves_the_raw_deltas_in_the_checkpoint()
    {
        using var folder = new AccountTestFolder();
        HandoffJournal.Record(folder.Path, new ProviderEvent.MessageDelta("reply", "Café 17"));
        HandoffJournal.Record(folder.Path, new ProviderEvent.MessageCompleted("reply", ""));
        var history = new ThreadDocument(Guid.NewGuid()) { Items = new ValueList<TimelineItem>([new(null, new TimelineItem.Content.Assistant(new() { Text = "display text" }), "reply", DateTimeOffset.UtcNow)]) };
        var restored = HandoffJournal.Restore(history, HandoffJournal.Read(folder.Path));
        Assert.Equal("Café 17", Assert.IsType<TimelineItem.Content.Assistant>(restored.Items[0].TimelineItemContent).Value.Text);
    }
}
