using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2, 15.3; ED-NEW-dextra-as15-5): what the breaker knows of every duty's runs is kept on disk
// (duty-health.json), so a duty it paused stays paused after a restart. The file carries a version; a newer one is left alone; fields this
// build does not know come back; a line that is not a duty's health is skipped, never repaired; a duty with nothing to remember is not kept.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-5")]
public sealed class DutyHealthBookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static DutyHealth Paused(string id) =>
        new(id) { ConsecutiveFailures = 3, Paused = true, PausedAt = T0, Reasons = new ValueList<string>(["a", "b", "c"]) };

    [Fact]
    public void A_duty_never_seen_is_healthy_and_one_with_nothing_to_remember_is_not_kept()
    {
        var book = new DutyHealthBook();
        Assert.Equal(new DutyHealth("d1"), book.Of("d1"));
        Assert.True(book.Set(new DutyHealth("d1") { ConsecutiveFailures = 2 }));
        Assert.Equal(2, book.Of("d1").ConsecutiveFailures);
        Assert.True(book.Set(new DutyHealth("d1")));                          // healed
        Assert.Empty(book.ToJson()!["duties"]!.AsArray());
        Assert.Empty(book.Paused);
    }

    [Fact]
    public void Changes_are_announced_and_the_paused_duties_are_listed_oldest_first()
    {
        var book = new DutyHealthBook();
        var changes = 0;
        book.Changed += () => changes++;
        book.Set(Paused("late") with { PausedAt = T0.AddHours(1) });
        book.Set(Paused("early"));
        Assert.Equal(2, changes);
        Assert.Equal(["early", "late"], book.Paused.Select(h => h.DutyId));
    }

    [Fact]
    public void The_document_round_trips_the_pause_the_count_and_the_reasons_and_unknown_fields_come_back()
    {
        var book = new DutyHealthBook();
        book.Set(Paused("d1"));
        book.Set(new DutyHealth("d2") { ConsecutiveFailures = 2, Reasons = new ValueList<string>(["x", "y"]) });
        var document = book.ToJson()!;
        Assert.Equal(1, document["version"]!.GetValue<int>());
        document["future"] = "kept";
        document["duties"]!.AsArray()[0]!["note"] = "also kept";

        var reopened = new DutyHealthBook();
        reopened.Load(document);
        Assert.Equal(Paused("d1"), reopened.Of("d1"));
        Assert.Equal(["x", "y"], reopened.Of("d2").Reasons);
        Assert.Equal(2, reopened.Of("d2").ConsecutiveFailures);
        Assert.False(reopened.Of("d2").Paused);
        var again = reopened.ToJson()!;
        Assert.Equal("kept", again["future"]!.GetValue<string>());
        Assert.Equal("also kept", again["duties"]!.AsArray()[0]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void A_newer_file_is_read_for_nothing_and_never_written_over()
    {
        var book = new DutyHealthBook();
        book.Set(Paused("d1"));
        var newer = book.ToJson()!;
        newer["version"] = DutyHealthBook.FormatVersion + 1;
        var held = new DutyHealthBook();
        held.Load(newer);
        Assert.True(held.ReadOnly);
        Assert.False(held.Of("d1").Paused);                                   // nothing was read ...
        Assert.False(held.Set(Paused("d9")));                                 // ... nothing is taken ...
        Assert.Null(held.ToJson());                                           // ... and the file stays as it is
    }

    [Fact]
    public void A_line_that_is_not_a_duty_s_health_is_skipped_and_never_repaired()
    {
        var good = new JsonObject { ["duty"] = "good", ["failures"] = 3, ["paused"] = true, ["pausedAt"] = T0.ToString("O"), ["reasons"] = new JsonArray("one", "two") };
        JsonObject With(string field, JsonNode? value)
        {
            var copy = (JsonObject)good.DeepClone();
            if (value is null) copy.Remove(field); else copy[field] = value;
            return copy;
        }
        var document = new JsonObject
        {
            ["version"] = 1,
            ["duties"] = new JsonArray(
                good.DeepClone(),
                With("duty", null),
                With("duty", ""),
                With("duty", new string('x', 200)),
                With("failures", -1),
                With("failures", 1.5),
                With("failures", "many"),
                JsonValue.Create(7),
                good.DeepClone()),                                            // a repeat
        };
        var book = new DutyHealthBook();
        book.Load(document);
        Assert.Equal(["good"], book.Paused.Select(h => h.DutyId));
        Assert.Equal(["one", "two"], book.Of("good").Reasons);
        Assert.Equal(T0, book.Of("good").PausedAt);
    }

    [Fact]
    public void Reasons_read_back_are_one_short_line_again_and_a_healthy_line_is_dropped()
    {
        var document = new JsonObject
        {
            ["version"] = 1,
            ["duties"] = new JsonArray(
                new JsonObject { ["duty"] = "a", ["failures"] = 1, ["paused"] = false, ["reasons"] = new JsonArray("multi\r\nline\tand " + new string('z', 400), "d", "e", "f") },
                new JsonObject { ["duty"] = "healthy", ["failures"] = 0, ["paused"] = false }),
        };
        var book = new DutyHealthBook();
        book.Load(document);
        var reasons = book.Of("a").Reasons;
        Assert.Equal(["d", "e", "f"], reasons);                               // only the last three
        Assert.Equal(0, book.Of("healthy").ConsecutiveFailures);
        Assert.Single(book.ToJson()!["duties"]!.AsArray());
        var long1 = new JsonObject { ["duty"] = "b", ["failures"] = 1, ["reasons"] = new JsonArray("multi\r\nline\tand " + new string('z', 400)) };
        var other = new DutyHealthBook();
        other.Load(new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(long1) });
        var one = other.Of("b").Reasons.Single();
        Assert.DoesNotContain("\n", one, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", one, StringComparison.Ordinal);
        Assert.Equal(DutyBreaker.ReasonChars, one.Length);
    }

    [Fact]
    public void At_most_512_duties_are_kept()
    {
        var book = new DutyHealthBook();
        for (var i = 0; i < DutyHealthBook.MaxDuties; i++) Assert.True(book.Set(new DutyHealth("d" + i) { ConsecutiveFailures = 1 }));
        Assert.False(book.Set(new DutyHealth("one-too-many") { ConsecutiveFailures = 1 }));
        Assert.True(book.Set(new DutyHealth("d0") { ConsecutiveFailures = 2 }));          // one it has still changes
    }
}
