// PORT-ONLY, owner-ruled addition (OD-0089; ED-251): the playground's frame clock (logic.js tick() and advance()) and the
// CSS transitions its registered properties run (gen.py REG: .6 s on the cubic-bezier(.25, .1, .25, 1) curve, the aura's
// intensity .9 s; the light groups' opacity .6 s in and .5 s out; a beam slot's .6 s cross-fade). Every motion is
// integrated on an eased time step, so a change of speed never jumps.

namespace Marrow.Core.Models.ComposerLights;

/// <summary>CSS timing functions.</summary>
public static class CssTiming
{
    /// <summary>The cubic-bezier timing function at progress x (x clamped to 0..1).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx, cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
        double X(double t) => ((ax * t + bx) * t + cx) * t;
        var u = x;
        for (var i = 0; i < 8; i++)
        {
            var fx = X(u) - x; var dx = (3 * ax * u + 2 * bx) * u + cx;
            if (Math.Abs(fx) < 1e-9 || Math.Abs(dx) < 1e-9) break;
            u -= fx / dx;
        }
        if (u < 0 || u > 1 || Math.Abs(X(u) - x) > 1e-7)
        {
            double lo = 0, hi = 1; u = x;
            for (var i = 0; i < 50; i++) { if (X(u) < x) lo = u; else hi = u; u = (lo + hi) / 2; }
        }
        return ((ay * u + by) * u + cy) * u;
    }

    /// <summary>CSS <c>ease</c>, cubic-bezier(.25, .1, .25, 1): the board's --ez2 and the keyword its fades use.</summary>
    public static double Ease(double x) => CubicBezier(0.25, 0.1, 0.25, 1, x);
}

/// <summary>
/// One transitioned number, as CSS Transitions Level 1 runs it: a change starts a transition from the current value; a
/// change back to where a running transition came from reverses it in proportionally less time (the reversing-adjusted
/// start value and the shortening factor). Duration 0 (or reduce motion) sets the value at once. The timing is CSS ease.
/// </summary>
public sealed class CssTransition
{
    private double from, to, adjustedStart, factor = 1, start, duration;

    public CssTransition(double value) { from = to = adjustedStart = value; }

    /// <summary>The value the transition is heading for.</summary>
    public double Target => to;

    /// <summary>True while the value is still moving at <paramref name="now"/>.</summary>
    public bool IsRunning(double now) => duration > 0 && now < start + duration;

    private double Progress(double now) => duration <= 0 ? 1 : Math.Clamp((now - start) / duration, 0, 1);

    /// <summary>The value at a time (seconds).</summary>
    public double Value(double now) => duration <= 0 ? to : from + (to - from) * CssTiming.Ease(Progress(now));

    /// <summary>Changes the target at <paramref name="now"/>; <paramref name="seconds"/> is the property's transition duration.</summary>
    public void Set(double target, double now, double seconds)
    {
        if (target == to) return;
        var current = Value(now);
        if (seconds <= 0)
        {
            from = to = adjustedStart = target; duration = 0; factor = 1;
            return;
        }
        if (IsRunning(now) && target == adjustedStart)
        {
            var output = CssTiming.Ease(Progress(now));
            var shortening = Math.Clamp(Math.Abs(output * factor + 1 - factor), 0, 1);
            adjustedStart = to;
            factor = shortening;
            duration = seconds * shortening;
        }
        else
        {
            adjustedStart = current;
            factor = 1;
            duration = seconds;
        }
        from = current; to = target; start = now;
    }

    /// <summary>Jumps to a value with no transition.</summary>
    public void Snap(double value) { from = to = adjustedStart = value; duration = 0; factor = 1; }
}

/// <summary>
/// The per-composer clock of the playground (advance()): the aura's eased turn and breathing, and Border beam's phases
/// (the beam angle, the 12 s and 8 s hue swings, Line's travel, breathing and spikes, the pulse oscillators and hue
/// wheels), each integrated on the eased time step and then put through the package's own curves.
/// </summary>
public sealed class ComposerLightClock
{
    // the global eased time scale (tick(): k eases to 1 over .22 s; reduce motion holds it at 0)
    public double K { get; private set; } = 1;
    public double Time { get; private set; }

