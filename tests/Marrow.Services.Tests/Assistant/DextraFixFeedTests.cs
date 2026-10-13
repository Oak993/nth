using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-6")]
public sealed class DextraFixFeedTests
{
    [Fact]
    public void The_first_event_after_a_torn_tail_is_read_and_prior_bytes_are_kept()
    {
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var at = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        store.AppendFeed(at, [new JsonObject { ["n"] = 1 }]);
        var path = Path.Combine(temp.Store, "feed", "2026-10-12.jsonl");
        File.AppendAllText(path, "{\"n\":2,\"cut");
        var before = File.ReadAllBytes(path);
        store.AppendFeed(at, [new JsonObject { ["n"] = 3 }, new JsonObject { ["n"] = 4 }]);
        Assert.Equal([1, 3, 4], store.ReadFeed(new DateOnly(2026, 10, 12)).Select(e => e["n"]!.GetValue<int>()));
        Assert.Equal(before, File.ReadAllBytes(path).Take(before.Length));
    }
}
