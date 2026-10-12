using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

public sealed class DextraC6cD7Tests
{
    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-7")]
    public void A_damaged_mission_mark_on_a_parent_cannot_exempt_its_child() => Check("missionID");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-7")]
    public void A_damaged_assistant_mark_on_a_parent_cannot_exempt_its_child() => Check("assistantID");

    private static void Check(string mark)
    {
        using var runtime = new TestRuntime();
        using var json = JsonDocument.Parse("{\"id\":\"cccccccc-0000-0000-0000-00000000000c\",\"projectID\":\"dddddddd-0000-0000-0000-00000000000d\",\"title\":\"PRIVATE PARENT\",\"provider\":\"claude\",\"hiddenFromAssistant\":true,\"" + mark + "\":\"broken\"}");
        var parent = ChatThreadConverter.Decode(json.RootElement);
        Assert.Equal(Guid.Empty, mark == "missionID" ? parent.MissionID : parent.AssistantID);
        var child = FeedSamples.Chat(title: "PRIVATE CHILD", parent: parent.Id);
        var facts = FeedChatFacts.From(child, FeedSamples.Lookup(parent, child));
        Assert.True(facts.Hidden);
        Assert.Contains(parent.Id, facts.HideAncestors);
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 0, "finished", child.Id, "PRIVATE REPLY", facts);
        var line = FeedBuilder.Build(draft, new FeedBrain(child.Provider), 1)!;
        Assert.Equal("A hidden chat finished a turn", line.Summary);
        Assert.Null(line.ThreadId);
        Assert.Null(line.MissionId);
        Assert.Null(line.AssistantId);

        // Before the parent is hidden, it must still be retained as a queue-hiding dependency.
        var visibleParent = parent with { HiddenFromAssistant = false };
        var visibleFacts = FeedChatFacts.From(child, FeedSamples.Lookup(visibleParent, child));
        Assert.False(visibleFacts.Hidden);
        Assert.Contains(parent.Id, visibleFacts.HideAncestors);
        var queue = new FeedQueue();
        queue.Enqueue(draft with { Chat = visibleFacts });
        queue.Hide(parent.Id);
        var ready = new List<FeedDraft>();
        queue.TakeReady(FeedSamples.Start, ready, 8);
        var queued = Assert.Single(ready);
        Assert.Null(queued.Detail);
        Assert.Equal("A hidden chat finished a turn", FeedBuilder.Build(queued, new FeedBrain(child.Provider), 1)!.Summary);
    }
}
