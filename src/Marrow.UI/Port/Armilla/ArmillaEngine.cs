// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "Moves", "Never clipped", "Reduce motion"; the canvas's DextraMark.Mark and its
// SlotMark, work/design/assistant/index.html lines 1179-1508 and 1655-1697; ED-NEW-dextra-as8-1). No Swift twin.
// One Armilla mark: a spring (damping 0.8) per pose value from the pose on screen to the next, the turns, the thinking scan on
// the shared clock, the voice level (60 ms attack, 240 ms release), the Astur nodes and the small form's busy band, the needs-you
// step-out and its backoff, the centre glyph that springs in from 45 % once when its state begins, and the drawing, which
// measures how far it reached (the bounds test reads MaxExtent). Pure: no Avalonia, no Skia, no clock of its own; the host
// ticks it with the shared clock's time and hands it a canvas.
namespace Marrow.UI.Port.Armilla;

/// <summary>What a mark shows: everything its owner sets. Immutable; a change is a new look.</summary>
public sealed record ArmillaLook
{
    public double Size { get; init; } = 24;

    public ArmillaState State { get; init; } = ArmillaState.Idle;

    public ArmillaGlyph Glyph { get; init; } = ArmillaGlyph.None;

    /// <summary>The Asturs on the orbit, 0 to 6 (more are not drawn). Rows carry none.</summary>
    public IReadOnlyList<ArmillaNode> Nodes { get; init; } = [];

    public bool Dark { get; init; } = true;
    // ED-NEW-dextra-open-8: a still pose may still ease its appearance palette on the compositor.
    public bool AnimateAppearance { get; init; }
    public bool ReduceAppearance { get; init; }

    /// <summary>One colour (the tray): white on dark, black on light.</summary>
    public bool Mono { get; init; }

    /// <summary>
    /// The tray's one colour keeps Needs you warm (ED-NEW-dextra-as8-1: the tray test of AS-6 and 2.5's "Only Needs you turns
    /// warm"); the canvas's mono mark drops it.
    /// </summary>
    public bool MonoKeepsWarm { get; init; }

    /// <summary>A row, card or decision mark: its idle pose does not breathe, so a settled row stops drawing.</summary>
    public bool Rest { get; init; }

    /// <summary>Reduce motion (the system's, or a list the motion budget stilled): each state shows its own still pose at once.</summary>
    public bool Reduce { get; init; }

    /// <summary>With no level fed, the voice poses follow a speech-like envelope (the canvas's autoVoice).</summary>
    public bool AutoVoice { get; init; } = true;

    public bool Equals(ArmillaLook? other) =>
        other is not null && Size == other.Size && State == other.State && Glyph == other.Glyph && Dark == other.Dark && Mono == other.Mono
        && MonoKeepsWarm == other.MonoKeepsWarm && Rest == other.Rest && Reduce == other.Reduce && AutoVoice == other.AutoVoice
        && AnimateAppearance == other.AnimateAppearance && ReduceAppearance == other.ReduceAppearance
        && Nodes.SequenceEqual(other.Nodes);

    public override int GetHashCode() => HashCode.Combine(Size, State, Glyph, Dark, Mono, Rest, Reduce, Nodes.Count);
}

internal sealed class ArmillaEngine
{
    private const double Deg = ArmillaPoses.Deg, Tau = ArmillaPoses.Tau, Z = ArmillaPoses.Damping;
    private const int Keys = (int)PoseKey.Count;

    private sealed class Sat
    {
        public int Idx;
        public double P, Vp, A, Va, B, Vb, Slot, Bt;
        public bool Alive;
        public ArmillaShape Shape;
        public ArmillaRgb? Tint;
    }

    private struct NodeDraw
    {
        public double X, Y, Gr, A, Ang, P;
        public ArmillaRgb Tint;
        public ArmillaShape Shape;
    }

    private readonly double[] p = new double[Keys];
    private readonly double[] v = new double[Keys];
    private double rho, vrho;
    private double phiM, phiI, phiS, phiT, vphiM, vphiI, vphiS, vphiT;
    private double thS, lvl, rmLvl, t;
    private double restBreathe, vRestBreathe, restWeight, vRestWeight;
    private double? extLevel;
    private double extAt = -9;
    private readonly List<Sat> sats = [];
    private double sp, vsp, band, vband, bandT;
    private double reachClock;
    private int reachN;
    private double glyphScale = 1, glyphVelocity;
    private readonly NodeDraw[] nodes = new NodeDraw[6];
    private readonly (double A, double G)[] gaps = new (double, double)[6];
    private double restKeyTime, restAccum;
    private ArmillaState? restKey;
    private bool snapPending = true;
    private static readonly Animation appearanceCurve = Animation.TimingCurve(.25, .1, .25, 1, .32);
    private double darkness, appearanceFrom, appearanceTo, appearanceElapsed = .32;

    public ArmillaEngine(ArmillaLook look)
    {
        Look = look;
        darkness = appearanceFrom = appearanceTo = look.Dark ? 1 : 0;
        State = look.State;
        var pose = Pose();
        Array.Copy(pose.V, p, Keys);
        rho = pose.Rho;
        vphiM = pose.Spin; vphiI = pose.InSpin; vphiS = pose.SatSpin; vphiT = pose.Park ? 0 : -.35;
        Sync(snap: true);
        sp = SpTarget();
        Dirty = true;
    }

