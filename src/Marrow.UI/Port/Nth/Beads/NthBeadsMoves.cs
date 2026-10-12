// PORT-ONLY: Calculi (ED-NEW-calculi-wu1-1; DESIGN.md sections 4 and 5). The move library ported 1:1 from
// reference/engine.js: motifs (MOT), designed wire paths (TRANS), generic moves, the shipped entrances (Scriptio, Ortus, the
// Reduce Motion pop, Rejoin) and exits (Occasus, error, stop). A move is a value: a recipe (what the planner queues) is built
// into an NthBeadsMove from the beads' start pose, and At() is a pure function of local time and slot. Nothing allocates.
// Pair A only (ED-NEW-calculi-wu1-2): Condensatio, Recessus and Reditus are not ported.
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>Every move (reference names in <see cref="NthBeadsMoves.NameOf"/>).</summary>
public enum NthBeadsMoveId : byte
{
    None,
    // motifs
    Rota, Libra, Halitus, Gradus, Machina, Clepsydra, Scriptor, Textus, Linea, Nuntius, Tactus, Concentus, Vicissim, Commutatio,
    Pulsus, Spiritus, Quies,
    // designed wire paths
    Descensus, Ascensus, Flick, Reflick, Toss, Catch, Levo, Demitto, Statio, Fornix, Surgo, Cado,
    // generic moves and beats
    Collige, Pausa, Emitte, Nodus, Flexus, Glide, Still, Sistere, Negatio, HoldMerged, HoldCalm,
    // entrances
    Ortus, Scriptio, Pop, Rejoin, RejoinCalm,
    // exits
    Occasus, SetDown, ShrinkAway,
}

/// <summary>The motifs (section 4.1); the four Quies entries are the quiet family on each hub.</summary>
public enum NthBeadsMotif : sbyte
{
    None = -1,
    Rota, Libra, Halitus, Gradus, Machina, Clepsydra, Scriptor, Textus, Linea, Nuntius, Tactus, Concentus, Vicissim, Commutatio,
    Pulsus, Spiritus, QuiesArcus, QuiesPedes, QuiesClavis, QuiesTripus,
}

/// <summary>What the planner queues: a move to build from wherever the beads are when it begins (the reference's factories).</summary>
internal struct NthBeadsRecipe
{
    public NthBeadsMoveId Id;
    public NthBeadsHub Hub;
    public NthBeadsMotif Motif;
    public int Dir;
    public double Arg;
    public bool Calm, Fly;
    // a motif cycle's variation (section 4): tempo, pick, pick2, a, b, c
    public double Tempo, Pick, Pick2, Va, Vb, Vc;

    public static NthBeadsRecipe Of(NthBeadsMoveId id, NthBeadsHub hub = NthBeadsHub.None) => new() { Id = id, Hub = hub, Motif = NthBeadsMotif.None };
}

/// <summary>A built move: timing, hubs, permutation and its parameters.</summary>
internal struct NthBeadsMove
{
    public NthBeadsMoveId Id;
    public NthBeadsMoveKind Kind;
    /// <summary>The motif this cycle belongs to (reference <c>m.id</c>), or None.</summary>
    public NthBeadsMotif Motif;
    public double T, Handoff, Tau, DipAt;
    public byte P0, P1, P2;
    public NthBeadsHub HubIn, HubOut, Hub;
    public bool Final, Flight, Mirrored, Hidden, F0;
    public int I0, I1, I2;
    public double A, B, C, D;
    public double Px, Py, Qx, Qy;
    public NthBead S0, S1, S2;

    public readonly int Perm(int k) => k switch { 0 => P0, 1 => P1, _ => P2 };
    public readonly NthBead Start(int k) => k switch { 0 => S0, 1 => S1, _ => S2 };
}

/// <summary>The move library.</summary>
internal static class NthBeadsMoves
{
    // Every curve is a CSS cubic-bezier (section 4), solved by NthCurve.
    public static readonly NthCurve Tick = new(.55, 0, .2, 1), Io = new(.45, 0, .55, 1), Io2 = new(.45, 0, .4, 1), Land = new(.35, 0, .3, 1.07),
        Fall = new(.55, 0, .5, 1.08), Rise = new(.3, 0, .2, 1.08), Slide = new(.45, 0, .3, 1.04), Climb = new(.35, 0, .25, 1.06),
        FlickC = new(.4, 0, .2, 1.05), GIn = new(.42, 0, .62, .92), GOut = new(.3, 0, .2, 1.06), OutC = new(.2, .7, .2, 1),
        PopC = new(.2, .8, .3, 1), ShrinkC = new(.5, 0, .85, .4), Write = new(.45, 0, .25, 1), BeadC = new(.5, 0, .25, 1),
        Drip = new(.5, 0, .6, 1.04), Fly = new(.4, 0, .2, 1);

    /// <summary>The io curve's peak slope (IO_PEAK), measured as the reference does.</summary>
    public static readonly double IoPeak = MeasureIoPeak();

    /// <summary>The Reduce Motion slide's cap in bead widths per 60 Hz frame (RM_CAP).</summary>
    public const double CalmCap = .22;

    private static double MeasureIoPeak()
    {
        double m = 0, p = 0;
        for (var i = 1; i <= 2000; i++)
        {
            var y = Io.Ease(i / 2000.0);
            m = Math.Max(m, (y - p) * 2000);
            p = y;
        }
        return m;
    }

    // ---- maths (reference clamp, win, smooth, lerp, st, quad, curl) ----
    public static double Clamp(double x, double a = 0, double b = 1) => x < a ? a : x > b ? b : x;
    public static double Win(double u, double a, double b) => Clamp((u - a) / (b - a));
    public static double Smooth(double x) { x = Clamp(x); return x * x * (3 - 2 * x); }
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double St(double v) { if (v <= 0 || v >= 1) return 0; var s = Math.Sin(Math.PI * v); return s * s; }

    private static NthBeadsPoint Quad(NthBeadsPoint a, NthBeadsPoint c, NthBeadsPoint b, double e)
    {
        var w = 1 - e;
        return new(w * w * a.X + 2 * w * e * c.X + e * e * b.X, w * w * a.Y + 2 * w * e * c.Y + e * e * b.Y);
    }

    private static NthBeadsPoint Curl(NthBeadsPoint a, NthBeadsPoint b, double k)
    {
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, dx = b.X - a.X, dy = b.Y - a.Y;
        return new(mx - dy * k, my + dx * k);
    }

