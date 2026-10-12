using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marrow.UI.Port.Nth.Beads;
using Xunit;
using Xunit.Abstractions;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;
using Tally = Marrow.UI.Tests.Nth.NthBeadsStress.Tally;

namespace Marrow.UI.Tests.Nth;

// Calculi WU2 (DESIGN.md section 14, ED-NEW-calculi-wu2-1): the headless frame-sampling gates. A port of reference/stress.js's CORE
// block (judge, summary, determinism) on the C# engine, over NthBeadsStress's sessions (stress.js's session with the shipped pair-A
// lists, ED-NEW-calculi-wu1-2): 200 random 40 s turns, 25% Reduce Motion, exits asked during the entrance, new turns while leaving,
// sampled at 240 Hz and judged at 60 Hz and 30 Hz. Every gate of section 13.1, plus the additions of ED-NEW-calculi-wu2-1: no
// orbits (section 1.1), every exit reaches gone, the error hold stays at its designed length, zero allocation in the sampling loop
// after warm-up, and reflows judged on screen (section 9). Figures of the node reference for the same pair-A sessions are in the ED.
[Trait("Decision", "ED-NEW-calcmerge-2")]
public sealed class NthBeadsSmoothnessTests(ITestOutputHelper output)
{
    private const double Turn = 2 * Math.PI;

    private static bool Between(in NthBeadsSample f) => f.Present && f.Kind != NthBeadsMoveKind.Enter && f.Kind != NthBeadsMoveKind.Exit && !f.Hidden;

    private static long JsRound(double x) => (long)Math.Floor(x + .5);

    private static string Tag(in NthBeadsSample f, NthBeadsStress.Session s, int i) =>
        f.MoveName + "@" + ((double)i / s.Fs).ToString("F2", CultureInfo.InvariantCulture) + "s #" + s.Seed;

    private static double Wrap(double d)
    {
        while (d > Math.PI) d -= Turn;
        while (d < -Math.PI) d += Turn;
        return d;
    }

