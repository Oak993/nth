using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Regression for the accepted Dextra C6-F2 finding. Offline only.
public sealed class DextraC6F2Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-2")]
    public void Malformed_mission_id_cannot_unhide_an_ordinary_hidden_chat()
    {
        using var runtime = new TestRuntime();
        using var json = JsonDocument.Parse("{\"id\":\"cccccccc-0000-0000-0000-00000000000c\",\"projectID\":\"dddddddd-0000-0000-0000-00000000000d\",\"provider\":\"claude\",\"title\":\"PRIVATE PLAN\",\"hiddenFromAssistant\":true,\"missionID\":\"broken\"}");
        var thread = ChatThreadConverter.Decode(json.RootElement);
        Assert.True(thread.HiddenFromAssistant);
        Assert.Equal(Guid.Empty, thread.MissionID);
        var facts = FeedChatFacts.From(thread, FeedSamples.Lookup(thread));
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 0, "finished", thread.Id,
            "PRIVATE PLAN completed", facts);
        var line = FeedBuilder.Build(draft, new FeedBrain(thread.Provider), 1)!;
        Assert.Equal("A hidden chat finished a turn", line.Summary);
    }
    [Theory]
    [Trait("Decision", "ED-NEW-dextra-cx-2")]
    [InlineData("assistantID", "missionID")]
    [InlineData("missionID", "assistantID")]
    public void A_damaged_mark_cannot_grant_an_exemption_even_beside_a_valid_mark(string badKey, string goodKey)
    {
        using var runtime = new TestRuntime();
        foreach (var bad in new[] { "\"broken\"", "42", "true", "{}", "[]", "\"00000000-0000-0000-0000-000000000000\"" })
        {
            using var json = JsonDocument.Parse("{\"id\":\"cccccccc-0000-0000-0000-00000000000c\",\"projectID\":\"dddddddd-0000-0000-0000-00000000000d\",\"hiddenFromAssistant\":true,\"" + badKey + "\":" + bad + ",\"" + goodKey + "\":\"aaaaaaaa-0000-0000-0000-00000000000a\"}");
            var thread = ChatThreadConverter.Decode(json.RootElement);
            var facts = FeedChatFacts.From(thread, FeedSamples.Lookup(thread));
            Assert.True(facts.Hidden);
            Assert.Equal(FeedTextAccess.Hidden, FeedPrivacy.Decide(facts, new FeedBrain(thread.Provider)));
        }
    }

}
