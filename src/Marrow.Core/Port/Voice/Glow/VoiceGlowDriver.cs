// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// The glow's driver (voice-glow.js <c>ka</c>, lines 759-816, with the helpers above it): the envelope, the flow phase, the processing scan,
/// the corner follow, the lobes, the hue drift, the warp and the band polyline. Deterministic per frame delta: the same sequence of
/// (delta, level, size) always gives the same frames. The frame cadence (the page's 60 fps cap and its half-rate governor) belongs to the
/// view, which calls <see cref="Step"/> once per frame it draws. Call it only while the glow is active; the state is kept between calls, as the
/// page keeps its element's state across a re-registration.
/// </summary>
public sealed class VoiceGlowDriver
{
    // voice-glow.js constants: Ua = 36 (the lobe pitch), Ka = Ua * lobes, Ma = 2 pi, hn / dn the warp's rise and fall, bn the warp-off threshold.
    private const double LobePitch = 36;
    private const double TwoPi = Math.PI * 2;
    private const double WarpRise = 0.35;
    private const double WarpFall = 0.06;
    private const double WarpOffBelow = 0.03;
    private const double BandHalfWidth = 170;
    private const double BandHeight = 64;
    private const int BandSegments = VoiceGlowFrame.BandPointCount - 1;
    private const double FirstFrameDelta = 1.0 / 60;
    private const double MaxDelta = 0.05;

    private readonly VoiceGlowFrame _frame = new();
    private readonly double[] _bands = new double[3];
    private VoiceGlowConfig _config;
    private double _level;
    private double _phase;
    private double _scanA;
    private double _scanT;
    private double _t;
    private double _warp = 1;
    private bool _restart = true;

    public VoiceGlowDriver(VoiceGlowConfig config)
    {
        _config = config;
        _frame.Warp = 1;
    }

    /// <summary>The frame the last <see cref="Step"/> filled.</summary>
    public VoiceGlowFrame Frame => _frame;

    /// <summary>Applies a (possibly changed) config; a change makes the next step a 1/60 s one, as a re-registration does in the page.</summary>
    public void Configure(VoiceGlowConfig config)
    {
        if (config.Equals(_config)) return;
        _config = config;
        _restart = true;
    }

    /// <summary>The next step is a 1/60 s one (the page's registration resets its last timestamp).</summary>
    public void Restart() => _restart = true;

