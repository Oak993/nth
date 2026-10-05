// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-10, voice-SPEC V-14.6): the field's layers crossfade (DESIGN voice-composer.html
// `.field>.lay`).
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// One layer of the composer's field, the placeholder or a note: hidden it is clear and 3 px low; shown it is opaque and in place. Coming in takes 0.26 s for the
/// opacity (CSS ease) and 0.38 s for the rise (ez), both 0.05 s late; going out 0.2 s and 0.3 s, at once. The layers change over together, so one fades out as the
/// other comes in. A change in the middle of a move goes on from where it is (<see cref="AnimatedScalar"/>); Reduce Motion lands at once.
/// </summary>
internal sealed class FieldFade
{
    /// <summary>The rise of a hidden layer, in px.</summary>
    public const double Rise = 3;

    private static readonly TimeSpan InDelay = TimeSpan.FromSeconds(0.05);

    private readonly AnimatedScalar opacity;
    private readonly AnimatedScalar rise;
    private double drawnOpacity;
    private double drawnRise;
    private WorkItem? pending;
    private bool visible;

    /// <param name="owner">The control whose time in the tree bounds the motion.</param>
    /// <param name="visible">Whether the layer starts shown.</param>
    /// <param name="apply">Draws the layer at an opacity and a rise.</param>
    public FieldFade(Control owner, bool visible, Action<double, double> apply)
    {
        this.visible = visible;
        drawnOpacity = visible ? 1 : 0;
        drawnRise = visible ? 0 : Rise;
        opacity = new AnimatedScalar(owner, drawnOpacity, value =>
        {
            drawnOpacity = value;
            apply(drawnOpacity, drawnRise);
        });
        rise = new AnimatedScalar(owner, drawnRise, value =>
        {
            drawnRise = value;
            apply(drawnOpacity, drawnRise);
        });
    }

    /// <summary>Whether the layer is meant to be shown (its fade may still be under way).</summary>
    public bool Visible => visible;

    public double Opacity => opacity.Value;

    public double RisePx => rise.Value;

    public void Set(bool shown)
    {
        if (shown == visible) return;
        visible = shown;
        pending?.Cancel();
        pending = null;
        if (shown)
        {
            pending = MainActor.AsyncAfter(InDelay, () =>
            {
                opacity.Set(1, LookCurve.Ease(0.26));
                rise.Set(0, LookCurve.Ez(0.38));
            });
            return;
        }
        opacity.Set(0, LookCurve.Ease(0.2));
        rise.Set(Rise, LookCurve.Ez(0.3));
    }
}
