// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-7 A and Q-V-8 A, OD-0106; first-release voice typing, VT-10): one note of the composer's note layer.
namespace Marrow.App.Port.Voice;

/// <summary>
/// A note the composer shows in its field (or, with text in the draft, in its notice line): <see cref="Text"/>, then at most one link
/// (<see cref="LinkText"/> opening <see cref="LinkTarget"/>, OD-0106: exactly one link each), for <see cref="Length"/>. A
/// <see cref="Warning"/> note carries the warning mark (V-14.6).
/// </summary>
public sealed record VoiceNoteContent(string Id, string Text, string? LinkText, string? LinkTarget, TimeSpan Length, bool Warning);
