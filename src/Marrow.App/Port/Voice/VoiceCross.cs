// PORT-ONLY, owner-ruled addition (OD-0091 fourth to sixth amendments; first-release voice typing, VT-12, voice-SPEC V-12.3, V-12.5, V-12.8): the cancel cross, in its two places.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>Where the cross is drawn: in its own glass disc at the pill's near end, or beside the orb (the Cross style).</summary>
internal enum VoiceCrossLook { Disc, Beside }

/// <summary>
/// The cancel control. <see cref="VoiceCrossLook.Disc"/> (V-12.3, voice-composer.html <c>.vpill .px</c>): 24 by 24, radius 12, a small glass disc of its own (a soft top highlight, the <c>pgXBg</c> fill, a
/// 0.5 px <c>pgXRim</c> ring, the dispersion fringe, a 1 px shadow), the icon 11 px with a 1.5 stroke in <c>pgX</c>; hover turns the icon <c>pgXHover</c> and the disc brighter by 12 %, a press scales it
/// to 0.92. <see cref="VoiceCrossLook.Beside"/> (V-12.5, <c>.xbtn</c>): 24 by 24, radius 12, the <c>hoverfill</c> with a 1 px <c>winedge</c> ring, the icon 12 px in <c>wink3</c>; hover fills with
/// <c>pressfill</c> and the icon turns <c>wink</c>. Named <c>Cancel voice input</c>, tooltip <c>Cancel (Esc)</c> (V-12.8); a tab stop only while <see cref="Live"/>.
/// Hover changes ease over 0.12 s ease-out (the design's <c>color .12s ease-out, filter .12s ease-out</c>; OD-0076).
/// </summary>
internal sealed class VoiceCross : Button
{
    private static readonly FuncControlTemplate<Button> PlainTemplate = new((owner, scope) => new Border { Background = Brushes.Transparent });
    private static readonly Geometry Strokes = StreamGeometry.Parse("M3.2,3.2 L8.8,8.8 M8.8,3.2 L3.2,8.8"); // viewBox 12

    private readonly LookMotion motion;
    private readonly LookNumber hover;
    private readonly LookNumber pressValue;
    private readonly ScaleTransform press = new(1, 1);
    private bool live;
    private bool pressed;

