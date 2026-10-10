using Marrow.App.Port.Voice.Notes;

namespace Marrow.App.Tests.Port.Voice.Notes;

// VT-17 (voice-SPEC V-14.5, V-14.6; OD-0106): which surface shows a note, how long it stays, and what a screen reader hears.
public sealed class VoiceNotePlacementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [Trait("Rule", "R-TEST-4")] // Req V-14.6; Oracle OD-0106
    public void An_empty_draft_shows_the_note_in_the_field(string? draft)
    {
        Assert.Equal(VoiceNoteSurface.Field, VoiceNotes.SurfaceFor(draft));
    }

    [Theory]
    [InlineData("Add a shortcut for the theme switch")]
    [InlineData(" ")]
    [InlineData("\n")]
    [Trait("Rule", "R-TEST-4")] // Req V-14.6; Oracle OD-0106
    public void A_draft_with_any_text_shows_the_note_in_the_composers_notice_line(string draft)
    {
        Assert.Equal(VoiceNoteSurface.NoticeLine, VoiceNotes.SurfaceFor(draft)); // the field's placeholder test is "no characters": a blank draft still hides the placeholder
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-14.6
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public void Every_note_stays_5_2_seconds_the_locked_notes_time()
    {
        Assert.Equal(TimeSpan.FromSeconds(5.2), VoiceNotes.DisplayLength);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-14.5
    public void The_locked_notes_announcement_is_V_14_5s_string()
    {
        Assert.Equal("The microphone is blocked. Allow nth in Windows Settings, Privacy and security, Microphone.", VoiceNotes.Announcement(VoiceNotes.Blocked));
    }

    [Theory]
    [InlineData(VoiceNoteCase.NoMicrophone, "No microphone found. Set one up in System, Sound.")]
    [InlineData(VoiceNoteCase.MicrophoneUnplugged, "Microphone disconnected. Choose another in Voice typing, Microphone.")]
    [InlineData(VoiceNoteCase.EngineFailure, "Voice typing stopped on an error. Check it in Settings, Voice typing.")]
    [InlineData(VoiceNoteCase.CloudOutOfQuota, "This key reached its OpenAI limit. Switch to this PC in Voice typing, Engine.")]
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public void Another_notes_announcement_is_the_note_read_aloud_with_the_greater_than_signs_as_commas_and_a_full_stop(VoiceNoteCase noteCase, string expected)
    {
        Assert.Equal(expected, VoiceNotes.Announcement(VoiceNotes.For(noteCase)));
    }

    [Fact]
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public void No_announcement_holds_a_greater_than_sign_and_each_ends_in_a_full_stop()
    {
        foreach (var note in VoiceNotes.All)
        {
            var spoken = VoiceNotes.Announcement(note);
            Assert.DoesNotContain(">", spoken, StringComparison.Ordinal);
            Assert.EndsWith(".", spoken, StringComparison.Ordinal);
            Assert.DoesNotContain("..", spoken, StringComparison.Ordinal);
        }
    }
}
