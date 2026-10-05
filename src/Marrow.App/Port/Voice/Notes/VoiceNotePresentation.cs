// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): a note as shown now.
namespace Marrow.App.Port.Voice.Notes;

/// <summary>The note, the surface that shows it, how long it stays, and what a screen reader hears (a polite announcement).</summary>
public sealed record VoiceNotePresentation(VoiceNote Note, VoiceNoteSurface Surface, TimeSpan Length, string Announcement);
