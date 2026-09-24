using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class AutoContinueTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-180")] // App/Runtime/AutoContinue.swift:31-46
    public void A_note_without_a_time_is_kept_only_when_the_existing_note_has_a_time()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var app = (AppModel)RuntimeHelpers.GetUninitializedObject(typeof(AppModel));
            var autoContinue = new AutoContinue(app);
            var threadWithTimedNote = Guid.NewGuid();
            var threadWithUnknownTimeNote = Guid.NewGuid();
            var threadWithNoNote = Guid.NewGuid();
            var earlier = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var later = new DateTimeOffset(2025, 4, 5, 6, 7, 8, TimeSpan.Zero);

            autoContinue.NoteLimit(threadWithTimedNote, earlier);
            autoContinue.NoteLimit(threadWithTimedNote, null);
            autoContinue.NoteLimit(threadWithUnknownTimeNote, null);
            autoContinue.NoteLimit(threadWithUnknownTimeNote, later);
            autoContinue.NoteLimit(threadWithNoNote, null);

            var field = typeof(AutoContinue).GetField("noted", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var notes = Assert.IsAssignableFrom<IDictionary>(field.GetValue(autoContinue));

            Assert.True(notes.Contains(threadWithTimedNote));
            Assert.Equal(earlier, notes[threadWithTimedNote]);
            Assert.True(notes.Contains(threadWithUnknownTimeNote));
            Assert.Equal(later, notes[threadWithUnknownTimeNote]);
            Assert.False(notes.Contains(Guid.NewGuid()));

            var unknownTimeThread = Guid.NewGuid();
            autoContinue.NoteLimit(unknownTimeThread, null);
            Assert.True(notes.Contains(unknownTimeThread));
            Assert.Null(notes[unknownTimeThread]);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-184")] // App/Runtime/AutoContinue.swift:14-15
    public void Margin_is_sixty_seconds()
    {
        Assert.Equal(60d, AutoContinue.Margin);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-185")] // App/Runtime/AutoContinue.swift:11-12
    public void Continue_message_matches_the_source_literal()
    {
        Assert.Equal(
            "The usage limit has reset. Continue exactly where you left off and finish the task.",
            AutoContinue.ContinueMessage);
    }
}
