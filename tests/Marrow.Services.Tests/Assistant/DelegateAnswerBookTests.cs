using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "saved as a past answer, so the delegate learns from it", 15.3; ED-NEW-dextra-as22-8): what the delegate answered
// for the owner and what he corrected. Texts are masked for secrets and cut, the file carries a version, a newer one is left alone, fields this build
// does not know come back, a line that is not a record is skipped, and the book is bounded by count and by age.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-8")]
public sealed class DelegateAnswerBookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ChatA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ChatB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ProjectX = Guid.Parse("11111111-0000-0000-0000-000000000011");
    private static readonly Guid ProjectY = Guid.Parse("22222222-0000-0000-0000-000000000022");

    private static DelegateRecord Rec(DateTimeOffset at, string answer = "Keep the existing chart style", Guid? chat = null, Guid? project = null, string question = "Which chart style?") =>
        new(Guid.NewGuid(), at, chat ?? ChatA, "Pitch deck v3", DelegateKind.Question, question, answer, "Dextra", "A decision says all reports keep one style.")
        {
            Project = project,
            Confidence = 0.9,
        };

    [Fact]
    public void An_answer_is_kept_found_and_listed_newest_first()
    {
        var book = new DelegateAnswerBook(new ManualTimeProvider(T0));
        var changed = 0;
        book.Changed += () => changed++;
        var first = book.Add(Rec(T0.AddMinutes(-5), "A"));
        var second = book.Add(Rec(T0.AddMinutes(-1), "B"));
        Assert.Equal(2, changed);
        Assert.Equal(first, book.Find(first.Id));
        Assert.Null(book.Find(Guid.NewGuid()));
        Assert.Equal(["B", "A"], book.Recent(10).Select(r => r.Answer));
        Assert.Equal([second.Id], book.Recent(1).Select(r => r.Id));
        Assert.Empty(book.Recent(0));
        Assert.Equal(["B"], book.Since(T0.AddMinutes(-2)).Select(r => r.Answer));
        Assert.Equal(["A", "B"], book.Since(T0.AddHours(-1)).Select(r => r.Answer));
    }

    [Fact]
    public void Texts_are_masked_for_secrets_and_cut_before_they_are_kept()
    {
        var book = new DelegateAnswerBook(new ManualTimeProvider(T0));
        var kept = book.Add(Rec(T0, "the password: hunter2") with
        {
            Question = "What is the key sk-abcdefghijklmnopqrstuv for? " + new string('q', 2_000),
            Reasoning = new string('r', 5_000),
            ChatTitle = new string('t', 500),
        });
        Assert.DoesNotContain("hunter2", kept.Answer, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuv", kept.Question, StringComparison.Ordinal);
        Assert.True(kept.Question.Length <= DelegateAnswerBook.QuestionChars + 1);
        Assert.True(kept.Reasoning.Length <= DelegateAnswerBook.ReasoningChars + 1);
        Assert.True(kept.ChatTitle.Length <= DelegateAnswerBook.TitleChars + 1);
        var file = book.ToJson()!.ToJsonString();
        Assert.DoesNotContain("hunter2", file, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuv", file, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_is_saved_once_masked_and_the_corrected_come_first_in_what_the_delegate_learns_from()
    {
        var book = new DelegateAnswerBook(new ManualTimeProvider(T0));
        var old = book.Add(Rec(T0.AddHours(-3), "Old answer", project: ProjectX));
        var recent = book.Add(Rec(T0.AddMinutes(-1), "Recent answer", project: ProjectX));
        var corrected = book.Correct(old.Id, "Use the new style, token ghp_abcdefghijklmnopqrstuvwxyz0123456789 is wrong", T0, sent: true)!;
        Assert.True(corrected.Corrected);
        Assert.True(corrected.CorrectionSent);
        Assert.Equal(T0, corrected.CorrectedAt);
        Assert.DoesNotContain("ghp_", corrected.Correction, StringComparison.Ordinal);
        Assert.Null(book.Correct(old.Id, "again", T0, sent: true));                       // one correction each
        Assert.Null(book.Correct(Guid.NewGuid(), "nothing to correct", T0, sent: true));
        Assert.Equal([old.Id, recent.Id], book.Past(ProjectX, 10).Select(r => r.Id));       // his correction first, though older
        Assert.Equal([old.Id], book.Past(ProjectX, 1).Select(r => r.Id));
    }

    [Fact]
    public void What_a_project_learns_from_is_its_own_and_the_ones_with_no_project()
    {
        var book = new DelegateAnswerBook(new ManualTimeProvider(T0));
        var x = book.Add(Rec(T0.AddMinutes(-3), "X", project: ProjectX));
        var y = book.Add(Rec(T0.AddMinutes(-2), "Y", project: ProjectY));
        var none = book.Add(Rec(T0.AddMinutes(-1), "None"));
        Assert.Equal([none.Id, x.Id], book.Past(ProjectX, 10).Select(r => r.Id));
        Assert.Equal(3, book.Past(null, 10).Count);
        Assert.Equal([none.Id, y.Id], book.Past(ProjectY, 10).Select(r => r.Id));
    }

    [Fact]
    public void The_digest_counts_the_questions_answered_since_per_chat_in_the_order_each_chat_began_and_leaves_approvals_out()
    {
        var book = new DelegateAnswerBook(new ManualTimeProvider(T0));
        book.Add(Rec(T0.AddHours(-30), "Before the last digest", ChatA));
        book.Add(Rec(T0.AddMinutes(-50), "One", ChatB) with { ChatTitle = "Thesis outline" });
        book.Add(Rec(T0.AddMinutes(-40), "Two", ChatA));
        book.Add(Rec(T0.AddMinutes(-30), "Three", ChatB) with { ChatTitle = "Thesis outline" });
        book.Add(Rec(T0.AddMinutes(-20), "Four", ChatB) with { ChatTitle = "Thesis outline" });
        book.Add(Rec(T0.AddMinutes(-10), "Allowed", ChatA) with { Kind = DelegateKind.Approval });
        var lines = book.Digest(T0.AddHours(-24));
        Assert.Equal([ChatB, ChatA], lines.Select(l => l.Chat));
        Assert.Equal(["3 questions in “Thesis outline”", "1 question in “Pitch deck v3”"], lines.Select(l => l.Text));
        Assert.Empty(book.Digest(T0));
    }

    [Fact]
    public void The_book_keeps_the_newest_records_and_nothing_older_than_ninety_days()
    {
        var clock = new ManualTimeProvider(T0);
        var book = new DelegateAnswerBook(clock);
        for (var i = 0; i < DelegateAnswerBook.MaxRecords + 25; i++) book.Add(Rec(T0.AddSeconds(i), "a" + i));
        Assert.Equal(DelegateAnswerBook.MaxRecords, book.Count);
        Assert.DoesNotContain(book.Recent(1_000), r => r.Answer == "a0");
        Assert.Contains(book.Recent(1_000), r => r.Answer == "a" + (DelegateAnswerBook.MaxRecords + 24));

        var aged = new DelegateAnswerBook(clock);
        aged.Add(Rec(T0.AddDays(-91), "gone"));
        aged.Add(Rec(T0.AddDays(-89), "kept"));
        Assert.Equal(["kept"], aged.Recent(10).Select(r => r.Answer));
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Empty(aged.ToJson()!["answers"]!.AsArray());                         // the older one is let go when it is written
    }

    [Fact]
    public void The_document_round_trips_and_keeps_what_this_build_does_not_know()
    {
        var clock = new ManualTimeProvider(T0);
        var book = new DelegateAnswerBook(clock);
        var one = book.Add(Rec(T0.AddMinutes(-2), "One", project: ProjectX));
        book.Add(Rec(T0.AddMinutes(-1), "Two", chat: ChatB));
        book.Correct(one.Id, "Wanted the new style", T0, sent: false);
        var document = book.ToJson()!;
        Assert.Equal(1, document["version"]!.GetValue<int>());
        document["future"] = "kept";
        document["answers"]!.AsArray()[0]!["note"] = "also kept";

        var reopened = new DelegateAnswerBook(clock);
        reopened.Load(document);
        Assert.Equal(["Two", "One"], reopened.Recent(10).Select(r => r.Answer));
        var back = reopened.Find(one.Id)!;
        Assert.Equal("Wanted the new style", back.Correction);
        Assert.False(back.CorrectionSent);
        Assert.Equal(ProjectX, back.Project);
        Assert.Equal(0.9, back.Confidence, 3);
        var again = reopened.ToJson()!;
        Assert.Equal("kept", again["future"]!.GetValue<string>());
        Assert.Equal("also kept", again["answers"]!.AsArray()[0]!["note"]!.GetValue<string>());
        Assert.Equal(2, again["answers"]!.AsArray().Count);
    }

    [Fact]
    public void A_file_with_no_version_is_the_first_and_a_newer_one_is_read_for_nothing_and_never_written_over()
    {
        var clock = new ManualTimeProvider(T0);
        var source = new DelegateAnswerBook(clock);
        source.Add(Rec(T0, "One"));
        var plain = source.ToJson()!;
        plain.Remove("version");
        var loaded = new DelegateAnswerBook(clock);
        loaded.Load(plain);
        Assert.False(loaded.ReadOnly);
        Assert.Equal(1, loaded.Count);

        var newer = source.ToJson()!;
        newer["version"] = DelegateAnswerBook.FormatVersion + 1;
        var held = new DelegateAnswerBook(clock);
        held.Load(newer);
        Assert.True(held.ReadOnly);
        Assert.Equal(0, held.Count);
        held.Add(Rec(T0, "Now"));                                                   // this run's answers still count in memory
        Assert.Equal(1, held.Count);
        Assert.Null(held.ToJson());                                                 // and the file stays as it is
    }

    [Fact]
    public void A_line_that_is_not_a_record_is_skipped_and_a_long_text_in_a_hand_edited_file_is_cut()
    {
        var clock = new ManualTimeProvider(T0);
        var good = new DelegateAnswerBook(clock);
        good.Add(Rec(T0, "Good"));
        var line = (JsonObject)good.ToJson()!["answers"]![0]!.DeepClone();
        JsonObject Bad(string field, JsonNode? value)
        {
            var copy = (JsonObject)line.DeepClone();
            if (value is null) copy.Remove(field); else copy[field] = value;
            return copy;
        }
        var long5k = (JsonObject)line.DeepClone();
        long5k["id"] = Guid.NewGuid().ToString("D");
        long5k["answer"] = new string('z', 5_000);
        var document = new JsonObject
        {
            ["version"] = 1,
            ["answers"] = new JsonArray(line.DeepClone(), Bad("id", null), Bad("id", "not a guid"), Bad("chat", null), Bad("at", "yesterday"), Bad("kind", "Maybe"),
                Bad("question", null), Bad("answer", 5), JsonValue.Create("nonsense"), long5k),
        };
        var book = new DelegateAnswerBook(clock);
        book.Load(document);
        Assert.Equal(2, book.Count);                                                // the good line (once) and the long one, cut
        Assert.True(book.Recent(10).All(r => r.Answer.Length <= DelegateAnswerBook.AnswerChars + 1));
    }
}