    private static NthBeadsPoint SwapArc(NthBeadsPoint a, NthBeadsPoint b, double e)
    {
        var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        return Quad(a, Curl(a, b, .78 / Math.Max(len, .5)), b, e);
    }

    private static NthBead P4(NthBeadsPoint p, double s = 1) => new(p.X, p.Y, s, 1);
    private static NthBeadsPoint Pt(NthBead b) => new(b.X, b.Y);
    public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    // ---- tables ----
    public static NthBeadsHub HubOf(NthBeadsMotif m) => m switch
    {
        NthBeadsMotif.Rota or NthBeadsMotif.Libra or NthBeadsMotif.Halitus or NthBeadsMotif.QuiesArcus => NthBeadsHub.Arcus,
        NthBeadsMotif.Nuntius => NthBeadsHub.Signum,
        NthBeadsMotif.Tactus or NthBeadsMotif.QuiesClavis => NthBeadsHub.Clavis,
        NthBeadsMotif.Concentus or NthBeadsMotif.Vicissim or NthBeadsMotif.Commutatio or NthBeadsMotif.QuiesTripus => NthBeadsHub.Tripus,
        NthBeadsMotif.Spiritus => NthBeadsHub.Punctum,
        _ => NthBeadsHub.Pedes,
    };

    /// <summary>Motifs that may be mirrored left to right for a whole dwell.</summary>
    public static bool Mirrors(NthBeadsMotif m) => m is NthBeadsMotif.Rota or NthBeadsMotif.Halitus or NthBeadsMotif.Gradus
        or NthBeadsMotif.Machina or NthBeadsMotif.Clepsydra or NthBeadsMotif.Textus;

    public static NthBeadsMoveId MoveOf(NthBeadsMotif m) => m switch
    {
        NthBeadsMotif.Rota => NthBeadsMoveId.Rota,
        NthBeadsMotif.Libra => NthBeadsMoveId.Libra,
        NthBeadsMotif.Halitus => NthBeadsMoveId.Halitus,
        NthBeadsMotif.Gradus => NthBeadsMoveId.Gradus,
        NthBeadsMotif.Machina => NthBeadsMoveId.Machina,
        NthBeadsMotif.Clepsydra => NthBeadsMoveId.Clepsydra,
        NthBeadsMotif.Scriptor => NthBeadsMoveId.Scriptor,
        NthBeadsMotif.Textus => NthBeadsMoveId.Textus,
        NthBeadsMotif.Linea => NthBeadsMoveId.Linea,
        NthBeadsMotif.Nuntius => NthBeadsMoveId.Nuntius,
        NthBeadsMotif.Tactus => NthBeadsMoveId.Tactus,
        NthBeadsMotif.Concentus => NthBeadsMoveId.Concentus,
        NthBeadsMotif.Vicissim => NthBeadsMoveId.Vicissim,
        NthBeadsMotif.Commutatio => NthBeadsMoveId.Commutatio,
        NthBeadsMotif.Pulsus => NthBeadsMoveId.Pulsus,
        NthBeadsMotif.Spiritus => NthBeadsMoveId.Spiritus,
        NthBeadsMotif.None => NthBeadsMoveId.None,
        _ => NthBeadsMoveId.Quies,
    };

    /// <summary>The designed wire path between two hubs (TRANS), or None.</summary>
    public static NthBeadsMoveId TransFor(NthBeadsHub a, NthBeadsHub b) => (a, b) switch
    {
        (NthBeadsHub.Arcus, NthBeadsHub.Pedes) => NthBeadsMoveId.Descensus,
        (NthBeadsHub.Pedes, NthBeadsHub.Arcus) => NthBeadsMoveId.Ascensus,
        (NthBeadsHub.Arcus, NthBeadsHub.Signum) => NthBeadsMoveId.Flick,
        (NthBeadsHub.Signum, NthBeadsHub.Arcus) => NthBeadsMoveId.Reflick,
        (NthBeadsHub.Pedes, NthBeadsHub.Signum) => NthBeadsMoveId.Toss,
        (NthBeadsHub.Signum, NthBeadsHub.Pedes) => NthBeadsMoveId.Catch,
        (NthBeadsHub.Pedes, NthBeadsHub.Clavis) => NthBeadsMoveId.Levo,
        (NthBeadsHub.Clavis, NthBeadsHub.Pedes) => NthBeadsMoveId.Demitto,
        (NthBeadsHub.Arcus, NthBeadsHub.Tripus) => NthBeadsMoveId.Statio,
        (NthBeadsHub.Tripus, NthBeadsHub.Arcus) => NthBeadsMoveId.Fornix,
        (NthBeadsHub.Pedes, NthBeadsHub.Tripus) => NthBeadsMoveId.Surgo,
        (NthBeadsHub.Tripus, NthBeadsHub.Pedes) => NthBeadsMoveId.Cado,
        _ => NthBeadsMoveId.None,
    };

    public static string NameOf(NthBeadsMoveId id) => id switch
    {
        NthBeadsMoveId.None => "",
        NthBeadsMoveId.Rota => "rota",
        NthBeadsMoveId.Libra => "libra",
        NthBeadsMoveId.Halitus => "halitus",
        NthBeadsMoveId.Gradus => "gradus",
        NthBeadsMoveId.Machina => "machina",
        NthBeadsMoveId.Clepsydra => "clepsydra",
        NthBeadsMoveId.Scriptor => "scriptor",
        NthBeadsMoveId.Textus => "textus",
        NthBeadsMoveId.Linea => "linea",
        NthBeadsMoveId.Nuntius => "nuntius",
        NthBeadsMoveId.Tactus => "tactus",
        NthBeadsMoveId.Concentus => "concentus",
        NthBeadsMoveId.Vicissim => "vicissim",
        NthBeadsMoveId.Commutatio => "commutatio",
        NthBeadsMoveId.Pulsus => "pulsus",
        NthBeadsMoveId.Spiritus => "spiritus",
        NthBeadsMoveId.Quies => "quies",
        NthBeadsMoveId.Descensus => "descensus",
        NthBeadsMoveId.Ascensus => "ascensus",
        NthBeadsMoveId.Flick => "flick",
        NthBeadsMoveId.Reflick => "reflick",
        NthBeadsMoveId.Toss => "toss",
        NthBeadsMoveId.Catch => "catch",
        NthBeadsMoveId.Levo => "levo",
        NthBeadsMoveId.Demitto => "demitto",
        NthBeadsMoveId.Statio => "statio",
        NthBeadsMoveId.Fornix => "fornix",
        NthBeadsMoveId.Surgo => "surgo",
        NthBeadsMoveId.Cado => "cado",
        NthBeadsMoveId.Collige => "collige",
        NthBeadsMoveId.Pausa => "pausa",
        NthBeadsMoveId.Emitte => "emitte",
        NthBeadsMoveId.Nodus => "nodus",
        NthBeadsMoveId.Flexus => "flexus",
        NthBeadsMoveId.Glide => "glide",
        NthBeadsMoveId.Still => "still",
        NthBeadsMoveId.Sistere => "sistere",
        NthBeadsMoveId.Negatio => "negatio",
        NthBeadsMoveId.HoldMerged or NthBeadsMoveId.HoldCalm => "hold",
        NthBeadsMoveId.Ortus => "ortus",
        NthBeadsMoveId.Scriptio => "scriptio",
        NthBeadsMoveId.Pop => "pop",
        NthBeadsMoveId.Rejoin or NthBeadsMoveId.RejoinCalm => "rejoin",
        NthBeadsMoveId.Occasus => "occasus",
        NthBeadsMoveId.SetDown => "set",
        NthBeadsMoveId.ShrinkAway => "shrink",
    };

