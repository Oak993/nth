using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint is cleared only by the owner's own message", 15.3; ED-NEW-dextra-as7-14):
// the home chats' taint survives a restart in one versioned document at the store's root. It comes back whole, fields
// nth does not know survive a load and save, a newer document is recognised (and left alone by the controller), a damaged
// one reads as nothing (so the controller fails closed), and the store tells a missing document from a damaged one.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-14")]
public sealed class HomeTaintJournalTests : IDisposable
{
    private static readonly Guid Home = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static readonly Guid Head = Guid.Parse("30000000-0000-4000-8000-000000000004");

    private readonly AssistantTempRoot temp = new();

    private static Dictionary<Guid, Taint> Chats() => new()
    {
        [Home] = Taint.From(ContentOrigin.Web, "https://shop.example").Union(Taint.From(ContentOrigin.Email, "a reply")),
        [Head] = Taint.From(ContentOrigin.Pdf, @"C:\in\invoice.pdf"),
        [Guid.NewGuid()] = Taint.Clean,
    };

    [Fact]
    public void The_tainted_chats_come_back_whole_and_clean_ones_are_left_out()
    {
        var body = HomeTaintJournal.ToJson(Chats());
        Assert.Equal(HomeTaintJournal.Version, body["version"]!.GetValue<int>());
        Assert.Equal(2, body["chats"]!.AsObject().Count);
        var back = HomeTaintJournal.FromJson(JsonNode.Parse(body.ToJsonString())!.AsObject())!;
        Assert.Equal(2, back.Count);
        Assert.Equal(Chats()[Home], back[Home]);
        Assert.Equal(Chats()[Head], back[Head]);
        Assert.Empty(HomeTaintJournal.FromJson(HomeTaintJournal.ToJson(new Dictionary<Guid, Taint>()))!);
    }

    [Fact]
    public void Fields_nth_does_not_know_survive_a_load_and_save_and_a_newer_document_is_recognised()
    {
        var kept = HomeTaintJournal.ToJson(Chats());
        kept["note"] = "from a later nth";
        var again = HomeTaintJournal.ToJson(HomeTaintJournal.FromJson(kept)!, kept);
        Assert.Equal("from a later nth", again["note"]!.GetValue<string>());
        Assert.Equal(2, again["chats"]!.AsObject().Count);

        var newer = new JsonObject { ["version"] = HomeTaintJournal.Version + 1, ["chats"] = new JsonObject() };
        Assert.True(HomeTaintJournal.IsNewer(newer));
        Assert.Null(HomeTaintJournal.FromJson(newer));
        Assert.False(HomeTaintJournal.IsNewer(kept));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":\"1\",\"chats\":{}}")]
    [InlineData("{\"version\":0,\"chats\":{}}")]
    [InlineData("{\"version\":1,\"chats\":{\"not-a-guid\":[{\"origin\":\"Web\",\"detail\":\"x\"}]}}")]
    [InlineData("{\"version\":1,\"chats\":{\"30000000-0000-4000-8000-000000000003\":[]}}")]
    [InlineData("{\"version\":1,\"chats\":{\"30000000-0000-4000-8000-000000000003\":[{\"origin\":\"Moon\",\"detail\":\"x\"}]}}")]
    [InlineData("{\"version\":1,\"chats\":{\"30000000-0000-4000-8000-000000000003\":[{\"origin\":\"99\"}]}}")]
    [InlineData("{\"version\":1,\"chats\":{\"30000000-0000-4000-8000-000000000003\":[null]}}")]
    [InlineData("{\"version\":1,\"chats\":{\"30000000-0000-4000-8000-000000000003\":[{\"origin\":\"Web\",\"detail\":7}]}}")]
    public void A_damaged_document_reads_as_nothing_rather_than_as_clean(string json) =>
        Assert.Null(HomeTaintJournal.FromJson(JsonNode.Parse(json)!.AsObject()));

    [Fact]
    public void The_store_keeps_it_at_its_root_and_tells_a_missing_document_from_a_damaged_one()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        {
            Assert.Null(store.HomeTaint());
            store.SaveHomeTaint(HomeTaintJournal.ToJson(Chats()));
        }
        using (var reopened = AssistantStore.Open(temp.Roots))
        {
            var back = HomeTaintJournal.FromJson(reopened.HomeTaint()!)!;
            Assert.Equal(Chats()[Home], back[Home]);
            File.WriteAllText(Path.Combine(reopened.Root, AssistantStore.HomeTaintFileName), "{\"version\":1,\"chats\":");
            Assert.Throws<InvalidDataException>(() => reopened.HomeTaint());
        }
    }

    public void Dispose() => temp.Dispose();
}
