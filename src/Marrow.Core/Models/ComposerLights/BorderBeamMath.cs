// PORT-ONLY, owner-ruled addition (OD-0089; ED-251): the pure mathematics of the composer-lights playground, ported number
// for number from E:/Droppy/work/design/composer-lights/playground/src/beam.js (border-beam 1.4.1's curves and the
// playground's fitted geometry; border-beam is Copyright (c) 2026 Jakub Antalik, MIT) and logic.js (bez, mixHex, DUR).
// Each member names the JavaScript function it ports. Tests compare them with values the playground's own functions
// return under node.

namespace Marrow.Core.Models.ComposerLights;

/// <summary>A point of a window profile: [share of the turn, alpha].</summary>
public readonly record struct ProfilePoint(double At, double Alpha);

/// <summary>The scale factors that map a type's reference box to the composer (bbFitK).</summary>
public readonly record struct FitScale(double RefW, double RefH, double Kx, double Ky, double Kd, double Ke);

/// <summary>
/// A CSS position on one axis as bbFitBlob writes it: a share of the box (<c>xp%</c>), px from the start
/// (<c>calc(0% + v)</c>) or px from the end (<c>calc(100% - v)</c>), so the same blob can be resolved on a larger box
/// (Pulse Outside's haloes).
/// </summary>
public readonly record struct CssPosition(int Kind, double Value)
{
    public static CssPosition Percent(double p) => new(0, p);
    public static CssPosition FromStart(double px) => new(1, px);
    public static CssPosition FromEnd(double px) => new(2, px);
    public double Resolve(double length) => Kind switch { 0 => Value / 100 * length, 1 => Value, _ => length + Value };
}

/// <summary>A blob mapped to the composer (bbFitBlob): its centre, its radii, and the axis scales.</summary>
public readonly record struct FittedBlob(CssPosition PX, CssPosition PY, double W, double H, double Ax, double Ay)
{
    public double X(double boxW) => PX.Resolve(boxW);
    public double Y(double boxH) => PY.Resolve(boxH);
}

/// <summary>The playground's numbers (beam.js and logic.js), as pure functions.</summary>
public static class BorderBeamMath
{
    /// <summary>BB_REF: the box each type was tuned on (the site's 348 x 122 card for Large, its 366 x 42 search bar for Line).</summary>
    public static (double W, double H) ReferenceBox(BeamType type) => type switch
    {
        BeamType.Large => (348, 122),
        BeamType.Small => (70, 36),
        BeamType.Line => (366, 42),
        _ => (350, 140),
    };

    /// <summary>BB_REF_R: the reference box's corner radius.</summary>
    public static double ReferenceRadius(BeamType type) => type switch
    {
        BeamType.Large => 20,
        BeamType.Small => 18,
        BeamType.Line => 21,
        _ => 16,
    };

    /// <summary>logic.js DUR: the package's duration per type (seconds a turn, a travel, or a breath).</summary>
    public static double PackageDuration(BeamType type) => type switch
    {
        BeamType.Large or BeamType.Small => 1.96,
        BeamType.Line => 3.1,
        _ => 2.3,
    };

    /// <summary>bbN / bbPx: a CSS length written with toFixed(2).</summary>
    public static double Fixed2(double v) => ToFixed(v, 2);

    /// <summary>JavaScript's Number.prototype.toFixed(n), read back as a number.</summary>
    public static double ToFixed(double v, int digits)
    {
        var scale = Math.Pow(10, digits);
        var n = Math.Floor(Math.Abs(v) * scale + 0.5) / scale;
        return v < 0 ? -n : n;
    }

    /// <summary>bbOff: a signed px term, dropped below 0.005.</summary>
    public static double Offset(double v) => Math.Abs(v) < 0.005 ? 0 : Fixed2(v);

    /// <summary>bbFitK.</summary>
    public static FitScale FitK(BeamType type, double w, double h)
    {
        var (rw, rh) = ReferenceBox(type);
        double kx = w / rw, ky = h / rh;
        return new FitScale(rw, rh, kx, ky, Math.Pow(ky, 0.25), Math.Sqrt(kx));
    }

    /// <summary>
    /// bbFitBlob: one blob, tuned on its reference box at (xp %, yp %) with radii (w, h), mapped to a box w x h. Along a long
    /// edge the position keeps its share of the edge; across it the depth scales with kd; on an end with ke and ky.
    /// </summary>
    public static FittedBlob FitBlob(double xp, double yp, double w, double h, FitScale k, double boxW, double boxH)
    {
        double dT = yp / 100 * k.RefH, dB = (100 - yp) / 100 * k.RefH, dL = xp / 100 * k.RefW, dR = (100 - xp) / 100 * k.RefW;
        var m = Math.Min(Math.Min(dT, dB), Math.Min(dL, dR));
        if (m == dT || m == dB)
        {
            var y = m == dT ? CssPosition.FromStart(Offset(dT * k.Kd)) : CssPosition.FromEnd(Offset(-dB * k.Kd));
            return new FittedBlob(CssPosition.Percent(xp), y, w * k.Kx, h * k.Kd, k.Kx, k.Kd);
        }
        var x = m == dL ? CssPosition.FromStart(Offset(dL * k.Ke)) : CssPosition.FromEnd(Offset(-dR * k.Ke));
        return new FittedBlob(x, CssPosition.Percent(yp), w * k.Ke, Math.Max(h * k.Ky, 0.3 * boxH), k.Ke, k.Ky);
    }

