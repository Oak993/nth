// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5): the old text of a revised word, lifting away.
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>What a revision replaced: the old text, fading and rising away (the design's <c>.t.old</c>), until <see cref="GoneAt"/>.</summary>
internal sealed class DictationOldText(string text, DictationTween opacity, DictationTween rise, ContinuousInstant goneAt)
{
    public string Text { get; } = text;

    internal DictationTween Opacity = opacity;

    internal DictationTween Rise = rise;

    public ContinuousInstant GoneAt { get; } = goneAt;
}
