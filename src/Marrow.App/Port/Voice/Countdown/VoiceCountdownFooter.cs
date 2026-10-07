// PORT-ONLY: OD-0106 Countdown C; vn-page.css .drn: 36/42/5/2, opacity .3s ease.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice.Countdown;

internal sealed class VoiceCountdownFooter : Control
{
    private bool shown;
    private double from;
    private ContinuousInstant changed;
    private double fraction;

    public VoiceCountdownFooter()
    {
        Name = "VoiceCountdown";
        IsHitTestVisible = false;
        Focusable = false;
        UseLayoutRounding = false;
        Opacity = 0;
    }

    internal bool IsMoving => Math.Abs(Opacity - (shown ? 1 : 0)) > .000001;
    internal CornerRadius CornerRadius => new(1);
    internal ISolidColorBrush? Background { get; private set; }

    internal void Update(CapCountdownFrame? frame)
    {
        var show = frame is { Visible: true };
        if (show != shown)
        {
            from = Opacity;
            changed = SwiftClock.Now;
            shown = show;
        }
        if (frame is { } value)
        {
            fraction = value.Fraction;
            var ink = value.Colour;
            Background = new SolidColorBrush(Color.FromArgb(Byte(ink.A), Byte(ink.R), Byte(ink.G), Byte(ink.B)));
            InvalidateVisual();
        }
        // The reduced-motion design keeps the opacity fade; only the length and colour timing change.
        var elapsed = (SwiftClock.Now - changed).TotalSeconds;
        var progress = elapsed >= .3 ? 1 : LookCurve.Ease(.3).Progress(Math.Max(0, elapsed));
        Opacity = from + ((shown ? 1 : 0) - from) * progress;
        InvalidateArrange();
    }

    internal void Place(double pillWidth, double pillHeight) =>
        Arrange(new Rect(36, pillHeight - 7, Math.Max(0, pillWidth - 78) * fraction, 2));

    public override void Render(DrawingContext context)
    {
        // CSS scaleX drains a rounded full-width line, scaling its end curve too.
        if (fraction <= 0 || Background is null) return;
        using (context.PushTransform(Matrix.CreateScale(fraction, 1)))
            context.DrawRectangle(Background, null, new Rect(0, 0, Bounds.Width / fraction, 2), 1, 1);
    }

    private static byte Byte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