    /// <summary>bbPerim's clamp: the corner radius a w x h box can have.</summary>
    public static double ClampRadius(double w, double h, double r) => Math.Max(0, Math.Min(r, Math.Min(w / 2, h / 2)));

    /// <summary>bbPerim(...).L / bbPerimLen: the length of a rounded rectangle's outline.</summary>
    public static double PerimeterLength(double w, double h, double r)
    {
        r = ClampRadius(w, h, r);
        return 2 * (w + h) - (8 - 2 * Math.PI) * r;
    }

    /// <summary>bbPerim(...).at(u): the point at a share u of the outline, from the top centre, clockwise.</summary>
    public static (double X, double Y) PerimeterAt(double w, double h, double r, double u)
    {
        r = ClampRadius(w, h, r);
        var a = Math.PI * r / 2;
        Span<double> segs = [w / 2 - r, a, h - 2 * r, a, w - 2 * r, a, h - 2 * r, a, w / 2 - r];
        double total = 0;
        foreach (var s0 in segs) total += s0;
        var s = (u % 1 + 1) % 1 * total;
        (double, double) Arc(double cx, double cy, double a0, double t) { var g = a0 + t * Math.PI / 2; return (cx + r * Math.Sin(g), cy - r * Math.Cos(g)); }
        (double, double) F(int i, double t) => i switch
        {
            0 => (w / 2 + t * (w / 2 - r), 0),
            1 => Arc(w - r, r, 0, t),
            2 => (w, r + t * (h - 2 * r)),
            3 => Arc(w - r, h - r, Math.PI / 2, t),
            4 => (w - r - t * (w - 2 * r), h),
            5 => Arc(r, h - r, Math.PI, t),
            6 => (0, h - r - t * (h - 2 * r)),
            7 => Arc(r, r, 1.5 * Math.PI, t),
            _ => (r + t * (w / 2 - r), 0),
        };
        for (var i = 0; i < segs.Length; i++)
        {
            if (s <= segs[i] || i == segs.Length - 1) return F(i, segs[i] > 0 ? Math.Min(1, s / segs[i]) : 0);
            s -= segs[i];
        }
        return (w / 2, 0);
    }

    // BB_PROF: the package's window profiles
    public static readonly ProfilePoint[] WindowW1 = P([0, 0], [0.30, 0], [0.36, 0.1], [0.44, 0.35], [0.52, 1], [0.80, 1], [0.86, 0.35], [0.92, 0.1], [0.95, 0], [1, 0]);
    public static readonly ProfilePoint[] WindowW2 = P([0, 0], [0.22, 0], [0.28, 0.12], [0.36, 0.4], [0.46, 1], [0.82, 1], [0.88, 0.4], [0.94, 0.12], [0.97, 0], [1, 0]);
    public static readonly ProfilePoint[] HighlightDark = P([0, 0], [0.54, 0], [0.57, 0.1], [0.60, 0.3], [0.63, 0.6], [0.66, 0.75], [0.69, 0.6], [0.72, 0.3], [0.75, 0.1], [0.78, 0], [1, 0]);
    public static readonly ProfilePoint[] HighlightLight = P([0, 0], [0.54, 0], [0.57, 0.08], [0.60, 0.2], [0.63, 0.4], [0.66, 0.55], [0.69, 0.4], [0.72, 0.2], [0.75, 0.08], [0.78, 0], [1, 0]);
    public static readonly ProfilePoint[] BloomDark = P([0, 0], [0.58, 0], [0.62, 0.03], [0.65, 0.08], [0.67, 0.2], [0.69, 0.45], [0.70, 0.85], [0.705, 0.85], [0.715, 0.45], [0.73, 0.2], [0.75, 0.08], [0.78, 0.03], [0.82, 0], [1, 0]);
    public static readonly ProfilePoint[] BloomLight = P([0, 0], [0.58, 0], [0.62, 0.02], [0.65, 0.08], [0.67, 0.2], [0.69, 0.4], [0.70, 0.6], [0.705, 0.6], [0.715, 0.4], [0.73, 0.2], [0.75, 0.08], [0.78, 0.02], [0.82, 0], [1, 0]);

