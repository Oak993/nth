using System.Text.Json.Nodes;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "state.json round trip" (ONDEMAND-PLAN.md 3.1: "installed items, versions, accepted terms, declined offers (per chat)", plus the
// found-on-this-PC results of 3.6 and DL-7). The file is canonical (one text for one state) and strict (anything unknown fails closed), as SK-1's
// catalogue is; ENGINEERING DECISIONS in ED-295.
[Trait("Spec", "DL-7")]
public sealed class AddonsStateJsonTests
{
    private static JsonObject Doc() => JsonNode.Parse(StateFixtures.Text(StateFixtures.Full()))!.AsObject();

    private static void Refused(JsonNode node) => Assert.Throws<StateFormatException>(() => AddonsStateJson.Parse(node.ToJsonString()));

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_empty_state_is_this_exact_text_and_parses_back_to_itself()
    {
        var text = StateFixtures.Text(AddonsState.Empty);
        Assert.Equal("{\n  \"schema\": 1,\n  \"items\": [],\n  \"acceptedTerms\": [],\n  \"declined\": [],\n  \"found\": []\n}\n", text);
        Assert.Equal(text, StateFixtures.Text(AddonsStateJson.Parse(text)));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_full_state_round_trips_to_the_same_text_and_the_same_values()
    {
        var state = StateFixtures.Full();
        var text = StateFixtures.Text(state);
        var back = AddonsStateJson.Parse(text);
        Assert.Equal(text, StateFixtures.Text(back));
        Assert.Equal(state.Items, back.Items);
        Assert.Equal(state.AcceptedTerms, back.AcceptedTerms);
        Assert.Equal(state.Found, back.Found);
        Assert.Equal(state.Declined.Select(d => d.ChatId), back.Declined.Select(d => d.ChatId));
        Assert.Equal(state.Declined.SelectMany(d => d.ItemIds), back.Declined.SelectMany(d => d.ItemIds));
        Assert.Equal(StateFixtures.Noon.AddDays(1), back.AcceptedTerms.Single(t => t.ItemId == "O5").AcceptedAt);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_text_is_canonical_sorted_by_id_hash_chat_and_item_whatever_order_the_state_was_built_in()
    {
        var state = StateFixtures.Full();
        var shuffled = AddonsState.Restore(state.Items.Reverse().ToList(), state.AcceptedTerms.Reverse().ToList(),
            state.Declined.Reverse().Select(d => new ChatDeclined(d.ChatId, d.ItemIds.Reverse().ToList())).ToList(), state.Found.Reverse().ToList());
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(shuffled));
        var doc = Doc();
        Assert.Equal(["A1", "P1", "T2", "T3"], doc["items"]!.AsArray().Select(i => (string)i!["id"]!));
        Assert.Equal([StateFixtures.Hash1, StateFixtures.Hash2], doc["acceptedTerms"]!.AsArray().Select(i => (string)i!["licenceSha256"]!));
        Assert.Equal(["chat-1", "chat-2"], doc["declined"]!.AsArray().Select(i => (string)i!["chatId"]!));
        Assert.Equal(["T1", "T3"], doc["declined"]!.AsArray()[1]!["itemIds"]!.AsArray().Select(i => (string)i!));
        Assert.Equal(["T1", "T8"], doc["found"]!.AsArray().Select(i => (string)i!["itemId"]!));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void States_and_dates_are_written_as_words_and_utc_instants_and_an_available_item_is_not_written_at_all()
    {
        var doc = Doc();
        Assert.Equal(["updateAvailable", "damaged", "failed", "installed"], doc["items"]!.AsArray().Select(i => (string)i!["state"]!));
        Assert.Equal("2026-10-04T12:00:00Z", (string)doc["acceptedTerms"]!.AsArray()[0]!["acceptedAt"]!);
        var local = AddonsState.Empty.AcceptTerms(StateFixtures.Hash1, "A2", new DateTimeOffset(2026, 10, 4, 14, 30, 0, TimeSpan.FromHours(2)));
        Assert.Equal("2026-10-04T12:30:00Z", (string)JsonNode.Parse(StateFixtures.Text(local))!["acceptedTerms"]![0]!["acceptedAt"]!);
        Assert.DoesNotContain(StateFixtures.Full().Items, i => i.State == ItemState.Available);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unknown_missing_null_duplicate_or_wrongly_cased_member_is_refused()
    {
        var d = Doc(); d["extra"] = 1; Refused(d);
        d = Doc(); d.Remove("found"); Refused(d);
        d = Doc(); d["items"]![0]!.AsObject()["bogus"] = true; Refused(d);
        d = Doc(); d["items"]![0]!.AsObject().Remove("failure"); Refused(d);
        d = Doc(); d["schema"] = null; Refused(d);
        d = Doc(); d["items"] = null; Refused(d);
        d = Doc(); d["items"]![0]!["state"] = "Installed"; Refused(d);
        d = Doc(); d["items"]![0]!["state"] = 4; Refused(d);
        d = Doc(); d["items"]![0]!["state"] = "gone"; Refused(d);
        d = Doc(); d["acceptedTerms"]![0]!["acceptedAt"] = "yesterday"; Refused(d);
        Assert.Throws<StateFormatException>(() => AddonsStateJson.Parse(StateFixtures.Text(StateFixtures.Full()).Replace("\"schema\": 1,", "\"schema\": 1, \"schema\": 1,", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Malformed_text_and_the_wrong_schema_are_refused_and_nothing_is_defaulted()
    {
        foreach (var bad in new[] { "", " ", "{", "[]", "null", "42", "{}", "{\"schema\":1}" }) Assert.Throws<StateFormatException>(() => AddonsStateJson.Parse(bad));
        var d = Doc(); d["schema"] = 2; Refused(d);
        d = Doc(); d["schema"] = 0; Refused(d);
        var text = StateFixtures.Text(StateFixtures.Full());
        Assert.Throws<StateFormatException>(() => AddonsStateJson.Parse(text[..(text.Length / 2)]));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_record_that_breaks_its_state_s_invariants_is_refused_on_reading_as_on_building()
    {
        var d = Doc(); d["items"]![3]!["installedPin"] = null; Refused(d);
        d = Doc(); d["items"]![3]!["targetPin"] = "2"; Refused(d);
        d = Doc(); d["items"]![0]!["targetPin"] = d["items"]![0]!["installedPin"]!.DeepClone(); Refused(d);
        d = Doc(); d["items"]![2]!["failure"] = null; Refused(d);
        d = Doc(); d["items"]![2]!["failure"] = "  "; Refused(d);
        d = Doc(); d["items"]![0]!["id"] = ""; Refused(d);
        d = Doc(); d["items"]![0]!["id"] = "A 1"; Refused(d);
        d = Doc(); d["items"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"Z\",\"state\":\"available\",\"installedPin\":null,\"targetPin\":null,\"failure\":null}")); Refused(d);
        d = Doc(); d["items"]!.AsArray().Add(d["items"]![0]!.DeepClone()); Refused(d);
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([StateFixtures.Rec("X", ItemState.Installed)], [], [], []));
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([StateFixtures.Rec("X", ItemState.Available)], [], [], []));
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([StateFixtures.Rec("X", ItemState.Installed, "1"), StateFixtures.Rec("X", ItemState.Damaged, "1")], [], [], []));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Terms_declined_and_found_entries_are_checked_on_reading()
    {
        var d = Doc(); d["acceptedTerms"]![0]!["licenceSha256"] = "ABC"; Refused(d);
        d = Doc(); d["acceptedTerms"]![0]!["licenceSha256"] = new string('A', 64); Refused(d);
        d = Doc(); d["acceptedTerms"]![1]!["licenceSha256"] = StateFixtures.Hash1; Refused(d);
        d = Doc(); d["acceptedTerms"]![0]!["itemId"] = ""; Refused(d);
        d = Doc(); d["declined"]![0]!["chatId"] = ""; Refused(d);
        d = Doc(); d["declined"]![0]!["itemIds"] = new JsonArray(); Refused(d);
        d = Doc(); d["declined"]![1]!["itemIds"] = new JsonArray("T1", "T1"); Refused(d);
        d = Doc(); d["declined"]![0]!["chatId"] = d["declined"]![1]!["chatId"]!.DeepClone(); Refused(d);
        d = Doc(); d["found"]![0]!["path"] = "python.exe"; Refused(d);
        d = Doc(); d["found"]![0]!["path"] = ""; Refused(d);
        d = Doc(); d["found"]![1]!["itemId"] = "T1"; Refused(d);
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([], [new AcceptedTerm("short", "A2", StateFixtures.Noon)], [], []));
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([], [], [new ChatDeclined("c", [])], []));
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([], [], [], [new FoundOnPc("T1", "rel/python.exe")]));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_null_entry_in_any_list_is_a_format_error_like_any_other_null()
    {
        foreach (var list in new[] { "items", "acceptedTerms", "declined", "found" })
        {
            var d = Doc(); d[list]!.AsArray().Add(null); Refused(d);
        }
        var c = Doc(); c["declined"]![0]!["itemIds"]!.AsArray().Add(null); Refused(c);
    }

    [Theory]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(@"C:\Python314\python.exe")]
    [InlineData("C:/Python314/python.exe")]
    [InlineData(@"\\server\share\python.exe")]
    [InlineData("/usr/bin/python3")]
    public void An_absolute_path_is_accepted_for_a_found_tool(string path) =>
        Assert.Equal(path, AddonsState.Restore([], [], [], [new FoundOnPc("T1", path)]).Found.Single().Path);

    [Theory]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("python.exe")]
    [InlineData(@"..\python.exe")]
    [InlineData(@".\python.exe")]
    [InlineData("C:python.exe")]
    [InlineData("%LOCALAPPDATA%\\python.exe")]
    [InlineData("C:\\a\0b")]
    public void A_relative_or_odd_path_is_refused_for_a_found_tool(string path) =>
        Assert.Throws<StateFormatException>(() => AddonsState.Restore([], [], [], [new FoundOnPc("T1", path)]));
}