    /// <summary>stress.js's judge, line for line; the orbit winding (ED-NEW-calculi-wu2-1) rides the same 60 Hz loop.</summary>
    private static void Judge(NthBeadsStress.Session S, Tally W)
    {
        var rec = S.Rec;
        var step = (int)JsRound(S.Fs / 60.0);
        const double R = BeadRadius, BW = BeadWidth;
        W.Sessions++;
        if (S.Early) W.Early++;
        if (S.Reenter) W.Reenter++;
        var near = new HashSet<long>();
        foreach (var h in S.Handoffs) { var j = JsRound(h * 60); for (var d = -2; d <= 2; d++) near.Add(j + d); }
        var still = 0;
        // orbit: each bead's winding about the n's centre and about each other bead, over one move (one motif cycle)
        Span<double> windC = stackalloc double[3], windP = stackalloc double[9];
        var runMove = (NthBeadsMoveId)255;
        var runCalm = false;
        for (var i = step; i < rec.Count; i += step)
        {
            NthBeadsSample f = rec[i], p = rec[i - step];
            bool fc = S.CalmAt[i], pc = S.CalmAt[i - step];
            // every frame, edges included: the envelope
            if (f.Present)
                for (var b = 0; b < 3; b++)
                {
                    var a = f[b];
                    if (f.Hidden || a.S <= .002 || f.Flight) continue;
                    var r = R * a.S;
                    W.EnvX0 = Math.Min(W.EnvX0, a.X - r); W.EnvX1 = Math.Max(W.EnvX1, a.X + r);
                    W.EnvY0 = Math.Min(W.EnvY0, a.Y - r); W.EnvY1 = Math.Max(W.EnvY1, a.Y + r);
                }
            if (f.Present && p.Present && f.Flight && p.Flight)
            {
                double d = 0;
                for (var b = 0; b < 3; b++) d = Math.Max(d, Math.Sqrt(Sq(f[b].X - p[b].X) + Sq(f[b].Y - p[b].Y)));
                W.Flight = Math.Max(W.Flight, d / Math.Sqrt(Sq(2.6) + Sq(3.4)));
            }
            if (!Between(f) || !Between(p)) { runMove = (NthBeadsMoveId)255; continue; }
            Orbit(S, i, f, p, fc, ref runMove, ref runCalm, windC, windP, W);
            if (fc || pc)
            {
                W.RmFrames++;
                for (var b = 0; b < 3; b++)
                {
                    if (f[b].O != 1) W.RmOp++;
                    var d = Math.Sqrt(Sq(f[b].X - p[b].X) + Sq(f[b].Y - p[b].Y)) / BW;
                    if (d > W.RmMax) { W.RmMax = d; W.RmWhere = Tag(f, S, i); }
                    W.RmSRate = Math.Max(W.RmSRate, Math.Abs(f[b].S - p[b].S));
                }
                continue;
            }
            W.Frames++;
            double mv = 0, acc = 0;
            var hasQ = i - 2 * step >= 0;
            var q = hasQ ? rec[i - 2 * step] : default;
            var q2 = hasQ && Between(q) && !S.CalmAt[i - 2 * step];
            for (var b = 0; b < 3; b++)
            {
                NthBead a = f[b], c = p[b];
                if (a.O != 1) W.Op++;
                if (a.S < .05) W.Pops++;
                if ((f.Kind == NthBeadsMoveKind.Motif || f.Kind == NthBeadsMoveKind.Trans) && a.S < W.MinS) { W.MinS = a.S; W.MinSWhere = Tag(f, S, i); }
                var d = Math.Sqrt(Sq(a.X - c.X) + Sq(a.Y - c.Y)) / BW;
                W.Trav.Add(d);
                if (d > W.Max) { W.Max = d; W.Where = Tag(f, S, i); }
                var ds = Math.Abs(a.S - c.S);
                if (ds > W.SRate) { W.SRate = ds; W.SRateWhere = Tag(f, S, i); }
                mv = Math.Max(mv, Math.Max(d, ds * .5));
                if (q2) acc = Math.Max(acc, Math.Sqrt(Sq(a.X - 2 * c.X + q[b].X) + Sq(a.Y - 2 * c.Y + q[b].Y)) / BW);
            }
            if (q2)
            {
                if (near.Contains(JsRound((double)i / S.Fs * 60))) W.AccHand.Add(acc);
                else W.AccIn.Add(acc);
            }
            var hold = f.State == NthBeadsState.Stopped || f.State == NthBeadsState.Error || f.MoveName == "hold" || f.MoveName == "still";
            if (mv < .002 && !hold)
            {
                still++;
                if (still > W.Still) { W.Still = still; W.StillWhere = f.MoveName + "/" + f.State + " " + Tag(f, S, i); }
            }
            else still = 0;
        }
        // cuts, judged on every sample (a real cut is instant): no bead jumps, and no visible content drops by 60% between samples
        var k4 = S.Fs / 60.0;
        for (var i = 1; i < rec.Count; i++)
        {
            NthBeadsSample f = rec[i], p = rec[i - 1];
            if (!p.Present) continue;
            if (f.Present && !f.Flight && !p.Flight)
                for (var b = 0; b < 3; b++)
                {
                    NthBead a = f[b], c = p[b];
                    if (f.Hidden || p.Hidden || a.S < .05 || c.S < .05) continue;
                    var d = Math.Sqrt(Sq(a.X - c.X) + Sq(a.Y - c.Y)) / BW * k4;
                    if (d > W.CutJump) { W.CutJump = d; W.CutJumpWhere = Tag(f, S, i); }
                }
            double i0 = Ink(p), i1 = f.Present ? Ink(f) : 0;
            if (i0 > .25 / k4 && i1 < .4 * i0)
            {
                W.CutInk++;
                W.CutInkWhere = p.MoveName + "->" + (f.Present ? Tag(f, S, i) : "gone #" + S.Seed);
            }
        }
        // the same stream judged at 30 Hz (small sites)
        var s30 = (int)JsRound(S.Fs / 30.0);
        for (var i = s30; i < rec.Count; i += s30)
        {
            NthBeadsSample f = rec[i], p = rec[i - s30];
            if (!Between(f) || !Between(p) || S.CalmAt[i] || S.CalmAt[i - s30]) continue;
            for (var b = 0; b < 3; b++)
            {
                var d = Math.Sqrt(Sq(f[b].X - p[b].X) + Sq(f[b].Y - p[b].Y)) / BW;
                if (d > W.Max30) { W.Max30 = d; W.Where30 = Tag(f, S, i); }
            }
        }
        var k = S.Ex + (S.Calm ? " (Reduce Motion)" : "") + (S.Early ? " (asked during the entrance)" : "");
        if (!W.Lat.TryGetValue(k, out var l)) W.Lat[k] = l = [];
        l.Add(S.Latency);
    }

