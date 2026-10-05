// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-7.5, V-8.4): one dictated word as drawn at an instant.
namespace Marrow.App.Port.Voice;

/// <summary>
/// How one dictated word looks at an instant: the word's own <see cref="Opacity"/> and <see cref="Rise"/> (px below its place), its <see cref="Tentative"/> colour (0 solid to 1
/// tentative, <see cref="DictationInk.AlphaOf"/>), its current text's own <see cref="TextOpacity"/> and <see cref="TextRise"/> (1 and 0 unless a revision is bringing it in), and the
/// <see cref="Old"/> texts a revision is lifting away.
/// </summary>
internal readonly record struct DictationWordLook(
    double Opacity,
    double Rise,
    double Tentative,
    double TextOpacity,
    double TextRise,
    IReadOnlyList<DictationLayerLook> Old);
