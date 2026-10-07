namespace Marrow.App.Views.Composer;

// DEVIATION OD-0090 (locked page E:\Droppy\work\design\jewel-knob-locked\jewel-knob.html): how the Jewel knob moves and
// how it reads the lightning.
//   KnobSpring: the page's spring() (SwiftUI's spring(response:dampingFraction:) as a physical spring: stiffness
//     (2 pi / response)^2, damping 4 pi z / response, integrated in steps of at most 4 ms, at rest within 1e-4 and 1e-3).
//   KnobSignal: the page's part(), shape() and rawSignal() over the strikes the tube draws, with orbAim()'s exit angle
//     (strike-engine.js). The port's fast mode is still the A2 strike at its own times (OD-0073, ED-248: the OD-0084 Mix is
//     not built yet), so each A2 strike gets the charge the locked Mix gives every A2 strike it draws (plan(): 0.14 +
//     0.12 hash(k, 13 S + 131) s before the leader): the orb charges first, then the bolt leaves it (where the page's
//     does: tl.exAt, 2 px before its centre on a quarter pixel).

/// <summary>One value on the page's spring: position and velocity.</summary>
public struct KnobSpring
{
    public double P;
    public double V;

    public KnobSpring(double p) { P = p; V = 0; }

    /// <summary>Moves toward <paramref name="target"/> over <paramref name="dt"/> s (the page's spring()).</summary>
    public void Step(double target, double dt, double response, double damping)
    {
        var w = 2 * Math.PI / response;
        double k = w * w, c = 2 * damping * w;
        var n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        var h = dt / n;
        for (var i = 0; i < n; i++) { V += (-k * (P - target) - c * V) * h; P += V * h; }
        if (Math.Abs(P - target) < 1e-4 && Math.Abs(V) < 1e-3) { P = target; V = 0; }
    }

    /// <summary>True when it rests on <paramref name="target"/>.</summary>
    public readonly bool AtRest(double target) => P == target && V == 0;
}

/// <summary>
/// The page's frame gate (strike-engine.js:56-66 frameGate, which jewel-knob.html:867-871 asks on every display refresh,
/// drawing only when it passes): the refresh r is measured from the gaps between frames (a gap counts when 2 &lt; d &lt; 50
/// ms: r = min(d, 0.9 r + 0.1 d), 1000/60 ms until one has), and a frame is drawn when every r - r/2 ms have passed since
/// the last drawn one, every = max(1, floor(1000/60/r + 0.05)): evenly paced on the refresh, every refresh at 60 to 90 Hz,
/// every 2nd at 120 to 165 Hz, every 4th at 239 and 240 Hz. The page asks on every refresh; the knob's loop stops asking
/// while nothing moves (ED-252). The gap to its next frame, a whole number of refreshes after the last drawn one, is
/// measured as any other: r = min(d, ...) is the refresh again at the frame after, and the wake frame is drawn or not as it
/// would be with the gap unmeasured (ties at the threshold aside). After a wake the first frame is drawn when the last
/// drawn is far enough past, as after any pause; the page, drawing all along, keeps its own cadence's phase.
/// </summary>
public sealed class KnobFrameGate
{
    private double? previous, lastDrawn, refresh;

    /// <summary>Whether the frame at <paramref name="now"/> (ms) is drawn.</summary>
    public bool Draw(double now)
    {
        if (previous is { } before)
        {
            var d = now - before;
            if (d > 2 && d < 50) refresh = refresh is { } r0 ? Math.Min(d, r0 * 0.9 + d * 0.1) : d;
        }
        previous = now;
        var r = refresh ?? 1000.0 / 60;
        var every = Math.Max(1, Math.Floor(1000.0 / 60 / r + 0.05));
        var draw = lastDrawn is not { } last || now - last >= every * r - r / 2;
        if (draw) lastDrawn = now;
        return draw;
    }
}

/// <summary>Which part of a strike the knob is in (the page's phase readout).</summary>
public enum KnobPhase { Rest, Settle, Charge, Strike }

/// <summary>One strike as the knob reads it: when it strikes, its leader and charge, its flickers, where its bolt leaves.</summary>
public readonly record struct KnobStrike(int K, double At, double Lead, double Charge, bool Triple, double ExitAngle)
{
    /// <summary>strike-engine.js pulseI (A2: str 1, never partial): the flickers' decaying pulses, at most 1.1.</summary>
    public double Pulse(double a) => Strike.Intensity(a, Triple);
}

