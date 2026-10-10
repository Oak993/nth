using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Tests.Port.Voice;

// VT-10, V-T83 and V-T84 (voice-SPEC V-14.5, V-14.6, V-16.3.5; OD-0099 Q-V-7 A and Q-V-8 A; OD-0106 Notes set A, exactly one link each): the words and the
// times of the composer's notes and announcements, as data. The notes are shown by the composer's note layer (ComposerVoiceNotesTests).
public sealed class VoiceNotesTests
{
    [Fact] // V-T83
    [Trait("Rule", "R-TEST-10")]
    public void Cancelled_says_Voice_input_cancelled_for_1_6_s_with_no_link()
    {
        var note = VoiceNotes.For(VoiceNoteKind.Cancelled)!;
        Assert.Equal("Voice input cancelled", note.Text);
        Assert.Null(note.LinkText);
        Assert.Null(note.LinkTarget);
        Assert.Equal(TimeSpan.FromSeconds(1.6), note.Length);
        Assert.False(note.Warning);
    }

    [Fact] // V-T83
    [Trait("Rule", "R-TEST-10")]
    public void No_speech_says_No_speech_was_heard_for_1_6_s_with_no_link()
    {
        var note = VoiceNotes.For(VoiceNoteKind.NoSpeech)!;
        Assert.Equal("No speech was heard", note.Text);
        Assert.Null(note.LinkText);
        Assert.Equal(TimeSpan.FromSeconds(1.6), note.Length);
        Assert.False(note.Warning);
    }

    [Fact] // V-T83, OD-0106 (the locked "Microphone blocked" note's shape: the text, then one underlined link)
    [Trait("Rule", "R-TEST-10")]
    public void Blocked_names_Privacy_and_security_Microphone_as_its_one_link_to_the_Windows_page_for_5_2_s_with_the_warning_mark()
    {
        var note = VoiceNotes.For(VoiceNoteKind.Blocked)!;
        Assert.Equal("Microphone blocked. Allow Nth in ", note.Text);
        Assert.Equal("Privacy & security > Microphone", note.LinkText);
        Assert.Equal("ms-settings:privacy-microphone", note.LinkTarget);
        Assert.Equal("ms-settings:privacy-microphone", VoiceNotes.BlockedDestination);
        Assert.Equal(TimeSpan.FromSeconds(5.2), note.Length);
        Assert.True(note.Warning);
    }

    [Fact] // VT-17 builds the other kinds' notes: until then they raise none
    [Trait("Rule", "R-TEST-10")]
    public void The_error_kinds_VT_17_words_have_no_note_yet()
    {
        foreach (var kind in new[] { VoiceNoteKind.NoMicrophone, VoiceNoteKind.CaptureFailed, VoiceNoteKind.ModelLoadFailed, VoiceNoteKind.EngineFailed })
            Assert.Null(VoiceNotes.For(kind));
    }

    [Fact] // Q-V-8 A, V-16.3.5
    [Trait("Rule", "R-TEST-10")]
    public void The_privacy_note_of_each_engine_is_the_pages_privacy_line_for_5_s()
    {
        var local = VoiceNotes.Privacy(VoiceEngine.Local);
        Assert.Equal("Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.", local.Text);
        var cloud = VoiceNotes.Privacy(VoiceEngine.Cloud);
        Assert.Equal("Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.", cloud.Text);
        Assert.All(new[] { local, cloud }, note =>
        {
            Assert.Equal(TimeSpan.FromSeconds(5), note.Length);
            Assert.Null(note.LinkText);
            Assert.False(note.Warning);
        });
        Assert.NotEqual(local.Id, cloud.Id);
    }

    [Fact] // V-T84, V-14.5
    [Trait("Rule", "R-TEST-10")]
    public void The_announcements_are_the_designs_exact_strings()
    {
        Assert.Equal("Listening. Your words are written into the composer as you speak. Click the orb again to finish, or press Escape to cancel.", VoiceAnnouncements.Listening);
        Assert.Equal("Finishing the text.", VoiceAnnouncements.Finishing);
        Assert.Equal("Voice input cancelled; the words were removed.", VoiceAnnouncements.Cancelled);
        Assert.Equal("The microphone is blocked. Allow Nth in Windows Settings, Privacy and security, Microphone.", VoiceAnnouncements.Blocked);
        Assert.Equal("Done: switch the theme. The text is in the composer; send it when you are ready.", VoiceAnnouncements.Done("switch the theme.", sending: false));
        Assert.Equal("Done: switch the theme. Sending.", VoiceAnnouncements.Done("switch the theme.", sending: true));
    }
}