    public ArmillaLook Look { get; private set; }

    public ArmillaState State { get; private set; }

    /// <summary>The shared clock's time of the last tick (s).</summary>
    public double Time => t;

    /// <summary>Something changed that the next draw must show.</summary>
    public bool Dirty { get; private set; }

    /// <summary>How far the last drawing reached, as a fraction of the half-box (the bounds test: under 1.0, 2.5 "Never clipped").</summary>
    public double MaxExtent { get; private set; }

    /// <summary>The glyph's spring scale (1 at rest; it springs in from 0.45 when its state begins).</summary>
    public double GlyphScale => glyphScale;

    /// <summary>The scan's phase (rad): every thinking mark on the shared clock has the same one.</summary>
    public double ScanPhase => thS;

    /// <summary>Where attention points now (deg).</summary>
    public double Attention => rho;

    /// <summary>The sprung value of a pose key now (tests).</summary>
    internal double Value(PoseKey key) => p[(int)key];

    private bool ReduceOn => Look.Reduce;

    private ArmillaPose Pose() => ArmillaPoses.Pose(State, Look.Size, Look.Rest);

    private int Tier => ArmillaPoses.TierOf(Look.Size);

    // ---- public ----

    public void Set(ArmillaLook next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var prev = Look;
        if (!next.AnimateAppearance || next.ReduceAppearance)
        {
            darkness = appearanceFrom = appearanceTo = next.Dark ? 1 : 0;
            appearanceElapsed = .32;
        }
        else if (next.Dark != prev.Dark)
        {
            appearanceFrom = darkness;
            appearanceTo = next.Dark ? 1 : 0;
            appearanceElapsed = 0;
        }
        Look = next;
        if (next.State != State)
        {
            State = next.State;
            Enter();
        }
        if (next.Glyph != prev.Glyph) SetGlyph(next.Glyph);
        if (next.Reduce != prev.Reduce) snapPending = true;
        Sync(snap: false);
        Dirty = true;
    }

    /// <summary>Whether the mark is on screen; a glyph that begins off screen is placed at full size (2.5).</summary>
    public bool Visible { get; set; } = true;

    // Window sleep is a sprung rest, not Reduce Motion or invisibility. Keep the last
    // drawing intact: neither entering nor leaving sleep resets any on-screen value.
    private bool sleeping, rejoining;
    internal bool Sleeping
    {
        get => sleeping;
        set { if (sleeping && !value) rejoining = true; sleeping = value; }
    }

    private static bool Cyclic(int key) => key is (int)PoseKey.Breathe or (int)PoseKey.ScanAmp or (int)PoseKey.Wob;

    private double Target(ArmillaPose pose, int key) => Sleeping && Cyclic(key) ? 0 : pose.V[key];

    /// <summary>A glyph change; it springs in from 45 % only where it is seen and motion is not reduced, and only then.</summary>
    private void SetGlyph(ArmillaGlyph glyph)
    {
        glyphScale = glyph == ArmillaGlyph.None || ReduceOn || !Visible ? 1 : .45;
        glyphVelocity = 0;
        Dirty = true;
    }

    /// <summary>The voice level 0..1 (the mic while listening, the speech while speaking); fresh for half a second.</summary>
    public void SetLevel(double level)
    {
        extLevel = Math.Clamp(double.IsFinite(level) ? level : 0, 0, 1);
        extAt = t;
    }

    /// <summary>
    /// The canvas's DXRest: a mark that has rested (Idle, Paused, Offline, motion not reduced) in one state for 1.2 s steps at
    /// 30 fps. Returns the time to step now, or 0 to skip this frame.
    /// </summary>
    public double RestStep(double dt)
    {
        ArmillaState? rest = !ReduceOn && State is ArmillaState.Idle or ArmillaState.Paused or ArmillaState.Offline ? State : null;
        if (rest is null) { restAccum = 0; restKeyTime = 0; restKey = null; return dt; }
        if (restKey != rest) { restKey = rest; restKeyTime = 0; restAccum = 0; }
        restKeyTime += dt;
        if (restKeyTime < 1.2) return dt;
        restAccum += dt;
        if (restAccum < 1.0 / 30 - .004) return 0;
        var d = restAccum;
        restAccum = 0;
        return d;
    }

