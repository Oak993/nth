using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

public sealed class DextraC6F3QueueTests
{
    [Theory]
    [Trait("Decision", "ED-NEW-dextra-cx-3")]
    [InlineData(false)]
    [InlineData(true)]
    public void Ancestor_hiding_redacts_ready_and_coalescing_grandchild_drafts(bool coalescing)
    {
        using var runtime = new TestRuntime();
        var parent = FeedSamples.Chat();
        var child = FeedSamples.Chat(parent: parent.Id);
        var grandchild = FeedSamples.Chat(parent: child.Id);
        var facts = FeedChatFacts.From(grandchild, FeedSamples.Lookup(parent, child, grandchild));
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Draft(coalescing ? FeedEventKind.AgentProgress : FeedEventKind.Error,
            FeedSeverity.Error, 0, "PRIVATE LEAD", grandchild.Id, "PRIVATE CONTENT", facts));
        Assert.Equal(1, queue.Hide(parent.Id));
        queue.ReleaseAll();
        var drafts = new List<FeedDraft>();
        queue.TakeReady(FeedSamples.Start.AddSeconds(3), drafts);
        var draft = Assert.Single(drafts);
        Assert.True(draft.Chat!.Hidden);
        Assert.Null(draft.Detail);
        Assert.Null(FeedBuilder.Build(draft, new FeedBrain(grandchild.Provider), 1));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-3")]
    public void Hiding_an_ordinary_ancestor_or_its_mission_child_keeps_valid_mission_work_readable()
    {
        using var runtime = new TestRuntime();
        var parent = FeedSamples.Chat();
        var child = FeedSamples.Chat(parent: parent.Id, mission: Guid.NewGuid());
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 0, "finished", child.Id,
            "MISSION CONTENT", FeedChatFacts.From(child, FeedSamples.Lookup(parent, child))));
        Assert.Equal(0, queue.Hide(parent.Id));
        Assert.Equal(0, queue.Hide(child.Id));
        var drafts = new List<FeedDraft>();
        queue.TakeReady(FeedSamples.Start, drafts);
        var line = FeedBuilder.Build(Assert.Single(drafts), new FeedBrain(child.Provider), 1)!;
        Assert.Contains("MISSION CONTENT", line.Summary, StringComparison.Ordinal);
    }
}
