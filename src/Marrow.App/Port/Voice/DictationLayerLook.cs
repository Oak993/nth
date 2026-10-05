// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-7.5): one layer of a revised word's text, as drawn at an instant.
namespace Marrow.App.Port.Voice;

/// <summary>The old text of a revision as it goes: <see cref="Text"/>, drawn at <see cref="Opacity"/> and <see cref="Rise"/> px below its place (negative: above).</summary>
internal readonly record struct DictationLayerLook(string Text, double Opacity, double Rise);
