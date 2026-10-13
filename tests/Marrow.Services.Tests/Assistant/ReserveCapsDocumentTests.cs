using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps", 11.7, 15.3; ED-NEW-dextra-as15-2): the owner's standing caps sit in the same document as his
// reserve rules (reserves.json), since he says them the same way. The document carries a version; the build before caps wrote none and its
// file loads and writes back unchanged; a newer file is left alone; fields this build does not know come back; a line that is not a cap is
// skipped, never repaired.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-2")]
public sealed class ReserveCapsDocumentTests
{
    private static CostCap Day(double percent, string? assistant = null) => new("", CostCapScope.AssistantDay) { Percent = percent, AssistantId = assistant };

    private static CostCap Money(decimal dollars, params string[] keys) => new("", CostCapScope.ApiDay) { Dollars = dollars, AccountNames = new ValueList<string>(keys) };

    private static ReserveRule Always(string name, double keep) => new("", ReserveScope.Named(name), ReserveLifetime.Standing) { KeepPercent = keep };

    [Fact]
    public void Standing_caps_are_added_in_order_with_an_id_and_removed_by_it()
    {
        var settings = new ReserveSettings();
        var changes = 0;
        settings.Changed += () => changes++;
        Assert.Null(settings.AddCap(Day(15, "channel-a")));
        Assert.Null(settings.AddCap(Money(20m, "OpenRouter")));
        Assert.Equal(2, changes);
        Assert.Equal([CostCapScope.AssistantDay, CostCapScope.ApiDay], settings.Caps.Select(c => c.Scope));
        Assert.All(settings.Caps, cap => Assert.Matches("^[0-9a-f]{32}$", cap.Id));
        Assert.NotEqual(settings.Caps[0].Id, settings.Caps[1].Id);

        Assert.True(settings.RemoveCap(settings.Caps[0].Id));
        Assert.Equal(3, changes);
        Assert.Equal([CostCapScope.ApiDay], settings.Caps.Select(c => c.Scope));
        Assert.False(settings.RemoveCap("nobody"));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void A_cap_that_is_a_tasks_or_limits_nothing_is_refused_with_the_reason()
    {
        var settings = new ReserveSettings();
        Assert.Equal("A cap for one task is set where the task starts.", settings.AddCap(new CostCap("", CostCapScope.Task) { Percent = 5 }));
        Assert.Equal("Say how much the cap is.", settings.AddCap(new CostCap("", CostCapScope.AssistantDay)));
        Assert.Equal("Say how much the cap is.", settings.AddCap(new CostCap("", CostCapScope.ApiDay) { Percent = 10 }));   // a key has money, not windows
        Assert.Empty(settings.Caps);
    }

    [Fact]
    public void A_percent_is_held_to_0_to_100_when_it_is_stored_and_a_repeated_id_gets_a_new_one()
    {
        var settings = new ReserveSettings();
        Assert.Null(settings.AddCap(new CostCap("same", CostCapScope.AssistantDay) { Percent = 250 }));
        Assert.Null(settings.AddCap(new CostCap("same", CostCapScope.AssistantDay) { Percent = 5 }));
        Assert.Equal(100, settings.Caps[0].Percent);
        Assert.Equal("same", settings.Caps[0].Id);
        Assert.NotEqual("same", settings.Caps[1].Id);
    }

    [Fact]
    public void At_most_64_caps_are_kept()
    {
        var settings = new ReserveSettings();
        for (var i = 0; i < ReserveSettings.MaxCaps; i++) Assert.Null(settings.AddCap(Day(1 + i % 90)));
        Assert.Equal("At most 64 caps are kept.", settings.AddCap(Day(5)));
        Assert.Equal(ReserveSettings.MaxCaps, settings.Caps.Count);
    }

    [Fact]
    public void The_document_carries_caps_only_when_there_are_some_so_the_build_before_them_wrote_the_same_file()
    {
        var before = new ReserveSettings();
        Assert.Null(before.Add(Always("Claude 20x", 30)));
        var oldDocument = before.ToJson()!;
        Assert.False(oldDocument.ContainsKey("caps"));

        var loaded = new ReserveSettings();
        loaded.Load(oldDocument);
        Assert.Empty(loaded.Caps);
        Assert.Equal(oldDocument.ToJsonString(), loaded.ToJson()!.ToJsonString());      // loads, and writes back, unchanged

        Assert.Null(loaded.AddCap(Day(15, "channel-a")));
        var written = loaded.ToJson()!;
        Assert.Single(written["caps"]!.AsArray());
        Assert.Equal(1, written["version"]!.GetValue<int>());                           // caps did not change the format's version: a new field is not a new format

        Assert.True(loaded.RemoveCap(loaded.Caps[0].Id));
        Assert.False(loaded.ToJson()!.ContainsKey("caps"));
    }

    [Fact]
    public void A_cap_round_trips_with_its_assistant_its_keys_and_both_amounts_and_unknown_fields_come_back()
    {
        var settings = new ReserveSettings();
        Assert.Null(settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 12.5, Dollars = 3.25m, AssistantId = "channel-a" }));
        Assert.Null(settings.AddCap(Money(7.5m, "OpenRouter", "DeepSeek")));
        var document = settings.ToJson()!;
        document["future"] = "kept";
        document["caps"]!.AsArray()[1]!["note"] = "also kept";