    /// <summary>
    /// Advances the driver by <paramref name="dtSeconds"/> (at most 0.05; the first step after a registration is 1/60 whatever is given) with the
    /// raw level (0 to 1) and the size of the shape the glow is drawn in. Returns the reused frame.
    /// </summary>
    public VoiceGlowFrame Step(double dtSeconds, double level, double widthPx, double heightPx)
    {
        var t = _config;
        double u;
        if (_restart)
        {
            u = FirstFrameDelta;
            _restart = false;
        }
        else
        {
            u = dtSeconds > 0 ? Math.Min(MaxDelta, dtSeconds) : 0;
        }

        _t += u;
        var l = _t;

        // the level source: a number 0..1 (`getLevel`), the three bands derived from it (voice-glow.js:769-776)
        var f = double.IsNaN(level) ? 0 : Clamp01(level);
        var sources = (f, f * (0.72 + 0.28 * Math.Sin(l * 9.1)), f * (0.6 + 0.4 * Math.Sin(l * 13.7 + 2)));
        _level = Follow(_level, Envelope(f, t.Threshold), u, t.Attack, t.Release);
        _bands[0] = Follow(_bands[0], Envelope(sources.Item1, t.Threshold * 0.6), u, t.Attack, t.Release * 1.15);
        _bands[1] = Follow(_bands[1], Envelope(sources.Item2, t.Threshold * 0.6), u, t.Attack, t.Release * 1.15);
        _bands[2] = Follow(_bands[2], Envelope(sources.Item3, t.Threshold * 0.6), u, t.Attack, t.Release * 1.15);

        var b = LobePitch * VoiceGlowFrame.LobeCount * t.LobeSpacing; // the flow period
        if (t.Processing && _scanA < 1e-3 && _scanT == 0) _scanT = Math.Max(0.05, t.ProcessingDuration) / 2;
        var d = Math.Max(0.05, t.ProcessingEase);
        _scanA = Follow(_scanA, t.Processing ? 1 : 0, u, d * 0.9, d * 0.8);
        if (t.Processing) _scanT += u;
        else if (_scanA < 1e-3) _scanT = 0;

        var h = _scanA * _scanA * (3 - 2 * _scanA);
        var width = widthPx;
        var height = heightPx;
        var travel = b / 2 * t.ProcessingTravel;
        var cycles = _scanT / Math.Max(0.05, t.ProcessingDuration);
        var whole = Math.Floor(cycles);
        var frac = cycles - whole;
        var curve = Math.Max(1, t.ProcessingCurve);
        var eased = frac < 0.5 ? 0.5 * Math.Pow(2 * frac, curve) : 1 - 0.5 * Math.Pow(2 - 2 * frac, curve);
        var direction = t.ReducedMotion ? 0 : whole % 2 == 0 ? 2 * eased - 1 : 1 - 2 * eased;
        var z = h * travel * direction;
        var g = 1 - h * 0.6;
        var mw = 1 - h * 0.45;
        var q = 1 + h * 0.3 * (1 - direction * direction);
        var breathe = t.ReducedMotion ? 0.5 : 0.5 + 0.5 * Math.Sin(TwoPi * l / t.BreatheDuration);
        var k0 = _level + (1 - _level) * t.Idle * breathe;
        var w0 = Math.Max(0, Math.Min(1, (h - 0.25) / 0.75));
        var r0 = w0 * w0 * (3 - 2 * w0);
        var intensity = Math.Max(k0, t.ProcessingLevel * r0);
        var glow = 0.15 + 0.85 * intensity;
        var vh = 0.5 + t.Reach * intensity;
        var vw = (0.85 + t.Spread * intensity) * q;
        if (t.Flow != 0 && !t.ReducedMotion) _phase = ((_phase + t.Flow * intensity * u) % b + b) % b;

        var bend = t.Bend * intensity;
        var bendA = t.Bend > 0 ? Math.Min(1, bend / t.Bend) : 0;
        var y = width / 2 + z * vw;
        var oe = 30 * t.Scale * vw;
        var xe = Limit(t.Radius, width, height);
        var re = h * t.CornerFollow;
        var cy = -Drop(y, width, xe, oe * 1.4) * re;
        _warp = Follow(_warp, t.Processing ? 0 : 1, u, WarpRise, WarpFall);
        var warp = _warp;
        var hasDisplace = t.Distortion > 0;
        var warpOff = hasDisplace && warp < WarpOffBelow;

        var fr = _frame;
        if (width != 0 && height != 0)
        {
            ComputeBand(fr, t, width, height, z, vw, vh, mw, bend, h);
            fr.HasBand = true;
        }
        else
        {
            fr.HasBand = false;
        }

        fr.DisplaceActive = hasDisplace && !warpOff;
        if (fr.DisplaceActive)
        {
            fr.DisplaceScale = t.ReducedMotion ? 0 : t.Distortion * 120 * t.Scale * (0.15 + 0.85 * intensity) * warp;
            fr.NoiseDx = 8 * t.Scale * Math.Sin(l * 0.9);
            fr.NoiseDy = 4 * t.Scale * Math.Sin(l * 0.6 + 1.3);
        }

        for (var i = 0; i < VoiceGlowFrame.LobeCount; i++)
        {
            var lobe = VoiceGlowResolver.Lobes[i];
            var j = Wrap(lobe.X * t.LobeSpacing + _phase, b);
            var zz = t.Bands ? 0.6 + 0.7 * _bands[lobe.Band] : 1;
            fr.LobeX[i] = j * g;
            fr.LobeL[i] = zz * Falloff(j, b);
            var ge = width / 2 + (z + j * g) * vw;
            fr.LobeY[i] = -Drop(ge, width, xe, oe) * re;
        }

        fr.Hue = t.StaticColors || t.ReducedMotion || t.HueRange == 0 ? 0 : -t.HueRange + 2 * t.HueRange * Cycle(l / t.HueDuration);
        fr.Level = _level;
        _bands.CopyTo(fr.Bands, 0);
        fr.Phase = _phase;
        fr.ScanA = _scanA;
        fr.ScanT = _scanT;
        fr.Time = _t;
        fr.Warp = warp;
        fr.Corner = h;
        fr.Cx = z;
        fr.Mw = mw;
        fr.Bh = Math.Max(0, bend);
        fr.BendA = bendA;
        fr.Cy = cy;
        fr.Glow = glow;
        fr.H = vh;
        fr.W = vw;
        fr.WarpOff = warpOff;
        return fr;
    }

