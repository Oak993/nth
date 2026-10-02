using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

/// <summary>One drawing step the locked canvas's own strike() made (tests/fixtures/strike/canvas-log.json).</summary>
public sealed record CanvasStrikeOp(string Op, double Width, string? Style, double[] Points, double X, double Y, double Rx, double Ry, double Alpha, string? Color);

/// <summary>Everything the canvas drew for one tube at one tile time.</summary>
public sealed record CanvasStrikeFrame(int Frame, double T, IReadOnlyList<CanvasStrikeOp> Ops);

/// <summary>A tube of the canvas log: its geometry, seed and frames.</summary>
public sealed record CanvasStrikeTube(string Name, string Brand, int Seed, double Off, double W, double H, double Knob, bool Top, double Start,
    double Ex, double Kr, double K, IReadOnlyList<CanvasStrikeFrame> Frames)
{
    public StrikeGeometry Geometry => new(W, H, Ex, Kr);

    public StrikeTint Tint => Brand switch { "deepseek" => StrikeTint.DeepSeek, "gemini" => StrikeTint.Gemini, _ => StrikeTint.Claude };
}

/// <summary>
/// OD-0073 (ED-248) helpers: the canvas log written by tests/fixtures/strike/canvas-log.mjs from the locked canvas file,
/// a plain model of the lightning layer at a point, and the port's frames of the reference tubes.
/// </summary>
public static class StrikeFixtures
{
    private static IReadOnlyList<CanvasStrikeTube>? log;

    public static string FixturePath(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine([folder.FullName, "tests", "fixtures", .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    /// <summary>The canvas log, read once.</summary>
    public static IReadOnlyList<CanvasStrikeTube> CanvasLog()
    {
        if (log is not null) return log;
        using var document = JsonDocument.Parse(File.ReadAllBytes(FixturePath("strike", "canvas-log.json")));
        var tubes = new List<CanvasStrikeTube>();
        foreach (var tube in document.RootElement.GetProperty("tubes").EnumerateArray())
        {
            var frames = new List<CanvasStrikeFrame>();
            foreach (var frame in tube.GetProperty("frames").EnumerateArray())
            {
                var ops = new List<CanvasStrikeOp>();
                double[] previous = [];
                foreach (var op in frame.GetProperty("ops").EnumerateArray())
                {
                    var kind = op.GetProperty("op").GetString()!;
                    if (kind == "stroke")
                    {
                        var points = op.TryGetProperty("same", out _) ? previous
                            : op.GetProperty("points").EnumerateArray().SelectMany(p => p.EnumerateArray().Select(v => v.GetDouble())).ToArray();
                        previous = points;
                        ops.Add(new CanvasStrikeOp(kind, op.GetProperty("width").GetDouble(), op.GetProperty("style").GetString(), points, 0, 0, 0, 0, 0, null));
                    }
                    else
                        ops.Add(new CanvasStrikeOp(kind, 0, null, [], op.GetProperty("x").GetDouble(), op.GetProperty("y").GetDouble(),
                            op.GetProperty("rx").GetDouble(), op.GetProperty("ry").GetDouble(), op.GetProperty("alpha").GetDouble(), op.GetProperty("color").GetString()));
                }
                frames.Add(new CanvasStrikeFrame(frame.GetProperty("frame").GetInt32(), frame.GetProperty("t").GetDouble(), ops));
            }
            tubes.Add(new CanvasStrikeTube(tube.GetProperty("name").GetString()!, tube.GetProperty("brand").GetString()!, tube.GetProperty("seed").GetInt32(),
                tube.GetProperty("off").GetDouble(), tube.GetProperty("w").GetDouble(), tube.GetProperty("h").GetDouble(), tube.GetProperty("knob").GetDouble(),
                tube.GetProperty("top").GetBoolean(), tube.GetProperty("start").GetDouble(), tube.GetProperty("ex").GetDouble(), tube.GetProperty("kr").GetDouble(),
                tube.GetProperty("k").GetDouble(), frames));
        }
        return log = tubes;
    }

    /// <summary>"rgba(r,g,b,a)" as its parts.</summary>
    public static (byte R, byte G, byte B, double A) Rgba(string style)
    {
        var parts = style[(style.IndexOf('(', StringComparison.Ordinal) + 1)..style.IndexOf(')', StringComparison.Ordinal)].Split(',');
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return (byte.Parse(parts[0], invariant), byte.Parse(parts[1], invariant), byte.Parse(parts[2], invariant), double.Parse(parts[3], invariant));
    }

    /// <summary>The distance from a point to a polyline of (x, y) pairs.</summary>
    public static double Distance(double x, double y, double[] points)
    {
        var best = double.MaxValue;
        for (var i = 0; i + 3 < points.Length; i += 2)
        {
            double ax = points[i], ay = points[i + 1], bx = points[i + 2], by = points[i + 3];
            double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
            var u = len == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x - ax - u * dx, 2) + Math.Pow(y - ay - u * dy, 2)));
        }
        if (points.Length == 2) best = Math.Sqrt(Math.Pow(x - points[0], 2) + Math.Pow(y - points[1], 2));
        return best;
    }

