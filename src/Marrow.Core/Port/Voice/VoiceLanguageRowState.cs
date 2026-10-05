// PORT-ONLY, owner-ruled addition (OD-0092, OD-0097, OD-0099 Q-V-17; first-release voice typing, VT-13): what the Voice typing page's Language row shows.
namespace Marrow.Core.Port.Voice;

/// <summary>The Language row's button text, the code of the checked menu item (null: no item is checked, language default D) and the row's detail (voice-SPEC V-16.3.1).</summary>
public sealed record VoiceLanguageRowState(string ButtonText, string? CheckedCode, string Detail);
