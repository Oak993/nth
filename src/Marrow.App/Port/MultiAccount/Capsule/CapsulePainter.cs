// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). Draws the ONE shared capsule: the plain account capsule and the twelve pill styles, from a snapshot of what it shows (multi-account.html .mcap, .mc-lt, .mc-sv, .mc-fill,
// .mc-pk, .mc-au, .mc-bd, .mc-glow). Geometry (points along the outline, the dashes, the ticks, the orbit) comes from CapsuleLayout in Core; this file only paints it.
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.App.Port.MultiAccount.Capsule;

/// <summary>Everything the painter needs for one frame.</summary>
internal sealed class CapsulePaintState
{
    public string Style { get; init; } = "plain";
    public double Width { get; set; }
    public string Mono { get; set; } = "";
    public CapsuleView? View { get; set; }
    public IReadOnlyList<StepMark> Marks { get; set; } = [];
    public string? NextMono { get; set; }
    public (byte R, byte G, byte B) Light { get; set; }
    public (byte R, byte G, byte B) Next { get; set; }
    public bool Dark { get; set; }
    public bool Handshake { get; set; }
    public bool Still { get; set; }
    public bool Hot { get; set; }
    public double Arc { get; set; }
    public double Fill { get; set; }
    public double Phase { get; set; }
    public double Pop { get; set; } = 1;
    public double Hover { get; set; }
    public bool KeyboardFocus { get; set; }
    public string? NameBelow { get; set; }
    public bool PeekShown { get; set; }
}

internal static class CapsulePainter
{
    public const double Height = 22;
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface FaceMedium = new(new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyle.Normal, FontWeight.Medium);

    private static SolidColorBrush Solid(Color color) => new(color);