    /// <summary>The canvas's wash gradient at a point: stops wash@1 at 0, @0.35 at 0.45, @0 at 1, over the ellipse.</summary>
    public static double WashAlpha(StrikeOp wash, double x, double y)
    {
        var d = Math.Sqrt(Math.Pow((x - wash.X) / wash.Rx, 2) + Math.Pow((y - wash.Y) / wash.Ry, 2));
        var g = d <= 0.45 ? 1 + (0.35 - 1) * d / 0.45 : d < 1 ? 0.35 * (1 - (d - 0.45) / 0.55) : 0;
        return g * wash.Alpha;
    }

    /// <summary>
    /// The lightning layer at a point (premultiplied, 0..1), every op composited source-over in order: washes by their
    /// gradient, and each stroke at the coverage <paramref name="coverage"/> gives it (0 for strokes away from the point).
    /// </summary>
    public static (double R, double G, double B, double A) Layer(IEnumerable<StrikeOp> ops, double x, double y, Func<StrikeOp, double> coverage)
    {
        double r = 0, g = 0, b = 0, a = 0;
        foreach (var op in ops)
        {
            var alpha = op.Kind == StrikeOpKind.Wash ? WashAlpha(op, x, y) : coverage(op) * op.Alpha;
            if (alpha <= 0) continue;
            r = op.Color.Red / 255.0 * alpha + r * (1 - alpha);
            g = op.Color.Green / 255.0 * alpha + g * (1 - alpha);
            b = op.Color.Blue / 255.0 * alpha + b * (1 - alpha);
            a = alpha + a * (1 - alpha);
        }
        return (r, g, b, a);
    }

    /// <summary>
    /// What the tube shows at height <paramref name="y"/> of <paramref name="height"/> when the lightning layer is
    /// screen-blended into the liquid under the glass's gloss (LiquidTrack's GlassMain: white 0.16 to 0.04 over the top
    /// 35 %, clear to 55 %, then black to 0.18 at the bottom), given what the tube shows there without it (8-bit).
    /// </summary>
    public static (double R, double G, double B) Screen(SKColor shown, (double R, double G, double B, double A) layer, double y, double height)
    {
        var ty = y / height;
        double glossColour, glossAlpha;
        if (ty <= 0.35) glossColour = glossAlpha = 0.16 + (0.04 - 0.16) * ty / 0.35;
        else if (ty <= 0.55) glossColour = glossAlpha = 0.04 * (1 - (ty - 0.35) / 0.20);
        else { glossColour = 0; glossAlpha = 0.18 * (ty - 0.55) / 0.45; }
        double S(byte value, double s)
        {
            var under = Math.Clamp((value / 255.0 - glossColour) / (1 - glossAlpha), 0, 1); // the liquid under the gloss
            return (glossColour + (1 - glossAlpha) * (s + under - s * under)) * 255;
        }
        return (S(shown.Red, layer.R), S(shown.Green, layer.G), S(shown.Blue, layer.B));
    }

