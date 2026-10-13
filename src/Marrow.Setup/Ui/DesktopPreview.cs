using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Setup.Motion;
using Marrow.Setup.Support;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using SkiaSharp;

namespace Marrow.Setup.Ui;

/// <summary>The preview's icon: dropping in (on), fading in where it rests, set at once, or fading away (off).</summary>
public enum DeskState
{
    Land,
    Show,
    On,
    Off,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, "The desktop component"; the canvas's <c>.desk</c>; unit U2, ED-NEW-inst-u2-1).
/// One component, two sizes: Options 144 × 90 DIP, Ready 176 × 110 (16:10, radius 6 % of the width; inside, 1 em is a tenth
/// of the width). A desktop with the Recycle Bin (a glass bin with a lid and three ribs, from tokens) and its label in the
/// first slot, nth's slot under it, a thin taskbar with its dots, a 1 DIP edge, a top highlight and a lift shadow. It
/// draws the user's own wallpaper when it has been read, else the theme gradient. The nth icon is driven by interruptible
/// springs (position, scale, opacity, label opacity, each keeping its value and speed; a new state only changes targets):
/// on, it drops from 0.9 em higher at 74 % (response 0.52 s, damping 0.74) with a 0.24 s fade, its contact shadow growing
/// on the ground as it comes down, the label 0.2 s later; off, it fades and shrinks to 86 % (critically damped, 0.26 s).
/// The large one plays one soft glow as it lands (1.2 s after 0.12 s, scale 0.5 → 1.35, opacity 0 → 1 → 0) and fades in as
/// a whole (0.4 s, rising 6 DIP over 0.5 s). The medium one lifts 2 DIP with a deeper shadow on hover, presses to 98.5 %, and
/// a tap is reported (<see cref="Activated"/>). Calm: only fades (0.2 s), no glow, no lift. A contrast theme: black with a
/// window-text edge, the icons outlined, no shadow or glow. Decorative to Narrator, never a tab stop.
/// </summary>
public sealed class DesktopPreview : Border, IMotion
{
    public const double MediumWidth = 144, LargeWidth = 176;
    public const double DropFrom = -.9, DropScale = .74, OffScale = .86, LabelDelay = .2, Rest = .0005;
    public const double GlowDelay = .12, GlowLength = 1.2, AppearFade = .4, AppearRise = 6, AppearMove = .5;

    private static readonly CubicBezier GlowCurve = new(.2, .7, .3, 1);
    private static readonly CubicBezier LiftCurve = new(.2, .8, .2, 1);
    private readonly Func<bool> calm;
    private readonly DeskLayout surface;
    private readonly DeskSurface wall;
    private readonly DeskIcon icon;
    private readonly ContactShadow contact;
    private readonly DeskEdge edge;
    private readonly TextBlock binLabel;
    private readonly TextBlock nthLabel;
    private readonly Spring y = new(.26, 1, 0, Rest), scale = new(.26, 1, OffScale, Rest), opacity = new(.2, 1, 0, Rest), label = new(.2, 1, 0, Rest);
    private readonly Tween hover = new(0), press = new(1), appear = new(1), rise = new(0), wallFade = new(0);
    private readonly TranslateTransform lift = new();
    private readonly ScaleTransform pressScale = new(1, 1);
    private ScreenTokens tokens = ScreenTokens.Dark;
    private Animator? animator;
    private double labelAt = -1;
    private double glowAt = -1;
    private WallpaperScene? scene;
    private WallpaperPicture? picture;
    private bool pressed;

