// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5, V-8.3, V-8.6): the design's durations and curves for the dictated words.
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>The curves and durations of voice-composer.html's <c>.txt .w</c> rules (lines 123-131), each as the design names it.</summary>
internal static class DictationMotion
{
    /// <summary>A word's opacity coming in: <c>.3s ease</c>.</summary>
    public static readonly Animation OpacityIn = LookCurve.Ease(0.3);

    /// <summary>A word's rise coming in, 6 px to 0: <c>.4s ez</c>.</summary>
    public static readonly Animation RiseIn = LookCurve.Ez(0.4);

    /// <summary>The tentative colour turning solid: <c>.45s ease</c>.</summary>
    public static readonly Animation Ink = LookCurve.Ease(0.45);

    /// <summary>A revision's text, going or coming, its opacity: <c>.22s ease</c>.</summary>
    public static readonly Animation ReviseOpacity = LookCurve.Ease(0.22);

    /// <summary>A revision's text, going or coming, its travel: <c>.3s ez</c>.</summary>
    public static readonly Animation ReviseRise = LookCurve.Ez(0.3);

    /// <summary>A word leaving, its opacity: <c>.26s ease</c>.</summary>
    public static readonly Animation OpacityOut = LookCurve.Ease(0.26);

    /// <summary>A word leaving, its travel: <c>.3s ez</c>.</summary>
    public static readonly Animation RiseOut = LookCurve.Ez(0.3);

    /// <summary>The place a later word takes when the words before it changed size (V-7.5's FLIP): the width's <c>.3s ez</c>.</summary>
    public static readonly Animation Flip = LookCurve.Ez(0.3);

    /// <summary>The selection a dictation replaces fading out as the first word comes in: <c>.22s ease</c> (the revision's opacity).</summary>
    public static readonly Animation Ghost = LookCurve.Ease(0.22);

    /// <summary>The layer's caret leaving as the text lands: <c>.18s ease</c>.</summary>
    public static readonly Animation CaretFade = LookCurve.Ease(0.18);

    /// <summary>The field's scroll to keep the end in view (V-8.10): <c>.36s ez</c> (the design's <c>.tin</c> transform).</summary>
    public static readonly Animation Scroll = LookCurve.Ez(0.36);

    /// <summary>How far below its place a word starts (and a revision's new text), px.</summary>
    public const double RiseFrom = 6;

    /// <summary>How far above its place a revision's old text goes, px.</summary>
    public const double ReviseUp = -6;

    /// <summary>How far below its place a leaving word goes, px.</summary>
    public const double LeaveDown = 5;

    /// <summary>A revision's old text and a leaving word are gone this long after they start (the design's <c>setTimeout(..., 340)</c>).</summary>
    public static readonly TimeSpan GoneAfter = TimeSpan.FromMilliseconds(340);
}