    public VoiceCross(VoiceCrossLook look)
    {
        Look = look;
        motion = new LookMotion(this);
        hover = new LookNumber(motion, 0);
        pressValue = new LookNumber(motion, 1);
        Template = PlainTemplate;
        Background = Brushes.Transparent;
        ClipToBounds = false;
        BorderThickness = default;
        Padding = default;
        Width = VoicePillGeometry.CrossSize;
        Height = VoicePillGeometry.CrossSize;
        Focusable = false;
        IsTabStop = false;
        RenderTransform = press;
        RenderTransformOrigin = RelativePoint.Center;
        AutomationProperties.SetName(this, "Cancel voice input");
        ToolTip.SetTip(this, "Cancel (Esc)");
        motion.Frame += DrawMotion;
        PointerEntered += (_, _) => hover.To(1, Animation.EaseOut(0.12));
        PointerExited += (_, _) =>
        {
            hover.To(0, Animation.EaseOut(0.12));
            SetPressed(false);
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public VoiceCrossLook Look { get; }

    /// <summary>The session is live: the cross is a tab stop (V-12.8) and can take the keyboard.</summary>
    public bool Live
    {
        get => live;
        set
        {
            live = value;
            Focusable = value;
            IsTabStop = value;
        }
    }

    /// <summary>How far the hover has come, 0 to 1 (a test reads it).</summary>
    internal double HoverAmount => hover.Value;

    /// <summary>The press scale (1, or 0.92 while the button is down: <c>.px:active</c>).</summary>
    internal double PressScale => press.ScaleX;

    internal ScaleTransform PressTransform => press;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Button consumes its pointer events. Its pressed property also covers keyboard activation and capture loss.
        if (change.Property == IsPressedProperty) SetPressed(IsPressed);
    }

    private void SetPressed(bool down)
    {
        if (pressed == down) return;
        pressed = down;
        if (Look == VoiceCrossLook.Disc) pressValue.To(down ? 0.92 : 1, LookCurve.Ez(0.38)); // `.vpill.open .px{transition:transform .38s var(--ez)}`
        DrawMotion();
    }

    private void DrawMotion()
    {
        press.ScaleX = press.ScaleY = pressValue.Value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        var bounds = new Rect(Bounds.Size);
        var round = new RoundedRect(bounds, VoicePillGeometry.CrossSize / 2);
        var amount = hover.Value;
        if (Look == VoiceCrossLook.Disc)
        {
            DrawDisc(context, tokens, bounds, round, amount);
            DrawIcon(context, bounds, Mix(tokens.PgX, tokens.PgXHover, amount), 11, 1.5);
        }
        else DrawBeside(context, tokens, bounds, round, amount);
    }

    // `.vpill .px`: radial-gradient(80% 60% at 50% 0%, pgHi, transparent 75%) over pgXBg, inset 0 0 0 .5px pgXRim, the fringe, 0 1px 2px rgba(0,0,0,.12); hover filter brightness(1.12)
    private static void DrawDisc(DrawingContext context, VoicePillTokens tokens, Rect bounds, RoundedRect round, double amount)
    {
        var shadow = new BoxShadows(new BoxShadow { OffsetY = 1, Blur = SwiftShadow.Blur(1), Color = Color.FromArgb(0x1F, 0, 0, 0) });
        context.DrawRectangle(new SolidColorBrush(tokens.PgXBg), null, round, shadow);
        using (context.PushClip(round))
        {
            using (context.PushTransform(Matrix.CreateScale(bounds.Width * 0.8, bounds.Height * 0.6) * Matrix.CreateTranslation(bounds.Width / 2, 0)))
                context.DrawRectangle(HighlightBrush(tokens.PgHi), null, new Rect(-1.25, -0.1, 2.5, 1.2));
            if (amount > 0) context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(0x1F * amount), 255, 255, 255)), null, round); // brightness(1.12): the disc lifts
        }
        // CSS lists the topmost inset first: blue, pink, then the .5 px ring. Blur .6 means sigma .3.
        context.Custom(new VoiceInsetFringe(bounds, Color.FromArgb(0x52, 96, 176, 255), -0.5,
            Color.FromArgb(0x4D, 255, 96, 150), 0.5, 0.3f));
        context.Custom(new VoiceInsetRing(bounds, 0.5, tokens.PgXRim, clipOuterDisc: true));
    }

    // `.xbtn`: hoverfill, inset 0 0 0 1px winedge, icon wink3, hover pressfill and wink
    private static void DrawBeside(DrawingContext context, VoicePillTokens tokens, Rect bounds, RoundedRect round, double amount)
    {
        context.DrawRectangle(new SolidColorBrush(Mix(tokens.HoverFill, tokens.PressFill, amount)), null, round);
        context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = tokens.WinEdge }));
        DrawIcon(context, bounds, Mix(tokens.Wink3, tokens.Wink, amount), 12, 1.5);
    }

    private static void DrawIcon(DrawingContext context, Rect bounds, Color color, double size, double stroke)
    {
        var scale = size / 12;
        var origin = new Point((bounds.Width - size) / 2, (bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), stroke, lineCap: PenLineCap.Round), Strokes);
    }

    // radial-gradient(80% 60% at 50% 0%, hi, transparent 75%), drawn in a unit space the caller scales to 80 % by 60 % of the box
    private static IBrush HighlightBrush(Color hi) => new RadialGradientBrush
    {
        Center = new RelativePoint(0, 0, RelativeUnit.Absolute),
        GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
        RadiusX = new RelativeScalar(1, RelativeUnit.Absolute),
        RadiusY = new RelativeScalar(1, RelativeUnit.Absolute),
        GradientStops = [new GradientStop(hi, 0), new GradientStop(Color.FromArgb(0, hi.R, hi.G, hi.B), 0.75)],
    };

    private static Color Mix(Color from, Color to, double amount) => SwiftColor.PremultipliedMix(from, to, amount);
}