    public static double TextWidth(string text, double size)
    {
        if (text.Length == 0) return 0;
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.Black).Width;
    }

    public static void Paint(DrawingContext ctx, CapsulePaintState s)
    {
        var t = CapsuleTokens.For(s.Dark);
        var w = s.Width;
        var uc = CapsuleTokens.Of(s.Light);
        var uc2 = CapsuleTokens.Of(s.Next);
        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, Height)); // takes the pointer
        var plain = s.Style == "plain";
        if (s.KeyboardFocus) ctx.DrawRectangle(null, new Pen(Solid(t.FocusRing), 2), new RoundedRect(new Rect(-1, -1, w + 2, Height + 2), 12));
        if (plain) { PaintPlain(ctx, s, t, w); return; }
        var view = s.View;
        switch (s.Style)
        {
            case "7": PaintStacked(ctx, s, t, uc, uc2); return;
            case "11": case "11o": PaintGlowRim(ctx, s, t, uc, uc2); return;
        }
        if (s.Style == "6") PaintFill(ctx, s, uc, w);
        if (s.Style == "8") PaintAurora(ctx, s, uc, w);
        if (view is not null)
        {
            switch (s.Style)
            {
                case "1": PaintLights(ctx, s, t, uc, uc2, w, 2.5, false); break;
                case "2": PaintLights(ctx, s, t, uc, uc2, w, -6, false); break;
                case "3": PaintLights(ctx, s, t, uc, uc2, w, -4, true); break;
                case "4": PaintRim(ctx, s, t, uc, uc2, w); break;
                case "5": PaintOrbit(ctx, s, t, uc, uc2, w); break;
                case "6": PaintTicks(ctx, s, t, uc, uc2, w); break;
                case "9": PaintDial(ctx, s, t, uc, uc2); break;
                case "10": PaintBreath(ctx, s, t, uc2, w); break;
            }
        }
        var shift = s.Style == "1" ? 1.5 : 0;
        PaintMark(ctx, s, t, w / 2, Height / 2 + shift, s.Mono, s.Style == "9" ? 9 : 11, t.Tx);
        if (s.Style == "2" && s.NameBelow is { Length: > 0 } name) PaintName(ctx, name, w / 2, 23, t);
    }

    // ---- text

    private static void Text(DrawingContext ctx, string text, double size, Typeface face, Color color, double cx, double cy, TextAlignment align = TextAlignment.Center, double opacity = 1)
    {
        if (text.Length == 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Solid(CapsuleTokens.Scaled(color, opacity)));
        var x = align == TextAlignment.Right ? cx - formatted.Width : align == TextAlignment.Left ? cx : cx - formatted.Width / 2;
        ctx.DrawText(formatted, new Point(x, cy - formatted.Height / 2));
    }

    private static void PaintName(DrawingContext ctx, string name, double cx, double top, CapsuleTokens t) =>
        Text(ctx, name, 8.5, FaceMedium, t.Tx2, cx, top + 5);

    private static void PaintMark(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, double cx, double cy, string mono, double size, Color color)
    {
        // a step change: every style moves in its own way (the page's stepMotion); Reduce Motion crossfades
        var pop = Math.Clamp(s.Pop, 0, 1);
        double scale = 1, opacity = 1, dx = 0;
        if (pop < 1)
        {
            if (s.Still) opacity = pop;
            else if (s.Style == "7") { scale = .84 + .16 * pop; dx = 9 * (1 - pop); opacity = .3 + .7 * pop; }
            else if (s.Style is "6" or "8" or "10" or "11" or "11o") { scale = pop < .5 ? .6 + (1.12 - .6) * (pop / .5) : 1.12 - .12 * ((pop - .5) / .5); opacity = Math.Min(1, pop * 2); }
            else scale = .86 + .14 * pop;
        }
        using (ctx.PushTransform(Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(cx + dx, cy)))
            Text(ctx, mono, size, Face, color, cx, cy, TextAlignment.Center, opacity);
    }

    // ---- shared pieces

    private static void Halo(DrawingContext ctx, Point centre, double radius, Color color, double strength)
    {
        if (strength <= 0.001) return;
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(.5, .5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("50%"),
            RadiusY = RelativeScalar.Parse("50%"),
            GradientStops = [new GradientStop(CapsuleTokens.Scaled(color, .75 * strength), 0), new GradientStop(CapsuleTokens.Scaled(color, 0), 1)],
        };
        ctx.DrawEllipse(brush, null, centre, radius, radius);
    }

    private static double Pulse(CapsulePaintState s) => s.Still ? 1 : .35 + .65 * (1 - Math.Cos(2 * Math.PI * s.Phase / 1.0)) / 2;

    private static Pen RoundPen(Color color, double thickness) => new(Solid(color), thickness, lineCap: PenLineCap.Round);

    private static StreamGeometry Polyline(IEnumerable<(double X, double Y)> points)
    {
        var geometry = new StreamGeometry();
        using var gc = geometry.Open();
        var first = true;
        foreach (var (x, y) in points)
        {
            if (first) { gc.BeginFigure(new Point(x, y), false); first = false; }
            else gc.LineTo(new Point(x, y));
        }
        if (!first) gc.EndFigure(false);
        return geometry;
    }

    private static IEnumerable<(double X, double Y)> Dash(Func<double, (double X, double Y)> at, double start, double length, double lap = 100)
    {
        var steps = Math.Max(2, (int)Math.Ceiling(length * 2));
        for (var i = 0; i <= steps; i++) yield return at((start + length * i / steps) / lap);
    }

    // ---- the plain account capsule: the hairline arc of the 5-hour use on its rim

    private static void PaintPlain(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, double w)
    {
        Func<double, (double X, double Y)> at = f => CapsuleLayout.Along(w, Height, .6, false, f);
        ctx.DrawGeometry(null, new Pen(Solid(t.Track), 1.1), Polyline(Dash(at, 0, 100)));
        if (s.Arc > 0.002) ctx.DrawGeometry(null, RoundPen(s.Hot ? t.Hot : t.Fill, 1.1), Polyline(Dash(at, 0, s.Arc * 100)));
        PaintMark(ctx, s, t, w / 2, Height / 2, s.Mono, 11, t.Tx);
    }

    // ---- 1 Merged capsule, 2 Name below, 3 Lights only: the step lights

    private static void PaintLights(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2, double w, double top, bool lines)
    {
        var marks = s.Marks;
        var n = marks.Count;
        if (n == 0) return;
        var pulse = Pulse(s);
        double size = 3.5, gap = lines ? 2 : 3;
        var each = lines ? (w - 10 - (n - 1) * gap) / n : size;
        var x0 = lines ? 5 : w / 2 - (n * size + (n - 1) * gap) / 2;
        for (var i = 0; i < n; i++)
        {
            var rect = lines ? new Rect(x0 + i * (each + gap), top, each, 1.5) : new Rect(x0 + i * (size + gap), top, size, size);
            var centre = rect.Center;
            var radius = lines ? .75 : size / 2;
            var m = marks[i];
            if (m.Stand)
            {
                Halo(ctx, centre, 7, uc2, pulse);
                ctx.DrawRectangle(Solid(CapsuleTokens.Scaled(uc2, pulse)), null, new RoundedRect(rect, radius));
                continue;
            }
            switch (m.Role)
            {
                case StepRole.Cur:
                    {
                        var grow = s.Pop < 1 && !s.Still ? (s.Pop < .45 ? .2 + 1.5 * (s.Pop / .45) : 1.7 - .7 * ((s.Pop - .45) / .55)) : 1;
                        var drawn = grow == 1 ? rect : Scale(rect, grow);
                        Halo(ctx, centre, radius + 5, uc, 1);
                        ctx.DrawRectangle(null, new Pen(Solid(CapsuleTokens.Scaled(uc, .3)), 1), new RoundedRect(drawn.Inflate(.5), radius + .5));
                        ctx.DrawRectangle(Solid(s.Pop < 1 && s.Still ? CapsuleTokens.Scaled(uc, s.Pop) : uc), null, new RoundedRect(drawn, radius));
                        break;
                    }
                case StepRole.Next:
                    ctx.DrawRectangle(Solid(CapsuleTokens.Scaled(t.Tx3, .55)), null, new RoundedRect(rect, radius));
                    break;
                default:
                    if (lines) ctx.DrawRectangle(Solid(CapsuleTokens.Scaled(t.Tx3, .25)), null, new RoundedRect(rect, radius));
                    else ctx.DrawEllipse(null, new Pen(Solid(CapsuleTokens.Scaled(t.Tx3, .8)), 1), centre, size / 2 - .5, size / 2 - .5);
                    break;
            }
        }
    }

    private static Rect Scale(Rect rect, double factor) =>
        new(rect.Center.X - rect.Width * factor / 2, rect.Center.Y - rect.Height * factor / 2, rect.Width * factor, rect.Height * factor);

    // ---- 4 Segmented rim and 9 Dial: one segment per step round the outline

    private static void PaintSegments(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2, IReadOnlyList<PathDash> dashes, Func<double, (double X, double Y)> at)
    {
        var pulse = Pulse(s);
        for (var i = 0; i < dashes.Count && i < s.Marks.Count; i++)
        {
            var m = s.Marks[i];
            var points = Polyline(Dash(at, dashes[i].Start, dashes[i].Length));
            if (m.Stand) { ctx.DrawGeometry(null, RoundPen(CapsuleTokens.Scaled(uc2, pulse), 1.4), points); continue; }
            switch (m.Role)
            {
                case StepRole.Cur:
                    ctx.DrawGeometry(null, RoundPen(CapsuleTokens.Scaled(uc, .35), 4), points);
                    ctx.DrawGeometry(null, RoundPen(uc, 1.4), points);
                    break;
                case StepRole.Next: ctx.DrawGeometry(null, RoundPen(CapsuleTokens.Scaled(t.Tx3, .7), 1.4), points); break;
                default: ctx.DrawGeometry(null, RoundPen(CapsuleTokens.Scaled(t.Tx3, .25), 1), points); break;
            }
        }
    }

    private static void PaintRim(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2, double w) =>
        PaintSegments(ctx, s, t, uc, uc2, CapsuleLayout.RimSegments(s.Marks.Count), f => CapsuleLayout.Along(w, Height, .75, true, f));

    private static void PaintDial(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2) =>
        PaintSegments(ctx, s, t, uc, uc2, CapsuleLayout.DialSegments(s.Marks.Count), f => CapsuleLayout.CircleAlong(11, 11, 9.9, f));

    // ---- 5 Orbit: tiny step dots drift round inside the rim, a lap in 28 s

    private static void PaintOrbit(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2, double w)
    {
        var starts = CapsuleLayout.Orbit(s.Marks.Count);
        var pulse = Pulse(s);
        for (var i = 0; i < starts.Count; i++)
        {
            var (x, y) = CapsuleLayout.Along(w, Height, 3.1, false, starts[i] + (s.Still ? 0 : s.Phase / CapsuleLayout.OrbitSeconds));
            var centre = new Point(x, y);
            var m = s.Marks[i];
            if (m.Stand) { Halo(ctx, centre, 5, uc2, pulse); ctx.DrawEllipse(Solid(CapsuleTokens.Scaled(uc2, pulse)), null, centre, 1.55, 1.55); continue; }
            switch (m.Role)
            {
                case StepRole.Cur:
                    Halo(ctx, centre, 5.5, uc, 1);
                    ctx.DrawEllipse(Solid(uc), null, centre, 1.9, 1.9);
                    break;
                case StepRole.Next: ctx.DrawEllipse(Solid(CapsuleTokens.Scaled(t.Tx3, .7)), null, centre, 1.55, 1.55); break;
                default: ctx.DrawEllipse(null, new Pen(Solid(CapsuleTokens.Scaled(t.Tx3, .6)), .8), centre, 1.55, 1.55); break;
            }
        }
    }

    // ---- 6 Liquid fill: the glass fills like a gauge, step ticks etched along the rim

    private static void PaintFill(DrawingContext ctx, CapsulePaintState s, Color uc, double w)
    {
        var level = Math.Clamp(s.Fill, 0, 1) * Height;
        using var clip = ctx.PushClip(new RoundedRect(new Rect(0, 0, w, Height), 11));
        var body = new Rect(0, Height - level, w, level);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(CapsuleTokens.Scaled(uc, .44), 0), new GradientStop(CapsuleTokens.Scaled(uc, .20), 1)],
        };
        ctx.DrawRectangle(brush, null, body);
        // the meniscus: a row of small scallops sliding sideways, 8 px in 2.8 s; still under Reduce Motion
        var shift = s.Still ? 0 : s.Phase / 2.8 % 1 * 8;
        var wave = Solid(CapsuleTokens.Scaled(uc, .44));
        for (var x = -8 + shift; x < w + 8; x += 8) ctx.DrawEllipse(wave, null, new Point(x + 4, body.Top), 4, 2);
        if (s.Handshake) ctx.DrawRectangle(Solid(Color.FromArgb(22, 255, 255, 255)), null, body);
    }

    private static void PaintTicks(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2, double w)
    {
        var ticks = CapsuleLayout.Ticks(s.Marks.Count, w);
        var pulse = Pulse(s);
        for (var i = 0; i < ticks.Count; i++)
        {
            var from = new Point(ticks[i], 1.2);
            var to = new Point(ticks[i], 4);
            var m = s.Marks[i];
            if (m.Stand) { ctx.DrawLine(RoundPen(CapsuleTokens.Scaled(uc2, pulse), 1), from, to); continue; }
            switch (m.Role)
            {
                case StepRole.Cur:
                    ctx.DrawLine(RoundPen(CapsuleTokens.Scaled(uc, .35), 4), from, to);
                    ctx.DrawLine(RoundPen(uc, 1.4), from, to);
                    break;
                case StepRole.Next: ctx.DrawLine(RoundPen(CapsuleTokens.Scaled(t.Tx3, .6), 1), from, to); break;
                default: ctx.DrawLine(RoundPen(CapsuleTokens.Scaled(t.Tx3, .25), 1), from, to); break;
            }
        }
    }

    // ---- 7 Stacked marks: the current card in front, the next one peeking out behind it

    private static void PaintStacked(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2)
    {
        var w = s.Width;
        var peekOn = s.PeekShown && s.NextMono is not null;
        if (peekOn)
        {
            var shift = s.Handshake ? 2 : 0;
            var rect = new Rect(w - 26 + shift, 2, 26, 18);
            if (s.Handshake) Halo(ctx, rect.Center, 16, uc2, .8);
            ctx.DrawRectangle(Solid(t.Surface), null, new RoundedRect(rect, 9));
            ctx.DrawRectangle(null, new Pen(Solid(s.Handshake ? CapsuleTokens.Scaled(uc2, .7) : t.Rim), s.Handshake ? 1 : .5), new RoundedRect(rect.Deflate(.25), 9));
            Text(ctx, s.NextMono!, 7.5, Face, s.Handshake ? t.Tx : t.Tx2, rect.Right - 3, rect.Center.Y, TextAlignment.Right);
        }
        var card = new Rect(0, 0, 34, Height);
        ctx.DrawRectangle(Solid(CapsuleTokens.Scaled(t.Shadow, .6)), null, new RoundedRect(new Rect(1.5, 1, 34, Height), 11));
        ctx.DrawRectangle(Solid(t.Card), null, new RoundedRect(card, 11));
        ctx.DrawRectangle(null, new Pen(Solid(t.Rim), .5), new RoundedRect(card.Deflate(.25), 11));
        ctx.DrawLine(new Pen(Solid(t.Highlight), 1), new Point(8, .5), new Point(26, .5));
        PaintMark(ctx, s, t, 17, Height / 2, s.Mono, 11, t.Tx);
    }

    // ---- 8 Aurora rim: a faint aurora runs slowly round the rim, led by the usage light (a turn in 14 s)

    private static void PaintAurora(DrawingContext ctx, CapsulePaintState s, Color uc, double w)
    {
        var angle = s.Still ? 0 : s.Phase / 14 % 1 * 360;
        var cyan = UsageLight.Mix((s.Light.R, s.Light.G, s.Light.B), (0x7F, 0xE3, 0xFF), .5);
        var violet = UsageLight.Mix((s.Light.R, s.Light.G, s.Light.B), (0xB7, 0x95, 0xFF), .55);
        var clear = Color.FromArgb(0, uc.R, uc.G, uc.B);
        var brush = new ConicGradientBrush
        {
            Center = new RelativePoint(.5, .5, RelativeUnit.Relative),
            Angle = angle,
            GradientStops = [new GradientStop(uc, 0), new GradientStop(CapsuleTokens.Of(cyan), .19), new GradientStop(clear, .38), new GradientStop(clear, .52), new GradientStop(CapsuleTokens.Of(violet), .76), new GradientStop(uc, 1)],
        };
        var opacity = s.Handshake ? 1 : .85;
        if (s.Handshake) ctx.DrawRectangle(null, new Pen(Solid(CapsuleTokens.Scaled(CapsuleTokens.Of(s.Next), .25)), 4), new RoundedRect(new Rect(.625, .625, w - 1.25, Height - 1.25), 10.4));
        using (ctx.PushOpacity(opacity))
            ctx.DrawRectangle(null, new Pen(brush, 1.25), new RoundedRect(new Rect(.625, .625, w - 1.25, Height - 1.25), 10.4));
    }

    // ---- 10 Minimal: one dot that breathes only while a Handshake runs

    private static void PaintBreath(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc2, double w)
    {
        if (!s.Handshake) return;
        var centre = new Point(w - 2, 2);
        var breath = s.Still ? .5 : (1 - Math.Cos(2 * Math.PI * s.Phase / 1.6)) / 2;
        Halo(ctx, centre, 5 + 4 * breath, uc2, .8);
        ctx.DrawEllipse(Solid(t.PlateRing), null, centre, 5, 5);
        ctx.DrawEllipse(Solid(uc2), null, centre, 3, 3);
    }

    // ---- 11 Glow rim and 11o: a gradient rim around dark glass (the blurred copy behind it is the glow layer)

    private static void PaintGlowRim(DrawingContext ctx, CapsulePaintState s, CapsuleTokens t, Color uc, Color uc2)
    {
        var w = s.Width;
        var (g1, g2) = GlowColors(s);
        var rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(g1, 0), new GradientStop(g2, 1)],
        };
        ctx.DrawRectangle(rim, null, new RoundedRect(new Rect(0, 0, w, Height), 11));
        var inner = new Rect(1.5, 1.5, w - 3, Height - 3);
        ctx.DrawRectangle(Solid(t.GlowInner), null, new RoundedRect(inner, 9.5));
        ctx.DrawLine(new Pen(Solid(t.Highlight), 1), new Point(8, 2), new Point(w - 8, 2));
        PaintMark(ctx, s, t, w / 2, Height / 2, s.Mono, 11, t.Tx);
    }

    /// <summary>The two colours of the glow rim: 11 is the usage light into the next account's light mixed 64 percent with white; 11o is the owner's blue-to-pink.</summary>
    public static (Color First, Color Second) GlowColors(CapsulePaintState s) =>
        s.Style == "11o"
            ? (Color.Parse("#03a9f4"), Color.Parse("#f441a5"))
            : (CapsuleTokens.Of(s.Light), CapsuleTokens.Of(UsageLight.Mix((s.Next.R, s.Next.G, s.Next.B), (255, 255, 255), .64)));
}
