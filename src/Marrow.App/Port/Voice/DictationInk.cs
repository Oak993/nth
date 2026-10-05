// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-8.4): the tentative colour.
namespace Marrow.App.Port.Voice;

/// <summary>V-8.4: <c>wink3</c>, the text colour at 0.6 (DESIGN voice-composer.html:124), for a tentative word; solid is the text colour.</summary>
internal static class DictationInk
{
    public const double TentativeAlpha = 0.6;

    /// <summary>The share of the text colour's own opacity a word is drawn with: 1 solid, <see cref="TentativeAlpha"/> tentative, between while it turns.</summary>
    /// <param name="tentative">0 solid to 1 tentative (<see cref="DictationWordLook.Tentative"/>).</param>
    public static double AlphaOf(double tentative) => 1 - (1 - TentativeAlpha) * tentative;
}
