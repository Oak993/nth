// PORT-ONLY: Calculi (ED-NEW-calculi-wu1-1; DESIGN.md sections 3, 5, 6, 7 and 8). The working beads' state machine, ported 1:1
// from reference/engine.js's Engine: inertialized hand-offs, the flow offset, the seeded picker (mulberry32, FNV-1a turn seed),
// the state routes, Reduce Motion and the shipped edges (pair A: Scriptio or Ortus in, Occasus out; error and stop; Rejoin).
// Time rule: state advances only in Update(now); every event carries its own time; Frame(t) only samples. So a replay with the
// same seed and event times is identical at 30, 60 or 240 Hz. After construction nothing here allocates.
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;
using static Marrow.UI.Port.Nth.Beads.NthBeadsMoves;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>The entrances the engine ships (pair A's Scriptio, and Ortus for the turns that do not write the n).</summary>
public enum NthBeadsEntrance : byte { Scriptio, Ortus }

/// <summary>The beads engine. One per glyph; not thread-safe (the host advances it on its animation slots).</summary>
public sealed class NthBeadsEngine
{
    /// <summary>The step thresholds in seconds since the last beat (section 7): under None nothing, under Full a Nodus.</summary>
    public const double StepNone = 1.6, StepFull = 4;
    /// <summary>The finite-difference step of the hand-off velocity estimate (1/480 s).</summary>
    public const double H = 1.0 / 480;

    private struct Offset
    {
        public double T0, Tau, Px, Py, Ps, Vx, Vy, Vs;
    }

    private struct Seq
    {
        public NthBeadsRecipe R0, R1, R2, R3;
        public int Count;

        public void Add(in NthBeadsRecipe r)
        {
            switch (Count)
            {
                case 0: R0 = r; break;
                case 1: R1 = r; break;
                case 2: R2 = r; break;
                case 3: R3 = r; break;
                default: throw new InvalidOperationException("a move sequence holds at most four moves");
            }
            Count++;
        }

        public readonly NthBeadsRecipe this[int i] => i switch { 0 => R0, 1 => R1, 2 => R2, _ => R3 };

        /// <summary>The sequence without its first move (reference <c>slice(1)</c>).</summary>
        public readonly Seq Tail()
        {
            var s = default(Seq);
            for (var i = 1; i < Count; i++) s.Add(this[i]);
            return s;
        }

        public NthBeadsRecipe Shift()
        {
            var first = R0;
            R0 = R1; R1 = R2; R2 = R3; R3 = default;
            Count--;
            return first;
        }

        public static Seq Of(in NthBeadsRecipe a) { var s = default(Seq); s.Add(a); return s; }
        public static Seq Of(in NthBeadsRecipe a, in NthBeadsRecipe b) { var s = Of(a); s.Add(b); return s; }
    }

    private static readonly NthBeadsMotif[] FConnecting = [NthBeadsMotif.Spiritus],
        FThinking = [NthBeadsMotif.Rota, NthBeadsMotif.Libra, NthBeadsMotif.Halitus],
        FTool = [NthBeadsMotif.Gradus, NthBeadsMotif.Machina, NthBeadsMotif.Clepsydra],
        FToolLong = [NthBeadsMotif.Clepsydra, NthBeadsMotif.Machina],
        FStreaming = [NthBeadsMotif.Scriptor, NthBeadsMotif.Textus, NthBeadsMotif.Linea],
        FWaiting = [NthBeadsMotif.Nuntius], FTactus = [NthBeadsMotif.Tactus],
        FHydra = [NthBeadsMotif.Concentus, NthBeadsMotif.Vicissim, NthBeadsMotif.Commutatio],
        FBackground = [NthBeadsMotif.Pulsus],
        QArcus = [NthBeadsMotif.QuiesArcus], QPedes = [NthBeadsMotif.QuiesPedes], QClavis = [NthBeadsMotif.QuiesClavis],
        QTripus = [NthBeadsMotif.QuiesTripus];

    private uint rng;
    private double clock, now, t0, lastBeat = -9, stepT, motifT0 = double.PositiveInfinity, dwell, exitAsked;
    private NthBeadsMode mode = NthBeadsMode.Gone;
    private NthBeadsState state = NthBeadsState.Thinking;
    private bool calm, hasMove, hasPlanned, mirror, dipped, hasFlow;
    private readonly bool quiet;
    private NthBeadsMove move;
    private NthBeadsRecipe planned;
    private Seq queue;
    private NthBeadsMotif motif = NthBeadsMotif.None, recent0 = NthBeadsMotif.None, recent1 = NthBeadsMotif.None;
    private int recentCount, cycles;
    private readonly NthBeadsMotif[]? only;
    private readonly int[] slotOf = [0, 1, 2];
    private readonly Offset[] off = new Offset[3];
    private Offset flow;
    private readonly NthBead[] cur = new NthBead[3], prv = new NthBead[3], pp = new NthBead[3], start = new NthBead[3], scratch = new NthBead[3];

    /// <summary>A turn's engine. <paramref name="quiet"/> runs the quiet family (small sites: one slow pulse, no beats, no
    /// steps); <paramref name="only"/> pins one motif (the canvas's motif demos).</summary>
    public NthBeadsEngine(uint seed, bool quiet = false, NthBeadsMotif only = NthBeadsMotif.None)
    {
        rng = seed;
        this.quiet = quiet;
        if (only != NthBeadsMotif.None) this.only = [only];
    }

    /// <summary>Raised when an exit ends (reference <c>gone</c>), with its time.</summary>
    public event Action<double>? Gone;
    /// <summary>Raised when a move begins, with its start time.</summary>
    public event Action<double>? MoveBegan;
    /// <summary>Raised once per Pausa when its dip reaches its smallest.</summary>
    public event Action? Dip;

    public NthBeadsMode Mode => mode;
    public NthBeadsState State => state;
    public bool Calm => calm;
    public bool Quiet => quiet;
    public double Clock => clock;
    public NthBeadsMotif Motif => motif;
    /// <summary>When the running exit was asked for.</summary>
    public double ExitAsked => exitAsked;
    /// <summary>The current move's start time.</summary>
    public double MoveStart => t0;
    /// <summary>True while a move is shown (between an entrance and the end of its exit).</summary>
    public bool HasMove => hasMove;
    /// <summary>The current move.</summary>
    public NthBeadsMoveId MoveId => hasMove ? move.Id : NthBeadsMoveId.None;
    /// <summary>Test readback: the current move, and the queued moves after it.</summary>
    internal NthBeadsMove CurrentMove => move;
    internal int QueueCount => queue.Count;
    internal NthBeadsRecipe Queued(int i) => queue[i];

    /// <summary>True when the formation is held still (nothing to draw until an event).</summary>
    public bool IsStill => !hasMove || (move.Kind == NthBeadsMoveKind.Still && !OffsetsLive(clock));

    /// <summary>The per-turn seed: FNV-1a of the turn id, as WorkingWords seeds by thread.</summary>
    public static uint SeedFrom(string turnId)
    {
        ArgumentNullException.ThrowIfNull(turnId);
        unchecked
        {
            var h = 0x811c9dc5u;
            foreach (var c in turnId) { h ^= c; h *= 0x01000193u; }
            return h;
        }
    }

    // mulberry32, bit for bit the reference's rngFrom
    private double Next()
    {
        unchecked
        {
            rng += 0x6D2B79F5u;
            var t = (rng ^ (rng >> 15)) * (1u | rng);
            t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    // ---------------- clock ----------------

    /// <summary>Advances the engine to <paramref name="time"/> (pacer slots only). Plans each move at its scheduled 50% point
    /// and hands off at its scheduled hand-off, whatever the update rate.</summary>
    public void Update(double time)
    {
        clock = time;
        var g = 0;
        while (hasMove && g++ < 60)
        {
            double hand = t0 + move.T * move.Handoff, pt = t0 + move.T * move.Handoff * .5;
            if (!hasPlanned && time >= pt && !move.Final)
            {
                now = pt;
                planned = Plan();
                hasPlanned = true;
            }
            if (time < hand) break;
            Advance(hand);
        }
        now = time;
        if (hasMove && !double.IsNaN(move.DipAt) && !dipped && time >= t0 + move.DipAt)
        {
            dipped = true;
            Dip?.Invoke();
        }
    }

    /// <summary>Advances by at most 1/20 s (the canvas's own tick).</summary>
    public void Tick(double dt) => Update(clock + Math.Min(dt, 1.0 / 20));

    private void Sync(double at)
    {
        if (at > clock) Update(at);
        now = clock;
    }

    private static void OffAt(in Offset o, double time, out double x, out double y, out double s, out bool live)
    {
        var t = Math.Max(0, time - o.T0);
        if (!(o.Tau > 0) || t >= o.Tau) { x = y = s = 0; live = false; return; }
        var q = 1 - t / o.Tau;
        double a = (1 + 2 * t / o.Tau) * q * q, b = t * q * q;
        x = o.Px * a + o.Vx * b;
        y = o.Py * a + o.Vy * b;
        s = o.Ps * a + o.Vs * b;
        live = true;
    }

    private bool OffsetsLive(double time)
    {
        for (var b = 0; b < 3; b++)
        {
            OffAt(off[b], time, out _, out _, out _, out var live);
            if (live) return true;
        }
        if (hasFlow) { OffAt(flow, time, out _, out _, out _, out var live); if (live) return true; }
        return false;
    }

    private void SampleAt(double time, NthBead[] output)
    {
        var lt = Clamp(time - t0, 0, move.T);
        for (var b = 0; b < 3; b++)
        {
            var r = At(in move, lt, slotOf[b]);
            OffAt(off[b], time, out var dx, out var dy, out var ds, out _);
            output[b] = new(r.X + dx, r.Y + dy, Math.Max(0, r.S + ds), r.O);
        }
    }

    /// <summary>Samples the beads at the engine's clock.</summary>
    public NthBeadsSample Frame() => Frame(clock);

    /// <summary>Samples the beads at <paramref name="time"/>: a pure read, it never changes state.</summary>
    public NthBeadsSample Frame(double time)
    {
        var f = default(NthBeadsSample);
        if (!hasMove) { f.Glint = -1; f.Mode = mode; f.State = state; return f; }
        var lt = Clamp(time - t0, 0, move.T);
        SampleAt(time, scratch);
        f.Present = true;
        f.B0 = scratch[0]; f.B1 = scratch[1]; f.B2 = scratch[2];
        f.Hidden = move.Hidden && lt < move.T - 1e-9;
        if (hasFlow)
        {
            OffAt(flow, time, out var fx, out var fy, out _, out _);
            f.FlowX = fx; f.FlowY = fy;
        }
        if (move.Id == NthBeadsMoveId.Scriptio)
        {
            if (lt < ScriptioWrite) { f.StrokeCount = 1; f.Stroke0From = 0; f.Stroke0To = WireLength * Write.Ease(lt / ScriptioWrite); }
            else
            {
                var v = BeadC.Ease((lt - ScriptioWrite) / (move.T - ScriptioWrite));
                f.StrokeCount = 3;
                ScriptioSegment(0, v, out f.Stroke0From, out f.Stroke0To);
                ScriptioSegment(1, v, out f.Stroke1From, out f.Stroke1To);
                ScriptioSegment(2, v, out f.Stroke2From, out f.Stroke2To);
            }
        }
        f.Glint = -1;
        f.Flight = move.Flight;
        f.Move = move.Id; f.Kind = move.Kind; f.Hub = move.HubOut; f.Motif = motif; f.State = state; f.Mode = mode;
        f.T = lt; f.Duration = move.T;
        return f;
    }

    // ---------------- hand-offs ----------------

    private void Advance(double at)
    {
        var old = move;
        SampleAt(at, cur);
        SampleAt(at - H, prv);
        SampleAt(at - 2 * H, pp);
        for (var b = 0; b < 3; b++) slotOf[b] = old.Perm(slotOf[b]);
        if (old.Final)
        {
            hasMove = false; mode = NthBeadsMode.Gone; hasPlanned = false; queue = default;
            Gone?.Invoke(at);
            return;
        }
        if (!hasPlanned) now = at;
        var f = hasPlanned ? planned : Plan();
        hasPlanned = false;
        Begin(f, at);
    }

    private void Begin(in NthBeadsRecipe f, double at)
    {
        for (var b = 0; b < 3; b++) start[slotOf[b]] = cur[b];
        var m = Build(f, start);
        if (m.Kind == NthBeadsMoveKind.Motif && mirror && !m.Mirrored && motif != NthBeadsMotif.None && Mirrors(motif)) NthBeadsMoves.Mirror(ref m);
        // inertialization: carry the gap in position and velocity (second-order estimates) and let it decay over tau
        for (var b = 0; b < 3; b++)
        {
            var k = slotOf[b];
            NthBead n0 = At(in m, 0, k), n1 = At(in m, H, k), n2 = At(in m, 2 * H, k), c = cur[b], p = prv[b], z = pp[b];
            off[b] = new Offset
            {
                T0 = at, Tau = m.Tau, Px = c.X - n0.X, Py = c.Y - n0.Y, Ps = c.S - n0.S,
                Vx = (3 * c.X - 4 * p.X + z.X) / (2 * H) - (-3 * n0.X + 4 * n1.X - n2.X) / (2 * H),
                Vy = (3 * c.Y - 4 * p.Y + z.Y) / (2 * H) - (-3 * n0.Y + 4 * n1.Y - n2.Y) / (2 * H),
                Vs = (3 * c.S - 4 * p.S + z.S) / (2 * H) - (-3 * n0.S + 4 * n1.S - n2.S) / (2 * H),
            };
        }
        // a large velocity gap into a motif or wire path is blended over a longer window, so a hub departure never kicks
        if (m.Kind is NthBeadsMoveKind.Motif or NthBeadsMoveKind.Trans)
        {
            var dv = double.NegativeInfinity;
            for (var b = 0; b < 3; b++) dv = Math.Max(dv, Hypot(off[b].Vx, off[b].Vy));
            var tau = Clamp(.2 + .12 * dv, m.Tau, .5);
            for (var b = 0; b < 3; b++) off[b].Tau = tau;
        }
        move = m; hasMove = true; dipped = false;
        t0 = at;
        OnBegin();
        MoveBegan?.Invoke(at);
    }

    private void Interrupt(in Seq seq)
    {
        var t = clock;
        SampleAt(t, cur);
        SampleAt(t - H, prv);
        SampleAt(t - 2 * H, pp);
        queue = seq.Tail();
        hasPlanned = false;
        Begin(seq[0], t);
    }

    private void OnBegin()
    {
        if (move.Kind == NthBeadsMoveKind.Motif && motif != NthBeadsMotif.None && move.Motif == motif)
        {
            if (cycles == 0) motifT0 = t0;
            cycles++;
        }
    }

    // ---------------- picker ----------------

    private void SetMotif(NthBeadsMotif n)
    {
        if (motif != NthBeadsMotif.None && motif != n)
        {
            recent1 = recentCount > 0 ? recent0 : NthBeadsMotif.None;
            recent0 = motif;
            recentCount = Math.Min(recentCount + 1, 2);
        }
        motif = n; cycles = 0; motifT0 = double.PositiveInfinity;
        dwell = 5 + Next() * 6;
        mirror = Mirrors(n) && Next() < .5;
    }

    private bool IsRecent(NthBeadsMotif n) => (recentCount > 0 && recent0 == n) || (recentCount > 1 && recent1 == n);

    private ReadOnlySpan<NthBeadsMotif> Family(NthBeadsState st)
    {
        if (only is not null) return only;
        if (st == NthBeadsState.Tool && now - stepT > 8) return quiet ? QPedes : FToolLong;
        if (quiet)
        {
            return st switch
            {
                NthBeadsState.Connecting => FConnecting,
                NthBeadsState.Tool or NthBeadsState.Streaming => QPedes,
                NthBeadsState.Waiting => FWaiting,
                NthBeadsState.Tactus => QClavis,
                NthBeadsState.Hydra => QTripus,
                NthBeadsState.Background => FBackground,
                _ => QArcus,
            };
        }
        return st switch
        {
            NthBeadsState.Connecting => FConnecting,
            NthBeadsState.Tool => FTool,
            NthBeadsState.Streaming => FStreaming,
            NthBeadsState.Waiting => FWaiting,
            NthBeadsState.Tactus => FTactus,
            NthBeadsState.Hydra => FHydra,
            NthBeadsState.Background => FBackground,
            _ => FThinking,
        };
    }

    private NthBeadsMotif PickIn(NthBeadsState st, NthBeadsMotif avoid = NthBeadsMotif.None)
    {
        var fam = Family(st);
        Span<NthBeadsMotif> pool = stackalloc NthBeadsMotif[4];
        var n = 0;
        foreach (var x in fam) if (x != avoid && !IsRecent(x)) pool[n++] = x;
        if (n == 0) foreach (var x in fam) if (x != avoid) pool[n++] = x;
        if (n == 0) foreach (var x in fam) pool[n++] = x;
        return pool[(int)Math.Floor(Next() * n)];
    }

    private NthBeadsRecipe CycleOf(NthBeadsMotif n)
    {
        var r = NthBeadsRecipe.Of(MoveOf(n), HubOf(n));
        r.Motif = n;
        r.Tempo = 1 + .08 * (Next() * 2 - 1);
        r.Pick = Next();
        r.Pick2 = Next();
        r.Va = Next() * 2 - 1;
        r.Vb = Next() * 2 - 1;
        r.Vc = Next() * 2 - 1;
        return r;
    }

    private static NthBeadsRecipe Collige(int dir) { var r = NthBeadsRecipe.Of(NthBeadsMoveId.Collige); r.Dir = dir; return r; }
    private static NthBeadsRecipe Of(NthBeadsMoveId id, NthBeadsHub hub = NthBeadsHub.None) => NthBeadsRecipe.Of(id, hub);
    private static NthBeadsRecipe Hold(NthBeadsMoveId id, double T) { var r = NthBeadsRecipe.Of(id); r.Arg = T; return r; }

    private Seq Locus(NthBeadsHub to)
    {
        var d = Next() < .5 ? 1 : -1;
        lastBeat = now;
        var s = Seq.Of(Collige(d), Of(NthBeadsMoveId.Pausa, to));
        if (to != NthBeadsHub.Punctum) s.Add(Of(NthBeadsMoveId.Emitte, to));
        return s;
    }

    private Seq Route(NthBeadsHub a, NthBeadsHub b)
    {
        if (a == b) return default;
        if (a == NthBeadsHub.Locus)
        {
            var s = Seq.Of(Of(NthBeadsMoveId.Pausa, b));
            if (b != NthBeadsHub.Punctum) s.Add(Of(NthBeadsMoveId.Emitte, b));
            return s;
        }
        var trans = TransFor(a, b);
        if (trans != NthBeadsMoveId.None) return Seq.Of(Of(trans));
        if (a == NthBeadsHub.Punctum) return Seq.Of(Of(NthBeadsMoveId.Pausa, b), Of(NthBeadsMoveId.Emitte, b));
        if (b != NthBeadsHub.Punctum && Next() < .5) return Seq.Of(Of(NthBeadsMoveId.Flexus, b));
        return Locus(b);
    }

    private static NthBeadsHub CalmHub(NthBeadsState s) => s switch
    {
        NthBeadsState.Connecting => NthBeadsHub.Punctum,
        NthBeadsState.Thinking => NthBeadsHub.Arcus,
        NthBeadsState.Waiting => NthBeadsHub.Signum,
        NthBeadsState.Tactus => NthBeadsHub.Clavis,
        NthBeadsState.Hydra => NthBeadsHub.Tripus,
        _ => NthBeadsHub.Pedes,
    };

    private NthBeadsRecipe First(in Seq r)
    {
        queue = r.Tail();
        return r[0];
    }

    private NthBeadsRecipe Plan()
    {
        if (queue.Count > 0) return queue.Shift();
        if (calm || motif == NthBeadsMotif.None)
        {
            var h = calm ? CalmHub(state) : (hasMove && HasPose(move.HubOut) ? move.HubOut : NthBeadsHub.Pedes);
            if (calm && hasMove && move.HubOut != h && move.Kind != NthBeadsMoveKind.Still) return Of(NthBeadsMoveId.Glide, h);
            return Of(NthBeadsMoveId.Still, h);
        }
        var hub = move.HubOut;
        if (only is null && cycles >= 2 && now - motifT0 > dwell)
        {
            var next = PickIn(state, motif);
            var nh = HubOf(next);
            SetMotif(next);
            if (nh == hub && Next() >= .3) return CycleOf(next);
            return First(nh == hub ? Locus(nh) : Route(hub, nh));
        }
        if (hub != HubOf(motif)) return First(Route(hub, HubOf(motif)));
        return CycleOf(motif);
    }

    private double Remaining() => t0 + move.T * move.Handoff - clock;

    private void CalmTo(NthBeadsHub hb)
    {
        var s = Seq.Of(Of(NthBeadsMoveId.Glide, hb), Of(NthBeadsMoveId.Still, hb));
        if (move.Kind == NthBeadsMoveKind.Enter) { queue = s; hasPlanned = false; return; }
        Interrupt(s);
    }

    private void Queue(in Seq s)
    {
        queue = s;
        hasPlanned = false;
    }

    // ---------------- events (each carries its own time) ----------------

    /// <summary>The turn's working state changed (section 6). Error and Stopped are set by <see cref="Fail"/> and <see cref="Stop"/>.</summary>
    public void SetState(NthBeadsState s, double at)
    {
        if (s is NthBeadsState.Error or NthBeadsState.Stopped) throw new ArgumentOutOfRangeException(nameof(s), s, "set by Fail and Stop");
        Sync(at);
        if (s == state) return;
        state = s;
        if (s == NthBeadsState.Tool) stepT = now;
        if (mode != NthBeadsMode.Running) return;
        if (calm)
        {
            motif = NthBeadsMotif.None;
            var hb = CalmHub(s);
            if (move.HubOut == hb && move.Kind == NthBeadsMoveKind.Still) return;
            CalmTo(hb);
            return;
        }
        var next = PickIn(s);
        SetMotif(next);
        var to = HubOf(next);
        if (move.Kind == NthBeadsMoveKind.Enter) { Queue(Route(move.HubOut, to)); return; }
        if (move.Id == NthBeadsMoveId.Collige) { Queue(Route(NthBeadsHub.Locus, to)); return; }
        if (move.Id == NthBeadsMoveId.Pausa)
        {
            Queue(to == NthBeadsHub.Punctum
                ? (move.HubOut == NthBeadsHub.Punctum ? default : Seq.Of(Of(NthBeadsMoveId.Pausa, NthBeadsHub.Punctum)))
                : Seq.Of(Of(NthBeadsMoveId.Emitte, to)));
            return;
        }
        if (move.Id is NthBeadsMoveId.Emitte or NthBeadsMoveId.Nodus or NthBeadsMoveId.Flexus) { Queue(Route(move.HubOut, to)); return; }
        bool same = move.HubOut == to, designed = TransFor(move.HubOut, to) != NthBeadsMoveId.None;
        if ((same || designed) && Remaining() <= .45) Queue(Route(move.HubOut, to));
        else if (same) Interrupt(Seq.Of(Of(NthBeadsMoveId.Flexus, to)));
        else Interrupt(Locus(to));
    }

    /// <summary>A new step (the activity text changed): a Locus beat, a Nodus, or nothing (section 7). True when it moves.</summary>
    public bool Step(double at)
    {
        Sync(at);
        if (mode != NthBeadsMode.Running) return false;
        stepT = now;
        if (calm || quiet || state is NthBeadsState.Connecting or NthBeadsState.Waiting) return false;
        if (move.Kind is NthBeadsMoveKind.Beat or NthBeadsMoveKind.Enter or NthBeadsMoveKind.Trans or NthBeadsMoveKind.Calm) return false;
        var since = now - lastBeat;
        if (since < StepNone) return false;
        var next = PickIn(state, motif);
        SetMotif(next);
        var hub = HubOf(next);
        Seq seq;
        if (since < StepFull) { lastBeat = now; seq = Seq.Of(Of(NthBeadsMoveId.Nodus, hub)); }
        else seq = Locus(hub);
        if (Remaining() <= .35) Queue(seq);
        else Interrupt(seq);
        return true;
    }

    /// <summary>Starts a turn. From nothing it plays the entrance; while the last turn is still leaving it plays Rejoin from
    /// wherever the beads are (section 5.2). <paramref name="seed"/> reseeds the picker (the turn's FNV seed).</summary>
    public void Enter(NthBeadsEntrance kind, double at, uint? seed = null)
    {
        Sync(at);
        if (mode == NthBeadsMode.Running) return;
        if (seed is { } s) rng = s;
        var was = mode;
        mode = NthBeadsMode.Running; lastBeat = -9; recentCount = 0; recent0 = recent1 = NthBeadsMotif.None; stepT = now;
        NthBeadsHub hb;
        if (calm) { hb = CalmHub(state); motif = NthBeadsMotif.None; }
        else { var mo = PickIn(state); SetMotif(mo); hb = HubOf(mo); }
        if (was == NthBeadsMode.Exiting && hasMove)
        {
            if (move.Id == NthBeadsMoveId.Scriptio)
            {
                Queue(calm ? Seq.Of(Of(NthBeadsMoveId.Glide, hb), Of(NthBeadsMoveId.Still, hb)) : Route(NthBeadsHub.Arcus, hb));
                return;
            }
            var rj = Of(NthBeadsMoveId.Rejoin, hb);
            rj.Calm = calm;
            rj.Fly = move.Flight;
            Interrupt(calm ? Seq.Of(rj, Of(NthBeadsMoveId.Still, hb)) : hb == NthBeadsHub.Punctum ? Seq.Of(rj) : Seq.Of(rj, Of(NthBeadsMoveId.Emitte, hb)));
            return;
        }
        slotOf[0] = 0; slotOf[1] = 1; slotOf[2] = 2;
        for (var b = 0; b < 3; b++) off[b] = new Offset { T0 = clock };
        hasPlanned = false;
        Seq seq;
        if (calm) seq = Seq.Of(Of(NthBeadsMoveId.Pop, hb), Of(NthBeadsMoveId.Still, hb));
        else if (kind == NthBeadsEntrance.Scriptio)
        {
            seq = Seq.Of(Of(NthBeadsMoveId.Scriptio));
            var r = Route(NthBeadsHub.Arcus, hb);
            for (var i = 0; i < r.Count; i++) seq.Add(r[i]);
        }
        else seq = hb == NthBeadsHub.Punctum ? Seq.Of(Of(NthBeadsMoveId.Ortus, hb)) : Seq.Of(Of(NthBeadsMoveId.Ortus, hb), Of(NthBeadsMoveId.Emitte, hb));
        queue = seq.Tail();
        move = Build(seq[0], []);
        hasMove = true; dipped = false;
        t0 = clock;
        OnBegin();
        MoveBegan?.Invoke(clock);
    }

    private bool Merged() => hasMove && move.Id is NthBeadsMoveId.Pausa or NthBeadsMoveId.Ortus or NthBeadsMoveId.Rejoin
        or NthBeadsMoveId.RejoinCalm or NthBeadsMoveId.HoldMerged or NthBeadsMoveId.HoldCalm or NthBeadsMoveId.Negatio;

    // An exit asked for during an entrance (Scriptio included) waits for the entrance's own hand-off, so a half-written stroke
    // is never cut. The host never waits for any of this: the UI follows the turn state at once; the exit is cosmetic.
    private void ExitWith(in Seq seq)
    {
        if (move.Kind == NthBeadsMoveKind.Enter || Remaining() <= .1) Queue(seq);
        else Interrupt(seq);
    }

    /// <summary>The turn finished: Occasus (gather, swell, shrink to nothing).</summary>
    public void Finish(double at)
    {
        Sync(at);
        if (mode != NthBeadsMode.Running) return;
        mode = NthBeadsMode.Exiting; exitAsked = now;
        var gather = !(Merged() && move.Kind != NthBeadsMoveKind.Enter);
        var dir = gather ? (Next() < .5 ? 1 : -1) : 0;
        if (move.Kind == NthBeadsMoveKind.Enter && move.HubOut == NthBeadsHub.Locus) gather = false;
        if (calm) { ExitWith(Seq.Of(Of(NthBeadsMoveId.ShrinkAway))); return; }
        ExitWith(gather ? Seq.Of(Collige(dir), Of(NthBeadsMoveId.Occasus)) : Seq.Of(Of(NthBeadsMoveId.Occasus)));
    }

    /// <summary>The turn failed: gather, shake no, hold about 0.8 s, set down (about 2 s; the UI never waits for it).</summary>
    public void Fail(double at)
    {
        Sync(at);
        if (mode != NthBeadsMode.Running) return;
        mode = NthBeadsMode.Exiting; exitAsked = now; state = NthBeadsState.Error;
        var skip = (move.Kind == NthBeadsMoveKind.Enter && move.HubOut == NthBeadsHub.Locus) || (Merged() && move.Kind != NthBeadsMoveKind.Enter);
        if (calm) { ExitWith(Seq.Of(Hold(NthBeadsMoveId.HoldCalm, 1.2), Of(NthBeadsMoveId.ShrinkAway))); return; }
        var s = default(Seq);
        if (!skip) s.Add(Collige(1));
        s.Add(Of(NthBeadsMoveId.Negatio));
        s.Add(Hold(NthBeadsMoveId.HoldMerged, .8));
        s.Add(Of(NthBeadsMoveId.SetDown));
        ExitWith(s);
    }

    /// <summary>The turn was stopped: a firm brake where the beads are, then set down, staggered (0.52 s).</summary>
    public void Stop(double at)
    {
        Sync(at);
        if (mode != NthBeadsMode.Running) return;
        mode = NthBeadsMode.Exiting; exitAsked = now; state = NthBeadsState.Stopped;
        var seq = calm ? Seq.Of(Of(NthBeadsMoveId.ShrinkAway)) : Seq.Of(Of(NthBeadsMoveId.Sistere), Of(NthBeadsMoveId.SetDown));
        if (move.Kind == NthBeadsMoveKind.Enter) Queue(seq);
        else Interrupt(seq);
    }

    /// <summary>The host is paused (hidden, minimised, scrolled away): an exit in progress jumps straight to gone.</summary>
    public bool Cut()
    {
        if (mode != NthBeadsMode.Exiting) return false;
        hasMove = false; mode = NthBeadsMode.Gone; queue = default; hasPlanned = false;
        Gone?.Invoke(clock);
        return true;
    }

    /// <summary>The glyph moved in layout (not scrolling) by (<paramref name="dx"/>, <paramref name="dy"/>) n units. The beads
    /// keep their place on screen and glide after it, position- and velocity-continuous (section 9).</summary>
    public void Reflow(double dx, double dy, double at)
    {
        Sync(at);
        var t = clock;
        double fx = 0, fy = 0, px0 = 0, py0 = 0;
        bool live = false, liveP = false;
        if (hasFlow)
        {
            OffAt(flow, t, out fx, out fy, out _, out live);
            OffAt(flow, t - H, out px0, out py0, out _, out liveP);
        }
        double px = (live ? fx : 0) - dx, py = (live ? fy : 0) - dy;
        double vx = live ? (fx - (liveP ? px0 : 0)) / H : 0, vy = live ? (fy - (liveP ? py0 : 0)) / H : 0;
        var mag = Hypot(px, py) / BeadWidth;
        flow = new Offset { T0 = t, Tau = Clamp(.2 + .04 * mag, .2, .45), Px = px, Py = py, Vx = vx, Vy = vy };
        hasFlow = true;
    }

    /// <summary>Reduce Motion turned on or off (section 8): one still formation per state, single slides between them.</summary>
    public void SetCalm(bool value, double at)
    {
        Sync(at);
        if (calm == value) return;
        calm = value;
        if (mode != NthBeadsMode.Running) return;
        if (value) { motif = NthBeadsMotif.None; CalmTo(CalmHub(state)); return; }
        var mo = PickIn(state);
        SetMotif(mo);
        var hb = HubOf(mo);
        if (move.Kind == NthBeadsMoveKind.Enter) { Queue(Route(move.HubOut, hb)); return; }
        var r = Route(move.HubOut, hb);
        Interrupt(r.Count > 0 ? r : Seq.Of(CycleOf(mo)));
    }
}
