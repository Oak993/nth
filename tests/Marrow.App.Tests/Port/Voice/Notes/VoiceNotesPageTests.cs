using System.Text;
using Marrow.App.Port.Voice.Notes;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Voice.Notes;

// VT-17 (OD-0106 Notes A Plain; OD-0099 Q-V-7 A): the eleven notes of set A, and the locked note, are the D-VN page's words byte for byte, each with the one link
// the page names. The page is read from the repository.
public sealed class VoiceNotesPageTests
{
    private static readonly (VoiceNoteCase Case, string Id)[] Eleven =
    [
        (VoiceNoteCase.NoMicrophone, "nomic"), (VoiceNoteCase.MicrophoneUnplugged, "unplug"), (VoiceNoteCase.MicrophoneBusy, "busy"),
        (VoiceNoteCase.ModelStillDownloading, "dl"), (VoiceNoteCase.ModelFailedToLoad, "load"), (VoiceNoteCase.NoAvx2, "avx"),
        (VoiceNoteCase.EngineFailure, "engine"), (VoiceNoteCase.CloudNoKey, "nokey"), (VoiceNoteCase.CloudKeyRefused, "refused"),
        (VoiceNoteCase.CloudOutOfQuota, "quota"), (VoiceNoteCase.CloudNoNetwork, "nonet"),
    ];

    private static VoiceNoteDestination Destination(string key) => key switch
    {
        "privacy" => VoiceNoteDestination.PrivacyMicrophone,
        "sound" => VoiceNoteDestination.SoundSettings,
        "mic" => VoiceNoteDestination.VoiceTypingMicrophone,
        "model" => VoiceNoteDestination.VoiceTypingSpeechModel,
        "engine" => VoiceNoteDestination.VoiceTypingEngine,
        "key" => VoiceNoteDestination.VoiceTypingOpenAiKey,
        "page" => VoiceNoteDestination.VoiceTypingPage,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "a destination the plan does not name for set A"),
    };

    public static TheoryData<VoiceNoteCase, string> Cases()
    {
        var data = new TheoryData<VoiceNoteCase, string>();
        foreach (var (c, id) in Eleven) data.Add(c, id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN set A; Req Q-V-7 A
    public void Each_note_of_set_A_is_the_pages_words_byte_for_byte_with_the_pages_link(VoiceNoteCase noteCase, string id)
    {
        var page = VoiceNotesPage.SetA(id);

        var note = VoiceNotes.For(noteCase);

        Assert.Equal(Encoding.UTF8.GetBytes(page.Before), Encoding.UTF8.GetBytes(note.Before));
        Assert.Equal(Encoding.UTF8.GetBytes(page.Link), Encoding.UTF8.GetBytes(note.Link));
        Assert.Equal(Destination(page.Destination), note.Destination);
        Assert.Equal(noteCase, note.Case);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN set A; Req Q-V-7 A
    public void There_are_exactly_the_pages_eleven_notes_in_its_order_and_none_is_the_locked_one()
    {
        Assert.Equal(Eleven.Select(e => e.Case), VoiceNotes.All.Select(n => n.Case));
        Assert.Equal(VoiceNotesPage.CaseIds.Skip(1), Eleven.Select(e => e.Id)); // the page lists blocked first, then these eleven
        Assert.DoesNotContain(VoiceNotes.All, n => n.Case == VoiceNoteCase.Blocked);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN locked; Req V-14.6
    public void The_locked_note_is_the_pages_and_opens_the_microphone_privacy_page()
    {
        var page = VoiceNotesPage.Locked;
        var note = VoiceNotes.Blocked;

        Assert.Equal(("Microphone blocked. Allow Nth in ", "Privacy & security > Microphone"), (note.Before, note.Link));
        Assert.Equal((page.Before, page.Link), (note.Before, note.Link));
        Assert.Equal(VoiceNoteDestination.PrivacyMicrophone, note.Destination);
        Assert.Equal(VoiceNoteCase.Blocked, note.Case);
        Assert.Same(note, VoiceNotes.For(VoiceNoteCase.Blocked));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Rule", "R-TEST-4")] // one link per note; Req Q-V-7 A
    public void Each_note_says_what_happened_then_ends_in_one_link_that_names_a_place(VoiceNoteCase noteCase, string id)
    {
        _ = id;
        var note = VoiceNotes.For(noteCase);

        Assert.True(note.Before.EndsWith(' '), "the link follows the words after a space");
        Assert.NotEmpty(note.Link);
        Assert.Equal(note.Before + note.Link, note.Text);
        Assert.DoesNotContain("<", note.Text, StringComparison.Ordinal);
        Assert.Single(VoiceNotes.All, n => n.Link == note.Link && n.Before == note.Before);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN set A; Req Q-V-7 A
    public void An_unknown_case_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VoiceNotes.For((VoiceNoteCase)99));
    }
}
