using System.Text.Json.Nodes;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Pills;

// MA-7: pills persisted in settings with migration-safe versioning.
public sealed class PillStorageTests
{
    private static (PillStore Store, PillStorage Storage, InMemorySettingsStore Settings) Make()
    {
        var settings = new InMemorySettingsStore();
        var storage = new PillStorage(settings);
        return (new PillStore(storage), storage, settings);
    }

    private static string Raw(InMemorySettingsStore settings) => (string)settings.Object(PillStorage.DefaultKey)!;

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T214")]
    [Trait("Rule", "R-TEST-4")]
    public void Everything_a_pill_holds_survives_a_restart_in_order()
    {
        var (store, storage, _) = Make();
        var a = store.Create("Long haul", [
            new PillStep("c1", "opus", "high", true, HandshakePoint.At(97), "k2"),
            new PillStep("x1"),
            new PillStep("d1", Fast: false, HandshakeAt: HandshakePoint.Smart)]);
        var b = store.Create("Second");
        store.SetEarlierAccount(a.Id, DecisionValues.Stay);
        store.SetHandshakeMode(a.Id, PillHandshakeMode.SameForEvery, HandshakePoint.At(90));
        store.SetUseForNewThreads(b.Id);

        var restarted = new PillStore(storage);
        Assert.Equal(["Long haul", "Second"], restarted.Pills.Select(p => p.Name).ToList());
        var back = restarted.Find(a.Id)!;
        Assert.Equal(a.Steps, back.Steps, EqualityComparer<PillStep>.Create((x, y) => x == y));
        Assert.Equal((DecisionValues.Stay, PillHandshakeMode.SameForEvery, HandshakePoint.At(90)), (back.EarlierAccount, back.HandshakeMode, back.SameHandshake));
        Assert.Equal(b.Id, restarted.UseForNewThreads);
        Assert.False(restarted.SaveBlocked);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T215")]
    [Trait("Rule", "R-TEST-4")]
    public void The_stored_text_is_json_with_a_version_and_nothing_is_stored_for_a_store_never_changed()
    {
        var (store, _, settings) = Make();
        Assert.Null(settings.Object(PillStorage.DefaultKey));
        store.Create("P", [new PillStep("c1")]);
        var root = JsonNode.Parse(Raw(settings))!.AsObject();
        Assert.Equal(1, root["version"]!.GetValue<int>());
        Assert.Equal("P", root["pills"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("c1", root["pills"]![0]!["steps"]![0]!["account"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T216")]
    [Trait("Rule", "R-TEST-4")]
    public void No_stored_text_loads_empty_without_a_problem()
    {
        var load = new PillStorage(new InMemorySettingsStore()).Load();
        Assert.Empty(load.Pills);
        Assert.Null(load.UseForNewThreads);
        Assert.Null(load.Problem);
        Assert.False(load.Newer);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T217")]
    [Trait("Rule", "R-TEST-4")]
    public void Unreadable_text_loads_empty_with_a_problem_and_is_not_overwritten_by_a_later_change()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(PillStorage.DefaultKey, "{ not json");
        var storage = new PillStorage(settings);
        var store = new PillStore(storage);
        Assert.Empty(store.Pills);
        store.Create("P");
        Assert.True(store.SaveBlocked);
        Assert.Equal("{ not json", Raw(settings));
        Assert.Single(store.Pills);
        Assert.NotNull(storage.Load().Problem);
        Assert.True(storage.Save(store.Pills, null, overwrite: true));
        Assert.Equal("P", JsonNode.Parse(Raw(settings))!["pills"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T218")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_that_cannot_be_read_is_skipped_and_counted_and_the_others_load()
    {
        var settings = new InMemorySettingsStore();
        var good = Guid.NewGuid();
        settings.Set(PillStorage.DefaultKey,
            $$"""{"version":1,"pills":[{"id":"nope","name":"bad id","steps":[]},{"id":"{{good}}","name":"Good","steps":[{"account":"c1"},{"model":"no account"},{"account":"c2","fast":"yes"}]},{"id":"{{Guid.NewGuid()}}","name":"  ","steps":[]},7]}""");
        var load = new PillStorage(settings).Load();
        var pill = Assert.Single(load.Pills);
        Assert.Equal("Good", pill.Name);
        Assert.Equal(["c1", "c2"], pill.Steps.Select(s => s.AccountId).ToList());
        Assert.Null(pill.Steps[1].Fast);
        Assert.Equal(4, load.Skipped);
        Assert.Null(load.Problem);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T219")]
    [Trait("Rule", "R-TEST-4")]
    public void Newer_stored_text_loads_what_is_known_and_is_never_saved_over()
    {
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        var text = $$"""{"version":7,"future":{"a":1},"pills":[{"id":"{{id}}","name":"Kept","steps":[{"account":"c1","hologram":true}],"extra":[1]}]}""";
        settings.Set(PillStorage.DefaultKey, text);
        var storage = new PillStorage(settings);
        var store = new PillStore(storage);
        Assert.Equal("Kept", Assert.Single(store.Pills).Name);
        Assert.True(storage.Load().Newer);
        Assert.Equal(7, storage.Load().FoundVersion);
        store.Rename(id, "Changed");
        Assert.True(store.SaveBlocked);
        Assert.Equal(text, Raw(settings));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T220")]
    [Trait("Rule", "R-TEST-4")]
    public void Older_text_is_migrated_step_by_step_in_memory_and_saved_at_the_current_version()
    {
        // A test-only chain: this app's version is 3; version 1 called a pill's name "title", version 2 stored the earlier-account rule as "earlier".
        var migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [1] = root =>
            {
                foreach (var pill in root["pills"]!.AsArray()) { pill!["name"] = pill["title"]!.DeepClone(); pill.AsObject().Remove("title"); }
                return root;
            },
            [2] = root =>
            {
                foreach (var pill in root["pills"]!.AsArray()) { if (pill!["earlier"] is { } e) { pill["earlierAccount"] = e.DeepClone(); pill.AsObject().Remove("earlier"); } }
                return root;
            },
        };
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        settings.Set(PillStorage.DefaultKey, $$"""{"version":1,"pills":[{"id":"{{id}}","title":"Old","earlier":"goBack","steps":[{"account":"c1"}]}]}""");
        var storage = new PillStorage(settings, currentVersion: 3, migrations: migrations);
        var store = new PillStore(storage);
        var pill = Assert.Single(store.Pills);
        Assert.Equal(("Old", DecisionValues.GoBack), (pill.Name, pill.EarlierAccount));
        Assert.False(storage.Load().Newer);
        store.Rename(id, "New");
        Assert.Equal(3, JsonNode.Parse(Raw(settings))!["version"]!.GetValue<int>());
        Assert.Equal("New", new PillStore(storage).Pills[0].Name);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T221")]
    [Trait("Rule", "R-TEST-4")]
    public void A_migration_that_throws_leaves_the_stored_text_alone_and_blocks_saving()
    {
        var migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [1] = _ => throw new InvalidOperationException("bad") };
        var settings = new InMemorySettingsStore();
        const string text = """{"version":1,"pills":[]}""";
        settings.Set(PillStorage.DefaultKey, text);
        var storage = new PillStorage(settings, currentVersion: 2, migrations: migrations);
        var store = new PillStore(storage);
        Assert.NotNull(storage.Load().Problem);
        store.Create("P");
        Assert.True(store.SaveBlocked);
        Assert.Equal(text, Raw(settings));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T222")]
    [Trait("Rule", "R-TEST-4")]
    public void A_handshake_point_is_smart_or_80_to_99_and_anything_else_means_same_as_the_account()
    {
        var settings = new InMemorySettingsStore();
        var id = Guid.NewGuid();
        settings.Set(PillStorage.DefaultKey, $$"""{"version":1,"pills":[{"id":"{{id}}","name":"P","handshakeMode":"sameForEvery","handshakeAll":150,"steps":[{"account":"a","handshake":"smart"},{"account":"b","handshake":85},{"account":"c","handshake":70},{"account":"d","handshake":"soon"},{"account":"e"}]}]}""");
        var pill = new PillStorage(settings).Load().Pills[0];
        Assert.True(pill.Steps[0].HandshakeAt!.Value.IsSmart);
        Assert.Equal(HandshakePoint.At(85), pill.Steps[1].HandshakeAt);
        Assert.Null(pill.Steps[2].HandshakeAt);
        Assert.Null(pill.Steps[3].HandshakeAt);
        Assert.Null(pill.Steps[4].HandshakeAt);
        Assert.Equal(PillHandshakeMode.SameForEvery, pill.HandshakeMode);
        Assert.True(pill.SameHandshake!.Value.IsSmart);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T223")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_change_is_saved_and_an_unknown_use_for_new_threads_id_is_dropped_on_load()
    {
        var (store, storage, settings) = Make();
        var p = store.Create("P");
        var writes = settings.Writes.Count;
        store.Rename(p.Id, "Q");
        Assert.Equal(writes + 1, settings.Writes.Count);
        store.SetUseForNewThreads(p.Id);
        var text = Raw(settings).Replace(p.Id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
        var node = JsonNode.Parse(Raw(settings))!.AsObject();
        node["useForNewThreads"] = Guid.NewGuid().ToString();
        settings.Set(PillStorage.DefaultKey, node.ToJsonString());
        Assert.Null(storage.Load().UseForNewThreads);
        Assert.NotEqual(text, Raw(settings));
    }
}
