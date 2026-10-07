// PORT-ONLY: MA-3b-2/9, page .bar :124-127, UsageLight and capsule tokens shared by account views.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountUsageBar : Control
{
    private readonly AnimatedScalar shown;
    internal AccountUsageBar() { Height = 4; MinWidth = 20; shown = new AnimatedScalar(this, 0, _ => InvalidateVisual()); }
    internal double? Target { get; private set; }
    internal bool Hot { get; private set; }
    internal bool Out { get; private set; }
    internal string Percent { get; private set; } = "";
    internal event Action? Applied;
    internal void Apply(PopoverUsageBar bar)
    {
        Target = bar.Fraction; Hot = bar.Hot; Out = bar.Out; Percent = bar.Percent;
        shown.Set(bar.Fraction ?? 0, FrameClock.IsBound && !CapsuleMotion.Reduce(this) ? Animation.TimingCurve(.32, .72, 0, 1, .6) : null);
        InvalidateVisual(); Applied?.Invoke();
    }
    public override void Render(DrawingContext context)
    {
        var tokens = CapsuleTokens.For(ActualThemeVariant == ThemeVariant.Dark);
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(new SolidColorBrush(tokens.Track), null, new RoundedRect(rect, 2));
        if (shown.Value > 0) context.DrawRectangle(new SolidColorBrush(Out ? tokens.Tx3 : Hot ? tokens.Hot : tokens.Fill), null,
            new RoundedRect(new Rect(0, 0, Bounds.Width * shown.Value, Bounds.Height), 2));
    }
}