    public DesktopPreview(bool large, Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        Large = large;
        DeskWidth = large ? LargeWidth : MediumWidth;
        Width = DeskWidth;
        Height = DeskWidth * .625;
        HorizontalAlignment = HorizontalAlignment.Center;
        CornerRadius = new CornerRadius(DeskWidth * .06);
        ClipToBounds = false;
        Focusable = false;
        KeyboardNavigation.SetIsTabStop(this, false);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new TransformGroup { Children = { pressScale, lift } };
        var em = Em;
        surface = new DeskLayout(this) { ClipToBounds = true };
        wall = new DeskSurface(this);
        icon = new DeskIcon(this) { Width = 1.35 * em, Height = 1.35 * em };
        contact = new ContactShadow(this) { Width = 1.053 * em, Height = .351 * em };
        edge = new DeskEdge(this);
        binLabel = Label(SetupStrings.DesktopRecycleBin);
        nthLabel = Label(SetupStrings.DesktopNth);
        nthLabel.Opacity = 0;
        surface.Children.Add(wall);
        surface.Children.Add(binLabel);
        surface.Children.Add(contact);
        surface.Children.Add(icon);
        surface.Children.Add(nthLabel);
        surface.Children.Add(edge);
        surface.Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), CornerRadius.TopLeft, CornerRadius.TopLeft);
        Child = surface;
        if (!large)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            // the whole desktop takes the pointer (its drawing is custom, so it needs a background to be hit)
            Background = Brushes.Transparent;
        }
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            Restyle();
        });
        Paint();
    }

    public bool Large { get; }

    public double DeskWidth { get; }

    /// <summary>A tenth of the width: the component's unit.</summary>
    public double Em => DeskWidth / 10;

    /// <summary>The state asked for last.</summary>
    public DeskState State { get; private set; } = DeskState.Off;

    /// <summary>The icon's pose: offset in em (negative is higher), scale, opacity, the label's opacity.</summary>
    public (double Y, double Scale, double Opacity, double Label) IconPose => (y.Value, scale.Value, Math.Clamp(opacity.Value, 0, 1), Math.Clamp(label.Value, 0, 1));

    /// <summary>The landing glow's opacity and scale now (0 when none).</summary>
    public (double Opacity, double Scale) Glow => GlowAt(glowAt);

    public bool IsGlowing => glowAt >= 0;

    /// <summary>The whole preview's own fade and rise (Ready's arrival).</summary>
    public (double Opacity, double Rise) Appearance => (appear.Value, rise.Value);

    /// <summary>The hover lift (0 to 1) and the press scale.</summary>
    public (double Hover, double Press) Lift => (hover.Value, press.Value);

    /// <summary>The wallpaper the preview draws now (null: the theme gradient).</summary>
    public WallpaperPicture? Picture => picture;

    /// <summary>True when the labels are drawn in ink with a pale halo (a light wallpaper, or the light theme).</summary>
    public bool InkLabels => tokens.Desk.Outlined ? false : picture?.InkLabels ?? tokens.Desk.LabelHalo;

    public TextBlock BinLabel => binLabel;

    public TextBlock NthLabel => nthLabel;

    /// <summary>A tap on the medium preview (Options flips its switch).</summary>
    public event Action? Activated;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>Sets the icon's state; with <paramref name="instant"/> it is placed at once (a screen being set up).</summary>
    public void SetState(DeskState state, bool instant = false)
    {
        State = state;
        labelAt = -1;
        glowAt = -1;
        var on = state != DeskState.Off;
        if (instant || animator is null)
        {
            foreach (var (spring, value) in new[] { (y, 0.0), (scale, on ? 1 : OffScale), (opacity, on ? 1 : 0), (label, on ? 1 : 0) }) spring.Snap(value);
            Paint();
            return;
        }
        if (on)
        {
            var fromRest = opacity.Value < .04;
            var still = calm() || state == DeskState.Show;
            if (still)
            {
                if (fromRest)
                {
                    y.Snap(0);
                    scale.Snap(1);
                }
                Use(y, .2, 1);
                Use(scale, .2, 1);
                Use(opacity, .2, 1);
                Use(label, .2, 1);
            }
            else
            {
                if (fromRest)
                {
                    // invisible (under 4 %): it may be placed at its start pose before it drops in
                    y.Snap(DropFrom);
                    scale.Snap(DropScale);
                }
                Use(y, .52, .74);
                Use(scale, .52, .74);
                Use(opacity, .24, 1);
                Use(label, .22, 1);
                if (Large && !tokens.Contrast) glowAt = 0;
            }
            y.Retarget(0);
            scale.Retarget(1);
            opacity.Retarget(1);
            labelAt = still ? 0 : LabelDelay;
            if (labelAt == 0)
            {
                label.Retarget(1);
                labelAt = -1;
            }
        }
        else
        {
            var c = calm();
            Use(y, c ? .2 : .26, 1);
            Use(scale, c ? .2 : .26, 1);
            Use(opacity, .2, 1);
            Use(label, .2, 1);
            y.Retarget(0);
            scale.Retarget(c ? scale.Value : OffScale);
            opacity.Retarget(0);
            label.Retarget(0);
        }
        animator.Start(this);
    }

    /// <summary>Ready's arrival: the preview fades in (0.4 s) rising 6 DIP (0.5 s); Calm only fades (0.2 s). Hidden: at once.</summary>
    public void Appear(bool shown, bool instant = false)
    {
        if (!shown || instant || animator is null)
        {
            appear.Snap(shown ? 1 : 0);
            rise.Snap(0);
            Paint();
            return;
        }
        var c = calm();
        if (appear.Value < .01 && !c) rise.Snap(AppearRise);
        appear.Retarget(1, c ? .2 : AppearFade, CubicBezier.Ease);
        rise.Retarget(0, c ? 0 : AppearMove, LiftCurve);
        animator.Start(this);
    }

    /// <summary>The wallpaper read in the background (null: the theme gradient). It cross-fades in.</summary>
    public void SetWallpaper(WallpaperScene? value)
    {
        scene = value;
        picture = value is null ? null : WallpaperPreview.Compose(value, (int)Math.Round(DeskWidth * 2), (int)Math.Round(DeskWidth * 2 * .625));
        wall.SetPicture(picture);
        if (picture is null) wallFade.Snap(0);
        else if (animator is null) wallFade.Snap(1);
        else
        {
            wallFade.Snap(0);
            wallFade.Retarget(1, calm() ? .2 : .3, CubicBezier.Ease);
            animator.Start(this);
        }
        Restyle();
    }

    internal double WallOpacity => wallFade.Value;

    internal ScreenTokens Tokens => tokens;

    internal double GlowProgress => glowAt;

    bool IMotion.Advance(double seconds)
    {
        if (labelAt > 0)
        {
            labelAt -= seconds;
            if (labelAt <= 0)
            {
                labelAt = -1;
                label.Retarget(1);
            }
        }
        var moving = false;
        foreach (var spring in new[] { y, scale, opacity, label })
            if (spring.Advance(seconds)) moving = true;
        if (glowAt >= 0)
        {
            glowAt += seconds;
            if (glowAt >= GlowDelay + GlowLength) glowAt = -1;
            else moving = true;
        }
        moving |= hover.Advance(seconds) | press.Advance(seconds) | appear.Advance(seconds) | rise.Advance(seconds) | wallFade.Advance(seconds);
        Paint();
        return moving || labelAt > 0;
    }

    /// <summary>The glow at <paramref name="t"/> seconds into the landing (−1: none).</summary>
    public static (double Opacity, double Scale) GlowAt(double t)
    {
        if (t < GlowDelay) return (0, .5);
        var p = Math.Clamp((t - GlowDelay) / GlowLength, 0, 1);
        var o = p <= .28 ? GlowCurve.Evaluate(p / .28) : 1 - GlowCurve.Evaluate((p - .28) / .72);
        return (Math.Clamp(o, 0, 1), .5 + .85 * GlowCurve.Evaluate(p));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (Large) return;
        hover.Retarget(Lifts ? 1 : 0, .28, LiftCurve);
        animator?.Start(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (Large) return;
        hover.Retarget(0, .28, LiftCurve);
        press.Retarget(1, .28, LiftCurve);
        pressed = false;
        animator?.Start(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Large || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        if (Lifts) press.Retarget(.985, .12, LiftCurve);
        animator?.Start(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Large || !pressed) return;
        pressed = false;
        press.Retarget(1, .28, LiftCurve);
        animator?.Start(this);
        var point = e.GetPosition(this);
        if (point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height) Activated?.Invoke();
        e.Handled = true;
    }

    /// <summary>Taps the preview (as a click on it does).</summary>
    public void Tap() => Activated?.Invoke();

    private bool Lifts => !calm() && !tokens.Contrast;

    private static void Use(Spring spring, double response, double damping)
    {
        spring.Response = response;
        spring.Damping = damping;
    }

    private TextBlock Label(string words)
    {
        var block = new TextBlock
        {
            Text = words,
            FontFamily = new FontFamily(SetupTheme.AppFamily),
            FontWeight = FontWeight.Medium,
            FontSize = .54 * Em,
            LineHeight = .54 * Em * 1.15,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            Width = 3.375 * Em,
            IsHitTestVisible = false,
        };
        return block;
    }

    private void Restyle()
    {
        var desk = tokens.Desk;
        var ink = desk.Outlined ? desk.Label : picture is { } p ? (p.InkLabels ? SetupPalette.Hex(0x141c2c) : Colors.White) : desk.Label;
        var halo = !desk.Outlined && (picture?.InkLabels ?? desk.LabelHalo);
        foreach (var block in new[] { binLabel, nthLabel })
        {
            block.Foreground = new ImmutableSolidColorBrush(ink);
            block.Effect = desk.Outlined ? null : halo
                ? new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = .3 * Em, Color = Colors.White, Opacity = .95 }
                : new DropShadowEffect { OffsetX = 0, OffsetY = .06 * Em, BlurRadius = .16 * Em * 2, Color = Colors.Black, Opacity = .85 };
        }
        BoxShadow = Shadows();
        wall.InvalidateVisual();
        icon.InvalidateVisual();
        contact.InvalidateVisual();
        edge.InvalidateVisual();
    }

    private BoxShadows Shadows()
    {
        var rest = tokens.Desk.Lift;
        var high = tokens.Desk.LiftHover;
        if (rest.Length == 0) return default;
        var h = hover.Value;
        if (h <= 0 || high.Length != rest.Length) return ShadowLayer.Many(rest);
        var mixed = new ShadowLayer[rest.Length];
        for (var i = 0; i < rest.Length; i++)
        {
            var a = rest[i];
            var b = high[i];
            mixed[i] = new ShadowLayer(a.X + (b.X - a.X) * h, a.Y + (b.Y - a.Y) * h, a.Blur + (b.Blur - a.Blur) * h, a.Spread + (b.Spread - a.Spread) * h, SetupPalette.Lerp(a.Color, b.Color, h), a.Inset);
        }
        return ShadowLayer.Many(mixed);
    }

    private void Paint()
    {
        var em = Em;
        var (iy, s, o, l) = IconPose;
        var u = Math.Clamp(-iy / .9, 0, 1);
        icon.RenderTransformOrigin = RelativePoint.Center;
        icon.RenderTransform = new MatrixTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(0, Math.Round(iy * em, 3)));
        icon.Opacity = Math.Round(o, 3);
        contact.RenderTransformOrigin = RelativePoint.Center;
        contact.RenderTransform = new ScaleTransform(1 - .6 * u, 1 - .6 * u);
        contact.Opacity = Math.Round(o * (1 - .7 * u), 3);
        nthLabel.Opacity = Math.Round(l, 3);
        icon.InvalidateVisual();
        lift.Y = Math.Round(-2 * hover.Value + rise.Value, 3);
        pressScale.ScaleX = pressScale.ScaleY = press.Value;
        Opacity = Math.Round(appear.Value, 3);
        if (hover.Value > 0 || !hover.IsAtRest) BoxShadow = Shadows();
        wall.InvalidateVisual();
    }

    /// <summary>The component's slots on its em grid: the bin's label, nth's icon, its contact shadow and its label.</summary>
    private sealed class DeskLayout(DesktopPreview owner) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(owner.Width, owner.Height));
            return new Size(owner.Width, owner.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var em = owner.Em;
            var slotLeft = .15 * em;
            var slotWidth = 3.375 * em;
            var iconLeft = slotLeft + (slotWidth - 1.35 * em) / 2;
            owner.wall.Arrange(new Rect(0, 0, owner.Width, owner.Height));
            owner.edge.Arrange(new Rect(0, 0, owner.Width, owner.Height));
            owner.binLabel.Arrange(new Rect(slotLeft, (.4 + 1.35 + .2) * em, slotWidth, owner.binLabel.DesiredSize.Height));
            owner.icon.Arrange(new Rect(iconLeft, 2.8 * em, 1.35 * em, 1.35 * em));
            owner.contact.Arrange(new Rect(slotLeft + slotWidth / 2 - .5265 * em, (2.8 + 1.35 * .84) * em, 1.053 * em, .351 * em));
            owner.nthLabel.Arrange(new Rect(slotLeft, (2.8 + 1.35 + .2) * em, slotWidth, owner.nthLabel.DesiredSize.Height));
            return finalSize;
        }
    }

    /// <summary>The desktop: the wallpaper (or the theme gradient), the Recycle Bin, the taskbar and its dots.</summary>
    private sealed class DeskSurface(DesktopPreview owner) : Control
    {
        private WriteableBitmap? bitmap;

        public void SetPicture(WallpaperPicture? picture)
        {
            bitmap?.Dispose();
            bitmap = null;
            if (picture is null) return;
            bitmap = new WriteableBitmap(new PixelSize(picture.Width, picture.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var frame = bitmap.Lock();
            for (var row = 0; row < picture.Height; row++)
                System.Runtime.InteropServices.Marshal.Copy(picture.Bgra, row * picture.Width * 4, frame.Address + row * frame.RowBytes, picture.Width * 4);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var t = owner.Tokens;
            var bounds = new Rect(0, 0, owner.Width, owner.Height);
            context.Custom(new WallOperation(bounds, t, owner.WallOpacity));
            if (bitmap is not null && owner.WallOpacity > 0 && !t.Desk.Outlined)
                using (context.PushOpacity(owner.WallOpacity))
                    context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), bounds);
            context.Custom(new BinAndBarOperation(bounds, t, owner.Em));
        }
    }

    /// <summary>The theme gradient: three soft glows over the wall colour (the canvas's three radial gradients).</summary>
    private sealed class WallOperation(Rect bounds, ScreenTokens tokens, double wallpaper) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var o = Math.Clamp(lease.CurrentOpacity, 0, 1);
            var desk = tokens.Desk;
            var w = (float)bounds.Width;
            var h = (float)bounds.Height;
            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = StepSlot.Sk(desk.Wall, o);
            canvas.DrawRect(0, 0, w, h, paint);
            if (desk.Outlined || wallpaper >= 1) return;
            Glow(canvas, paint, w, h, .75f, .95f, .12f, .08f, desk.GlowA, o);
            Glow(canvas, paint, w, h, .60f, .85f, .92f, .28f, desk.GlowB, o);
            Glow(canvas, paint, w, h, .75f, .75f, .60f, 1.12f, desk.GlowC, o);
        }

        // radial-gradient(rx% ry% at cx% cy%, colour, transparent 70%)
        private static void Glow(SKCanvas canvas, SKPaint paint, float w, float h, float rx, float ry, float cx, float cy, Color color, double o)
        {
            var saved = canvas.Save();
            canvas.Translate(cx * w, cy * h);
            canvas.Scale(rx * w, ry * h);
            using var shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, [StepSlot.Sk(color, o), StepSlot.Sk(Color.FromArgb(0, color.R, color.G, color.B), 1)], [0, .7f], SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawRect(-cx * w / (rx * w), -cy * h / (ry * h), w / (rx * w), h / (ry * h), paint);
            paint.Shader = null;
            canvas.RestoreToCount(saved);
        }
    }

    /// <summary>The Recycle Bin glyph in its slot and the taskbar with its three dots.</summary>
    private sealed class BinAndBarOperation(Rect bounds, ScreenTokens tokens, double em) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var o = Math.Clamp(lease.CurrentOpacity, 0, 1);
            var desk = tokens.Desk;
            var e = (float)em;
            using var paint = new SKPaint { IsAntialias = true };
            // the bin: a 32 unit glyph drawn 1.35 em wide, centred in the first slot
            var size = 1.35f * e;
            var left = .15f * e + (3.375f * e - size) / 2;
            var saved = canvas.Save();
            canvas.Translate(left, .4f * e);
            canvas.Scale(size / 32, size / 32);
            using (var body = SKPath.ParseSvgPathData("M8 10.4h16l-1.55 16a2.5 2.5 0 0 1-2.5 2.25h-7.9a2.5 2.5 0 0 1-2.5-2.25z"))
            {
                Fill(canvas, paint, body, desk.BinGlass, o);
                Stroke(canvas, paint, body, desk.BinRim, 1.1f, o, SKStrokeCap.Butt);
            }
            using (var lid = new SKPath())
            {
                lid.AddRoundRect(new SKRect(6.2f, 6.9f, 25.8f, 10.4f), 1.3f, 1.3f);
                Fill(canvas, paint, lid, desk.BinGlass, o);
                Stroke(canvas, paint, lid, desk.BinRim, 1.1f, o, SKStrokeCap.Butt);
            }
            using (var handle = SKPath.ParseSvgPathData("M13 5.6h6")) Stroke(canvas, paint, handle, desk.BinRim, 1.4f, o, SKStrokeCap.Round);
            using (var ribs = SKPath.ParseSvgPathData("M12.9 14v10.6M16 14v10.6M19.1 14v10.6")) Stroke(canvas, paint, ribs, desk.BinLine, 1.4f, o, SKStrokeCap.Round);
            canvas.RestoreToCount(saved);
            // the taskbar: .9 em at the bottom, a 1 DIP top line, three dots centred .29 em down
            var w = (float)bounds.Width;
            var h = (float)bounds.Height;
            var top = h - .9f * e;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = StepSlot.Sk(desk.Bar, o);
            canvas.DrawRect(0, top, w, .9f * e, paint);
            paint.Color = StepSlot.Sk(desk.BarEdge, o);
            canvas.DrawRect(0, top, w, 1, paint);
            for (var i = 0; i < 3; i++)
            {
                var cx = w / 2 - 1.05f * e + (.35f + .7f * i) * e;
                var cy = top + .29f * e + .16f * e;
                using var dot = SKShader.CreateRadialGradient(new SKPoint(cx, cy), .385f * e, [StepSlot.Sk(desk.Dots, o), StepSlot.Sk(desk.Dots, o), StepSlot.Sk(Color.FromArgb(0, desk.Dots.R, desk.Dots.G, desk.Dots.B), 1)], [0, .42f, .5f], SKShaderTileMode.Clamp);
                paint.Shader = dot;
                canvas.DrawRect(cx - .35f * e, top + .29f * e, .7f * e, .32f * e, paint);
                paint.Shader = null;
            }
        }

        private static void Fill(SKCanvas canvas, SKPaint paint, SKPath path, Color color, double o)
        {
            if (color.A == 0) return;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = StepSlot.Sk(color, o);
            canvas.DrawPath(path, paint);
        }

        private static void Stroke(SKCanvas canvas, SKPaint paint, SKPath path, Color color, float width, double o, SKStrokeCap cap)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = width;
            paint.StrokeCap = cap;
            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.Color = StepSlot.Sk(color, o);
            canvas.DrawPath(path, paint);
        }
    }

    /// <summary>The nth icon (or its outline in a contrast theme) with the landing glow behind it.</summary>
    private sealed class DeskIcon : Control
    {
        private readonly DesktopPreview owner;

        public DeskIcon(DesktopPreview owner)
        {
            this.owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = false;
        }

        public override void Render(DrawingContext context)
        {
            var side = Bounds.Width;
            var glow = owner.Glow;
            context.Custom(new IconOperation(new Rect(-side * 1.5, -side * 1.5, side * 4, side * 4), side, owner.Tokens, glow.Opacity, glow.Scale));
        }
    }

    private sealed class IconOperation(Rect bounds, double side, ScreenTokens tokens, double glowOpacity, double glowScale) : ICustomDrawOperation
    {
        private static readonly SKColor G800 = new(0x14, 0x16, 0x1c), G600 = new(0x3b, 0x41, 0x4f), S400 = new(0x9a, 0xa3, 0xb5), S300 = new(0xb9, 0xc0, 0xcd);
        private static readonly SKColor S200 = new(0xd6, 0xda, 0xe2), S100 = new(0xe8, 0xeb, 0xf0);

        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var o = Math.Clamp(lease.CurrentOpacity, 0, 1);
            if (o <= 0) return;
            var s = (float)side;
            using var paint = new SKPaint { IsAntialias = true };
            var desk = tokens.Desk;
            // the halo: 250 % of the icon, centred, radial closest-side halo → halo edge 55 % → clear
            if (glowOpacity > .002 && !desk.Outlined)
            {
                var r = 1.25f * s * (float)glowScale;
                using var halo = SKShader.CreateRadialGradient(new SKPoint(s / 2, s / 2), r, [StepSlot.Sk(desk.Halo, o * glowOpacity), StepSlot.Sk(desk.HaloEdge, o * glowOpacity), StepSlot.Sk(Color.FromArgb(0, desk.HaloEdge.R, desk.HaloEdge.G, desk.HaloEdge.B), 1)], [0, .55f, 1], SKShaderTileMode.Clamp);
                paint.Shader = halo;
                canvas.DrawCircle(s / 2, s / 2, r, paint);
                paint.Shader = null;
            }
            var saved = canvas.Save();
            canvas.Scale(s / 256, s / 256);
            var alpha = (byte)Math.Round(255 * o);
            if (desk.Outlined)
            {
                var ink = StepSlot.Sk(desk.Label, o);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = ink;
                paint.StrokeWidth = 12;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(14, 14, 242, 242), 50, 50), paint);
                paint.StrokeWidth = 24;
                using (var n = SKPath.ParseSvgPathData("M70 196V126a44 44 0 0 1 88 0v70")) canvas.DrawPath(n, paint);
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(190, 72, 17, paint);
                canvas.RestoreToCount(saved);
                return;
            }
            using (var tile = SKShader.CreateLinearGradient(new SKPoint(8, 8), new SKPoint(248, 248), [G800.WithAlpha(alpha), G600.WithAlpha(alpha), S400.WithAlpha(alpha)], [0, .55f, 1], SKShaderTileMode.Clamp))
            {
                paint.Shader = tile;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(8, 8, 248, 248), 52, 52), paint);
                paint.Shader = null;
            }
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;
            paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * .38 * o));
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(9, 9, 247, 247), 51, 51), paint);
            using (var glyph = SKShader.CreateLinearGradient(new SKPoint(0, 78), new SKPoint(0, 200), [SKColors.White.WithAlpha(alpha), S100.WithAlpha(alpha), S300.WithAlpha(alpha)], [0, .55f, 1], SKShaderTileMode.Clamp))
            using (var n = SKPath.ParseSvgPathData("M66 200V126a48 48 0 0 1 96 0v74"))
            {
                paint.Shader = glyph;
                paint.StrokeWidth = 36;
                canvas.DrawPath(n, paint);
                paint.Shader = null;
            }
            paint.Style = SKPaintStyle.Fill;
            using (var bead = SKShader.CreateRadialGradient(new SKPoint(174 + .36f * 40, 48 + .3f * 40), .8f * 40, [SKColors.White.WithAlpha(alpha), S200.WithAlpha(alpha), S400.WithAlpha(alpha)], [0, .5f, 1], SKShaderTileMode.Clamp))
            {
                paint.Shader = bead;
                canvas.DrawCircle(194, 68, 20, paint);
                paint.Shader = null;
            }
            canvas.RestoreToCount(saved);
        }
    }

    /// <summary>The icon's contact shadow on the ground: radial closest-side, shadow → clear.</summary>
    private sealed class ContactShadow(DesktopPreview owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var desk = owner.Tokens.Desk;
            if (desk.Outlined || desk.ContactShadow.A == 0) return;
            var brush = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(desk.ContactShadow, 0), new GradientStop(Color.FromArgb(0, desk.ContactShadow.R, desk.ContactShadow.G, desk.ContactShadow.B), 1) },
            };
            context.DrawEllipse(brush, null, new Rect(Bounds.Size));
        }
    }

    /// <summary>The 1 DIP edge and the top highlight, above everything (the canvas's <c>.desk::after</c>).</summary>
    private sealed class DeskEdge(DesktopPreview owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var desk = owner.Tokens.Desk;
            var r = owner.CornerRadius.TopLeft;
            var box = new Rect(0, 0, owner.Width, owner.Height);
            context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(desk.Edge), 1), new RoundedRect(box.Deflate(.5), r - .5));
            if (desk.Outlined) return;
            using (context.PushClip(new RoundedRect(box, r)))
                context.DrawRectangle(new ImmutableSolidColorBrush(desk.TopLight), null, new Rect(r * .5, 0, box.Width - r, 1));
        }
    }
}
