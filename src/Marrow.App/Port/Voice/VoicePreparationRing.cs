// PORT-ONLY: the locked qring viewBox20/r7.5/stroke2.2, scaled to16. Real progress only.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

internal sealed class VoicePreparationRing : Control
{
    private readonly LookMotion motion;
    private readonly LookNumber fraction;

    internal VoicePreparationRing()
    {
        motion = new LookMotion(this);
        fraction = new LookNumber(motion, 0);
        motion.Frame += InvalidateVisual;
    }

    internal double Fraction { get => fraction.Value; set => fraction.To(Math.Clamp(value, 0, 1), LookCurve.Ease(.3)); }

    public override void Render(DrawingContext context)
    {
        var dark = LookDraw.IsDark(ActualThemeVariant);
        var track = new SolidColorBrush(Color.FromArgb(26, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0));
        context.DrawEllipse(null, new Pen(track, 1.76), new Point(8, 8), 6, 6);
        var progress = fraction.Value;
        if (progress <= 0) return;
        var ink = Marrow.UI.Chrome.Chrome.Accent.Brush(this);
        var pen = new Pen(ink, 1.76, lineCap: PenLineCap.Round);
        if (progress >= 1) { context.DrawEllipse(null, pen, new Point(8, 8), 6, 6); return; }
        var angle = progress * Math.Tau - Math.PI / 2;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(8, 2), false);
            path.ArcTo(new Point(8 + 6 * Math.Cos(angle), 8 + 6 * Math.Sin(angle)), new Size(6, 6), 0, progress > .5, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
