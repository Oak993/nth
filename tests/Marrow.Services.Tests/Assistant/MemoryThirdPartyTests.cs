using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2 "Never stored"): "Dossiers on other people. Notes about a third party are kept only when the owner states
// them, and only what he said." The store keeps such a note only in words the owner used, and never from untrusted content.
[Trait("Decision", "ED-NEW-dextra-as12-3")]
[Trait("Feature", "AS-12")]
public sealed class MemoryThirdPartyTests : IDisposable
{
    private const string Said = "My manager Priya wants the weekly report on Monday morning.";
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public void A_note_about_a_person_is_kept_when_the_owner_said_all_of_it()
    {
        var outcome = rig.Memory.Propose(MemoryRig.Clean("Priya wants the weekly report on Monday morning", ownerWords: Said));
        Assert.Equal(NoteResult.Kept, outcome.Result);
        Assert.Equal("Priya wants the weekly report on Monday morning", rig.Memory.Find(outcome.Note!.Id)!.Text);
    }

    [Fact]
    public void A_note_about_a_person_without_the_owners_words_is_refused()
    {
        var outcome = rig.Memory.Propose(MemoryRig.Clean("The owner's manager Priya wants the weekly report on Monday morning"));
        Assert.Equal(NoteResult.Refused, outcome.Result);
        Assert.Equal(NoteRefusal.ThirdParty, outcome.Refusal);
        Assert.Contains("Notes about people are kept only when the owner states them", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.DoesNotContain("Priya", rig.AllText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Priya is nervous about the weekly report")]
    [InlineData("Priya wants the weekly report every Friday")]
    [InlineData("Priya's birthday is on June 3")]
    [InlineData("Priya works late and skips lunch")]
    public void Only_what_the_owner_said_is_kept_nothing_inferred(string note)
    {
        var outcome = rig.Memory.Propose(MemoryRig.Clean(note, ownerWords: Said));
        Assert.Equal(NoteRefusal.ThirdParty, outcome.Refusal);
        Assert.Empty(rig.Memory.Notes(null));
    }

    [Fact]
    public void A_note_the_proposer_says_is_about_a_person_is_held_to_the_rule_even_when_no_name_shows()
    {
        var flagged = MemoryRig.Clean("She prefers tea over coffee") with { AboutPerson = true };
        Assert.Equal(NoteRefusal.ThirdParty, rig.Memory.Propose(flagged).Refusal);
        Assert.Equal(NoteRefusal.ThirdParty, rig.Memory.Propose(flagged with { OwnerWords = "He likes coffee" }).Refusal);
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(flagged with { OwnerWords = "she prefers tea over coffee, my sister said" }).Result);
    }

    [Theory]
    [InlineData("Call Priya on +91 98765 43210")]
    [InlineData("Priya's address is asha.k@example.test")]
    [InlineData("Dr. Rao is the dentist")]
    public void Contact_details_and_named_people_follow_the_same_rule(string note)
    {
        Assert.Equal(NoteRefusal.ThirdParty, rig.Memory.Propose(MemoryRig.Clean(note)).Refusal);
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean(note, ownerWords: note)).Result);
    }

    [Fact]
    public void Untrusted_content_never_makes_a_note_about_a_person_not_even_a_quarantined_one()
    {
        var text = "Priya wants the weekly report on Monday morning";
        var outcome = rig.Memory.Propose(MemoryRig.FromWeb(text, ownerWords: Said));
        Assert.Equal(NoteRefusal.ThirdParty, outcome.Refusal);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.Empty(rig.FileNames(MemoryRig.Main));
        var email = MemoryRig.Clean(text, ownerWords: Said) with { SourceTaint = Taint.From(ContentOrigin.Email, "from: someone") };
        Assert.Equal(NoteRefusal.ThirdParty, rig.Memory.Propose(email).Refusal);
    }

    [Fact]
    public void The_owner_typing_a_note_about_a_person_in_the_Memory_view_is_his_own_words()
    {
        var outcome = rig.Memory.Propose(MemoryRig.Typed("My sister Asha is vegetarian"));
        Assert.Equal(NoteResult.Kept, outcome.Result);
        Assert.Equal(NoteAuthor.Owner, outcome.Note!.Author);
    }

    [Fact]
    public void Notes_about_the_owner_and_his_work_need_no_statement()
    {
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference)).Result);
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean("Droppy Code builds with dotnet 10", NoteKind.Project)).Result);
    }
}
