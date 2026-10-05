// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.7): the state of one progress item.
namespace Marrow.App.Port.Voice;

/// <summary>Where a progress item is: not begun, working, or finished.</summary>
public enum VoicePrepState { Waiting, Running, Done }
