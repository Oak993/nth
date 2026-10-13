using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.Services.Tests.Fixtures.DigestFixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2, 6, 12.5, 15.3; lean rule 10 "Persisted formats"; ED-NEW-dextra-as11-6): what the store keeps of the digests. The last fourteen,
// the final HMAC of each day a digest printed (the activity log is checked against them), the suggestion ledger and whether the Morning digest duty was made.
// The format carries a version; a newer file, or one that cannot be read, is shown and never written over; unknown fields survive; nothing grows without bound.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-6")]
public sealed class DigestBookTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private string DigestFile => Path.Combine(temp.Store, AssistantStore.DigestFileName);

    private static DigestBook Opened(AssistantStore store)
    {
        var book = new DigestBook();
        book.Attach(store);
        return book;
    }

    private static string Describe(DigestSection section) =>
        $"{section.Part}|{section.Label}|{section.Lead}|{section.Inline}|" + string.Join(";", section.Items.Select(i => $"{i.Text}~{i.Note}~{i.Link.Kind}:{i.Link.Id}"));

    [Fact]
    public void A_digest_with_every_part_comes_back_whole_after_a_restart()
    {
        var digest = Full();
        using (var store = AssistantStore.Open(temp.Roots))
        using (var book = Opened(store))
        {
            book.Add(digest);
            book.Flush();
        }
        using var again = AssistantStore.Open(temp.Roots);
        using var restored = Opened(again);
        var back = Assert.Single(restored.Digests);
        Assert.Equal(digest.Text, back.Text);
        Assert.Equal(digest.Spoken, back.Spoken);
        Assert.Equal(digest.Header, back.Header);
        Assert.Equal(digest.At, back.At);
        Assert.Equal(digest.Since, back.Since);
        Assert.Equal(digest.Seal, back.Seal);
        Assert.Equal(digest.Taint, back.Taint);
        Assert.Equal(digest.Suggestions, back.Suggestions);
        Assert.Equal(digest.Sections.Select(Describe), back.Sections.Select(Describe));
        Assert.True(back.Taint.IsTainted);
    }

    [Fact]
    public void A_missing_file_is_an_empty_book_that_writes_and_the_file_has_a_version()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        Assert.False(book.ReadOnly);
        Assert.Empty(book.Digests);
        Assert.Null(book.PrintedDayFinals());
        Assert.False(book.DutyMade);
        book.MarkDutyMade();
        book.Flush();
        var document = JsonNode.Parse(File.ReadAllText(DigestFile))!.AsObject();
        Assert.Equal(DigestBook.FormatVersion, (int)document["version"]!);
        Assert.True((bool)document["dutyMade"]!);
    }

    [Fact]
    public void Only_the_last_fourteen_digests_are_kept_newest_first()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        for (var i = 0; i < 20; i++) book.Add(Plain("d" + i, Morning.AddDays(i)));
        book.Flush();
        Assert.Equal(DigestBook.DigestsKept, book.Digests.Count);
        Assert.Equal("d19", book.Latest!.Id);
        Assert.Equal("d6", book.Digests[^1].Id);
        Assert.Equal(DigestBook.DigestsKept, JsonNode.Parse(File.ReadAllText(DigestFile))!["digests"]!.AsArray().Count);
    }

    [Fact]
    public void A_digest_added_again_with_the_same_id_replaces_the_old_one()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        book.Add(Plain("d1", Morning));
        book.Add(Full("d1"));
        Assert.Single(book.Digests);
        Assert.Contains("Needs you: 1.", book.Latest!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_newest_digest_of_a_local_day_is_found_by_day()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        book.Add(Plain("old", Morning.AddDays(-1)));
        book.Add(Plain("today", Morning));
        Assert.Equal("today", book.OfDay(new DateOnly(2026, 10, 13), Zone)!.Id);
        Assert.Equal("old", book.OfDay(new DateOnly(2026, 10, 12), Zone)!.Id);
        Assert.Null(book.OfDay(new DateOnly(2026, 10, 11), Zone));
    }

    [Fact]
    public void A_day_final_a_digest_printed_is_kept_as_first_printed_and_never_replaced()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        var day = new DateOnly(2026, 10, 12);
        book.Add(Full("d1"));
        Assert.Equal(new string('a', 64), book.PrintedFinal(day));
        // A later digest that prints another value for the same day (the day's file changed since) does not hide it by taking its place.
        var other = Full("d2") with { Seal = new DigestSeal(day, new string('b', 64)) };
        book.Add(other);
        Assert.Equal(new string('a', 64), book.PrintedFinal(day));
        Assert.Equal(new string('a', 64), book.PrintedDayFinals()![day]);
    }

    [Fact]
    public void Printed_finals_survive_a_restart_and_are_bounded()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        using (var book = Opened(store))
        {
            for (var i = 0; i < DigestBook.FinalsKept + 10; i++)
                book.Add(Plain("d" + i, Morning.AddDays(i)) with { Seal = new DigestSeal(new DateOnly(2026, 1, 1).AddDays(i), i.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0')) });
            book.Flush();
            Assert.Equal(DigestBook.FinalsKept, book.PrintedDayFinals()!.Count);
        }
        using var again = AssistantStore.Open(temp.Roots);
        using var restored = Opened(again);
        var finals = restored.PrintedDayFinals()!;
        Assert.Equal(DigestBook.FinalsKept, finals.Count);
        Assert.DoesNotContain(new DateOnly(2026, 1, 1), finals.Keys);
        Assert.Contains(new DateOnly(2026, 1, 1).AddDays(DigestBook.FinalsKept + 9), finals.Keys);
    }

    [Fact]
    public void The_ledger_is_kept_with_what_the_owner_taught()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        using (var book = Opened(store))
        {
            var candidates = new[]
            {
                new SuggestionCandidate(SuggestionKind.IdleDuty, "a", "Line a", "Cost a", new SuggestionPlan(SuggestionDo.ChangeDuty, "A") { DutyId = "d1", EveryMinutes = 360 }) { Urgency = 2 },
                new SuggestionCandidate(SuggestionKind.FailingCheck, "b", "Line b", "Cost b", new SuggestionPlan(SuggestionDo.StartMission, "B")) { Urgency = 1 },
            };
            book.Update(l => l.Admit(candidates, Morning, new DateOnly(2026, 10, 13)).Ledger);
            book.Update(l => l.Dismiss("a", Morning));
            book.Update(l => l.Accept("b", Morning));
            book.Flush();
        }
        using var again = AssistantStore.Open(temp.Roots);
        using var restored = Opened(again);
        var ledger = restored.Ledger;
        Assert.Equal(new DateOnly(2026, 10, 13), ledger.Day);
        Assert.Equal([SuggestionState.Dismissed, SuggestionState.Accepted], ledger.Today.Select(s => s.State));
        Assert.Equal(1, ledger.DismissalsOf(SuggestionKind.IdleDuty));
        Assert.Equal(["a", "b"], ledger.Settled);
        Assert.Equal(360, ledger.Today[0].Plan.EveryMinutes);
        Assert.Equal("d1", ledger.Today[0].Plan.DutyId);
    }

    [Fact]
    public void A_ledger_change_that_changes_nothing_writes_nothing_and_says_nothing()
    {
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        var changes = 0;
        book.Changed += () => changes++;
        book.Update(l => l);
        book.Update(_ => null);
        book.Flush();
        Assert.Equal(0, changes);
        Assert.False(File.Exists(DigestFile));
        var candidate = new SuggestionCandidate(SuggestionKind.IdleDuty, "a", "L", "C", new SuggestionPlan(SuggestionDo.StartMission, "A"));
        book.Update(l => l.Admit([candidate], Morning, new DateOnly(2026, 10, 13)).Ledger);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_field_this_build_does_not_know_survives_a_load_and_a_save()
    {
        Directory.CreateDirectory(temp.Store);
        File.WriteAllText(DigestFile, """{"version": 1, "future": {"kept": [1, 2, 3]}, "dutyMade": false}""");
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        Assert.False(book.ReadOnly);
        book.Add(Plain("d1", Morning));
        book.Flush();
        var document = JsonNode.Parse(File.ReadAllText(DigestFile))!.AsObject();
        Assert.Equal(3, document["future"]!["kept"]!.AsArray().Count);
        Assert.Single(document["digests"]!.AsArray());
    }

    [Fact]
    public void A_file_from_a_newer_version_is_shown_as_nothing_and_never_written_over()
    {
        Directory.CreateDirectory(temp.Store);
        const string future = """{"version": 2, "digests": [{"id": "x"}], "novel": true}""";
        File.WriteAllText(DigestFile, future);
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        Assert.True(book.ReadOnly);
        Assert.Contains("newer", book.Problem, StringComparison.Ordinal);
        Assert.Empty(book.Digests);
        book.Add(Plain("d1", Morning));
        book.MarkDutyMade();
        book.Flush();
        Assert.Equal(future, File.ReadAllText(DigestFile));
        Assert.Single(book.Digests);                       // the session still shows its own digest from memory
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"version": 1, "version": 1}""")]
    [InlineData("[1, 2]")]
    public void A_file_that_cannot_be_read_is_never_replaced_by_an_empty_one(string text)
    {
        Directory.CreateDirectory(temp.Store);
        File.WriteAllText(DigestFile, text);
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        Assert.True(book.ReadOnly);
        book.Add(Plain("d1", Morning));
        book.Flush();
        Assert.Equal(text, File.ReadAllText(DigestFile));
        Assert.Equal("d1", book.Latest!.Id);
    }

    [Fact]
    public void A_digest_that_does_not_fit_is_dropped_and_the_rest_load()
    {
        var good = StoredForm(Plain("good", Morning));
        var unknownKind = StoredForm(Plain("bad", Morning));
        unknownKind["kind"] = "Quarterly";
        var tooLong = StoredForm(Plain("long", Morning));
        tooLong["header"] = new string('h', 5_000);
        var noSections = StoredForm(Plain("none", Morning));
        noSections.Remove("sections");
        var document = new JsonObject { ["version"] = 1, ["digests"] = new JsonArray(unknownKind, good, tooLong, noSections, JsonValue.Create(5)) };
        Directory.CreateDirectory(temp.Store);
        File.WriteAllText(DigestFile, document.ToJsonString());
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        Assert.Equal(["good"], book.Digests.Select(d => d.Id));
    }

    [Fact]
    public void A_ledger_with_values_it_does_not_know_loads_what_it_can()
    {
        var document = new JsonObject
        {
            ["version"] = 1,
            ["ledger"] = new JsonObject
            {
                ["day"] = "2026-10-13",
                ["today"] = new JsonArray(new JsonObject { ["key"] = "z" }),
                ["dismissals"] = new JsonObject { ["IdleDuty"] = 2, ["Nonsense"] = 5, ["FailingCheck"] = -4 },
                ["off"] = new JsonArray("IdleDuty", "Nonsense", 7),
                ["settled"] = new JsonArray("a", "", 5, "b"),
            },
        };
        Directory.CreateDirectory(temp.Store);
        File.WriteAllText(DigestFile, document.ToJsonString());
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        var ledger = book.Ledger;
        Assert.Empty(ledger.Today);
        Assert.Equal(2, ledger.DismissalsOf(SuggestionKind.IdleDuty));
        Assert.Equal(0, ledger.DismissalsOf(SuggestionKind.FailingCheck));
        Assert.Equal([SuggestionKind.IdleDuty], ledger.Off);
        Assert.Equal(["a", "b"], ledger.Settled);
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_the_book_goes_on()
    {
        var store = AssistantStore.Open(temp.Roots);
        var book = Opened(store);
        var faults = new System.Collections.Concurrent.ConcurrentQueue<string>();
        book.Faulted += faults.Enqueue;
        store.Dispose();                                                  // the store is let go under the book
        book.Add(Plain("d1", Morning));
        book.Flush();
        Assert.Contains(faults, f => f.StartsWith("The digest could not be saved", StringComparison.Ordinal));
        Assert.Equal("d1", book.Latest!.Id);
    }

    [Fact]
    public void A_secret_in_a_title_never_reaches_the_file_in_clear()
    {
        var secret = "sk-" + new string('q', 40);
        var facts = new DigestFacts("d1", Morning, Zone, DigestKind.Scheduled) { NeedsYou = [new DigestAsk("Send " + secret, DigestLink.None)] };
        using var store = AssistantStore.Open(temp.Roots);
        using var book = Opened(store);
        book.Add(DigestComposer.Compose(facts));
        book.Flush();
        Assert.DoesNotContain(secret, File.ReadAllText(DigestFile), StringComparison.Ordinal);
    }

    // A digest as the store writes it, in a store of its own.
    private static JsonObject StoredForm(Digest digest)
    {
        using var root = new AssistantTempRoot();
        using var store = AssistantStore.Open(root.Roots);
        using var book = new DigestBook();
        book.Attach(store);
        book.Add(digest);
        book.Flush();
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root.Store, AssistantStore.DigestFileName)))!["digests"]![0]!.AsObject().DeepClone().AsObject();
    }
}
