// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2; ED-NEW-lightv2-2): the shader lights over time, as the approved
// page runs one study (index.html step(), settle()): the state's energy, work and rate easing between Idle, Typing and Working
// (.45 s, .45 s, .7 s), the keystroke lift near the caret (+.75 a key, capped at 1, decaying over .16 s), and each light's loop
// phase advanced by its speed and the rate (24 s at speed 1 and rate 1; every term a whole harmonic, so it wraps seamlessly).
// Reduce Motion is the page's settle(): every value at its state's target, no lift, and each light at its still phase. Each
// light also fades in and out with its style (.6 s in, .5 s out, as the other lights), and its spread, intensity, speed and
// colours transition (.6 s, OD-0076) where the page sets them at once. v3's and v4's pulses (uPulse, uPulseA: four rings
// born at a state change, amplitude 1, and at a keystroke once the last is .65 s old, amplitude .3, each aging at the light's
// speed; Reduce Motion's still holds the page's settle() rings) and the Text and light settings (ED-NEW-lightv2-5: the modes
// cross over in .2 s, the two sliders transition as the others) are here too.

namespace Marrow.Core.Models.ComposerLights;

/// <summary>What one shader light draws in one frame (the page's uniforms).</summary>
public sealed class ShaderLightFrame(ShaderLightDef def)
{
    public ShaderLightDef Def { get; } = def;
    public double Alpha;
    public double Loop;
    public double Energy, Work, Key, Caret;
    public double Spread, Intensity;
    public double W, H, Radius;
    /// <summary>The pack's four colours, linear, r g b each (uPal).</summary>
    public readonly float[] Palette = new float[12];
    /// <summary>Their mean (uAvg).</summary>
    public readonly float[] Average = new float[3];
    /// <summary>The four pulses' ages in seconds and amplitudes (uPulse, uPulseA; v3, v4).</summary>
    public readonly float[] Pulse = new float[4], PulseAmp = new float[4];
    /// <summary>Text and light: the three modes' weights (text above light, light above text, difference), Light behind text, Text halo.</summary>
    public readonly float[] Modes = new float[3];
    public double Behind, Halo;
    /// <summary>True in the dark appearance: the chat text is lighter than what lies behind it.</summary>
    public bool Dark;
    /// <summary>
    /// The readability guard's hold (ED-NEW-lightv2-8): how much brighter the light was within the last 150 ms than it is now
    /// (1: not brighter). The guard follows a brighter light at once and lets go of a dimmer one over that.
    /// </summary>
    public double GuardLift = 1;

    /// <summary>How far this frame's light reaches outside the composer (px): the band it shades.</summary>
    public double Reach => Math.Max(Def.Reach * Spread, Def.ReachPx) + Def.ReachHalfWidth * W / 2 + 2;
}

/// <summary>The shader lights of one composer, frame by frame.</summary>
public sealed class ShaderLightAnimator
{
    // the page's STATES: energy, rate, work
    private static (double E, double Rate, double W) Target(ComposerLightPhase phase) => phase switch
    {
        ComposerLightPhase.Typing => (.55, 1, 0),
        ComposerLightPhase.Running => (1, 1.9, 1),
        _ => (0, .55, 0),
    };

    private sealed class Slot(ShaderLightDef def)
    {
        public readonly ShaderLightDef Def = def;
        public readonly ShaderLightFrame Frame = new(def);
        public CssTransition Alpha = new(0);
        public CssTransition Spread = new(def.Spread), Intensity = new(1), Speed = new(1);
        public readonly CssTransition[] Colour = new CssTransition[12];
        public double Phase; // loops advanced since the still (the page's loop - still)
        public double Peak;  // the light's brightness held for the readability guard, letting go over GuardSeconds
        public readonly double[] Pulse = [99, 99, 99, 99], PulseAmp = [0, 0, 0, 0]; // the page's pa, pm

        public void Push(double amp)
        {
            for (var i = 3; i > 0; i--) { Pulse[i] = Pulse[i - 1]; PulseAmp[i] = PulseAmp[i - 1]; }
            Pulse[0] = 0; PulseAmp[0] = amp;
        }

        public void SettlePulses() { (Pulse[0], Pulse[1], Pulse[2], Pulse[3]) = (.55, 1.25, 1.95, 99); (PulseAmp[0], PulseAmp[1], PulseAmp[2], PulseAmp[3]) = (1, .7, .5, 0); }
    }

    /// <summary>The Text and light modes' cross-over (s).</summary>
    public const double ModeSeconds = .2;