    public static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    /// <summary>The liquid of <paramref name="spec"/> with the given lightning (or none), at a device scale.</summary>
    public static SKBitmap Liquid(LiquidSpec spec, Size size, double track, double time, LiquidFlow flow, IReadOnlyList<StrikeOp>? strike, int scale = 2,
        BubbleLanes? bubbles = null, ThemeVariant? theme = null)
    {
        var appearance = theme ?? ThemeVariant.Light;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
            LiquidTrack.Draw(context, size, track, time, spec, LiquidTrack.GradientFor(spec, (int)Math.Ceiling(size.Width * scale), appearance), bubbles, false, appearance, flow, strike);
        return Decode(bitmap);
    }

    /// <summary>
    /// One port frame of a reference tube at the reference's crop (the tube plus 8 px each side, 2x): the pane colour
    /// taken from the reference frame's corner, the tube's track, the port's liquid with its Strike over the filled
    /// part, and a stand-in knob of the board's size at the knob centre. Without <paramref name="lightning"/>, the same
    /// frame with no Strike layer (the liquid alone, to measure the light the strike adds).
    /// </summary>
    public static SKBitmap PortFrame(CanvasStrikeTube tube, double tileTime, SKColor pane, bool dark, double liquidWidth, BubbleLanes bubbles, bool lightning = true)
    {
        const int scale = 2; const double pad = 8;
        var tubeWidth = tube.Name.Contains("520", StringComparison.Ordinal) ? 520.0 : 300.0;
        var brand = tube.Brand switch { "deepseek" => EffortBrand.Deepseek, "gemini" => EffortBrand.Antigravity, _ => EffortBrand.Claude };
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fusion, brand));
        var size = new Size(liquidWidth, tube.H);
        var knob = tube.Top ? liquidWidth - 2 - tube.Knob / 2 : liquidWidth - 2;
        var geometry = new StrikeGeometry(liquidWidth, tube.H, knob, tube.Knob / 2);
        var ops = lightning ? new StrikeChannels(tube.Seed).Scene(geometry, tileTime, tube.Tint) : null;
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)((tubeWidth + pad * 2) * scale), (int)((tube.H + pad * 2) * scale)), new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(pane.Red, pane.Green, pane.Blue)), new Rect(0, 0, tubeWidth + pad * 2, tube.H + pad * 2));
            var track = new RoundedRect(new Rect(pad, pad, tubeWidth, tube.H), tube.H / 2);
            context.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(64, 0, 0, 0) : Color.FromArgb(26, 24, 34, 48)), null, track);
            using (context.PushTransform(Matrix.CreateTranslation(pad, pad)))
                LiquidTrack.Draw(context, size, tubeWidth, tileTime, spec, LiquidTrack.GradientFor(spec, (int)Math.Ceiling(liquidWidth * scale), theme), bubbles, false, theme,
                    LiquidFlow.At(tileTime, true), ops);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(87, 255, 255, 255)), 1), new RoundedRect(new Rect(pad + 0.5, pad + 0.5, tubeWidth - 1, tube.H - 1), tube.H / 2 - 0.5));
            var centre = new Point(pad + knob, pad + tube.H / 2);
            var lens = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.34, 0.26, RelativeUnit.Relative), Center = new RelativePoint(0.34, 0.26, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(191, 255, 255, 255), 0), new GradientStop(Color.FromArgb(191, 255, 255, 255), 0.14),
                    new GradientStop(Color.FromArgb(20, 255, 255, 255), 0.46), new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.78), new GradientStop(Color.FromArgb(102, 255, 255, 255), 1) },
            };
            context.DrawEllipse(lens, new Pen(new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)), 1), centre, tube.Knob / 2, tube.Knob / 2);
        }
        return Decode(bitmap);
    }
}