    private static NthBeadsMove Mk(NthBeadsMoveId id, NthBeadsMoveKind kind, double T, NthBeadsHub hubIn, NthBeadsHub hubOut,
        double handoff = 1, double tau = .28) => new()
    {
        Id = id, Kind = kind, T = T, Handoff = handoff, Tau = tau, HubIn = hubIn, HubOut = hubOut,
        P0 = 0, P1 = 1, P2 = 2, DipAt = double.NaN, Motif = NthBeadsMotif.None,
    };

    private static NthBeadsMove Motif(NthBeadsMoveId id, double T, NthBeadsHub hub, double handoff = 1) =>
        Mk(id, NthBeadsMoveKind.Motif, T, hub, hub, handoff);

    private static void Centroid(ReadOnlySpan<NthBead> s, out double x, out double y)
    {
        x = (s[0].X + s[1].X + s[2].X) / 3;
        y = (s[0].Y + s[1].Y + s[2].Y) / 3;
    }

    private static double MaxDistTo(ReadOnlySpan<NthBead> s, NthBeadsHub hub)
    {
        double m = double.NegativeInfinity;
        for (var k = 0; k < 3; k++)
        {
            var p = HubPoint(hub, k);
            m = Math.Max(m, Hypot(s[k].X - p.X, s[k].Y - p.Y));
        }
        return m;
    }

    private static void SetStart(ref NthBeadsMove m, ReadOnlySpan<NthBead> s)
    {
        if (s.Length < 3) return;
        m.S0 = s[0]; m.S1 = s[1]; m.S2 = s[2];
    }