    // Sa
    private static double Clamp01(double n) => n < 0 ? 0 : n > 1 ? 1 : n;

    // Ge: an exponential follower with a time constant for each direction
    private static double Follow(double current, double target, double a, double up, double down)
    {
        var s = target > current ? up : down;
        var o = 1 - Math.Exp(-a / Math.Max(1e-3, s));
        return current + (target - current) * o;
    }

    // pa: the noise gate and soft knee
    private static double Envelope(double n, double e)
    {
        if (n <= e) return 0;
        var a = (n - e) / Math.Max(1e-3, 1 - e);
        return Clamp01((1 - Math.Exp(-3 * a)) / (1 - Math.Exp(-3)));
    }

    // sn: n wrapped into [-e/2, e/2)
    private static double Wrap(double n, double e)
    {
        var a = e / 2;
        return ((n + a) % e + e) % e - a;
    }

    // cn: a lobe's fall-off with distance from the centre
    private static double Falloff(double n, double e)
    {
        var a = n / (e / 2 + 4);
        return Math.Max(0, 1 - a * a);
    }

    // Tn
    private static double Cycle(double n) => (1 - Math.Cos(TwoPi * n)) / 2;

    // Ta: the corner radius that fits the shape
    private static double Limit(double n, double e, double a) => Math.Max(0, Math.Min(n, Math.Min(e / 2, a / 2)));

    // gt: how far the shape's rounded corner pulls a point at x down
    private static double Drop(double n, double e, double a, double r = 0)
    {
        if (a <= 0) return 0;
        var t = Math.Min(n, e - n) - r;
        if (t >= a) return 0;
        if (t <= 0) return a;
        var s = a - t;
        return a - Math.Sqrt(Math.Max(0, a * a - s * s));
    }

    // $n: the band's bell profile
    private static double Bell(double n, double e, double a, double r)
    {
        var t = n < 0 ? 1 - r : 1 + r;
        var s = Math.Max(0.05, a * t);
        var o = Math.Exp(-Math.Pow(Math.Abs(n) / s, e));
        var i = Math.Exp(-Math.Pow(1 / s, e));
        return Math.Max(0, (o - i) / (1 - i));
    }

    // wn: the band's tail
    private static double Tail(double n, double e, double a, double r, double t)
    {
        if (a <= 0 || e <= 0) return 0;
        var s = e * Math.Max(0, Math.Min(0.98, r));
        if (n <= s) return 0;
        var o = Math.Min(1, (n - s) / Math.Max(1, e - s));
        return a * Math.Pow(o, Math.Max(0.5, t));
    }

    // xn: the polyline of the band across the shape
    private static void ComputeBand(VoiceGlowFrame fr, VoiceGlowConfig n, double a, double r, double cx, double w, double hh, double mw, double lift, double corner)
    {
        var t = a / 2 + cx * w;
        var s = BandHalfWidth * n.RangeWidth * w * mw;
        var o = r * 0.82 * Math.Min(1, n.Scale);
        var i = Math.Min(o, (BandHeight * n.RangeHeight * hh + lift) * n.BandPosition);
        var u = r - n.BandOffset;
        var l = Math.Min(1, corner * 4);
        var p = n.BandTail * (1 - l * l * (3 - 2 * l));
        var b = p > 1e-3;
        var m = b ? n.BandTailOverflow : 0;
        var d = b ? -m : t - s;
        var hi = b ? a + m : t + s;
        for (var index = 0; index <= BandSegments; index++)
        {
            var x = d + (hi - d) * index / BandSegments;
            var normal = Math.Max(-1, Math.Min(1, (x - t) / Math.Max(1, s)));
            var reach = (x < t ? t : a - t) + m;
            var profile = Bell(normal, n.BandCurve, n.BandSpread, n.BandSkew) + Tail(Math.Abs(x - t), reach, p, n.BandTailPosition, n.BandTailCurve);
            var e = corner > 0 ? Drop(x, a, Limit(n.Radius, a, r)) * corner : 0;
            fr.BandX[index] = x;
            fr.BandY[index] = u - i * profile - e;
        }
    }
}