    /// <summary>How long the readability guard takes to let go of a light that has dimmed (s; ED-NEW-lightv2-8).</summary>
    public const double GuardSeconds = .15;

    // the light's brightness as the v4 page's updateHalo() reckons it: intensity, energy and the keystroke lift
    private double Brightness(Slot s, double now) => s.Intensity.Value(now) * (.38 + .82 * energy) * (1 + .3 * key);

    private readonly Slot[] slots;
    private ComposerLightSettings settings;
    private ComposerLightPhase phase;
    private ComposerLightPalette palette;
    private bool dark, reduce;
    private double energy, work, rate, key, caret;
    private readonly CssTransition[] modes = new CssTransition[3];
    private CssTransition behind, halo;

    /// <summary>The frames drawn now, and how many.</summary>
    public ShaderLightFrame[] Drawn { get; }
    public int DrawnCount { get; private set; }

    public double Energy => energy;
    public double Work => work;
    public double Rate => rate;
    public double KeyLift => key;

    public ShaderLightAnimator(ComposerLightSettings settings, ComposerLightPhase phase, bool dark, bool reduceMotion, ComposerLightPalette palette)
    {
        this.settings = settings; this.phase = phase; this.dark = dark; reduce = reduceMotion; this.palette = palette;
        slots = ShaderLightCatalog.All.Select(d => new Slot(d)).ToArray();
        Drawn = new ShaderLightFrame[slots.Length];
        var t = Target(phase);
        (energy, rate, work) = (t.E, t.Rate, t.W);
        for (var i = 0; i < 3; i++) modes[i] = new CssTransition((int)settings.TextMode == i ? 1 : 0);
        behind = new CssTransition(settings.TextBehind);
        halo = new CssTransition(settings.TextHalo);
        foreach (var s in slots)
        {
            if (reduce) s.SettlePulses();
            var o = settings.ShaderOptions(s.Def.Id);
            s.Alpha = new CssTransition(settings.ActiveShader?.Id == s.Def.Id ? 1 : 0);
            s.Spread = new CssTransition(o.Spread); s.Intensity = new CssTransition(o.Intensity); s.Speed = new CssTransition(o.Speed);
            var c = Colours(o);
            for (var i = 0; i < 12; i++) s.Colour[i] = new CssTransition(c[i]);
        }
    }

    private double[] Colours(ShaderLightOptions o)
    {
        var hex = ShaderLightCatalog.Colours(o, dark ? palette.AccentDark : palette.AccentLight);
        var v = new double[12];
        for (var i = 0; i < 4; i++) { var (r, g, b) = ShaderLightCatalog.Linear(hex[i]); v[i * 3] = r; v[i * 3 + 1] = g; v[i * 3 + 2] = b; }
        return v;
    }

    /// <summary>New settings, state, theme, accent or reduce motion at <paramref name="now"/>: every value transitions to them.</summary>
    public void Retarget(ComposerLightSettings next, ComposerLightPhase nextPhase, bool nextDark, ComposerLightPalette nextPalette, bool nextReduce, double now)
    {
        var stateChanged = nextPhase != phase;
        (settings, phase, dark, palette, reduce) = (next, nextPhase, nextDark, nextPalette, nextReduce);
        var p = reduce ? 0 : ComposerLightAnimator.PropertySeconds;
        for (var i = 0; i < 3; i++) modes[i].Set((int)settings.TextMode == i ? 1 : 0, now, reduce ? 0 : ModeSeconds);
        behind.Set(settings.TextBehind, now, p);
        halo.Set(settings.TextHalo, now, p);
        if (stateChanged && !reduce) foreach (var s in slots) s.Push(1); // the page's applyEff(): a ring at each state change
        foreach (var s in slots)
        {
            var on = settings.ActiveShader?.Id == s.Def.Id;
            s.Alpha.Set(on ? 1 : 0, now, reduce ? 0 : on ? ComposerLightAnimator.FadeInSeconds : ComposerLightAnimator.FadeOutSeconds);
            var o = settings.ShaderOptions(s.Def.Id);
            s.Spread.Set(o.Spread, now, p); s.Intensity.Set(o.Intensity, now, p); s.Speed.Set(o.Speed, now, p);
            var c = Colours(o);
            for (var i = 0; i < 12; i++) s.Colour[i].Set(c[i], now, p);
        }
        if (reduce) Settle();
    }