    /// <summary>Builds a recipe from the start pose (slot-indexed; empty for an entrance from nothing).</summary>
    public static NthBeadsMove Build(in NthBeadsRecipe r, ReadOnlySpan<NthBead> start)
    {
        const NthBeadsHub Arc = NthBeadsHub.Arcus, Ped = NthBeadsHub.Pedes, Sig = NthBeadsHub.Signum, Cla = NthBeadsHub.Clavis,
            Tri = NthBeadsHub.Tripus, Loc = NthBeadsHub.Locus, Pun = NthBeadsHub.Punctum, No = NthBeadsHub.None;
        const NthBeadsMoveKind Tr = NthBeadsMoveKind.Trans, Bt = NthBeadsMoveKind.Beat, En = NthBeadsMoveKind.Enter, Ex = NthBeadsMoveKind.Exit;
        NthBeadsMove m;
        double tempo = r.Tempo;
        switch (r.Id)
        {
            // ---- motifs ----
            case NthBeadsMoveId.Rota:
                m = Motif(r.Id, 1.25 * tempo, Arc, .86); m.P0 = 1; m.P1 = 2; m.P2 = 0; m.A = .06 * r.Va; break;
            case NthBeadsMoveId.Libra:
                m = Motif(r.Id, 2.4 * tempo, Arc); m.A = 2 * Math.PI / m.T; m.B = .5 * (1 + .15 * r.Vb); m.C = .12 * (1 + .25 * r.Vc); break;
            case NthBeadsMoveId.Halitus:
                m = Motif(r.Id, 2.2 * tempo, Arc); m.A = .8 * (1 + .06 * r.Vb); m.B = .05 + .03 * r.Va; break;
            case NthBeadsMoveId.Gradus:
                m = Motif(r.Id, 1.6 * tempo, Ped, .86); m.P0 = 2; m.P1 = 0; m.P2 = 1;
                m.A = .52 + .04 * r.Vb; m.B = .14 * (1 + .25 * r.Vc); m.C = .03 * r.Va; break;
            case NthBeadsMoveId.Machina:
                m = Motif(r.Id, 1.8 * tempo, Ped); m.A = 1.42 * (1 + .08 * r.Vb); break;
            case NthBeadsMoveId.Clepsydra:
                m = Motif(r.Id, 2.8 * tempo, Ped); m.P0 = 1; m.P1 = 0; m.P2 = 2; m.A = .07 * (1 + .3 * r.Vc); break;
            case NthBeadsMoveId.Scriptor:
                {
                    var brk = r.Pick < .2;
                    m = Motif(r.Id, .8 * tempo * (brk ? 1.3 : 1), Ped, .86); m.P0 = 2; m.P1 = 0; m.P2 = 1;
                    m.A = .8 * (1 + .15 * r.Vb) * (brk ? .8 : 1); m.B = .08 * (1 + .3 * r.Vc); m.C = .03 * r.Va; break;
                }
            case NthBeadsMoveId.Textus:
                m = Motif(r.Id, 2.4 * tempo, Ped); m.A = .34 * (1 + .1 * r.Vb); break;
            case NthBeadsMoveId.Linea:
                m = Motif(r.Id, 1.5 * tempo, Ped, .88); m.P0 = 1; m.P1 = 2; m.P2 = 0; m.A = .84 * (1 + .1 * r.Vb); break;
            case NthBeadsMoveId.Nuntius:
                m = Motif(r.Id, 2.6 * tempo, Sig); m.F0 = r.Pick < .25; m.A = 1 + .1 * r.Vb; break;
            case NthBeadsMoveId.Tactus:
                m = Motif(r.Id, 1.8 * tempo, Cla); m.I0 = r.Pick < .5 ? -1 : 1; m.F0 = r.Pick2 < .3; break;
            case NthBeadsMoveId.Concentus:
                m = Motif(r.Id, 3.6 * tempo, Tri); m.F0 = r.Pick < .5; break;
            case NthBeadsMoveId.Vicissim:
                m = Motif(r.Id, 2.7 * tempo, Tri); m.F0 = r.Pick < .5; break;
            case NthBeadsMoveId.Commutatio:
                {
                    m = Motif(r.Id, 1.9 * tempo, Tri, .92);
                    int a, b;
                    if (r.Pick < .5) { a = 0; b = 2; } else if (r.Pick < .75) { a = 0; b = 1; } else { a = 1; b = 2; }
                    m.I0 = a; m.I1 = b; m.I2 = 3 - a - b;
                    Span<byte> perm = [0, 1, 2];
                    perm[a] = (byte)b; perm[b] = (byte)a;
                    m.P0 = perm[0]; m.P1 = perm[1]; m.P2 = perm[2];
                    break;
                }
            case NthBeadsMoveId.Pulsus:
                m = Motif(r.Id, 3.2 * tempo, Ped); m.F0 = r.Pick < .5; break;
            case NthBeadsMoveId.Spiritus:
                m = Motif(r.Id, 2.4 * tempo, Pun); m.A = .24 * (1 + .1 * r.Vb); m.B = .02 + .03 * r.Vc; break;
            case NthBeadsMoveId.Quies:
                m = Motif(r.Id, 3.6 * tempo, r.Hub); m.F0 = r.Pick < .5; m.Hub = r.Hub; break;

            // ---- designed wire paths ----
            case NthBeadsMoveId.Descensus: m = Mk(r.Id, Tr, .92, Arc, Ped, .92); break;
            case NthBeadsMoveId.Ascensus: m = Mk(r.Id, Tr, .98, Ped, Arc, .92); break;
            case NthBeadsMoveId.Flick: m = Mk(r.Id, Tr, .84, Arc, Sig, .92); break;
            case NthBeadsMoveId.Reflick: m = Mk(r.Id, Tr, .84, Sig, Arc, .92); break;
            case NthBeadsMoveId.Toss: m = Mk(r.Id, Tr, 1.1, Ped, Sig, .92); break;
            case NthBeadsMoveId.Catch: m = Mk(r.Id, Tr, 1.05, Sig, Ped, .92); break;
            case NthBeadsMoveId.Levo: m = Mk(r.Id, Tr, .62, Ped, Cla, .94); break;
            case NthBeadsMoveId.Demitto: m = Mk(r.Id, Tr, .62, Cla, Ped, .94); break;
            case NthBeadsMoveId.Statio: m = Mk(r.Id, Tr, .8, Arc, Tri, .92); break;
            case NthBeadsMoveId.Fornix: m = Mk(r.Id, Tr, .86, Tri, Arc, .92); break;
            case NthBeadsMoveId.Surgo: m = Mk(r.Id, Tr, 1.02, Ped, Tri, .92); break;
            case NthBeadsMoveId.Cado: m = Mk(r.Id, Tr, .96, Tri, Ped, .92); break;

            // ---- generic moves ----
            case NthBeadsMoveId.Collige:
                {
                    Centroid(start, out var px, out var py);
                    double md = double.NegativeInfinity;
                    for (var k = 0; k < 3; k++) md = Math.Max(md, Hypot(start[k].X - px, start[k].Y - py));
                    var T = md < .12 ? .14 : Clamp(.3 + md * .2, .34, .56);
                    m = Mk(r.Id, Bt, T, No, Loc, tau: .24); m.Px = px; m.Py = py; m.A = r.Dir; SetStart(ref m, start); break;
                }
            case NthBeadsMoveId.Pausa:
                {
                    Centroid(start, out var px, out var py);
                    var q = r.Hub != No ? HubCentre(r.Hub) : new NthBeadsPoint(px, py);
                    var T = Clamp(.52 + (Hypot(q.X - px, q.Y - py) - 1.2) * .26, .52, .92);
                    m = Mk(r.Id, Bt, T, Loc, r.Hub == Pun ? Pun : Loc, tau: .12);
                    m.Px = px; m.Py = py; m.Qx = q.X; m.Qy = q.Y; m.A = T / .52; m.B = start[0].S; m.DipAt = .2 * m.A; break;
                }
            case NthBeadsMoveId.Emitte:
                {
                    Centroid(start, out var qx, out var qy);
                    double md = double.NegativeInfinity;
                    for (var k = 0; k < 3; k++) { var p = HubPoint(r.Hub, k); md = Math.Max(md, Hypot(p.X - qx, p.Y - qy)); }
                    var T = Clamp(.3 + md * .28 + Math.Max(0, md - 2) * .14, .46, 1.5);
                    m = Mk(r.Id, Bt, T, Loc, r.Hub, .92, .12); m.Qx = qx; m.Qy = qy; m.Hub = r.Hub; SetStart(ref m, start); break;
                }
            case NthBeadsMoveId.Nodus:
                {
                    Centroid(start, out var cx, out var cy);
                    var c1 = HubCentre(r.Hub);
                    m = Mk(r.Id, Bt, .72, No, r.Hub, .96, .26); m.Px = cx; m.Py = cy; m.Qx = c1.X; m.Qy = c1.Y; m.Hub = r.Hub; SetStart(ref m, start); break;
                }
            case NthBeadsMoveId.Flexus:
                {
                    var md = MaxDistTo(start, r.Hub);
                    var T = md < .2 ? .24 : Clamp(.4 + md * .18, .6, .86);
                    m = Mk(r.Id, Tr, T, No, r.Hub, .96, .16); m.Hub = r.Hub; SetStart(ref m, start); break;
                }
            case NthBeadsMoveId.Glide:
                {
                    var d = MaxDistTo(start, r.Hub);
                    double ds = double.NegativeInfinity;
                    for (var k = 0; k < 3; k++) ds = Math.Max(ds, Math.Abs(start[k].S - 1));
                    var T = Math.Max(Math.Max(.4, d * IoPeak / (60 * CalmCap * BeadWidth)), ds * IoPeak / (60 * .06));
                    m = Mk(r.Id, NthBeadsMoveKind.Calm, T, No, r.Hub, tau: .2); m.Hub = r.Hub; SetStart(ref m, start); break;
                }
            case NthBeadsMoveId.Still:
                m = Mk(r.Id, NthBeadsMoveKind.Still, 3600, r.Hub, r.Hub); m.Hub = r.Hub; break;
            case NthBeadsMoveId.Sistere:
                m = Mk(r.Id, Bt, .22, No, Loc, tau: .22); SetStart(ref m, start); break;
            case NthBeadsMoveId.Negatio:
                {
                    Centroid(start, out var px, out var py);
                    m = Mk(r.Id, Bt, .66, No, Loc, tau: .2); m.Px = px; m.Py = py; m.B = start[0].S; break;
                }
            case NthBeadsMoveId.HoldMerged:
                {
                    Centroid(start, out var px, out var py);
                    m = Mk(r.Id, Bt, r.Arg, No, Loc, tau: .1); m.Px = px; m.Py = py; m.B = start[0].S; break;
                }
            case NthBeadsMoveId.HoldCalm:
                m = Mk(r.Id, Bt, r.Arg, No, Loc, tau: .05); SetStart(ref m, start); break;

            // ---- entrances ----
            case NthBeadsMoveId.Ortus:
                {
                    var q = HubCentre(r.Hub);
                    m = Mk(r.Id, En, .62, No, r.Hub == Pun ? Pun : Loc); m.Qx = q.X; m.Qy = q.Y; break;
                }
            case NthBeadsMoveId.Scriptio:
                m = Mk(r.Id, En, .55, No, Arc); m.Hidden = true; break;
            case NthBeadsMoveId.Pop:
                m = Mk(r.Id, En, .24, No, r.Hub); m.Hub = r.Hub; break;
            case NthBeadsMoveId.Rejoin:
            case NthBeadsMoveId.RejoinCalm:
                {
                    Centroid(start, out var px, out var py);
                    var q = HubCentre(r.Hub);
                    var o0 = Math.Min(Math.Min(start[0].O, start[1].O), start[2].O);
                    if (r.Calm)
                    {
                        m = Mk(NthBeadsMoveId.RejoinCalm, En, .5, No, r.Hub, tau: .12); m.Flight = r.Fly;
                    }
                    else
                    {
                        var outside = r.Fly;
                        for (var k = 0; k < 3; k++)
                        {
                            var s = start[k];
                            if (s.X < EnvelopeX0 || s.X > EnvelopeX1 || s.Y < EnvelopeY0 || s.Y > EnvelopeY1) outside = true;
                        }
                        m = Mk(NthBeadsMoveId.Rejoin, En, .62, No, r.Hub == Pun ? Pun : Loc, tau: .12); m.Flight = outside;
                    }
                    m.Px = px; m.Py = py; m.Qx = q.X; m.Qy = q.Y; m.A = o0; m.Hub = r.Hub; SetStart(ref m, start); break;
                }

            // ---- exits ----
            case NthBeadsMoveId.Occasus:
                m = Mk(r.Id, Ex, .28, No, No, tau: .1); m.Final = true; m.B = start[0].S; SetStart(ref m, start); break;
            case NthBeadsMoveId.SetDown:
                m = Mk(r.Id, Ex, .3, No, No, tau: .08); m.Final = true; SetStart(ref m, start); break;
            case NthBeadsMoveId.ShrinkAway:
                m = Mk(r.Id, Ex, .2, No, No, tau: .05); m.Final = true; SetStart(ref m, start); break;
            default:
                throw new ArgumentOutOfRangeException(nameof(r), r.Id, "not a move");
        }
        if (m.Kind == NthBeadsMoveKind.Motif) m.Motif = r.Motif;
        return m;
    }