    // the aura
    private double? rate;
    private double p, bp, amp;
    public double AuraAngle { get; private set; } = 10;   // --au-r, degrees
    public double AuraScale { get; private set; } = 1;    // --au-s

    // the beam
    private double ap, hs, hs8, lp, lbp, lsp, ls2p, tau;
    private double tm;
    public double BeamAngle { get; private set; }         // --bb-a, degrees
    public double HueSwing { get; private set; } = -1;    // --bb-hw
    public double HueSwing8 { get; private set; } = -1;   // --bb-hw8
    public double PulseHueInner { get; private set; }     // --bb-hpi
    public double PulseHueOutside { get; private set; }   // --bb-hpo
    public double ThemeMix => tm;                          // 0 dark, 1 light (the oscillator tables mix)
    public double LineX { get; private set; }
    public double LineW { get; private set; }
    public double LineH { get; private set; }
    public double LineEdge { get; private set; }
    public double LineSpike1 { get; private set; }
    public double LineSpike2 { get; private set; }
    public double WindowRadius { get; private set; } = double.NaN; // c.rr: the fitted windows' radius, eased over .22 s

    /// <summary>The 17 oscillators of Pulse Inner (pi) and Pulse Outside (po), mixed between the dark and light tables.</summary>
    public double[] PulseInner { get; } = new double[17];
    public double[] PulseOutside { get; } = new double[17];

    private static readonly double[][] InnerDark = BorderBeamMath.Oscillators(BeamType.PulseInner, true);
    private static readonly double[][] InnerLight = BorderBeamMath.Oscillators(BeamType.PulseInner, false);
    private static readonly double[][] OutsideDark = BorderBeamMath.Oscillators(BeamType.PulseOutside, true);
    private static readonly double[][] OutsideLight = BorderBeamMath.Oscillators(BeamType.PulseOutside, false);

    public ComposerLightClock(bool breathing, bool lightTheme)
    {
        amp = breathing ? 1 : 0;
        tm = lightTheme ? 1 : 0;
        Compute();
    }

    /// <summary>
    /// One frame (tick() then advance()): <paramref name="dt"/> is the frame's real step (capped at 50 ms), the eased step
    /// e = dt k moves every phase. <paramref name="auraSpeed"/> is seconds a turn, <paramref name="beamDuration"/> the
    /// beam's duration in effect, <paramref name="staticHue"/> holds the pulse hue wheels (they glide to a whole turn).
    /// </summary>
    public void Advance(double dt, bool reduceMotion, double auraSpeed, bool breathing, double beamDuration, bool staticHue, bool lightTheme, double windowRadius)
    {
        dt = Math.Min(0.05, Math.Max(0, dt));
        if (reduceMotion) K = 0;
        else
        {
            K += (1 - K) * (1 - Math.Exp(-dt / 0.22));
            if (Math.Abs(K - 1) < 0.002) K = 1;
        }
        var e = dt * K;
        Time += e;

        // the aura: the reference's speen (10 -> 190 -> 370 deg, eased per half), the rate easing to its target over .9 s
        var target = 1 / Math.Max(0.5, auraSpeed);
        rate = rate is { } r0 ? r0 + (target - r0) * (1 - Math.Exp(-dt / 0.9)) : target;
        p += e * rate.Value;
        amp += ((breathing ? 1 : 0) - amp) * (1 - Math.Exp(-dt / 0.4));
        bp += e / 4;

        // the beam
        var dur = double.IsFinite(beamDuration) ? Math.Max(0.3, beamDuration) : 1.96; // never a NaN into the integrated phases
        var (pB, pS, pS2) = BorderBeamMath.LinePeriods(dur);
        ap += e / dur; hs += e / 12; hs8 += e / 8;
        lp += e / dur; lbp += e / pB; lsp += e / pS; ls2p += e / pS2;
        static double ToWhole(double v, double dt) => v + (Math.Floor(v / 360 + 0.5) * 360 - v) * (1 - Math.Exp(-dt / 0.5)); // Math.round
        PulseHueInner = staticHue ? ToWhole(PulseHueInner, dt) : (PulseHueInner + e * 360 / 16) % 360;
        PulseHueOutside = staticHue ? ToWhole(PulseHueOutside, dt) : (PulseHueOutside + e * 360 / 14) % 360;
        tau += e * 2.3 / dur;
        tm += ((lightTheme ? 1 : 0) - tm) * (1 - Math.Exp(-dt / 0.3));
        WindowRadius = double.IsNaN(WindowRadius) ? windowRadius : WindowRadius + (windowRadius - WindowRadius) * (1 - Math.Exp(-dt / 0.22));
        Compute();
    }