    /// <summary>The allocation gate for one session after warm-up. A real allocation is deterministic: the same seed and event times
    /// allocate again at the same sample. The runtime itself can move this thread's count by part or all of an 8 KB allocation
    /// quantum at a random moment (measured: once per test run, in a different session each time, even with the engine gone and
    /// no GC), so a session that shows bytes is replayed twice and only bytes on every replay count (ED-NEW-calculi-wu2-1).</summary>
    private static void Allocation(NthBeadsStress.Session S, double secs, double calmP, bool quiet, Tally W)
    {
        if (S.Seed <= Warmup || S.LoopAllocated == 0) return;
        var worst = S;
        for (var r = 0; r < 2 && worst.LoopAllocated > 0; r++)
        {
            var again = NthBeadsStress.Run(S.Seed, secs, calmP, S.Fs, quiet);
            if (again.LoopAllocated < worst.LoopAllocated) worst = again;
        }
        if (worst.LoopAllocated <= W.Allocated) return;
        W.Allocated = worst.LoopAllocated;
        var a = worst.Rec[worst.FirstAllocSample];
        W.AllocatedWhere = "#" + S.Seed + ", first at " + Tag(a, worst, worst.FirstAllocSample) + " (" + a.Kind + ", " + a.State + ", " + a.Mode +
            (worst.FirstAllocWithGc ? ", a GC ran" : ", no GC") + ")";
    }

    /// <summary>Sessions that warm the engine up (first use of each move, static tables) before the allocation gate counts.</summary>
    private const int Warmup = 20;

    private static void Orbit(NthBeadsStress.Session S, int i, in NthBeadsSample f, in NthBeadsSample p, bool calm,
        ref NthBeadsMoveId runMove, ref bool runCalm, Span<double> windC, Span<double> windP, Tally W)
    {
        // a new move, a new cycle of the same motif (its local time wrapped) or a Reduce Motion toggle starts a new count
        if (f.Move != runMove || calm != runCalm || p.Move != f.Move || f.T < p.T)
        {
            runMove = f.Move; runCalm = calm;
            windC.Clear(); windP.Clear();
            return;
        }
        for (var b = 0; b < 3; b++)
        {
            NthBead a = f[b], c = p[b];
            double ax = a.X - BoxCentreX, ay = a.Y - BoxCentreY, cx = c.X - BoxCentreX, cy = c.Y - BoxCentreY;
            if (Math.Sqrt(ax * ax + ay * ay) > .3 && Math.Sqrt(cx * cx + cy * cy) > .3)
            {
                windC[b] += Wrap(Math.Atan2(ay, ax) - Math.Atan2(cy, cx));
                var w = Math.Abs(windC[b]) / Turn;
                if (w > W.OrbitCentre) { W.OrbitCentre = w; W.OrbitCentreWhere = Tag(f, S, i); }
            }
            for (var o = 0; o < 3; o++)
            {
                if (o == b) continue;
                double fx = a.X - f[o].X, fy = a.Y - f[o].Y, px = c.X - p[o].X, py = c.Y - p[o].Y;
                if (Math.Sqrt(fx * fx + fy * fy) <= .3 || Math.Sqrt(px * px + py * py) <= .3) continue;
                windP[b * 3 + o] += Wrap(Math.Atan2(fy, fx) - Math.Atan2(py, px));
                var w = Math.Abs(windP[b * 3 + o]) / Turn;
                if (w > W.OrbitPair) { W.OrbitPair = w; W.OrbitPairWhere = Tag(f, S, i); }
            }
        }
    }

    private static double Ink(in NthBeadsSample x)
    {
        double m = 0;
        if (!x.Hidden)
            for (var b = 0; b < 3; b++) m = Math.Max(m, x[b].S * x[b].S * x[b].O);
        return m + x.StrokeLength / BeadWidth;
    }

    private static double Sq(double v) => v * v;

    private static double Pct(List<double> a, double q)
    {
        if (a.Count == 0) return 0;
        var s = a.ToArray();
        Array.Sort(s);
        return s[(int)Math.Floor(q * (s.Length - 1))];
    }

    private static double Max(List<double> a)
    {
        double m = 0;
        foreach (var x in a) if (x > m) m = x;
        return m;
    }

    private static double LatMax(Tally W, string k)
    {
        if (!W.Lat.TryGetValue(k, out var l)) return 0;
        double m = 0;
        var any = false;
        foreach (var x in l) if (x is { } v) { m = any ? Math.Max(m, v) : v; any = true; }
        return any ? m : 0;
    }