    /// <summary>A mirrored motif: slot k plays slot 2 − k flipped left to right (reference <c>mirrorMove</c>).</summary>
    public static void Mirror(ref NthBeadsMove m)
    {
        int p0 = m.P0, p1 = m.P1, p2 = m.P2;
        m.P0 = (byte)(2 - p2); m.P1 = (byte)(2 - p1); m.P2 = (byte)(2 - p0);
        m.Mirrored = true;
    }

    /// <summary>The pose of slot <paramref name="k"/> at local time <paramref name="t"/>.</summary>
    public static NthBead At(in NthBeadsMove m, double t, int k)
    {
        if (!m.Mirrored) return AtBase(in m, t, k);
        var r = AtBase(in m, t, 2 - k);
        return new(-r.X, r.Y, r.S, r.O);
    }

    private static NthBead AtBase(in NthBeadsMove m, double t, int k)
    {
        const double PI = Math.PI, RT = Foot;
        double T = m.T, u = t / T;
        switch (m.Id)
        {
            case NthBeadsMoveId.Rota:
                {
                    var lag = m.A;
                    if (k < 2)
                    {
                        var w = k == 0 ? (lag > 0 ? Win(u, 0, 1 - lag) : Win(u, -lag, 1)) : (lag > 0 ? Win(u, lag, 1) : Win(u, 0, 1 + lag));
                        return P4(Wire((k != 0 ? SCrown : SLeft) + PI / 2 * Tick.Ease(w)));
                    }
                    double b0 = BackSegment(0), b1 = BackSegment(1), len = BackLength;
                    var d = len * Io.Ease(u);
                    var back = Smooth(Win(d, 0, b0 + b1)) * (1 - Smooth(Win(d, len - b0 - b1, len)));
                    return P4(BackRun(d), 1 - .27 * back);
                }
            case NthBeadsMoveId.Libra:
                {
                    var s0 = k == 0 ? SLeft : k == 1 ? SCrown : SRight;
                    return P4(Wire(s0 + m.B * Math.Sin(m.A * t) + m.C * Math.Sin(2 * m.A * t) * (k - 1)));
                }
            case NthBeadsMoveId.Halitus:
                {
                    double A = m.A, lag = m.B, bL = St(Win(u, 0, 1 - lag)), bR = St(Win(u, lag, 1)), bC = (bL + bR) / 2;
                    if (k == 0) return P4(Wire(SLeft + A * bL), 1 - .04 * bL);
                    if (k == 2) return P4(Wire(SRight - A * bR), 1 - .04 * bR);
                    return P4(new(0, -1 - .1 * bC), 1 + .1 * bC);
                }
            case NthBeadsMoveId.Gradus:
                {
                    double sp = m.A, cr = m.B, sh = m.C;
                    if (k == 0)
                    {
                        var s = WireLength * (sp * Io2.Ease(Win(u, 0, .56)) + (1 - sp) * Land.Ease(Win(u, .4, 1)));
                        return P4(Wire(s), 1 + cr * Math.Sin(PI * Clamp((s - SLeft) / (SRight - SLeft))));
                    }
                    if (k == 1) return P4(new(-Io.Ease(Win(u, .22 + sh, .88 + sh)), RT));
                    return P4(new(1 - Io.Ease(Win(u, .3 - sh, .92 - sh)), RT));
                }
            case NthBeadsMoveId.Machina:
                {
                    // ED-NEW-calculi-wu1-3: the left stroke's window is [0, .56], not the reference's [-.06, .5], so the beam
                    // engine starts and ends exactly on Pedes like every other motif (the right stroke keeps [.44, 1]).
                    double pL = St(u / .56), pR = St((u - .44) / .56);
                    if (k == 0) return P4(Wire(m.A * pL));
                    if (k == 2) return P4(Wire(WireLength - m.A * pR));
                    return P4(new(.22 * (pR - pL), RT - .16 * (pL + pR)));
                }
            case NthBeadsMoveId.Clepsydra:
                {
                    var sw = m.A;
                    if (k == 0)
                    {
                        if (u < .46) { var s = SCrown * Io.Ease(Win(u, 0, .42)); return P4(Wire(s), 1 + sw * Smooth(Win(u, .3, .44))); }
                        var f = Drip.Ease(Win(u, .5, .8));
                        return P4(new(0, -1 + (RT + 1) * f), 1 + sw - sw * Smooth(Win(u, .5, .62)));
                    }
                    if (k == 1) return P4(new(-Io.Ease(Win(u, .46, .76)), RT));
                    return P4(new(1, RT + .05 * St(Win(u, .76, .96))));
                }
            case NthBeadsMoveId.Scriptor:
                {
                    double hh = m.A, sb = m.B, sh = m.C;
                    if (k == 0) { var e = Io.Ease(u); return P4(new(-1 + 2 * e, RT - hh * Math.Sin(PI * e)), 1 + sb * Math.Sin(PI * e)); }
                    if (k == 1) return P4(new(-Io.Ease(Win(u, .12 + sh, .86 + sh)), RT));
                    return P4(new(1 - Io.Ease(Win(u, .18 - sh, .9 - sh)), RT));
                }
            case NthBeadsMoveId.Textus:
                {
                    var th = 2 * PI * t / T;
                    if (k == 1) { var s = Math.Sin(th); return P4(new(.44 * s, RT + m.A * Math.Sin(2 * th)), 1 + .06 * s * s); }
                    if (k == 0) return P4(new(-1 - .14 * Math.Pow(Math.Max(0, Math.Cos(th - 3 * PI / 2)), 6), RT));
                    return P4(new(1 + .14 * Math.Pow(Math.Max(0, Math.Cos(th - PI / 2)), 6), RT));
                }
            case NthBeadsMoveId.Linea:
                {
                    var e = Io.Ease(Win(u, 0, .84));
                    var p = Quad(FootRight, new(0, RT + m.A), FootLeft, e);
                    var sw = Math.Sin(PI * e);
                    if (k == 2) return P4(p, 1 - .2 * sw);
                    var x = k == 0 ? -1 + Io.Ease(Win(u, .14, .92)) : Io.Ease(Win(u, .08, .88));
                    var g = (x - p.X) / .55;
                    return P4(new(x, RT - .22 * sw * Math.Exp(-(g * g))));
                }
            case NthBeadsMoveId.Nuntius:
                {
                    const double kw = .2, gap = .26;
                    var amp = m.A;
                    if (k == 1)
                    {
                        var kn = St(t / kw) + St((t - gap) / kw) + (m.F0 ? St((t - 2 * gap) / kw) : 0);
                        var fl = St(u);
                        return P4(new(Dot.X + .05 * Math.Sin(2 * PI * u), Dot.Y - .14 * amp * kn - .1 * fl), 1 + .24 * amp * kn);
                    }
                    var sw = .06 * Math.Sin(2 * PI * u);
                    return P4(new(k == 0 ? -1 - sw : 1 + sw, RT), 1 + .04 * St(u));
                }
            case NthBeadsMoveId.Tactus:
                {
                    double side = m.I0;
                    NthBeadsPoint hv = new(side * .78, RT - .86), pr = new(side * .9, RT - .42), c1 = new(side * .55, .32), c2 = new(side * .3, .5);
                    var key = side < 0 ? 0 : 2;
                    var press = m.F0 ? (St(Win(u, .34, .49)) + St(Win(u, .49, .64))) : St(Win(u, .36, .58));
                    if (k == 1)
                    {
                        double go = Io.Ease(Win(u, 0, .34)), back = Io.Ease(Win(u, .66, .98));
                        var b = back > 0 ? Quad(hv, c2, Cursor, back) : Quad(Cursor, c1, hv, go);
                        return P4(new(b.X + press * (pr.X - hv.X), b.Y + press * (pr.Y - hv.Y)), 1 + .04 * go * (1 - back) - .08 * press);
                    }
                    if (k == key) return P4(new(HubPoint(NthBeadsHub.Clavis, k).X, RT + .07 * press), 1 - .08 * press);
                    return P4(HubPoint(NthBeadsHub.Clavis, k));
                }
            case NthBeadsMoveId.Concentus:
                {
                    double n = m.F0 ? 2 + k : 4 - k, s = Math.Sin(PI * n * u), w = s * s;
                    var p = HubPoint(NthBeadsHub.Tripus, k);
                    return P4(new(p.X, p.Y - .16 * w), 1 + .07 * w);
                }
            case NthBeadsMoveId.Vicissim:
                {
                    var c = HubCentre(NthBeadsHub.Tripus);
                    var j = m.F0 ? k : 2 - k;
                    var w = St(Win(u, j * .29, j * .29 + .42));
                    var p = HubPoint(NthBeadsHub.Tripus, k);
                    return P4(new(p.X + (c.X - p.X) * .22 * w, p.Y + (c.Y - p.Y) * .22 * w), 1 + .06 * w);
                }
            case NthBeadsMoveId.Commutatio:
                {
                    int a = m.I0, b = m.I1, o = m.I2;
                    var hk = HubPoint(NthBeadsHub.Tripus, k);
                    if (k == o) { var w = St(u); return P4(new(hk.X, hk.Y - .08 * w), 1 + .06 * w); }
                    var e = Io2.Ease(Win(u, k == a ? 0 : .04, k == a ? .9 : .94));
                    var to = k == a ? b : a;
                    return P4(SwapArc(hk, HubPoint(NthBeadsHub.Tripus, to), e), 1 + .05 * Math.Sin(PI * e));
                }
            case NthBeadsMoveId.Pulsus:
                {
                    var j = m.F0 ? k : 2 - k;
                    var w = St(Win(u, j * .29, j * .29 + .42));
                    return P4(new(HubPoint(NthBeadsHub.Pedes, k).X, RT - .14 * w), 1 + .06 * w);
                }
            case NthBeadsMoveId.Spiritus:
                {
                    var sw = .05 * Math.Sin(2 * PI * u);
                    if (k == 0) { var b0 = St(u); return P4(new(Dot.X + sw, Dot.Y), 1 + .04 * b0); }
                    var b = k == 1 ? St(Win(u, .03, .95)) : St(Win(u, .06, 1));
                    var r = m.A * k * b;
                    return P4(new(Dot.X + sw - r, Dot.Y + m.B * k * b), 1 - (k == 1 ? .05 : .12) * b);
                }
            case NthBeadsMoveId.Quies:
                {
                    var j = m.F0 ? k : 2 - k;
                    var w = St(Win(u, j * .29, j * .29 + .42));
                    var p = HubPoint(m.Hub, k);
                    return P4(new(p.X, p.Y - .12 * w), 1 * (1 + .05 * w));
                }

            // ---- designed wire paths ----
            case NthBeadsMoveId.Descensus:
                if (k == 0) return P4(Wire(SLeft * (1 - Slide.Ease(Win(u, 0, .8)))));
                if (k == 2) return P4(Wire(SRight + RT * Slide.Ease(Win(u, .06, .86))));
                return P4(new(0, -1 + (RT + 1) * Fall.Ease(Win(u, .1, 1))));
            case NthBeadsMoveId.Ascensus:
                if (k == 0) return P4(Wire(SLeft * Climb.Ease(Win(u, .08, .9))));
                if (k == 2) return P4(Wire(WireLength - RT * Climb.Ease(Win(u, .12, .94))));
                return P4(new(0, RT - (RT + 1) * Rise.Ease(Win(u, 0, .84))));
            case NthBeadsMoveId.Flick:
                {
                    if (k == 0) return P4(Wire(SLeft * (1 - Slide.Ease(Win(u, 0, .82)))));
                    if (k == 2) return P4(Wire(SRight + RT * Slide.Ease(Win(u, .05, .86))));
                    var e = FlickC.Ease(Win(u, 0, .88));
                    return P4(Quad(Crown, new(.9, -1.75), Dot, e), 1 + .1 * Math.Sin(PI * Clamp(e)));
                }
            case NthBeadsMoveId.Reflick:
                {
                    if (k == 0) return P4(Wire(SLeft * Climb.Ease(Win(u, .06, .9))));
                    if (k == 2) return P4(Wire(WireLength - RT * Climb.Ease(Win(u, .1, .94))));
                    var e = FlickC.Ease(Win(u, 0, .88));
                    return P4(Quad(Dot, new(.9, -1.75), Crown, e), 1 + .1 * Math.Sin(PI * Clamp(e)));
                }
            case NthBeadsMoveId.Toss:
                if (k == 1) return P4(Quad(FootMiddle, new(.35, -1.7), Dot, Io2.Ease(Win(u, .04, .94))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .08 * St(Win(u, 0, .32)));
            case NthBeadsMoveId.Catch:
                if (k == 1) return P4(Quad(Dot, new(.35, -1.7), FootMiddle, Land.Ease(Win(u, 0, .9))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .08 * St(Win(u, .6, .95)));
            case NthBeadsMoveId.Levo:
                if (k == 1) return P4(new(0, RT - (RT - Cursor.Y) * Climb.Ease(Win(u, 0, .9))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .06 * St(Win(u, 0, .4)));
            case NthBeadsMoveId.Demitto:
                if (k == 1) return P4(new(0, Cursor.Y + (RT - Cursor.Y) * Land.Ease(Win(u, 0, .9))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .06 * St(Win(u, .55, .95)));
            case NthBeadsMoveId.Statio:
                if (k == 0) return P4(Wire(SLeft * (1 - Slide.Ease(Win(u, 0, .8)))));
                if (k == 2) return P4(Wire(SRight + RT * Slide.Ease(Win(u, .06, .86))));
                return P4(Crown, 1 + .06 * St(u));
            case NthBeadsMoveId.Fornix:
                if (k == 0) return P4(Wire(SLeft * Climb.Ease(Win(u, .06, .9))));
                if (k == 2) return P4(Wire(WireLength - RT * Climb.Ease(Win(u, .1, .94))));
                return P4(Crown, 1 + .06 * St(u));
            case NthBeadsMoveId.Surgo:
                if (k == 1) return P4(new(0, RT - (RT + 1) * Rise.Ease(Win(u, 0, .86))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .06 * St(Win(u, 0, .4)));
            case NthBeadsMoveId.Cado:
                if (k == 1) return P4(new(0, -1 + (RT + 1) * Fall.Ease(Win(u, .04, 1))));
                return P4(HubPoint(NthBeadsHub.Pedes, k), 1 - .06 * St(Win(u, .6, .95)));

            // ---- generic moves ----
            case NthBeadsMoveId.Collige:
                {
                    var e = GIn.Ease(u);
                    var s0 = m.Start(k);
                    double dx = s0.X - m.Px, dy = s0.Y - m.Py, a = m.A * .7 * e * e, ca = Math.Cos(a), sa = Math.Sin(a), f = 1 - e;
                    return new(m.Px + f * (dx * ca - dy * sa), m.Py + f * (dx * sa + dy * ca), Lerp(s0.S, 1.18, e), 1);
                }
            case NthBeadsMoveId.Pausa:
                {
                    double z = m.A, s0 = m.B, k1 = .2 * z, k2 = .38 * z, s;
                    if (t <= k1) s = Lerp(s0, .48, Smooth((t - 0) / (k1 - 0)));
                    else if (t <= k2) s = Lerp(.48, 1.1, Smooth((t - k1) / (k2 - k1)));
                    else s = Lerp(1.1, 1, Smooth((t - k2) / (T - k2)));
                    var e = Io.Ease(u);
                    return new(Lerp(m.Px, m.Qx, e), Lerp(m.Py, m.Qy, e), s, 1);
                }
            case NthBeadsMoveId.Emitte:
                {
                    var e = GOut.Ease(Win(u, k * .06, .8 + k * .06));
                    NthBeadsPoint q = new(m.Qx, m.Qy), tg = HubPoint(m.Hub, k);
                    return P4(Quad(q, Curl(q, tg, -.22), tg, e), Lerp(m.Start(k).S, 1, Clamp(e)));
                }
            case NthBeadsMoveId.Nodus:
                {
                    double c = 1 - .7 * St(u), w = Smooth(Win(u, .3, .72)), e = Io.Ease(u);
                    double cx = Lerp(m.Px, m.Qx, e), cy = Lerp(m.Py, m.Qy, e);
                    var s0 = m.Start(k);
                    var tg = HubPoint(m.Hub, k);
                    double sx = Lerp(s0.X - m.Px, tg.X - m.Qx, w), sy = Lerp(s0.Y - m.Py, tg.Y - m.Qy, w);
                    return P4(new(cx + c * sx, cy + c * sy), Lerp(s0.S, 1, w) * (1 + .06 * St(u)));
                }
            case NthBeadsMoveId.Flexus:
                {
                    var e = Io2.Ease(Win(u, k * .04, .88 + k * .04));
                    var s0 = m.Start(k);
                    var tg = HubPoint(m.Hub, k);
                    return P4(Quad(Pt(s0), Curl(Pt(s0), tg, .16), tg, e), Lerp(s0.S, 1, e) * (1 + .05 * St(e)));
                }
            case NthBeadsMoveId.Glide:
                {
                    var e = Io.Ease(t / T);
                    var s0 = m.Start(k);
                    var tg = HubPoint(m.Hub, k);
                    return P4(new(Lerp(s0.X, tg.X, e), Lerp(s0.Y, tg.Y, e)), Lerp(s0.S, 1, e));
                }
            case NthBeadsMoveId.Still:
                return P4(HubPoint(m.Hub, k), 1);
            case NthBeadsMoveId.Sistere:
            case NthBeadsMoveId.HoldCalm:
                { var s0 = m.Start(k); return P4(Pt(s0), s0.S); }
            case NthBeadsMoveId.Negatio:
                return new(m.Px + .16 * Math.Sin(2 * PI * 2.5 * u) * Smooth(Win(u, 0, .24)) * (1 - u) * (1 - u), m.Py, m.B - (m.B - 1) * Smooth(u), 1);
            case NthBeadsMoveId.HoldMerged:
                return new(m.Px, m.Py, m.B, 1);

            // ---- entrances ----
            case NthBeadsMoveId.Ortus:
                {
                    var s = u < .4 ? 1.12 * PopC.Ease(u / .4) : 1.12 - .12 * Smooth((u - .4) / .6);
                    var e = Io.Ease(Win(u, .12, 1));
                    return new(Lerp(Dot.X, m.Qx, e), Lerp(Dot.Y, m.Qy, e), s, 1);
                }
            case NthBeadsMoveId.Scriptio:
                {
                    if (t < ScriptioWrite) return P4(Wire(0), 0);
                    var v = BeadC.Ease((t - ScriptioWrite) / (T - ScriptioWrite));
                    ScriptioSegment(k, v, out var a, out var b);
                    return P4(Wire((a + b) / 2));
                }
            case NthBeadsMoveId.Pop:
                return P4(HubPoint(m.Hub, k), 1 * OutC.Ease(t / .24));
            case NthBeadsMoveId.RejoinCalm:
                {
                    var e = Io.Ease(t / T);
                    var s0 = m.Start(k);
                    var tg = HubPoint(m.Hub, k);
                    return new(Lerp(s0.X, tg.X, e), Lerp(s0.Y, tg.Y, e), Lerp(s0.S, 1, e), Lerp(m.A, 1, Smooth(t / T / .3)));
                }
            case NthBeadsMoveId.Rejoin:
                {
                    var s0 = m.Start(k);
                    double g = 1 - GIn.Ease(Win(u, 0, .5)), e = Io.Ease(Win(u, .12, 1));
                    double cx = Lerp(m.Px, m.Qx, e), cy = Lerp(m.Py, m.Qy, e);
                    var s = u < .4 ? Lerp(s0.S, 1.12, PopC.Ease(u / .4)) : 1.12 - .12 * Smooth((u - .4) / .6);
                    return new(cx + g * (s0.X - m.Px), cy + g * (s0.Y - m.Py), s, Lerp(m.A, 1, Smooth(u / .3)));
                }

            // ---- exits ----
            case NthBeadsMoveId.Occasus:
                {
                    var s0 = m.B;
                    var s = u < .28 ? Lerp(s0, s0 * 1.07, Smooth(u / .28)) : s0 * 1.07 * (1 - ShrinkC.Ease((u - .28) / .72));
                    return P4(Pt(m.Start(k)), s);
                }
            case NthBeadsMoveId.SetDown:
                { var s0 = m.Start(k); return P4(Pt(s0), s0.S * (1 - ShrinkC.Ease(Win(t / T, k * .12, .76 + k * .12)))); }
            case NthBeadsMoveId.ShrinkAway:
                { var s0 = m.Start(k); return P4(Pt(s0), s0.S * (1 - Io.Ease(t / .2))); }
            default:
                return P4(new(0, 0));
        }
    }

    /// <summary>Scriptio's write share (0.28 s of 0.55 s).</summary>
    public const double ScriptioWrite = .28;

    /// <summary>Scriptio's bead-up: segment <paramref name="i"/> shrinking from its third of the wire onto its arch point.</summary>
    public static void ScriptioSegment(int i, double v, out double from, out double to)
    {
        double half0 = (SLeft + SCrown) / 2, half1 = (SCrown + SRight) / 2;
        double b0 = i == 0 ? 0 : i == 1 ? half0 : half1, b1 = i == 0 ? half0 : i == 1 ? half1 : WireLength;
        var target = i == 0 ? SLeft : i == 1 ? SCrown : SRight;
        from = Lerp(b0, target, v);
        to = Lerp(b1, target, v);
    }
}
