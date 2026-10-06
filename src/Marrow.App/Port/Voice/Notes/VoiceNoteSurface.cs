// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): which surface shows a note (V-14.6).
namespace Marrow.App.Port.Voice.Notes;

/// <summary>Field: the composer's text field, over the placeholder (an empty draft). NoticeLine: the composer's existing notice line (the draft holds text: never hide it).</summary>
public enum VoiceNoteSurface { Field, NoticeLine }