/// <summary>
/// The knob's reading of the lightning, frame by frame (jewel-knob.html rawSignal + shape). The charge follows the build-up
/// and, when the strike lets go, eases back down over the 0.65 s settle (smoothstep); the flare is an envelope follower
/// (the strike's peak held 0.18 s, rising with a 20 ms and falling with a 150 ms time constant, through two stages).
/// </summary>
public sealed class KnobSignal
{
    /// <summary>The charge's settle, s.</summary>
    public const double Settle = 0.65;

    /// <summary>How long the flare's peak is held, s.</summary>
    public const double Hold = 0.18;

    private const double Attack = 0.02, Release = 0.15;

    private readonly KnobStrike[] strikes = new KnobStrike[8];
    private readonly double[] ages = new double[8];
    private int count;
    private double? envT;
    private double c, cFrom, cAt = -1, held, holdT = -1, y1, y2;
    private readonly Dictionary<int, double> exits = [];
    private StrikeGeometry? exitsFor;

    /// <summary>The exit angle the knob shows (radians, screen convention: 0 right, +pi/2 down); pi until a strike is seen.</summary>
    public double ExitAngle { get; private set; } = Math.PI;

    public double RawCharge { get; private set; }
    public double RawFlare { get; private set; }
    public double Charge { get; private set; }
    public double Flare { get; private set; }
    public KnobPhase Phase { get; private set; }
    public int? MixK { get; private set; }
    public bool? MixTwin { get; private set; }

    /// <summary>
    /// True when the shaped light has reached the raw one: the charge's settle is over, the flare's hold is over and both of
    /// its stages are within 1e-4 of it (the 1e-4 under which shape() reads a flare as 0). Read by the knob's loop under
    /// reduce motion, which idles only then (EffortSlider.LightMoves).
    /// </summary>
    public bool Settled => Charge == RawCharge && held == RawFlare && Math.Abs(y1 - held) < 1e-4 && Math.Abs(y2 - held) < 1e-4;

    private static double Ss(double a, double b, double x) { var u = Math.Clamp((x - a) / (b - a), 0, 1); return u * u * (3 - 2 * u); }

    /// <summary>part(B, a): one strike's charge, flare and phase <paramref name="a"/> s after it strikes.</summary>
    public static (double Charge, double Flare, KnobPhase Phase) Part(KnobStrike b, double a)
    {
        var lead = b.Lead;
        if (a >= 0) return (a < 0.2 ? 1 : 0, Math.Min(1, Math.Max(b.Pulse(a), 0.3 * Math.Exp(-a / 0.45))), a < 0.2 ? KnobPhase.Strike : a < 0.85 ? KnobPhase.Settle : KnobPhase.Rest);
        if (a >= -lead) return (1, 0.62 * Math.Exp(-(a + lead) / 0.06), KnobPhase.Strike);
        if (b.Charge == 0 || a < -lead - b.Charge) return (0, 0, KnobPhase.Rest);
        return (Math.Pow(Ss(0, 1, (a + lead + b.Charge) / b.Charge), 1.3), 0, KnobPhase.Charge);
    }