    /// <summary>
    /// Advances the mark to the shared clock's time <paramref name="now"/> by <paramref name="dt"/> seconds. Returns whether it
    /// still moves (asks for another frame). With Reduce motion it snaps to the still pose; only a voice state keeps moving
    /// (its brightness follows the voice).
    /// </summary>
    public bool Tick(double dt, double now)
    {
        var appearanceMoving = appearanceElapsed < .32;
        if (appearanceMoving)
        {
            appearanceElapsed = Math.Min(.32, appearanceElapsed + Math.Max(0, dt));
            darkness = appearanceFrom + (appearanceTo - appearanceFrom) * appearanceCurve.Progress(appearanceElapsed);
            appearanceMoving = appearanceElapsed < .32;
            Dirty = true;
        }
        if (ReduceOn)
        {
            var pose = Pose();
            var voice = pose[PoseKey.VIn] > 0 || pose[PoseKey.VOut] > 0;
            if (Dirty || snapPending) { Snap(); snapPending = false; }
            if (Sleeping) return appearanceMoving;
            t = now;
            if (voice)
            {
                var target = extLevel is { } x && t - extAt < .5 ? x : Look.AutoVoice ? SynthVoice(t, pose[PoseKey.VIn] > 0 ? .37 : 1.91) : 0;
                rmLvl += (target - rmLvl) * (1 - Math.Exp(-dt / (target > rmLvl ? .06 : .24)));
                Dirty = true;
            }
            return voice || appearanceMoving;
        }
        snapPending = false;
        var more = false;
        if (Look.Glyph != ArmillaGlyph.None && (Math.Abs(glyphScale - 1) > 1e-3 || Math.Abs(glyphVelocity) > 1e-3))
        {
            glyphVelocity += (-196 * (glyphScale - 1) - 18.2 * glyphVelocity) * dt;
            glyphScale += glyphVelocity * dt;
            more = true;
        }
        Update(dt, now);
        Dirty = true;
        return !Settled() || more || appearanceMoving;
    }

    /// <summary>Draws the mark as it stands after the last tick (still when motion is reduced).</summary>
    public void Draw(IArmillaCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        Render(canvas, ReduceOn);
        Dirty = false;
    }

    /// <summary>Snaps to the still pose of the current state and draws it (the canvas's still()).</summary>
    public void DrawStill(IArmillaCanvas canvas)
    {
        Snap();
        Render(canvas, still: true);
        Dirty = false;
    }

    // ---- internals ----

    private List<ArmillaNode> Areas()
    {
        var list = Look.Nodes;
        var n = Math.Min(list.Count, 6);
        var areas = new List<ArmillaNode>(n);
        for (var i = 0; i < n; i++) areas.Add(list[i]);
        return areas;
    }

    private int AreaCount => Math.Min(Look.Nodes.Count, 6);

    private int BusyCount()
    {
        var n = 0;
        for (var i = 0; i < AreaCount; i++) if (Look.Nodes[i].Busy) n++;
        return n;
    }

    private int BusyEff() => ArmillaPoses.Pose(State, Look.Size, false)[PoseKey.BusyK] > .3 ? Math.Clamp(BusyCount(), 0, 6) : 0;

    private double SpTarget() => Tier <= 1 ? (Math.Min(BusyEff(), AreaCount) > 0 ? 1 : 0) : (AreaCount > 0 ? 1 : 0);

    private void Sync(bool snap)
    {
        var list = Areas();
        var n = list.Count;
        for (var i = 0; i < Math.Max(n, sats.Count); i++)
        {
            Sat st;
            if (i >= sats.Count)
            {
                st = new Sat { Idx = i, P = snap ? 1 : 0, A = 210 + i * 360.0 / Math.Max(1, n) };
                sats.Add(st);
            }
            else st = sats[i];
            st.Alive = i < n;
            if (i < n) { st.Shape = list[i].Shape; st.Tint = list[i].Tint; }
            st.Slot = i < n ? 210 + i * 360.0 / n : st.A;
            st.Bt = i < n && list[i].Busy ? 1 : 0;
            if (snap) { st.P = st.Alive ? 1 : 0; st.A = st.Slot; st.B = st.Bt; st.Vp = st.Va = st.Vb = 0; }
        }
        var be = Math.Min(BusyEff(), n);
        bandT = be switch { 0 => 0, 1 => 34, 2 => 62, 3 => 90, _ => 118 };
        if (snap) { band = bandT; vband = 0; sp = SpTarget(); }
    }

    private void Enter()
    {
        reachClock = 0; reachN = 0;
        if (ReduceOn) return; // Reduce motion: the new still pose shows at once
        var pose = Pose();
        if (pose.Reach > 0) { v[(int)PoseKey.Off2] += .9; v[(int)PoseKey.RingK] += .3; }
    }

    private void Snap()
    {
        var pose = Pose();
        Array.Copy(pose.V, p, Keys);
        Array.Clear(v);
        if (Sleeping && !ReduceOn)
            for (var k = 0; k < Keys; k++) p[k] = Target(pose, k);
        restWeight = Sleeping && !ReduceOn ? 1 : 0;
        restBreathe = Sleeping && !ReduceOn ? pose[PoseKey.Breathe] : 0;
        vRestWeight = vRestBreathe = 0;
        rho = pose.Rho; vrho = 0;
        phiM = phiI = phiS = phiT = 0;
        vphiM = vphiI = vphiS = vphiT = 0;
        thS = 0;
        lvl = pose[PoseKey.VIn] > 0 || pose[PoseKey.VOut] > 0 ? .45 : 0;
        sats.RemoveAll(s => !s.Alive);
        Sync(snap: true);
    }

    private static double Wrap180(double a) => ((a + 180) % 360 + 360) % 360 - 180;

    private static double Smooth(double a, double b, double x)
    {
        var u = Math.Clamp((x - a) / (b - a), 0, 1);
        return u * u * (3 - 2 * u);
    }