    private static ProfilePoint[] P(params double[][] points) => points.Select(p => new ProfilePoint(p[0], p[1])).ToArray();

    /// <summary>bbProf: a profile at a share f of the turn (wrapped), linear between its points.</summary>
    public static double Profile(ReadOnlySpan<ProfilePoint> p, double f)
    {
        f = (f % 1 + 1) % 1;
        for (var i = 1; i < p.Length; i++)
            if (f <= p[i].At)
            {
                var a = p[i - 1]; var b = p[i];
                var t = b.At > a.At ? (f - a.At) / (b.At - a.At) : 1;
                return a.Alpha + (b.Alpha - a.Alpha) * t;
            }
        return p[^1].Alpha;
    }

    /// <summary>bbFrac.</summary>
    public static double Frac(double v) => v - Math.Floor(v);

    /// <summary>bbEase: cubic-bezier(.42, 0, .58, 1), CSS ease-in-out, solved for y at x (22 bisections, as the playground).</summary>
    public static double EaseInOut(double x)
    {
        const double x1 = 0.42, x2 = 0.58;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 22; i++)
        {
            t = (lo + hi) / 2;
            var cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t;
            if (cx < x) lo = t; else hi = t;
        }
        return 3 * t * t * (1 - t) + t * t * t;
    }

    /// <summary>logic.js bez: cubic-bezier(.56, .15, .28, .86), the reference's speen timing (24 bisections).</summary>
    public static double Speen(double x)
    {
        const double x1 = 0.56, y1 = 0.15, x2 = 0.28, y2 = 0.86;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            var cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t;
            if (cx < x) lo = t; else hi = t;
        }
        return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
    }

    /// <summary>bbKf: keyframes at a phase (wrapped), each segment linear or eased in and out.</summary>
    public static double Keyframes(ReadOnlySpan<ProfilePoint> k, double f, bool ease)
    {
        f = Frac(f);
        for (var i = 1; i < k.Length; i++)
            if (f <= k[i].At)
            {
                var a = k[i - 1]; var b = k[i];
                var t = (f - a.At) / (b.At - a.At);
                return a.Alpha + (b.Alpha - a.Alpha) * (ease ? EaseInOut(t) : t);
            }
        return k[^1].Alpha;
    }

    /// <summary>bbSwing: the hue swing, -1 to +1 and back over one period, each half eased in and out.</summary>
    public static double Swing(double phase)
    {
        var u = Frac(phase) * 2;
        return u < 1 ? -1 + 2 * EaseInOut(u) : 1 - 2 * EaseInOut(u - 1);
    }

    // Line's keyframes (BB_TRAVEL_X, BB_TRAVEL_W, BB_EDGE, BB_BREATHE, BB_SPIKE, BB_SPIKE2)
    public static readonly ProfilePoint[] TravelX = P([0, 0.06], [0.1, 0.15], [0.2, 0.25], [0.3, 0.35], [0.4, 0.44], [0.5, 0.5], [0.6, 0.56], [0.7, 0.65], [0.8, 0.75], [0.9, 0.85], [1, 0.94]);
    public static readonly ProfilePoint[] TravelW = P([0, 0.5], [0.1, 0.8], [0.2, 1.1], [0.3, 1.3], [0.4, 1.45], [0.5, 1.5], [0.6, 1.45], [0.7, 1.3], [0.8, 1.1], [0.9, 0.8], [1, 0.5]);
    public static readonly ProfilePoint[] Edge = P([0, 0], [0.125, 0], [0.325, 1], [0.675, 1], [0.875, 0], [1, 0]);
    public static readonly ProfilePoint[] Breathe = P([0, 0.8], [0.25, 1.25], [0.55, 0.85], [0.8, 1.3], [1, 0.8]);
    public static readonly ProfilePoint[] SpikeA = P([0, 0.8], [0.25, 1.3], [0.5, 0.9], [0.75, 1.4], [1, 0.8]);
    public static readonly ProfilePoint[] SpikeB = P([0, 1.2], [0.25, 0.7], [0.5, 1.4], [0.75, 0.8], [1, 1.2]);

    /// <summary>bbLinePeriods: Line's breathe and spike periods, written by the package with toFixed(1).</summary>
    public static (double Breathe, double Spike, double Spike2) LinePeriods(double duration) =>
        (ToFixed(duration * 1.3, 1), ToFixed(duration * 1.33, 1), ToFixed(duration * 1.7, 1));

    /// <summary>bbGe: a pulse type's parameters for a theme.</summary>
    public static (double Sp, double Dr, double Op, double Gh, double Bs, double Ss, double Ghs, double HuePeriod) PulseParameters(BeamType type, bool dark) =>
        type == BeamType.PulseInner
            ? (0.28, dark ? 33 : 40, dark ? 0.48 : 0.45, dark ? 0.34 : 0.22, dark ? 1.9 : 2.6, dark ? 2.6 : 4.6, dark ? 2.4 : 5.5, 16)
            : (dark ? 0.28 : 0.36, dark ? 14 : 19, dark ? 0.46 : 0, dark ? 0.16 : 0.58, dark ? 2.3 : 3.7, dark ? 6.4 : 4.6, dark ? 2.4 : 3.8, 14);

    /// <summary>BB_OSC_NAMES: the 17 oscillators, in order.</summary>
    public static readonly string[] OscillatorNames = ["bw1", "bh1", "bx1", "by1", "bw2", "bh2", "bx2", "by2", "bw3", "bh3", "bx3", "by3", "bgh", "bop-tl", "bop-tr", "bop-bl", "bop-br"];

    /// <summary>bbOsc: [a, b, period, delay] per oscillator at the default duration.</summary>
    public static double[][] Oscillators(BeamType type, bool dark)
    {
        var (o, r, t, s, i, c, p, _) = PulseParameters(type, dark);
        return
        [
            [1 - o, 1 + o * 1.1, c * 0.9, 0], [1 + o * 0.9, 1 - o * 0.85, c * 1.26, 0], [-r, r * 0.9, i * 1.6, 0], [r * 0.55, -r * 0.7, i * 1.6, 0],
            [1 + o, 1 - o * 0.85, c * 1.1, 0], [1 - o * 0.8, 1 + o * 1.05, c * 0.81, 0], [r * 0.8, -r * 0.9, i * 1.88, 0], [-r, r * 0.65, i * 1.88, 0],
            [1 - o * 0.6, 1 + o * 1.15, c * 0.98, 0], [1 + o * 0.75, 1 - o, c * 1.4, 0], [-r * 0.6, r, i * 1.45, 0], [-r * 0.85, r * 0.45, i * 1.45, 0],
            [1 - s, 1 + s, p, 0], [1 - t, 1, i, 0], [1 - t, 1, i * 1.32, i * 0.28], [1 - t, 1, i * 0.84, i * 0.55], [1 - t, 1, i * 1.58, i * 0.83],
        ];
    }

    /// <summary>bbOscAt: an oscillator at scaled time tau: a + (b - a) (1 - cos 2 pi x) / 2.</summary>
    public static double OscillatorAt(double[] o, double tau) => o[0] + (o[1] - o[0]) * (1 - Math.Cos(2 * Math.PI * ((tau - o[3]) / o[2]))) / 2;

    /// <summary>bbR: a blur radius times the glow size, at least .5 px, to the hundredth (JavaScript's Math.round).</summary>
    public static double GlowRadius(double a, double glowSize) => Math.Max(0.5, Math.Floor(a * glowSize * 100 + 0.5) / 100);

    /// <summary>
    /// bbFitDur: fitted, the automatic duration keeps the speed the type has on its reference box (px a second along the
    /// outline for the rotate types, along the bottom for Line); the pulse types keep theirs.
    /// </summary>
    public static double FitDurationScale(BeamType type, double w, double h, double r)
    {
        // a composer not laid out yet (0 x 0) keeps the package's duration: 0 / 0 would poison every integrated phase
        if (!(w > 0) || !(h > 0)) return 1;
        if (type is BeamType.Large or BeamType.Small)
        {
            var (rw, rh) = ReferenceBox(type);
            return PerimeterLength(w, h, r) / PerimeterLength(rw, rh, ReferenceRadius(type));
        }
        if (type == BeamType.Line) return w / (h / 42) / 366;
        return 1;
    }

    /// <summary>beamDur / beamEff's autoDur: the slider's duration, else the package's, scaled when fitted.</summary>
    public static double Duration(BorderBeamOptions b, double w, double h, double defaultRadius)
    {
        if (b.Duration is { } d) return d;
        return AutoDuration(b, w, h, defaultRadius);
    }

    /// <summary>The Duration slider's Auto value (for 820 x 52, radius 22, Large fitted: 3.69 s a turn).</summary>
    public static double AutoDuration(BorderBeamOptions b, double w, double h, double defaultRadius) =>
        PackageDuration(b.Type) * (b.Fitted ? FitDurationScale(b.Type, w, h, b.Radius ?? defaultRadius) : 1);

    /// <summary>logic.js mixHex: h mixed toward w by t, per channel, rounded.</summary>
    public static string MixHex(string h, string w, double t)
    {
        static int C(string s, int i) => Convert.ToInt32(s.Substring(i, 2), 16);
        var outp = "#";
        foreach (var i in new[] { 1, 3, 5 })
        {
            double a = C(h, i), b = C(w, i);
            outp += ((int)Math.Floor(a + (b - a) * t + 0.5)).ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
        }
        return outp;
    }
}
