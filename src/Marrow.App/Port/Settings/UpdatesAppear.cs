// PORT-ONLY, first-release UE-4 (OD-0076: a control that appears fades and scales in). Holds a control that comes and goes with the state of its row (a row's own Update): hidden it takes no input and
// is not focusable, shown it fades in (.22 s) and grows from 0.8 (.3 s ez). The box itself keeps its size, so the row does not move.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesAppear : Decorator
{
    private const double Small = .8;

    private readonly CzNumber fade, grow;
    private bool shown;

    public UpdatesAppear(Control content, bool shown)
    {
        Child = content;
        this.shown = shown;
        IsEnabled = shown;
        RenderTransformOrigin = new RelativePoint(1, .5, RelativeUnit.Relative);
        var motion = new CzMotion(this);
        fade = new CzNumber(motion, shown ? 1 : 0, v => Opacity = v);
        grow = new CzNumber(motion, shown ? 1 : Small, v => RenderTransform = new ScaleTransform(v, v));
        Opacity = shown ? 1 : 0;
        RenderTransform = new ScaleTransform(shown ? 1 : Small, shown ? 1 : Small);
    }

    public bool Shown
    {
        get => shown;
        set
        {
            if (shown == value) return;
            shown = value;
            IsEnabled = value;
            fade.To(value ? 1 : 0, CzMotion.Ease(.22));
            grow.To(value ? 1 : Small, CzMotion.Ez(.3));
        }
    }
}
