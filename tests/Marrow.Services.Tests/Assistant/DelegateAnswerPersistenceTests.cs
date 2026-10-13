using System.Collections.Concurrent;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3, 15.3; ED-NEW-dextra-as22-8): the delegate's answers are kept in the assistant's store, written as soon as one is
// added or corrected and again when the store is let go; a newer version's file is never written over; a write that fails is reported and the app goes on.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-8")]
public sealed class DelegateAnswerPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static DelegateRecord Rec(string answer) =>
        new(Guid.NewGuid(), T0, Guid.NewGuid(), "Pitch deck v3", DelegateKind.Question, "Which chart style?", answer, "Dextra", "A decision settles it.") { Confidence = 0.9 };

    [Fact]
    public void An_answer_survives_a_restart_and_so_does_the_owners_correction()
    {
        var clock = new ManualTimeProvider(T0);
        Guid id;
        using (var store = AssistantStore.Open(temp.Roots))
        {
            var book = new DelegateAnswerBook(clock);
            using var persistence = new DelegateAnswerPersistence(store, book);
            Assert.Null(store.DelegateAnswers());                                      // nothing yet
            id = book.Add(Rec("Keep the existing chart style")).Id;
            persistence.Flush();
            Assert.NotNull(store.DelegateAnswers());
            book.Correct(id, "Use the new style", T0, sent: true);
        }                                                                              // Dispose writes what is pending
        using var reopened = AssistantStore.Open(temp.Roots);
        var again = new DelegateAnswerBook(clock);
        using var follow = new DelegateAnswerPersistence(reopened, again);
        var record = again.Find(id)!;
        Assert.Equal("Keep the existing chart style", record.Answer);
        Assert.Equal("Use the new style", record.Correction);
        Assert.True(record.CorrectionSent);
    }

    [Fact]
    public void A_file_from_a_newer_nth_is_never_written_over()
    {
        var clock = new ManualTimeProvider(T0);
        using var store = AssistantStore.Open(temp.Roots);
        var newer = new System.Text.Json.Nodes.JsonObject { ["version"] = DelegateAnswerBook.FormatVersion + 1, ["answers"] = new System.Text.Json.Nodes.JsonArray(), ["mystery"] = "kept" };
        store.SaveDelegateAnswers(newer);
        var path = Path.Combine(store.Root, AssistantStore.DelegateAnswersFile);
        var before = File.ReadAllText(path);

        var book = new DelegateAnswerBook(clock);
        using (var persistence = new DelegateAnswerPersistence(store, book))
        {
            Assert.True(book.ReadOnly);
            book.Add(Rec("Written only in memory"));
            persistence.Flush();
        }
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_never_thrown_into_the_app()
    {
        var clock = new ManualTimeProvider(T0);
        var store = AssistantStore.Open(temp.Roots);
        var book = new DelegateAnswerBook(clock);
        using var persistence = new DelegateAnswerPersistence(store, book);
        var faults = new ConcurrentBag<string>();
        persistence.Faulted += faults.Add;
        store.Dispose();                                                               // the disk is gone from under it
        book.Add(Rec("Lost for now"));
        persistence.Flush();
        Assert.Contains(faults, f => f.StartsWith("What Dextra answered for you could not be saved", StringComparison.Ordinal));
        Assert.Equal(1, book.Count);                                                   // and the answer is still there in memory
    }
}