    /// <summary>The canvas's speech-like level, for demos and for speech output that gives no level.</summary>
    internal static double SynthVoice(double time, double seed)
    {
        var ph = ((time * 0.37 + seed) % 1 + 1) % 1;
        var gate = Smooth(0, .05, ph) * (1 - Smooth(.74, .84, ph));
        var syl = .5 + .5 * Math.Sin(Tau * 4.1 * time + 1.6 * Math.Sin(Tau * .71 * time + seed * 5));
        var amp = .62 + .38 * Math.Sin(Tau * .53 * time + seed * 3);
        return Math.Clamp(gate * (.22 + .78 * syl * syl) * amp, 0, 1);
    }

    private static void Spin(ref double phase, ref double velocity, double speed, double settle, double h)
    {
        if (speed != 0)
        {
            velocity += (speed - velocity) * (1 - Math.Exp(-h * 2));
            phase += velocity * h;
        }
        else if (settle > 0)
        {
            var target = Math.Round(phase / settle) * settle;
            const double w = 2.6, z = .92;
            velocity += (-w * w * (phase - target) - 2 * z * w * velocity) * h;
            phase += velocity * h;
        }
        else
        {
            velocity *= Math.Exp(-h * 2.5);
            phase += velocity * h;
        }
        if (Math.Abs(phase) > 7200) phase -= Math.Sign(phase) * 7200;
    }

    private static double Backoff(double baseSeconds, int n) => n <= 0 ? baseSeconds : n == 1 ? baseSeconds * 4 : n == 2 ? baseSeconds * 20 : double.PositiveInfinity;

    private static void RestSpring(ref double value, ref double velocity, double target, double h)
    {
        const double w = 2.6;
        velocity += (-w * w * (value - target) - 2 * Z * w * velocity) * h;
        value += velocity * h;
    }

    private void Update(double dt, double now)
    {
        t = now;
        var pose = Pose();
        var w = pose.Rate;
        var steps = Math.Max(1, (int)Math.Ceiling(dt * 240));
        var h = dt / steps;
        var nSat = Math.Max(1, AreaCount);
        var vIn = pose[PoseKey.VIn] > 0;
        var target = 0.0;
        if (Sleeping) target = vIn || pose[PoseKey.VOut] > 0 ? .45 : 0;
        else if (extLevel is { } x && t - extAt < .5) target = x;
        else if (Look.AutoVoice && (vIn || pose[PoseKey.VOut] > 0)) target = SynthVoice(t, vIn ? .37 : 1.91);
        lvl += (target - lvl) * (1 - Math.Exp(-dt / (target > lvl ? .06 : .24)));
        // a row or card steps out once when it begins needing you, then rests (ED-NEW-dextra-as8-1): the motion budget counts
        // no row mark that needs you, so a row that kept stepping out would move uncounted and never stop drawing
        if (!Sleeping && pose.Reach > 0 && !Look.Rest)
        {
            reachClock += dt;
            if (reachClock > Backoff(pose.Reach, reachN))
            {
                reachClock = 0; reachN++;
                v[(int)PoseKey.Off2] += .5; v[(int)PoseKey.RingK] += .18;
            }
        }
        for (var s = 0; s < steps; s++)
        {
            for (var k = 0; k < Keys; k++)
            {
                // The existing phase-parking spring's rate also eases cyclic motion
                // into/out of rest. Rejoining the shared phase must not amplify a step.
                var rate = (Sleeping || rejoining) && Cyclic(k) ? 2.6 : w;
                var a = -rate * rate * (p[k] - Target(pose, k)) - 2 * Z * rate * v[k];
                v[k] += a * h;
                p[k] += v[k] * h;
            }
            // Complement the retiring cyclic values with the SAME still drawing,
            // including Idle's fixed sway and voice's intrinsic still brightness.
            RestSpring(ref restBreathe, ref vRestBreathe, Sleeping ? pose[PoseKey.Breathe] : 0, h);
            RestSpring(ref restWeight, ref vRestWeight, Sleeping ? 1 : 0, h);
            var e = Wrap180(rho - pose.Rho);
            if (e > 179.5 || e < -179.5) e = -180; // a half turn always goes clockwise
            vrho += (-w * w * e - 2 * Z * w * vrho) * h;
            rho += vrho * h;
            Spin(ref phiM, ref vphiM, Sleeping ? 0 : pose.Spin, 360, h);
            Spin(ref phiI, ref vphiI, Sleeping ? 0 : pose.InSpin, 360, h);
            Spin(ref phiS, ref vphiS, Sleeping ? 0 : pose.SatSpin, Sleeping ? 360 : pose.Park ? 360.0 / nSat : 0, h);
            Spin(ref phiT, ref vphiT, Sleeping || pose.Park ? 0 : -.35, Sleeping ? 360 : pose.Park ? 10 : 0, h);
            foreach (var st in sats)
            {
                st.Vp += (-81 * (st.P - (st.Alive ? 1 : 0)) - 15.3 * st.Vp) * h; st.P += st.Vp * h;
                var ea = Wrap180(st.A - st.Slot); st.Va += (-36 * ea - 2 * .9 * 6 * st.Va) * h; st.A += st.Va * h;
                var eb = st.B - st.Bt; st.Vb += (-49 * eb - 14 * st.Vb) * h; st.B += st.Vb * h;
            }
            vsp += (-36 * (sp - SpTarget()) - 11.4 * vsp) * h; sp += vsp * h;
            vband += (-49 * (band - bandT) - 12.6 * vband) * h; band += vband * h;
        }
        sats.RemoveAll(st => !st.Alive && st.P <= .002);
        if (rejoining && Math.Abs(p[(int)PoseKey.Breathe] - pose[PoseKey.Breathe]) < 1e-3 &&
            Math.Abs(p[(int)PoseKey.ScanAmp] - pose[PoseKey.ScanAmp]) < 1e-3 && Math.Abs(p[(int)PoseKey.Wob] - pose[PoseKey.Wob]) < 1e-3)
            rejoining = false;
        // every thinking scan on the page sweeps in step, on the shared clock (the canvas's SlotMark)
        thS = Tau * .30 * t;
    }

