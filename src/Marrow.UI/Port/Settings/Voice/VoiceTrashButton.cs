// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-13 B; first-release voice typing, VT-14): the trash icon that removes an installed model.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// Q-V-13 B: "an installed model (not the one in use) can be removed from a trash icon shown on hover at the row's end, confirmed by a second click". The trash is there to be
/// found by the pointer or the keyboard only while the row is hovered or focused (<see cref="SetRevealed"/>: opacity 0.15 s ease; hidden it takes no pointer and no focus). The
/// first click ARMS it (ENGINEERING DECISION: the fill and the glyph move to the system error colour over 0.12 s, the name says it asks for the second click, and the icon stays
/// where it is); the second click raises <see cref="Confirmed"/>. Leaving the row, or losing the hover and the focus, disarms it. There is no timer: nothing here waits on a clock.
/// </summary>
public sealed class VoiceTrashButton : VoiceGlyphButton
{
    private readonly CzNumber reveal;
    private string subject = "";
    private bool armed;
    private bool revealed;

    public VoiceTrashButton() : base("Remove")
    {
        reveal = new CzNumber(Motion, 0, v => Opacity = v);
        Opacity = 0;
        IsHitTestVisible = false;
        Focusable = false;
        Invoked += OnInvoked;
    }

    /// <summary>Raised by the second click.</summary>
    public event Action? Confirmed;

    public bool Armed => armed;

    public bool Revealed => revealed;

    /// <summary>The icon's opacity now (0 hidden, 1 shown).</summary>
    public double RevealOpacity => reveal.Value;

    /// <summary>Names the model the button removes, for the name a screen reader gives.</summary>
    public void SetSubject(string modelName)
    {
        subject = modelName;
        Rename();
    }

    /// <summary>True while the row is hovered or has the focus.</summary>
    public void SetRevealed(bool value)
    {
        if (revealed == value) return;
        revealed = value;
        IsHitTestVisible = value;
        Focusable = value;
        reveal.To(value ? 1 : 0, CzMotion.Ease(.15));
        if (!value) Disarm();
    }

    /// <summary>Back to the first click.</summary>
    public void Disarm()
    {
        if (!armed) return;
        armed = false;
        Rename();
        Restyle();
    }

    private void OnInvoked()
    {
        if (!armed)
        {
            armed = true;
            Rename();
            Restyle();
            return;
        }
        armed = false;
        Rename();
        Restyle();
        Confirmed?.Invoke();
    }

    private void Rename()
    {
        var name = armed ? "Click again to remove " + subject : "Remove " + subject;
        AutomationProperties.SetName(this, name.TrimEnd());
    }

    private Color Danger() => Marrow.UI.Chrome.Chrome.Danger.Resolve(ActualThemeVariant);

    protected override Color FillTarget()
    {
        if (!armed) return base.FillTarget();
        var danger = Danger();
        return Color.FromArgb(46, danger.R, danger.G, danger.B);
    }

    protected override Color InkTarget() => armed ? Danger() : base.InkTarget();

    protected override void DrawGlyph(DrawingContext context, Color colour)
    {
        var pen = new Pen(new SolidColorBrush(colour), 1.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawLine(pen, new Point(5.8, 6.4), new Point(14.2, 6.4)); // the lid
        context.DrawLine(pen, new Point(8.6, 4.8), new Point(11.4, 4.8)); // the handle
        var body = new StreamGeometry();
        using (var path = body.Open())
        {
            path.BeginFigure(new Point(6.9, 8), false);
            path.LineTo(new Point(7.4, 14.2));
            path.LineTo(new Point(12.6, 14.2));
            path.LineTo(new Point(13.1, 8));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, body);
        context.DrawLine(pen, new Point(8.9, 9.4), new Point(9.1, 12.6));
        context.DrawLine(pen, new Point(11.1, 9.4), new Point(10.9, 12.6));
    }
}