    // the page's settle(): the state's values at once, no lift, every light at its still
    private void Settle()
    {
        var t = Target(phase);
        (energy, rate, work, key) = (t.E, t.Rate, t.W, 0);
        foreach (var s in slots) { s.Phase = 0; s.SettlePulses(); s.Peak = 0; }
    }

    /// <summary>A keystroke at <paramref name="caretX"/> (composer px from its left): the page's lift, +.75 capped at 1 (none under reduce motion).</summary>
    public void Keystroke(double caretX)
    {
        caret = caretX;
        if (reduce) return;
        key = Math.Min(1, key + .75);
        foreach (var s in slots) if (s.Pulse[0] > .65) s.Push(.3); // the page's step(): a small ring at a keystroke
    }

    /// <summary>Where the caret is (composer px from its left), without a keystroke.</summary>
    public void SetCaret(double caretX) => caret = caretX;

    /// <summary>One frame: <paramref name="dt"/> the real step (capped at 50 ms as the page does); the loops move by <paramref name="dt"/> x <paramref name="motionScale"/>.</summary>
    public void Advance(double dt, double motionScale, double now)
    {
        dt = Math.Min(.05, Math.Max(0, dt));
        if (reduce) { Settle(); return; }
        var t = Target(phase);
        var k = 1 - Math.Exp(-dt / .45);
        energy += (t.E - energy) * k;
        work += (t.W - work) * k;
        rate += (t.Rate - rate) * (1 - Math.Exp(-dt / .7));
        key *= Math.Exp(-dt / .16);
        foreach (var s in slots)
            if (s.Alpha.Target > 0 || s.Alpha.Value(now) > 0)
            {
                var speed = s.Speed.Value(now);
                s.Phase = (s.Phase + dt * motionScale * speed * rate / ShaderLightCatalog.LoopSeconds) % 1;
                for (var i = 0; i < 4; i++) s.Pulse[i] = Math.Min(99, s.Pulse[i] + dt * motionScale * speed);
                var b = Brightness(s, now);
                s.Peak = b >= s.Peak ? b : b + (s.Peak - b) * Math.Exp(-dt / GuardSeconds);
            }
    }

    /// <summary>Fills <see cref="Drawn"/> at <paramref name="now"/> for a composer <paramref name="w"/> x <paramref name="h"/>.</summary>
    public void Produce(double now, double w, double h, double radius)
    {
        DrawnCount = 0;
        foreach (var s in slots)
        {
            var f = s.Frame;
            f.Alpha = s.Alpha.Value(now);
            if (f.Alpha <= .0005) { f.Alpha = 0; continue; }
            f.Loop = (s.Def.Still + s.Phase) % 1;
            f.Energy = energy; f.Work = work; f.Key = key; f.Caret = caret;
            f.Spread = s.Spread.Value(now); f.Intensity = s.Intensity.Value(now);
            f.W = w; f.H = h; f.Radius = Math.Min(radius, Math.Min(w, h) / 2);
            double ar = 0, ag = 0, ab = 0;
            for (var i = 0; i < 4; i++)
            {
                double r = s.Colour[i * 3].Value(now), g = s.Colour[i * 3 + 1].Value(now), b = s.Colour[i * 3 + 2].Value(now);
                f.Palette[i * 3] = (float)r; f.Palette[i * 3 + 1] = (float)g; f.Palette[i * 3 + 2] = (float)b;
                ar += r; ag += g; ab += b;
            }
            f.Average[0] = (float)(ar / 4); f.Average[1] = (float)(ag / 4); f.Average[2] = (float)(ab / 4);
            for (var i = 0; i < 4; i++) { f.Pulse[i] = (float)s.Pulse[i]; f.PulseAmp[i] = (float)s.PulseAmp[i]; }
            for (var i = 0; i < 3; i++) f.Modes[i] = (float)modes[i].Value(now);
            f.Behind = behind.Value(now); f.Halo = halo.Value(now); f.Dark = dark;
            var bright = Brightness(s, now);
            f.GuardLift = reduce || bright <= 1e-6 ? 1 : Math.Max(1, s.Peak / bright);
            Drawn[DrawnCount++] = f;
        }
    }

    /// <summary>True while a light fades in or out.</summary>
    public bool Fading(double now) => slots.Any(s => s.Alpha.IsRunning(now)) || modes.Any(m => m.IsRunning(now)) || behind.IsRunning(now) || halo.IsRunning(now);

    /// <summary>The loop phase light <paramref name="id"/> is at (tests).</summary>
    public double LoopOf(string id) => slots.First(s => s.Def.Id == id) is var s ? (s.Def.Still + s.Phase) % 1 : 0;
}