    /// <summary>Nothing moves and nothing will until the look changes: the mark leaves the clock.</summary>
    public bool Settled()
    {
        if (appearanceElapsed < .32) return false;
        var pose = Pose();
        if (!Sleeping && (pose[PoseKey.Breathe] > 0 || pose.Spin != 0 || pose[PoseKey.ScanAmp] > 0 || pose[PoseKey.VIn] > 0 || pose[PoseKey.VOut] > 0 || (pose.Reach > 0 && !Look.Rest))) return false;
        if (!Sleeping && Tier >= 1 && pose.InSpin != 0) return false;
        if (!Sleeping && Tier >= 2 && !pose.Park) return false; // ticks and nodes drift
        for (var k = 0; k < Keys; k++) if (Math.Abs(p[k] - Target(pose, k)) > 1e-3 || Math.Abs(v[k]) > 1e-3) return false;
        if (Math.Abs(Wrap180(rho - pose.Rho)) > .01 || Math.Abs(vrho) > .01) return false;
        // a turn counts only where it is drawn (ED-NEW-dextra-as8-1): the inner ring from S up, the ticks and the nodes from M up.
        // The canvas checks every turn at every size, so its idle and small working rows never settle; their drift draws nothing.
        if (Math.Abs(vphiM) > .01 || (Tier >= 1 && Math.Abs(vphiI) > .01) || (Tier >= 2 && (Math.Abs(vphiS) > .01 || Math.Abs(vphiT) > .01))) return false;
        foreach (var st in sats) if (Math.Abs(st.Vp) > 1e-3 || Math.Abs(st.Va) > .01 || Math.Abs(st.Vb) > 1e-3) return false;
        if (Look.Glyph != ArmillaGlyph.None && (Math.Abs(glyphScale - 1) > 1e-3 || Math.Abs(glyphVelocity) > 1e-3)) return false;
        if (Math.Abs(restBreathe - (Sleeping ? pose[PoseKey.Breathe] : 0)) > 1e-3 || Math.Abs(vRestBreathe) > 1e-3 ||
            Math.Abs(restWeight - (Sleeping ? 1 : 0)) > 1e-3 || Math.Abs(vRestWeight) > 1e-3) return false;
        return Math.Abs(vsp) < 1e-3 && Math.Abs(vband) < 1e-3 && (!Sleeping || Math.Abs(lvl - (pose[PoseKey.VIn] > 0 || pose[PoseKey.VOut] > 0 ? .45 : 0)) < 1e-3);
    }

    // ---- drawing ----

    private double maxExt;

    private void Track(double r)
    {
        if (r > maxExt) maxExt = r;
    }

    private void Arc(IArmillaCanvas ctx, double r, double a0, double a1, double w, ArmillaRgb rgb, double alpha, bool round)
    {
        if (alpha <= .003 || w <= .02 || r <= 0) return;
        var len = a1 - a0;
        if (len <= .02) return;
        if (round)
        {
            var capDeg = w / 2 / r / Deg;
            if (len < 2 * capDeg)
            {
                // shorter than its own caps: shrink into nothing, never a dot that pops
                var k = len / (2 * capDeg);
                var m = (a0 + a1) / 2;
                var rr = w / 2 * k;
                ctx.FillCircle(Math.Cos((m - 90) * Deg) * r, Math.Sin((m - 90) * Deg) * r, rr, rgb, Math.Min(1, alpha));
                Track(r + rr);
                return;
            }
            a0 += capDeg; a1 -= capDeg;
            if (a1 - a0 < 8)
            {
                // very short arcs: a round-capped chord (tiny arcs render badly)
                double c0 = (a0 - 90) * Deg, c1 = (a1 - 90) * Deg;
                ctx.StrokeLine(Math.Cos(c0) * r, Math.Sin(c0) * r, Math.Cos(c1) * r, Math.Sin(c1) * r, w, rgb, Math.Min(1, alpha), roundCap: true);
                Track(r + w / 2);
                return;
            }
        }
        ctx.StrokeArc(r, a0, a1, w, rgb, Math.Min(1, alpha), round);
        Track(r + w / 2);
    }