    /// <summary>stress.js's summary as gates, plus ED-NEW-calculi-wu2-1's additions. Returns the failed rows (empty when all pass)
    /// and writes every row to the test output.</summary>
    private List<string> Gates(Tally W, string label)
    {
        var rows = new List<(bool Ok, string Text)>();
        void Ok(bool b, string s) => rows.Add((b, s));
        static string F(double v, int d = 3) => v.ToString("F" + d, CultureInfo.InvariantCulture);
        double aIn = Max(W.AccIn), aH = Max(W.AccHand), r999 = Pct(W.AccHand, .999) / Pct(W.AccIn, .999), r99 = Pct(W.AccHand, .99) / Pct(W.AccIn, .99);
        Ok(W.Op == 0 && W.RmOp == 0, $"opacity is exactly 1 between entrance and exit: {W.Op + W.RmOp} violations (Reduce Motion included)");
        Ok(W.Pops == 0, $"no bead below 5% scale between entrance and exit: {W.Pops}");
        Ok(W.MinS >= .7, $"scale floor in motifs and wire paths >= 0.70: min {F(W.MinS, 2)} ({W.MinSWhere})");
        Ok(W.Max <= .25, $"travel per 60 Hz frame <= 0.25 bead widths: max {F(W.Max)} ({W.Where}), p99.9 {F(Pct(W.Trav, .999))}, p99 {F(Pct(W.Trav, .99))}, median {F(Pct(W.Trav, .5))}");
        Ok(W.Max30 <= .5, $"travel per 30 Hz frame (small sites) <= 0.5 bead widths: max {F(W.Max30)} ({W.Where30})");
        Ok(W.SRate <= .1, $"scale change per 60 Hz frame <= 0.10: max {F(W.SRate)} ({W.SRateWhere})");
        Ok(W.AccHand.Count > 0 && W.AccIn.Count > 0 && aH <= aIn * 1.15 && r99 <= 1.15 && r999 <= 1.15,
            $"hand-offs no sharper than the motion itself: max {F(aH / aIn, 2)}x, p99.9 {F(r999, 2)}x, p99 {F(r99, 2)}x (gate 1.15 each; {W.AccHand.Count} hand-off frames)");
        Ok(W.Still <= 15, $"never still: longest near-still stretch {W.Still} frames (holds excluded, waiting included) {W.StillWhere}");
        Ok(W.CutJump <= .6, $"no cut on any sample: largest bead step {F(W.CutJump)} bead widths per 60 Hz frame (gate 0.6) ({W.CutJumpWhere})");
        Ok(W.CutInk == 0, $"no visible content vanishes between two samples: {W.CutInk} {W.CutInkWhere}");
        Ok(W.EnvX0 >= EnvelopeX0 && W.EnvX1 <= EnvelopeX1 && W.EnvY0 >= EnvelopeY0 && W.EnvY1 <= EnvelopeY1,
            $"inside the envelope: x {F(W.EnvX0, 2)}..{F(W.EnvX1, 2)} of {EnvelopeX0}..{EnvelopeX1}, y {F(W.EnvY0, 2)}..{F(W.EnvY1, 2)} of {EnvelopeY0}..{EnvelopeY1}");
        Ok(W.RmMax <= .3 && W.RmSRate <= .1, $"Reduce Motion slides: max {F(W.RmMax)} bead widths per frame (gate 0.3), scale {F(W.RmSRate)} per frame ({W.RmWhere})");
        Ok(W.Flight <= .08, $"flight: largest step {F(W.Flight * 100, 1)}% of the flight per frame (gate 8%)");
        Ok(LatMax(W, "occasus") <= .95, $"Occasus: Finish to gone <= 0.95 s: max {F(LatMax(W, "occasus"), 2)} s");
        Ok(LatMax(W, "stop") <= .6, $"Stop: Stop to gone <= 0.6 s: max {F(LatMax(W, "stop"), 2)} s");
        double early = 0;
        foreach (var k in W.Lat.Keys) if (k.Contains("entrance", StringComparison.Ordinal) && !k.Contains("fail", StringComparison.Ordinal)) early = Math.Max(early, LatMax(W, k));
        Ok(early <= 1.5, $"an exit asked during the entrance waits for its hand-off, then plays: max {F(early, 2)} s (gate 1.5 s)");
        // ED-NEW-calculi-wu2-1 additions
        var never = 0;
        foreach (var l in W.Lat.Values) foreach (var x in l) if (x is null) never++;
        Ok(never == 0, $"every last exit reaches gone: {never} sessions never went");
        Ok(LatMax(W, "fail") <= 2.4, $"Error: the shake and the designed hold, Fail to gone <= 2.4 s: max {F(LatMax(W, "fail"), 2)} s (designed 2.32)");
        // no orbits (section 1.1): within one move no bead circles another (a hop over a bead is half a turn; a satellite is a whole
        // one) and none laps the n (node, 200 sessions: 0.50 and 0.89, Clepsydra)
        Ok(W.OrbitPair <= .6 && W.OrbitCentre < 1,
            $"no orbits: largest winding in one move {F(W.OrbitPair)} turns about another bead ({W.OrbitPairWhere}) (gate 0.6), {F(W.OrbitCentre)} about the n ({W.OrbitCentreWhere}) (gate under 1)");
        Ok(W.Allocated == 0, $"no allocation in the sampling loop after {Warmup} warm-up sessions: max {W.Allocated} bytes {W.AllocatedWhere}");
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{label}: {W.Sessions} sessions ({W.Early} exit during the entrance, {W.Reenter} new turn while leaving), {W.Frames} frames at 60 Hz, {W.RmFrames} Reduce Motion frames");
        foreach (var (ok, text) in rows) sb.AppendLine((ok ? "PASS " : "FAIL ") + text);
        foreach (var (k, l) in W.Lat.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.AppendLine(CultureInfo.InvariantCulture, $"      exit {k} n {l.Count} max {F(LatMax(W, k), 2)}");
        output.WriteLine(sb.ToString());
        return rows.Where(r => !r.Ok).Select(r => r.Text).ToList();
    }

    private Tally Run(int sessions, double secs, double calmP, bool quiet = false)
    {
        var W = new Tally();
        for (var s = 1; s <= sessions; s++)
        {
            var S = NthBeadsStress.Run(s, secs, calmP, 240, quiet);
            Judge(S, W);
            Allocation(S, secs, calmP, quiet, W);
        }
        return W;
    }

    [Fact]
    public void Two_hundred_turns_pass_every_frame_gate_at_60_and_30_hz_in_under_five_seconds()
    {
        // stress.js's defaults: 200 sessions of 40 s, 25% Reduce Motion. Node's figures for the same pair-A sessions (ED-NEW-calculi-wu2-1):
        // 218,057 frames, travel max 0.221 (Gradus), 30 Hz 0.439, scale rate 0.088, hand-offs 1.06x / 0.92x / 0.85x, still 10 frames.
        var sw = Stopwatch.StartNew();
        var W = Run(200, 40, .25);
        sw.Stop();
        var failed = Gates(W, "CALM 0.25");
        output.WriteLine($"200 sessions judged in {sw.Elapsed.TotalSeconds:F2} s");
        Assert.True(failed.Count == 0, string.Join("\n", failed));
        // the session mix stress.js's defaults give: exits during the entrance, new turns while leaving, Reduce Motion frames
        Assert.InRange(W.Early, 20, 40);
        Assert.InRange(W.Reenter, 45, 70);
        Assert.True(W.Frames > 200_000 && W.RmFrames > 150_000, $"{W.Frames} frames, {W.RmFrames} Reduce Motion frames");
        Assert.True(sw.Elapsed.TotalSeconds < 5, $"200 sessions took {sw.Elapsed.TotalSeconds:F2} s (gate 5 s)");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Every_gate_holds_with_reduce_motion_never_and_always(double calmP)
    {
        var W = Run(100, 40, calmP);
        var failed = Gates(W, "CALM " + calmP.ToString(CultureInfo.InvariantCulture));
        Assert.True(failed.Count == 0, string.Join("\n", failed));
        if (calmP == 0) Assert.Equal(0, W.RmFrames);
        else Assert.True(W.RmFrames > W.Frames, $"{W.RmFrames} Reduce Motion frames, {W.Frames} others");
    }

    [Fact]
    public void Quiet_engines_for_small_sites_pass_every_gate()
    {
        var W = Run(100, 40, .25, quiet: true);
        var failed = Gates(W, "quiet");
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Fact]
    public void Same_seed_and_event_times_give_identical_samples_at_240_and_30_hz()
    {
        // stress.js's determinism(K, 12, 20): presence and every bead's x, y and scale. The reference gates 1e-9; the port is exact.
        double worst = 0;
        for (var s = 1; s <= 12; s++)
        {
            var A = NthBeadsStress.Run(s, 20, .25, 240);
            var B = NthBeadsStress.Run(s, 20, .25, 30);
            for (var i = 0; i < B.Rec.Count; i++)
            {
                // the run's last 30 Hz sample can fall past the 240 Hz run's end (the end rounds per rate); both are gone there
                if (i * 8 >= A.Rec.Count) { Assert.True(!B.Rec[i].Present && !A.Rec[^1].Present, $"seed {s}: present past the end"); continue; }
                NthBeadsSample a = A.Rec[i * 8], b = B.Rec[i];
                Assert.True(a.Present == b.Present, $"seed {s}: presence differs at {i / 30.0:F3} s");
                if (!a.Present) continue;
                for (var k = 0; k < 3; k++)
                    worst = Math.Max(worst, Math.Max(Math.Abs(a[k].X - b[k].X), Math.Max(Math.Abs(a[k].Y - b[k].Y), Math.Abs(a[k].S - b[k].S))));
            }
            Assert.Equal(A.Gone.Count, B.Gone.Count);
            for (var g = 0; g < A.Gone.Count; g++) Assert.Equal(A.Gone[g], B.Gone[g]);
        }
        Assert.Equal(0, worst);
    }

    [Fact]
    public void Line_reflows_never_jump_the_beads_on_screen()
    {
        // Section 9: the beads keep their place on screen when the glyph moves, then glide after it. On screen is the glyph's layout
        // origin (every reflow so far) + the bead + the flow offset. Judged on every sample like the cut gate (0.6 bead widths per
        // 60 Hz frame); node measures 0.440 for these sessions (a Reduce Motion glide while a reflow decays).
        double worst = 0;
        var where = "";
        for (var s = 1; s <= 200; s++)
        {
            var S = NthBeadsStress.Run(s, 40, .25, 240, reflow: true);
            var k4 = S.Fs / 60.0;
            for (var i = 1; i < S.Rec.Count; i++)
            {
                NthBeadsSample f = S.Rec[i], p = S.Rec[i - 1];
                if (!f.Present || !p.Present || f.Hidden || p.Hidden) continue;
                for (var b = 0; b < 3; b++)
                {
                    if (f[b].S < .05 || p[b].S < .05) continue;
                    double fx = S.OriginX[i] + f[b].X + f.FlowX, fy = S.OriginY[i] + f[b].Y + f.FlowY;
                    double px = S.OriginX[i - 1] + p[b].X + p.FlowX, py = S.OriginY[i - 1] + p[b].Y + p.FlowY;
                    var d = Math.Sqrt(Sq(fx - px) + Sq(fy - py)) / BeadWidth * k4;
                    if (d > worst) { worst = d; where = Tag(f, S, i); }
                }
            }
            Assert.True(S.OriginX.Count == S.Rec.Count && S.OriginX[^1] != 0, $"#{s}: no reflow was applied");
        }
        output.WriteLine($"largest on-screen step with reflows {worst:F3} bead widths per 60 Hz frame ({where})");
        Assert.True(worst <= .6, $"largest on-screen step {worst:F3} bead widths per 60 Hz frame ({where}) (gate 0.6)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_whole_line_move_glides_at_most_point_four_two_bead_widths_a_frame(bool calm)
    {
        // Section 9: "a whole 26 px line at 18 DIP (7.4 bead widths) peaks at 0.41 bead widths per 60 Hz frame" (node: 0.413).
        var e = new NthBeadsEngine(5);
        e.SetState(NthBeadsState.Tool, 0);
        e.SetCalm(calm, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        var dy = 26 * Box / 18;
        Assert.InRange(dy / BeadWidth, 7.4, 7.5);
        double peak = 0, prev = 0;
        for (var i = 0; i <= 240 * 4; i++)
        {
            var t = i / 240.0;
            if (i == 480) e.Reflow(0, dy, t);
            e.Update(t);
            var f = e.Frame();
            if (i % 4 != 0) continue;
            var y = (i >= 480 ? dy : 0) + f.FlowY; // the glyph's origin plus the flow: the offset the beads ride on screen
            if (i > 480) peak = Math.Max(peak, Math.Abs(y - prev) / BeadWidth);
            prev = y;
            if (i == 480) Assert.Equal(0, y, 9); // at the call the beads stay exactly where they were on screen
        }
        Assert.True(peak is > .3 and <= .42, $"line glide peak {peak:F3} bead widths per 60 Hz frame (gate 0.42, design 0.41)");
    }
}