        var reopened = new ReserveSettings();
        reopened.Load(document);
        Assert.Equal(settings.Caps, reopened.Caps);
        Assert.Equal(12.5, reopened.Caps[0].Percent);
        Assert.Equal(3.25m, reopened.Caps[0].Dollars);
        Assert.Equal("channel-a", reopened.Caps[0].AssistantId);
        Assert.Equal(["OpenRouter", "DeepSeek"], reopened.Caps[1].AccountNames);
        var again = reopened.ToJson()!;
        Assert.Equal("kept", again["future"]!.GetValue<string>());
        Assert.Equal("also kept", again["caps"]!.AsArray()[1]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void A_newer_file_shows_no_caps_changes_none_and_is_never_written_over()
    {
        var settings = new ReserveSettings();
        Assert.Null(settings.AddCap(Day(15)));
        var newer = settings.ToJson()!;
        newer["version"] = ReserveSettings.FormatVersion + 1;
        var held = new ReserveSettings();
        held.Load(newer);
        Assert.True(held.ReadOnly);
        Assert.Empty(held.Caps);
        Assert.Equal("These settings were saved by a newer version; this one leaves them as they are.", held.AddCap(Day(5)));
        Assert.False(held.RemoveCap("anything"));
        Assert.Null(held.ToJson());
    }

    [Fact]
    public void A_line_that_is_not_a_cap_is_skipped_and_never_repaired()
    {
        var good = new JsonObject { ["id"] = "good", ["scope"] = "AssistantDay", ["percent"] = 15, ["accounts"] = new JsonArray() };
        JsonObject With(string field, JsonNode? value)
        {
            var copy = (JsonObject)good.DeepClone();
            copy["id"] = "id-" + field + "-" + (value?.ToJsonString() ?? "none");
            if (value is null) copy.Remove(field); else copy[field] = value;
            return copy;
        }
        var document = new JsonObject
        {
            ["version"] = 1,
            ["caps"] = new JsonArray(
                good.DeepClone(),
                With("scope", "Task"),                      // a task's cap is never stored
                With("scope", "Fortnight"),
                With("scope", null),
                With("percent", null),                      // no amount
                With("percent", "lots"),
                With("id", new string('x', 70)),
                JsonValue.Create("nonsense"),
                good.DeepClone()),                          // a repeat of the first id
        };
        var settings = new ReserveSettings();
        settings.Load(document);
        Assert.Equal(["good"], settings.Caps.Select(c => c.Id));
    }
}