    private void Render(IArmillaCanvas ctx, bool still)
    {
        maxExt = 0;
        var s = Look.Size;
        var hb = s / 2;
        var tier = Tier;
        var ti = ArmillaPoses.Tiers[tier];
        var mono = Look.Mono;
        var ink = mono ? ArmillaRgb.Mix(ArmillaPoses.MonoLight, ArmillaPoses.MonoDark, darkness)
            : ArmillaRgb.Mix(ArmillaPoses.InkLight, ArmillaPoses.InkDark, darkness);
        var warmC = mono && !Look.MonoKeepsWarm ? ink : ArmillaRgb.Mix(ArmillaPoses.WarmLight, ArmillaPoses.WarmDark, darkness);
        var glow = tier >= 2 && !mono ? darkness : 0;
        var glowOn = glow > 0;
        var baseW = ArmillaPoses.StrokeFor(s);
        var cap = ti.RoundCap;
        var level = lvl;
        double P(PoseKey k) => p[(int)k];
        var vIn = Math.Clamp(P(PoseKey.VIn), 0, 1);
        var vOut = Math.Clamp(P(PoseKey.VOut), 0, 1);
        var vv = vIn + vOut;
        var bt = still ? 0 : t;
        var voiceStill = still ? 1 : Math.Clamp(restWeight, 0, 1);
        var voiceBrightness = .8 + .3 * rmLvl;
        var al = Math.Clamp(P(PoseKey.Ink), 0, 1) * (still && vv > 0 ? voiceBrightness : 1 - voiceStill * Math.Clamp(vv, 0, 1) * (1 - voiceBrightness));
        var breathe = P(PoseKey.Breathe);
        var staticBreathe = still ? 0 : restBreathe;
        var breath = (tier >= 2 ? .012 : 0) * breathe * Math.Sin(Tau * bt / 7.4);
        var sway = (tier >= 2 ? 2.5 : 0) * (breathe * Math.Sin(Tau * bt / 13.7 + 1) + staticBreathe * Math.Sin(1));
        var r0 = hb * (ti.R0 + (ti.R1 - ti.R0) * Math.Clamp(sp, 0, 1.05)) * P(PoseKey.RingK) * (1 + breath);
        var attention = rho + phiM + sway;
        var scanOff = P(PoseKey.ScanAmp) * Math.Sin(thS);
        var cs = attention + scanOff;
        var l2Base = P(PoseKey.L2);
        var l1Base = P(PoseKey.L1);
        var w2Key = P(PoseKey.W2);
        // small sizes: the attention arc grows (taken from the long arc) so it never reads as a dot
        var xs = (tier == 0 ? .75 : tier == 1 ? .35 : 0) * Math.Max(0, l2Base) * Math.Clamp((l1Base - l2Base) / 100, 0, 1);
        // and never shorter than a few pixels of visible arc (round caps eat into it)
        var l2Min = tier <= 1 ? ((tier == 0 ? 2.6 : 3.4) + baseW * w2Key) / r0 / Deg * Math.Clamp(l2Base / 12, 0, 1) : 0;
        var l2 = Math.Max(l2Min, l2Base + xs + 40 * level * vv);
        var l1 = Math.Max(0, l1Base - xs + (10 * vIn + 28 * vOut) * level);
        var wLong = baseW * P(PoseKey.W) * (1 + .40 * vOut * level);
        var w2 = baseW * w2Key * (1 + (.25 * vIn + .45 * vOut) * level);
        // the long arc, kept clear of the attention arc by at least g degrees
        var off2 = P(PoseKey.Off2);
        var g = Math.Max(6, (tier <= 1 ? 1.3 : 2) / r0 / Deg) * (1 - Math.Clamp(Math.Abs(off2) / .06, 0, 1));
        var dl = P(PoseKey.Delta) - scanOff;
        var lo = Math.Max(dl - l1 / 2, l2 / 2 + g);
        var hi = Math.Min(dl + l1 / 2, 360 - l2 / 2 - g);
        var lStart = cs + lo;
        var lEnd = cs + hi;
        var warm = Math.Clamp(P(PoseKey.Warm), 0, 1);
        var shortRgb = ArmillaRgb.Mix(ink, warmC, warm);
        var xsBreath = tier <= 1 ? 1 - .1 * (breathe * (.5 + .5 * Math.Sin(Tau * bt / 8.2)) + staticBreathe * .5) : 1;

        // 1 core haze (dark, M and L): ambient light only, faint, wide and soft, so it never reads as a ball or a lens
        if (glowOn)
        {
            var ca = glow * .32 * al * (P(PoseKey.CoreA) + (.15 * vIn + .30 * vOut) * level + .03 * breathe * Math.Sin(Tau * bt / 11.3));
            if (ca > .002)
            {
                ctx.FillHaze(.9 * hb, ink, ca);
                Track(.9 * hb);
            }
        }
        // 2 tick bezel (M and L): the light of attention shows on it, at least 1.5 px clear of the box
        if (tier >= 2)
        {
            var count = tier == 3 ? 72 : 36;
            var ro = Math.Min(.97 * hb, hb - 1.5);
            var step = 360.0 / count;
            var lobeA = P(PoseKey.LobeA) + .35 * level * vv;
            var lobeW = Math.Max(4, P(PoseKey.LobeW) * (1 + .5 * level * vv));
            var tickA = P(PoseKey.TickA);
            for (var i = 0; i < count; i++)
            {
                var ang = i * step + phiT;
                var major = tier == 3 && i % 6 == 0;
                var d = Math.Abs(Wrap180(ang - cs));
                var lobe = lobeA * Math.Exp(-(d / lobeW) * (d / lobeW));
                var a = al * (tickA * (major ? 1.8 : 1) + lobe);
                if (a < .01) continue;
                var len = (major ? .06 : tier == 3 ? .035 : .05) * hb;
                var rad = (ang - 90) * Deg;
                double c = Math.Cos(rad), sn = Math.Sin(rad);
                var color = ArmillaRgb.Mix(ink, warmC, warm * (lobe / (lobe + tickA + 1e-4)));
                ctx.StrokeLine(c * ro, sn * ro, c * (ro - len), sn * (ro - len), 1, color, Math.Min(1, a), roundCap: false);
            }
            Track(ro + .5);
        }
        // 3 inner ring (S and up)
        if (tier >= 1)
        {
            var fr = Math.Clamp(P(PoseKey.Frac), 0, 1);
            ctx.Save();
            ctx.Translate(.035 * hb * fr, .025 * hb * fr);
            var ir = (P(PoseKey.InR) + (.06 * vIn + .03 * vOut) * level) * hb;
            var ia = P(PoseKey.InA) + (.35 * vIn + .25 * vOut) * level;
            var lw = tier == 3 ? 1.25 : 1;
            var inBase = P(PoseKey.InBase);
            if (tier >= 2 && inBase > .005)
            {
                ctx.StrokeArc(ir, 0, 360, 1, ink, Math.Min(1, al * inBase), roundCap: false);
                Track(ir + .5 + double.Hypot(.035, .025) * hb * fr);
            }
            var inLen = P(PoseKey.InLen);
            var il = Math.Clamp(inLen, 0, 360) * (tier == 1 ? Math.Clamp((330 - inLen) / 60, 0, 1) : 1); // S: no full inner ring (clutter)
            if (il > .5)
            {
                var c = attention + 180 + phiI;
                Arc(ctx, ir, c - il / 2, c + il / 2, lw, ink, al * ia, tier < 2);
            }
            ctx.Restore();
        }
        // 4 Astur nodes and their stems (M and L)
        var nodeCount = 0;
        var tr = (ti.Track + P(PoseKey.SatOut)) * hb;
        if (tier >= 2)
        {
            var busyK = Math.Clamp(P(PoseKey.BusyK), 0, 1);
            var satA = P(PoseKey.SatA);
            foreach (var st in sats)
            {
                if (st.P < .003 || nodeCount >= nodes.Length) continue;
                var i = st.Idx;
                var wob = still ? 0 : P(PoseKey.Wob);
                var ang = st.A + phiS + wob * 2.2 * Math.Sin(Tau * t / ArmillaPoses.WobblePeriods[i % 6] + i * 1.7);
                var rr = tr + wob * .006 * hb * Math.Sin(Tau * t / ArmillaPoses.WobblePeriods[(i + 3) % 6] + i);
                var be = Math.Clamp(st.B, 0, 1) * busyK;
                var gr = (tier == 3 ? .055 * hb : Math.Max(2.2, .065 * hb)) * (1 + .25 * be) * Math.Clamp(st.P, 0, 1.2);
                var tint = mono ? ink : st.Tint ?? ArmillaRgb.Mix(ArmillaPoses.Tint((int)st.Shape, false), ArmillaPoses.Tint((int)st.Shape, true), darkness); // each shape has its own tint (2.5)
                var rad = (ang - 90) * Deg;
                double c = Math.Cos(rad), sn = Math.Sin(rad);
                if (be > .01)
                {
                    // a short stem pointing back to Dextra: "working for you"
                    var r1 = rr - gr - (tier == 3 ? 3 : 2);
                    var rs = r1 - (tier == 3 ? .1 : .09) * hb * be;
                    ctx.StrokeLine(c * rs, sn * rs, c * r1, sn * r1, 1, tint, Math.Min(1, al * .55 * be * satA), roundCap: false);
                }
                nodes[nodeCount++] = new NodeDraw { X = c * rr, Y = sn * rr, Gr = gr, Tint = tint, A = al * satA * (.5 + .5 * be), Shape = st.Shape, Ang = ang, P = st.P };
                Track(rr + gr * 1.2);
            }
            // the orbit they ride: a hairline that parts around each node
            var ta = al * .085 * Math.Clamp(sp, 0, 1) * (.4 + .6 * satA);
            if (nodeCount > 0 && ta > .004)
            {
                for (var i = 0; i < nodeCount; i++)
                    gaps[i] = (((nodes[i].Ang % 360) + 360) % 360, (nodes[i].Gr + 3.5) / tr / Deg * Math.Clamp(nodes[i].P, 0, 1));
                // At most six entries. Array.Sort's comparer-to-delegate conversion allocated on every frame.
                for (var i = 1; i < nodeCount; i++)
                {
                    var next = gaps[i];
                    var j = i - 1;
                    while (j >= 0 && gaps[j].A > next.A) { gaps[j + 1] = gaps[j]; j--; }
                    gaps[j + 1] = next;
                }
                for (var i = 0; i < nodeCount; i++)
                {
                    var (ga, gg) = gaps[i];
                    var (na, ng) = gaps[(i + 1) % nodeCount];
                    var a0 = ga + gg;
                    var a1 = (i + 1 < nodeCount ? na : na + 360) - ng;
                    Arc(ctx, tr, a0, a1, 1, ink, ta, round: false);
                }
            }
        }
        // 5 long arc (with the crack of Offline)
        var frac = Math.Clamp(P(PoseKey.Frac), 0, 1);
        var aLong = al * P(PoseKey.LongA);
        var gO = tier == 3 ? 3.6 : 2.6;
        var gI = tier == 3 ? 2.1 : 1.7;
        void Glow(double r, double a0, double a1, double w, ArmillaRgb rgb, double a)
        {
            Arc(ctx, r, a0, a1, w * gO, rgb, a * .02 * glow, round: false);
            Arc(ctx, r, a0, a1, w * gI, rgb, a * .05 * glow, round: false);
        }
        void DrawLong(double r, double a0, double a1)
        {
            if (glowOn) Glow(r, a0, a1, wLong, ink, aLong);
            Arc(ctx, r, a0, a1, wLong, ink, aLong, cap);
        }
        // the voice thickens the long arc; its outer edge stays 1.5 px clear of the box
        var rl = Math.Min(r0, hb - 1.5 - wLong / 2);
        if (hi > lo)
        {
            if (frac < .005) DrawLong(rl, lStart, lEnd);
            else
            {
                // the crack: one half slips inward and back past the other end, like a split ring
                var mid = (lStart + lEnd) / 2;
                var gc = 12 * frac;
                DrawLong(rl, lStart, mid - gc / 2);
                ctx.Save();
                ctx.Rotate(-16 * frac);
                DrawLong(rl - (tier <= 1 ? .17 : .12) * hb * frac, mid + gc / 2, lEnd + 16 * frac);
                ctx.Restore();
            }
        }
        // 6 scan trail (thinking, M and L): the attention may step out, but its light never leaves the box
        var r2 = Math.Min(Math.Min(r0 + off2 * hb, .955 * hb - w2 * (.5 + glow * (tier == 3 ? 1.3 : .8))), hb - 1.5 - w2 / 2);
        var scanAmp = P(PoseKey.ScanAmp);
        if (tier >= 2 && scanAmp > 2 && !still)
        {
            for (var k = 1; k <= 4; k++)
            {
                var off = scanAmp * Math.Sin(thS - k * .2);
                var c = attention + off;
                var len = l2 * (1 - .1 * k);
                Arc(ctx, r2, c - len / 2, c + len / 2, w2 * (1 - .14 * k), shortRgb, al * .42 * Math.Pow(.55, k - 1) * Math.Clamp(scanAmp / 50, 0, 1), round: false);
            }
        }
        // 7 the attention arc
        if (glowOn) Glow(r2, cs - l2 / 2, cs + l2 / 2, w2, shortRgb, al * 1.6);
        Arc(ctx, r2, cs - l2 / 2, cs + l2 / 2, w2, shortRgb, al * xsBreath, cap);
        // 7b the presence point: a small still dot at the centre (about 6 % of the box; 1.2 px up to 24 px)
        // (with a centre glyph the point is not drawn: the canvas clears it with destination-out in its own canvas, which on a
        // shared composition surface would cut through the card beneath)
        var dot = (s <= 24 ? 1.2 : .03 * s) * (1 + .2 * level * vv * (1 - voiceStill));
        if (Look.Glyph == ArmillaGlyph.None) ctx.FillCircle(0, 0, dot, ink, Math.Min(1, al * .92));
        // 8 nodes on top
        for (var i = 0; i < nodeCount; i++)
        {
            var nd = nodes[i];
            ctx.FillShape(nd.Shape, nd.X, nd.Y, nd.Gr, nd.Tint, Math.Min(1, nd.A));
        }
        // 9 busy band (XS and S): a thin outer arc that grows back along the long arc from its end
        if (tier <= 1 && band > .5)
        {
            var bw = tier == 0 ? Math.Max(1.15, .06 * s + .25) : .055 * s + .3;
            var trb = Math.Min(ti.Track * hb, hb - 1.5 - bw / 2);
            var end = (hi > lo ? lEnd : attention + P(PoseKey.Delta) + 60) - 2;
            Arc(ctx, trb, end - band, end, bw, ink, al * .9, round: true);
        }
        // the centre glyph of Done, Stopping and Stopped, in place of the presence point (the canvas's SlotMark)
        if (Look.Glyph != ArmillaGlyph.None)
        {
            var unit = Math.Max(5, s * .3) * Math.Max(0, glyphScale);
            ctx.Glyph(Look.Glyph, unit, Math.Max(1.2, s * .06), ink, .92);
            Track(unit / 2 * Math.Sqrt(2) + Math.Max(1.2, s * .06) / 2);
        }
        MaxExtent = maxExt / hb;
    }

}