    private void Compute()
    {
        var h = p * 2; var k = Math.Floor(h);
        AuraAngle = 10 + 180 * k + 180 * BorderBeamMath.Speen(h - k);
        AuraScale = 1 - 0.25 * amp * (1 - Math.Cos(2 * Math.PI * bp)) / 2;
        BeamAngle = BorderBeamMath.Frac(ap) * 360;
        HueSwing = BorderBeamMath.Swing(hs);
        HueSwing8 = BorderBeamMath.Swing(hs8);
        for (var i = 0; i < 17; i++)
        {
            PulseInner[i] = BorderBeamMath.OscillatorAt(InnerDark[i], tau) * (1 - tm) + BorderBeamMath.OscillatorAt(InnerLight[i], tau) * tm;
            PulseOutside[i] = BorderBeamMath.OscillatorAt(OutsideDark[i], tau) * (1 - tm) + BorderBeamMath.OscillatorAt(OutsideLight[i], tau) * tm;
        }
        LineX = BorderBeamMath.Keyframes(BorderBeamMath.TravelX, lp, false);
        LineW = BorderBeamMath.Keyframes(BorderBeamMath.TravelW, lp, false);
        LineEdge = BorderBeamMath.Keyframes(BorderBeamMath.Edge, lp, false);
        LineH = BorderBeamMath.Keyframes(BorderBeamMath.Breathe, lbp, true);
        LineSpike1 = BorderBeamMath.Keyframes(BorderBeamMath.SpikeA, lsp, true);
        LineSpike2 = BorderBeamMath.Keyframes(BorderBeamMath.SpikeB, ls2p, true);
    }

    /// <summary>The test hook of the playground (window.__bbT): every beam phase at absolute seconds T.</summary>
    public void SetBeamTime(double seconds, double hueSeconds, double beamDuration, bool staticHue, bool lightTheme)
    {
        var dur = double.IsFinite(beamDuration) ? Math.Max(0.3, beamDuration) : 1.96; // never a NaN into the integrated phases
        var (pB, pS, pS2) = BorderBeamMath.LinePeriods(dur);
        ap = seconds / dur; hs = hueSeconds / 12; hs8 = hueSeconds / 8; lp = seconds / dur; lbp = seconds / pB; lsp = seconds / pS; ls2p = seconds / pS2;
        tau = seconds * 2.3 / dur;
        PulseHueInner = staticHue ? 0 : BorderBeamMath.Frac(hueSeconds / 16) * 360;
        PulseHueOutside = staticHue ? 0 : BorderBeamMath.Frac(hueSeconds / 14) * 360;
        tm = lightTheme ? 1 : 0;
        Compute();
    }

    /// <summary>Sets the aura's phase directly (tests and stills): turns since the start and the breathing's phase.</summary>
    public void SetAuraPhase(double turns, double breathPhase, bool breathing)
    {
        p = turns; bp = breathPhase; amp = breathing ? 1 : 0;
        Compute();
    }
}