    /// <summary>
    /// The exit angle of strike <paramref name="k"/> (strike-engine.js orbGeo): its channel from where it leaves (g.ex, the
    /// orb's centre as orbGeo reads it), to the first vertex at or past the knob's radius less half a pixel.
    /// </summary>
    public static double ExitOf(StrikeGeometry g, int seed, int k)
    {
        // the channel's vertices as StrikeChannels.Main builds them (Strike.Channel), only as far as the rim
        double x0 = g.Knob, y0 = g.Middle, x1 = g.CapEnd, y1 = g.Middle + (Strike.Hash(k, unchecked(seed + 1)) - 0.5) * g.Height * 0.45;
        var s = unchecked(seed * 37 + k);
        double amp = g.Height * 0.34, step = 5 * g.K + 2, lo = g.Height * 0.14, hi = g.Height * 0.86, r = g.KnobRadius - 0.5;
        var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(x1 - x0) / step));
        double dx = 0, dy = 0;
        for (var i = 0; i <= n; i++)
        {
            var u = (double)i / n;
            var x = x0 + (x1 - x0) * u;
            var y = Math.Min(hi, Math.Max(lo, y0 + (y1 - y0) * u + Math.Pow(Math.Sin(Math.PI * u), 0.55) * amp * Strike.Fbm(x / g.K, s)));
            dx = x - g.Knob; dy = y - g.Middle;
            if (dx * dx + dy * dy >= r * r) break;
        }
        return Math.Atan2(dy, dx);
    }

    /// <summary>The charge before strike k's leader, s (the locked Mix's plan(): 0.14 + 0.12 hash(k, 13 S + 131)).</summary>
    public static double ChargeOf(int k, int seed) => 0.14 + 0.12 * Strike.Hash(k, unchecked(seed * 13 + 131));

    /// <summary>
    /// The page's orbList over the port's strikes: every strike whose orb window (charge, leader, strike and glow-out)
    /// holds <paramref name="t"/>, from k0 - 2 to k0 + 1. Returns how many; read them with <see cref="StrikeAt"/>.
    /// </summary>
    public int Strikes(StrikeGeometry g, int seed, double t)
    {
        if (exitsFor != g) { exits.Clear(); exitsFor = g; }
        count = 0;
        var k0 = (int)Math.Floor(t / Strike.Period);
        for (var k = k0 - 2; k <= k0 + 1; k++)
        {
            var at = Strike.StrikeAt(k, seed);
            var charge = ChargeOf(k, seed);
            var a = t - at;
            if (a < -Strike.Lead - charge || a > 1.9) continue;
            if (!exits.TryGetValue(k, out var exit))
            {
                if (exits.Count > 16) exits.Clear();
                exits[k] = exit = ExitOf(g, seed, k);
            }
            strikes[count] = new KnobStrike(k, at, Strike.Lead, charge, Strike.Triple(k, seed), exit);
            ages[count] = a;
            count++;
        }
        return count;
    }

    public (KnobStrike Strike, double Age) StrikeAt(int i) => (strikes[i], ages[i]);

    /// <summary>
    /// The page's signal: rawSignal over the strikes listed at <paramref name="t"/> (s, the strike's own clock; none when
    /// <paramref name="g"/> is null: no lightning), scaled by <paramref name="gain"/> (the lightning's own opacity), then
    /// shaped on <paramref name="clock"/> (s, the knob's clock, so the light lets go smoothly when the lightning stops). A
    /// jump back in that clock or a gap over 0.25 s starts the envelope again from the raw values.
    /// </summary>
    public void Read(StrikeGeometry? g, int seed, double t, double clock, double gain = 1)
    {
        double charge = 0, flare = 0;
        var phase = KnobPhase.Rest;
        double? phaseAt = null;
        var n = g is null ? 0 : Strikes(g.Value, seed, t);
        // orbAim(g, list, 0, 1): the exit angles averaged on the circle, each weighted by its envelope
        double sx = 0, sy = 0, latest = double.NegativeInfinity, th = ExitAngle;
        for (var i = 0; i < n; i++)
        {
            var (b, a) = (strikes[i], ages[i]);
            var (pc, pf, pp) = Part(b, a);
            if (pc > charge) charge = pc;
            if (pf > flare) flare = pf;
            if (pp > phase || pp == phase && phaseAt is { } was && b.At > was) { phase = pp; phaseAt = b.At; }
            var a0 = -b.Lead - b.Charge;
            var env = Ss(a0, a0 + 0.08, a);
            var wt = (env * Math.Exp(-Math.Max(0, a) / 0.5) + 1e-4) * (1 - Ss(1.7, 1.9, a));
            sx += wt * Math.Cos(b.ExitAngle); sy += wt * Math.Sin(b.ExitAngle);
            if (b.At > latest) { latest = b.At; th = b.ExitAngle; }
        }
        if (n > 0) ExitAngle = sx * sx + sy * sy > 1e-12 ? Math.Atan2(sy, sx) : th;
        charge *= gain; flare *= gain;
        Phase = phase;
        Shape(charge, flare, clock);
    }

    // P:1482-1504, E:714-738: the production Mix signal; legacy Read/Part stay unchanged.
    public void ReadMix(StrikeGeometry g, StrikePlans pool, double t, double clock, double gain = 1)
    {
        double charge = 0, flare = 0, sx = 0, sy = 0, latest = double.NegativeInfinity, th = ExitAngle;
        var phase = KnobPhase.Rest; double? phaseAt = null; var active = 0; MixK = null; MixTwin = null;
        var k0 = (int)Math.Floor(t / 2.4);
        for (var k = k0 - 2; k <= k0 + 1; k++)
        {
            var timing=pool.TimingFor(k);
            if(!Active(timing)&&!(timing.Event==2&&Active(pool.TimingFor(k,true))))continue;
            var p = pool.Get(g, k); ReadPlan(p); if (p.Twin is { } twin) ReadPlan(twin);
        }
        if (active > 0) ExitAngle = sx * sx + sy * sy > 1e-12 ? Math.Atan2(sy, sx) : th;
        Phase = phase; Shape(charge * gain, flare * gain, clock);

        bool Active(StrikeTiming timing) {var age=t-timing.At;return age>=-timing.Lead-timing.Charge&&age<=1.9;}

        void ReadPlan(StrikePlan plan)
        {
            var b = plan.Timing; var a = t - b.At; if (a < -b.Lead - b.Charge || a > 1.9) return; active++;
            double pc, pf; KnobPhase pp;
            if (a >= 0) { pc = a < 0.2 ? 1 : 0; pf = Math.Min(1, Math.Max(b.Pulse(a), 0.3 * Math.Exp(-a / 0.45))); pp = a < 0.2 ? KnobPhase.Strike : a < 0.85 ? KnobPhase.Settle : KnobPhase.Rest; }
            else if (b.Event == 3) { var q = Math.Clamp((a + b.Lead) / b.Lead, 0, 1); pc = 0.23 * q * q; pf = 0; pp = KnobPhase.Charge; }
            else if (a >= -b.Lead) { pc = 1; pf = 0.62 * Math.Exp(-(a + b.Lead) / 0.06); pp = KnobPhase.Strike; }
            else if (b.Charge == 0 || a < -b.Lead - b.Charge) { pc = pf = 0; pp = KnobPhase.Rest; }
            else { pc = Math.Pow(Strike.Ss(0, 1, (a + b.Lead + b.Charge) / b.Charge), 1.3); pf = 0; pp = KnobPhase.Charge; }
            if (pc > charge) charge = pc; if (pf > flare) flare = pf;
            if (pp > phase || pp == phase && phaseAt is { } previous && b.At > previous) { phase = pp; phaseAt = b.At; MixK = b.K; MixTwin = b.IsTwin; }
            var a0 = -b.Lead - b.Charge; var env = b.Event == 3 ? Strike.Ss(-b.Lead, 0, a) : Strike.Ss(a0, a0 + 0.08, a);
            var wt = (env * Math.Exp(-Math.Max(0, a) / 0.5) + 1e-4) * (1 - Strike.Ss(1.7, 1.9, a));
            sx += wt * Math.Cos(plan.ExitAngle); sy += wt * Math.Sin(plan.ExitAngle);
            if (b.At > latest) { latest = b.At; th = plan.ExitAngle; }
        }
    }

    // shape(st, t)
    private void Shape(double charge, double flare, double t)
    {
        RawCharge = charge; RawFlare = flare;
        var dt = envT is { } last ? t - last : 0;
        if (envT is null || dt < 0 || dt > 0.25) { c = charge; cAt = -1; held = flare; holdT = t; y1 = y2 = flare; }
        envT = t;
        if (charge >= c - 1e-4) { c = charge; cAt = -1; }
        else
        {
            if (cAt < 0) { cFrom = c; cAt = t; }
            c = Math.Max(charge, cFrom * (1 - Ss(0, 1, (t - cAt) / Settle)));
        }
        if (flare >= held) { held = flare; holdT = t; } else if (t - holdT > Hold) held = flare;
        double K(double target, double y) => 1 - Math.Exp(-dt / (target > y ? Attack : Release));
        y1 += (held - y1) * K(held, y1);
        y2 += (y1 - y2) * K(y1, y2);
        Charge = c; Flare = y2 < 1e-4 ? 0 : y2;
    }
}
