// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
// voice-glow 0.2.1 (Jakub Antalik, MIT; see THIRD_PARTY_NOTICES), carried through the design's voice-glow.js.
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>The package's colour scheme (<c>theme</c>): Marrow passes the app's appearance (V-13.3).</summary>
public enum VoiceGlowTheme { Dark, Light }

/// <summary>The package's type presets (<c>Pn</c>): <c>default</c> is the one Marrow uses (V-13.3).</summary>
public enum VoiceGlowType { Default, Pill, Mobile }

/// <summary>One of the seven glow lobes (<c>we</c>): its offset and size, and which of the three band followers lights it.</summary>
public readonly record struct VoiceGlowLobe(double X, double W, double H, int Band);

/// <summary>The four band colours as <c>"r, g, b"</c> text (<c>Ne</c> / <c>Ue</c>).</summary>
public sealed record VoiceGlowBandColors(string Core, string Above, string Mid, string Below);

/// <summary>Band colours as a caller may give them: a hex or <c>rgb()</c> text each, any of them null.</summary>
public sealed record VoiceGlowBandColorProps(string? Core = null, string? Above = null, string? Mid = null, string? Below = null);
