using System.Runtime.CompilerServices;
using System.Text.Json;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Tests.Nth;

// Calculi WU1 (ED-NEW-calculi-wu1-1; E:/Droppy/work/design/working-motion/DESIGN.md section 14, WU1): the engine, pure C#,
// against the design's numbers and against the reference engine's own samples (Fixtures/nth-beads-reference.json, made by
// Fixtures/nth-beads-reference.js from reference/engine.js and the stress.js session).
public sealed class NthBeadsEngineTests
{
    private const double Frame60 = 1.0 / 60;

    private static readonly NthBeadsMotif[] AllMotifs = Enum.GetValues<NthBeadsMotif>().Where(m => m != NthBeadsMotif.None).ToArray();

    private static readonly Dictionary<NthBeadsState, NthBeadsMotif[]> Families = new()
    {
        [NthBeadsState.Thinking] = [NthBeadsMotif.Rota, NthBeadsMotif.Libra, NthBeadsMotif.Halitus],
        [NthBeadsState.Tool] = [NthBeadsMotif.Gradus, NthBeadsMotif.Machina, NthBeadsMotif.Clepsydra],
        [NthBeadsState.Streaming] = [NthBeadsMotif.Scriptor, NthBeadsMotif.Textus, NthBeadsMotif.Linea],
        [NthBeadsState.Hydra] = [NthBeadsMotif.Concentus, NthBeadsMotif.Vicissim, NthBeadsMotif.Commutatio],
    };

    private static NthBeadsRecipe Cycle(NthBeadsMotif m, Func<double> r)
    {
        var x = NthBeadsRecipe.Of(NthBeadsMoves.MoveOf(m), NthBeadsMoves.HubOf(m));
        x.Motif = m;
        x.Tempo = 1 + .08 * (r() * 2 - 1); x.Pick = r(); x.Pick2 = r(); x.Va = r() * 2 - 1; x.Vb = r() * 2 - 1; x.Vc = r() * 2 - 1;
        return x;
    }

    private static double Dist(NthBead a, NthBeadsPoint p) => Math.Sqrt((a.X - p.X) * (a.X - p.X) + (a.Y - p.Y) * (a.Y - p.Y));
    private static double Dist(NthBead a, NthBead b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---------------- hubs and motifs ----------------

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Hub_tables_match_NthGlyph_Regular()
    {
        var g = NthGlyph.Regular;
        double r = (g.Right - g.Left) / 2.0, cx = (g.Left + g.Right) / 2.0, cy = g.Top;
        Assert.Equal(44, r);
        // stems at x = +-1, shoulders at y 0, feet at 1.64, the dot at (1.64, -1.14): the glyph rounded to two decimals
        Assert.Equal((g.Left - cx) / r, ShoulderLeft.X, 9);
        Assert.Equal((g.Right - cx) / r, ShoulderRight.X, 9);
        Assert.Equal(Foot, (g.Bottom - cy) / r, 2);
        Assert.Equal((g.BeadX - cx) / r, Dot.X, 2);
        Assert.Equal((g.BeadY - cy) / r, Dot.Y, 2);
        Assert.Equal(new NthBeadsPoint(0, -1), Crown);
        // the beads are never smaller than the logo's dot (0.318), so the envelope gate is on the safe side
        Assert.True(BeadRadius >= g.BeadRadius / r);
        Assert.Equal(new NthBeadsPoint(0, .42), Cursor);
        // the hub poses (section 2), slots left to right
        Assert.Equal([Dot, Dot, Dot], Pose(NthBeadsHub.Punctum));
        Assert.Equal([ShoulderLeft, Crown, ShoulderRight], Pose(NthBeadsHub.Arcus));
        Assert.Equal([FootLeft, FootMiddle, FootRight], Pose(NthBeadsHub.Pedes));
        Assert.Equal([FootLeft, Dot, FootRight], Pose(NthBeadsHub.Signum));
        Assert.Equal([FootLeft, Cursor, FootRight], Pose(NthBeadsHub.Clavis));
        Assert.Equal([FootLeft, Crown, FootRight], Pose(NthBeadsHub.Tripus));
        // the wire passes the feet, shoulders and crown; Rota's back run is 4.94 units
        AssertPoint(FootLeft, Wire(0));
        AssertPoint(ShoulderLeft, Wire(SLeft));
        AssertPoint(Crown, Wire(SCrown));
        AssertPoint(ShoulderRight, Wire(SRight));
        AssertPoint(FootRight, Wire(WireLength));
        Assert.Equal(4.94, BackLength, 2);
        AssertPoint(ShoulderRight, BackRun(0));
        AssertPoint(ShoulderLeft, BackRun(BackLength));
        // the envelope (section 9): 1.05 x 1.23 of the n box
        Assert.Equal(1.05, (EnvelopeX1 - EnvelopeX0) / Box, 2);
        Assert.Equal(1.23, (EnvelopeY1 - EnvelopeY0) / Box, 2);
    }

    private static NthBeadsPoint[] Pose(NthBeadsHub h) => [HubPoint(h, 0), HubPoint(h, 1), HubPoint(h, 2)];

    private static void AssertPoint(NthBeadsPoint want, NthBeadsPoint got)
    {
        Assert.Equal(want.X, got.X, 9);
        Assert.Equal(want.Y, got.Y, 9);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-3")]
    public void Every_motif_starts_on_its_hub_and_ends_on_it_after_its_permutation_for_fifty_variations()
    {
        var r = NthBeadsStress.Rng(5);
        foreach (var motif in AllMotifs)
        {
            var hub = NthBeadsMoves.HubOf(motif);
            for (var i = 0; i < 50; i++)
            {
                var m = NthBeadsMoves.Build(Cycle(motif, r), []);
                Assert.Equal(NthBeadsMoveKind.Motif, m.Kind);
                Assert.Equal(hub, m.HubIn);
                Assert.Equal(hub, m.HubOut);
                for (var pass = 0; pass < 2; pass++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var a = NthBeadsMoves.At(m, 0, k);
                        var b = NthBeadsMoves.At(m, m.T, k);
                        var at0 = HubPoint(hub, k);
                        var atT = HubPoint(hub, m.Perm(k));
                        Assert.True(Dist(a, at0) < 1e-12 && Math.Abs(a.S - 1) < 1e-12 && a.O == 1, $"{motif} slot {k} at t = 0: {a}");
                        Assert.True(Dist(b, atT) < 1e-12 && Math.Abs(b.S - 1) < 1e-12 && b.O == 1, $"{motif} slot {k} at t = T: {b}");
                    }
                    if (!NthBeadsMoves.Mirrors(motif)) break;
                    NthBeadsMoves.Mirror(ref m); // a mirrored dwell keeps the same promise (hubs are symmetric)
                }
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Every_wire_path_runs_exactly_from_its_hub_to_its_hub()
    {
        NthBeadsMoveId[] paths = [NthBeadsMoveId.Descensus, NthBeadsMoveId.Ascensus, NthBeadsMoveId.Flick, NthBeadsMoveId.Reflick,
            NthBeadsMoveId.Toss, NthBeadsMoveId.Catch, NthBeadsMoveId.Levo, NthBeadsMoveId.Demitto, NthBeadsMoveId.Statio,
            NthBeadsMoveId.Fornix, NthBeadsMoveId.Surgo, NthBeadsMoveId.Cado];
        double[] durations = [.92, .98, .84, .84, 1.1, 1.05, .62, .62, .8, .86, 1.02, .96];
        for (var i = 0; i < paths.Length; i++)
        {
            var m = NthBeadsMoves.Build(NthBeadsRecipe.Of(paths[i]), []);
            Assert.Equal(paths[i], NthBeadsMoves.TransFor(m.HubIn, m.HubOut));
            Assert.Equal(durations[i], m.T, 9);
            for (var k = 0; k < 3; k++)
            {
                Assert.True(Dist(NthBeadsMoves.At(m, 0, k), HubPoint(m.HubIn, k)) < 1e-9, $"{paths[i]} slot {k} start");
                Assert.True(Dist(NthBeadsMoves.At(m, m.T, k), HubPoint(m.HubOut, k)) < 1e-9, $"{paths[i]} slot {k} end");
                Assert.Equal(1, NthBeadsMoves.At(m, m.T, k).S, 9);
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void The_turn_seed_is_FNV_1a_of_the_turn_id()
    {
        Assert.Equal(0x811c9dc5u, NthBeadsEngine.SeedFrom(""));
        Assert.Equal(0xe40c292cu, NthBeadsEngine.SeedFrom("a"));
        Assert.Equal(0xbf9cf968u, NthBeadsEngine.SeedFrom("foobar"));
    }

    // ---------------- the reference, replayed ----------------

    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "nth-beads-reference.json");

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Replays_the_reference_engine_sample_for_sample_on_its_stress_sessions()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var cases = doc.RootElement.GetProperty("cases");
        var stride = doc.RootElement.GetProperty("stride").GetInt32();
        Assert.Equal(20, cases.GetArrayLength());
        double worst = 0;
        foreach (var c in cases.EnumerateArray())
        {
            var seed = c.GetProperty("seed").GetInt32();
            var tag = c.GetProperty("tag").GetString() + " #" + seed;
            var S = NthBeadsStress.Run(seed, c.GetProperty("secs").GetDouble(), c.GetProperty("calmP").GetDouble(), 240,
                c.GetProperty("quiet").GetBoolean(), c.GetProperty("reflow").GetBoolean());
            Assert.Equal(c.GetProperty("en").GetString(), S.En);
            Assert.Equal(c.GetProperty("ex").GetString(), S.Ex);
            Assert.Equal(c.GetProperty("early").GetBoolean(), S.Early);
            Assert.Equal(c.GetProperty("reenter").GetBoolean(), S.Reenter);
            Assert.Equal(c.GetProperty("samples").GetInt32(), S.Rec.Count);
            var hand = c.GetProperty("handoffs").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.True(hand.Length == S.Handoffs.Count, $"{tag}: {S.Handoffs.Count} hand-offs, reference {hand.Length}");
            for (var i = 0; i < hand.Length; i++) Assert.True(Math.Abs(hand[i] - S.Handoffs[i]) < 1e-6, $"{tag}: hand-off {i} at {S.Handoffs[i]}, reference {hand[i]}");
            var gone = c.GetProperty("gone").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(gone.Length, S.Gone.Count);
            for (var i = 0; i < gone.Length; i++) Assert.True(Math.Abs(gone[i] - S.Gone[i]) < 1e-6, $"{tag}: gone {i}");
            foreach (var f in c.GetProperty("frames").EnumerateArray())
            {
                var i = f[0].GetInt32();
                Assert.Equal(0, i % stride);
                var s = S.Rec[i];
                if (f.GetArrayLength() == 1) { Assert.False(s.Present, $"{tag}@{i}"); continue; }
                Assert.True(s.Present, $"{tag}@{i}");
                Assert.True(f[1].GetString() == s.MoveName, $"{tag}@{i / 240.0:0.000}s: {s.MoveName}, reference {f[1].GetString()}");
                worst = Math.Max(worst, Math.Abs(f[3].GetDouble() - s.StrokeLength));
                worst = Math.Max(worst, Math.Abs(f[4].GetDouble() - s.FlowX));
                worst = Math.Max(worst, Math.Abs(f[5].GetDouble() - s.FlowY));
                for (var b = 0; b < 3; b++)
                {
                    var bead = s[b];
                    worst = Math.Max(worst, Math.Abs(f[6 + 5 * b].GetDouble() - bead.X));
                    worst = Math.Max(worst, Math.Abs(f[7 + 5 * b].GetDouble() - bead.Y));
                    worst = Math.Max(worst, Math.Abs(f[8 + 5 * b].GetDouble() - bead.S));
                    Assert.Equal(f[9 + 5 * b].GetDouble(), bead.O, 6);
                    Assert.Equal(f[10 + 5 * b].GetInt32() == 1, s.Hidden);
                }
                Assert.True(worst < 2e-5, $"{tag}@{i / 240.0:0.000}s ({s.MoveName}): off the reference by {worst}");
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void The_same_seed_and_event_times_give_identical_samples_at_240_Hz_and_at_30_Hz()
    {
        for (var seed = 1; seed <= 12; seed++)
        {
            foreach (var quiet in new[] { false, true })
            {
                var a = NthBeadsStress.Run(seed, 20, .25, 240, quiet, reflow: seed % 3 == 0);
                var b = NthBeadsStress.Run(seed, 20, .25, 30, quiet, reflow: seed % 3 == 0);
                Assert.True(b.Rec.Count > 500);
                for (var i = 0; i < b.Rec.Count && i * 8 < a.Rec.Count; i++)
                {
                    NthBeadsSample x = a.Rec[i * 8], y = b.Rec[i];
                    Assert.Equal(x.Present, y.Present);
                    if (!x.Present) continue;
                    Assert.Equal(x.Move, y.Move);
                    for (var k = 0; k < 3; k++) Assert.Equal(x[k], y[k]); // exactly, not within a tolerance
                    Assert.Equal(x.FlowX, y.FlowX);
                    Assert.Equal(x.FlowY, y.FlowY);
                }
            }
        }
    }

    // ---------------- picker ----------------

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void The_picker_never_repeats_the_current_motif_or_the_last_two_and_dwells_at_least_two_cycles()
    {
        foreach (var state in new[] { NthBeadsState.Thinking, NthBeadsState.Streaming, NthBeadsState.Hydra, NthBeadsState.Tool })
        {
            for (uint seed = 1; seed <= 6; seed++)
            {
                var e = new NthBeadsEngine(seed);
                e.SetState(state, 0);
                var changes = new List<(NthBeadsMotif Motif, double At)>();
                var cycles = new Dictionary<int, int>();
                e.MoveBegan += t =>
                {
                    if (changes.Count == 0 || changes[^1].Motif != e.Motif) changes.Add((e.Motif, t));
                    var m = e.CurrentMove;
                    if (m.Kind == NthBeadsMoveKind.Motif && m.Motif == e.Motif) cycles[changes.Count - 1] = cycles.GetValueOrDefault(changes.Count - 1) + 1;
                };
                e.Enter(NthBeadsEntrance.Ortus, 0);
                // a step now and then keeps the tool state off its long-running family, as the live line does
                for (var i = 1; i <= 120 * 60; i++)
                {
                    e.Update(i * Frame60);
                    if (state == NthBeadsState.Tool && i % (7 * 60) == 0) e.Step(i * Frame60);
                }
                Assert.True(changes.Count >= 8, $"{state} #{seed}: only {changes.Count} motifs in 120 s");
                var family = Families[state];
                for (var i = 1; i < changes.Count; i++)
                {
                    var current = changes[i - 1].Motif;
                    Assert.NotEqual(current, changes[i].Motif);
                    // the last two before the current one, as the engine keeps them
                    var recent = new[] { i >= 2 ? changes[i - 2].Motif : NthBeadsMotif.None, i >= 3 ? changes[i - 3].Motif : NthBeadsMotif.None };
                    var allowed = family.Where(m => m != current && !recent.Contains(m)).ToArray();
                    if (allowed.Length > 0) Assert.Contains(changes[i].Motif, allowed);
                }
                // a dwell that ended by itself (not by a step) held two cycles and 5 s at least
                if (state != NthBeadsState.Tool)
                    for (var i = 0; i < changes.Count - 1; i++)
                    {
                        Assert.True(cycles.GetValueOrDefault(i) >= 2, $"{state} #{seed}: {changes[i].Motif} ran {cycles.GetValueOrDefault(i)} cycles");
                        Assert.True(changes[i + 1].At - changes[i].At >= 5, $"{state} #{seed}: {changes[i].Motif} dwelt {changes[i + 1].At - changes[i].At:0.00} s");
                    }
            }
        }
    }

    // ---------------- steps ----------------

    private static void RunTo(NthBeadsEngine e, double t)
    {
        for (var x = Math.Ceiling(e.Clock * 240) / 240; x <= t + 1e-12; x += 1.0 / 240) e.Update(x);
        e.Update(t);
    }

    private static double RunUntilMotif(NthBeadsEngine e, double from)
    {
        var t = from;
        RunTo(e, t);
        while (e.CurrentMove.Kind != NthBeadsMoveKind.Motif || e.CurrentMove.T * e.CurrentMove.Handoff - (t - e.MoveStart) < .5)
        {
            t += 1.0 / 240;
            e.Update(t);
            Assert.True(t < from + 30, "no motif");
        }
        return t;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Steps_beat_after_4_s_knot_between_1_6_and_4_s_and_do_nothing_sooner()
    {
        var e = new NthBeadsEngine(3);
        e.SetState(NthBeadsState.Tool, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        var t = RunUntilMotif(e, 3);
        Assert.True(e.Step(t)); // the first step of the turn: a Locus beat, at once (the hand-off is more than 0.35 s away)
        Assert.Equal(NthBeadsMoveId.Collige, e.MoveId);
        var beat = t;
        Assert.False(e.Step(beat + .5)); // a step during a beat, and under 1.6 s since it: only the text changes
        t = RunUntilMotif(e, beat + 2.4);
        Assert.True(t - beat < NthBeadsEngine.StepFull);
        Assert.True(e.Step(t));
        Assert.Equal(NthBeadsMoveId.Nodus, e.MoveId);
        Assert.Equal(.72, e.CurrentMove.T, 9);
        var knot = t;
        t = RunUntilMotif(e, knot + 1.0);
        Assert.False(e.Step(t)); // a Nodus is a beat too: under 1.6 s since it nothing moves
        t = RunUntilMotif(e, knot + 4.2);
        Assert.True(e.Step(t));
        Assert.Equal(NthBeadsMoveId.Collige, e.MoveId);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    [InlineData(NthBeadsState.Connecting, false, false)]
    [InlineData(NthBeadsState.Waiting, false, false)]
    [InlineData(NthBeadsState.Tool, true, false)]
    [InlineData(NthBeadsState.Streaming, false, true)]
    public void Connecting_waiting_quiet_and_reduce_motion_engines_ignore_steps(NthBeadsState state, bool quiet, bool calm)
    {
        var e = new NthBeadsEngine(9, quiet);
        e.SetState(state, 0);
        e.SetCalm(calm, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        for (var t = 3.0; t < 30; t += .9)
        {
            RunTo(e, t);
            var before = e.MoveId;
            var start = e.MoveStart;
            Assert.False(e.Step(t));
            Assert.Equal(before, e.MoveId);
            Assert.Equal(start, e.MoveStart);
        }
    }

    // ---------------- retargeting ----------------

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void A_state_change_during_Collige_sends_the_Pausa_to_the_new_hub_and_during_Pausa_sends_the_Emitte()
    {
        var e = new NthBeadsEngine(4);
        e.SetState(NthBeadsState.Thinking, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        var t = RunUntilMotif(e, 3);
        Assert.True(e.Step(t));
        Assert.Equal(NthBeadsMoveId.Collige, e.MoveId);
        e.SetState(NthBeadsState.Waiting, t + .1); // Signum, during the gather
        Assert.Equal(NthBeadsMoveId.Collige, e.MoveId);
        Assert.Equal(2, e.QueueCount);
        Assert.Equal(NthBeadsMoveId.Pausa, e.Queued(0).Id);
        Assert.Equal(NthBeadsHub.Signum, e.Queued(0).Hub);
        Assert.Equal(NthBeadsMoveId.Emitte, e.Queued(1).Id);
        Assert.Equal(NthBeadsHub.Signum, e.Queued(1).Hub);
        while (e.MoveId != NthBeadsMoveId.Pausa) { t += 1.0 / 240; e.Update(t); }
        var c = HubCentre(NthBeadsHub.Signum);
        Assert.Equal(c.X, e.CurrentMove.Qx, 12);
        Assert.Equal(c.Y, e.CurrentMove.Qy, 12);
        // during the Pausa the Emitte goes to the newest hub (Hydra's Tripus)
        e.SetState(NthBeadsState.Hydra, t + .05);
        Assert.Equal(NthBeadsMoveId.Pausa, e.MoveId);
        Assert.Equal(1, e.QueueCount);
        Assert.Equal(NthBeadsMoveId.Emitte, e.Queued(0).Id);
        Assert.Equal(NthBeadsHub.Tripus, e.Queued(0).Hub);
        while (e.MoveId != NthBeadsMoveId.Emitte) { t += 1.0 / 240; e.Update(t); }
        Assert.Equal(NthBeadsHub.Tripus, e.CurrentMove.HubOut);
        while (e.MoveId == NthBeadsMoveId.Emitte) { t += 1.0 / 240; e.Update(t); }
        Assert.Equal(NthBeadsHub.Tripus, NthBeadsMoves.HubOf(e.Motif));
        Assert.Equal(NthBeadsMoveKind.Motif, e.CurrentMove.Kind);
    }

    // ---------------- edges ----------------

    [Theory]
    [Trait("Decision", "ED-NEW-calculi-wu1-2")]
    [InlineData("occasus")]
    [InlineData("fail")]
    [InlineData("stop")]
    public void Enter_during_every_exit_rejoins_from_the_current_pose_with_no_jump(string exit)
    {
        foreach (var delay in new[] { .05, .15, .3, .45, .6, .72 })
        {
            for (uint seed = 1; seed <= 4; seed++)
            {
                var e = new NthBeadsEngine(seed);
                e.SetState(NthBeadsState.Streaming, 0);
                e.Enter(NthBeadsEntrance.Ortus, 0);
                var t = RunUntilMotif(e, 4 + seed * .37);
                if (exit == "fail") e.Fail(t); else if (exit == "stop") e.Stop(t); else e.Finish(t);
                var at = t + delay;
                RunTo(e, at);
                if (e.Mode != NthBeadsMode.Exiting) continue; // already gone: a fresh entrance, not this case
                var before = e.Frame(at);
                e.Enter(NthBeadsEntrance.Ortus, at);
                Assert.Equal(NthBeadsMode.Running, e.Mode);
                Assert.Equal(NthBeadsMoveId.Rejoin, e.MoveId);
                var after = e.Frame(at);
                for (var k = 0; k < 3; k++)
                {
                    Assert.True(Dist(before[k], after[k]) < 1e-9, $"{exit} +{delay}s #{seed}: bead {k} jumped");
                    Assert.Equal(before[k].S, after[k].S, 9);
                }
                // and it carries on smoothly. Section 5.2's measured 0.15 bead widths and 0.11 scale per 60 Hz frame hold for
                // Occasus asked up to 0.6 s before; later (the bead is all but gone) and after an error or a stop, the reference
                // itself regrows like Ortus (ED-NEW-calculi-wu1-4): the travel gate of section 13.1 (0.25), no shrink faster
                // than 0.11 a frame (nothing vanishes) and no growth faster than Ortus's own pop (0.30).
                var strict = exit == "occasus" && delay <= .6;
                double travel = strict ? .15 : .25, grow = strict ? .11 : .30;
                var prev = after;
                for (var x = at + Frame60; x < at + 1.2; x += Frame60)
                {
                    e.Update(x);
                    var f = e.Frame();
                    for (var k = 0; k < 3; k++)
                    {
                        Assert.True(Dist(prev[k], f[k]) / BeadWidth <= travel, $"{exit} +{delay}s #{seed}: bead {k} {Dist(prev[k], f[k]) / BeadWidth:0.000} bead widths at {x - at:0.00}s");
                        Assert.True(prev[k].S - f[k].S <= .11 && f[k].S - prev[k].S <= grow, $"{exit} +{delay}s #{seed}: scale {prev[k].S} -> {f[k].S}");
                        Assert.Equal(1, f[k].O);
                    }
                    prev = f;
                }
            }
        }
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calculi-wu1-2")]
    [InlineData("occasus", 1.4)]
    [InlineData("fail", 2.9)]
    [InlineData("stop", 1.1)]
    public void Finish_fail_and_stop_during_Scriptio_wait_for_its_hand_off_and_never_cut_the_stroke(string exit, double goneBy)
    {
        foreach (var ask in new[] { .02, .1, .2, .27, .35, .5 })
        {
            var e = new NthBeadsEngine(11);
            e.SetState(NthBeadsState.Thinking, 0);
            double? gone = null;
            e.Gone += t => gone = t;
            e.Enter(NthBeadsEntrance.Scriptio, 0);
            double prevInk = 0;
            for (var i = 1; i <= 240 * 4; i++)
            {
                var t = i / 240.0;
                if (Math.Abs(t - ask) < .5 / 240) { if (exit == "fail") e.Fail(t); else if (exit == "stop") e.Stop(t); else e.Finish(t); }
                e.Update(t);
                var f = e.Frame();
                if (t < .55 - 1e-9) Assert.Equal(NthBeadsMoveId.Scriptio, f.Move); // the write and bead-up play out
                if (!f.Present) break;
                var ink = f.Hidden ? f.StrokeLength / BeadWidth : Math.Max(f.B0.S * f.B0.S, Math.Max(f.B1.S * f.B1.S, f.B2.S * f.B2.S));
                if (prevInk > .25 / 4) Assert.True(ink >= .4 * prevInk, $"{exit} asked at {ask}: the content dropped at {t:0.000}s");
                prevInk = ink;
            }
            Assert.NotNull(gone);
            Assert.True(gone <= goneBy, $"{exit} asked at {ask}: gone at {gone:0.00} s");
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Reflow_keeps_the_beads_on_screen_at_the_call_and_decays_to_zero_within_tau()
    {
        var e = new NthBeadsEngine(2);
        e.SetState(NthBeadsState.Tool, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        var t = RunUntilMotif(e, 3);
        // the glyph moves down a line (26 px at 18 DIP is 7.4 bead widths) and left a little
        double dx = -.3, dy = 7.4 * BeadWidth;
        var before = e.Frame(t);
        e.Reflow(dx, dy, t);
        var after = e.Frame(t);
        for (var k = 0; k < 3; k++)
        {
            Assert.Equal(before[k].X + before.FlowX, after[k].X + after.FlowX + dx, 12);
            Assert.Equal(before[k].Y + before.FlowY, after[k].Y + after.FlowY + dy, 12);
        }
        var mag = Math.Sqrt(dx * dx + dy * dy) / BeadWidth;
        var tau = Math.Clamp(.2 + .04 * mag, .2, .45);
        // a second move mid-glide keeps the screen position too, and the glide's speed carries on (velocity-continuous)
        var mid = t + tau / 2;
        RunTo(e, mid);
        double H = NthBeadsEngine.H, y0 = e.Frame(mid).FlowY, y1 = e.Frame(mid - H).FlowY, y2 = e.Frame(mid - 2 * H).FlowY;
        e.Reflow(0, -dy / 2, mid);
        var m1 = e.Frame(mid);
        Assert.Equal(y0, m1.FlowY - dy / 2, 12);
        double vBefore = (3 * y0 - 4 * y1 + y2) / (2 * H),
            vAfter = (-3 * m1.FlowY + 4 * e.Frame(mid + H).FlowY - e.Frame(mid + 2 * H).FlowY) / (2 * H);
        Assert.True(Math.Abs(vBefore - vAfter) < .05 * Math.Abs(vBefore) + .3, $"flow speed {vBefore} -> {vAfter}");
        var tau2 = Math.Clamp(.2 + .04 * Math.Sqrt(m1.FlowX * m1.FlowX + m1.FlowY * m1.FlowY) / BeadWidth, .2, .45);
        RunTo(e, mid + tau2 + 1e-6);
        var end = e.Frame();
        Assert.Equal(0, end.FlowX);
        Assert.Equal(0, end.FlowY);
        // the glide never takes more than 0.45 bead widths in a 60 Hz frame for a whole line
        var e2 = new NthBeadsEngine(2);
        e2.SetState(NthBeadsState.Tool, 0);
        e2.Enter(NthBeadsEntrance.Ortus, 0);
        var t2 = RunUntilMotif(e2, 3);
        e2.Reflow(0, 7.4 * BeadWidth, t2);
        var p = e2.Frame(t2);
        for (var x = t2 + Frame60; x < t2 + .5; x += Frame60)
        {
            e2.Update(x);
            var f = e2.Frame();
            Assert.True(Math.Abs(f.FlowY - p.FlowY) / BeadWidth <= .45, $"line glide {Math.Abs(f.FlowY - p.FlowY) / BeadWidth:0.000} bead widths per frame");
            p = f;
        }
    }

    // ---------------- Reduce Motion ----------------

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Reduce_Motion_holds_one_still_formation_per_state_and_slides_at_least_0_4_s_under_the_cap()
    {
        var e = new NthBeadsEngine(6);
        e.SetState(NthBeadsState.Tool, 0);
        e.SetCalm(true, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        RunTo(e, 1.5);
        AssertStillOn(e, NthBeadsHub.Pedes); // tool rests on Pedes (revision 2)
        e.SetState(NthBeadsState.Streaming, 1.5); // streaming shares Pedes: nothing moves at all
        Assert.Equal(NthBeadsMoveId.Still, e.MoveId);
        RunTo(e, 2);
        AssertStillOn(e, NthBeadsHub.Pedes);
        Assert.False(e.Step(2));
        (NthBeadsState State, NthBeadsHub Hub)[] route = [(NthBeadsState.Thinking, NthBeadsHub.Arcus), (NthBeadsState.Waiting, NthBeadsHub.Signum),
            (NthBeadsState.Connecting, NthBeadsHub.Punctum), (NthBeadsState.Hydra, NthBeadsHub.Tripus), (NthBeadsState.Tactus, NthBeadsHub.Clavis),
            (NthBeadsState.Background, NthBeadsHub.Pedes)];
        var t = 2.0;
        foreach (var (state, hub) in route)
        {
            e.SetState(state, t);
            Assert.Equal(NthBeadsMoveId.Glide, e.MoveId);
            Assert.True(e.CurrentMove.T >= .4, $"{state}: a {e.CurrentMove.T:0.00} s slide");
            var prev = e.Frame(t);
            var end = t + e.CurrentMove.T + .4;
            for (var x = t + Frame60; x <= end; x += Frame60)
            {
                e.Update(x);
                var f = e.Frame();
                for (var k = 0; k < 3; k++)
                {
                    Assert.True(Dist(prev[k], f[k]) / BeadWidth <= .3, $"{state}: {Dist(prev[k], f[k]) / BeadWidth:0.000} bead widths per frame");
                    Assert.True(Math.Abs(prev[k].S - f[k].S) <= .1);
                    Assert.Equal(1, f[k].O);
                }
                prev = f;
            }
            t = end;
            AssertStillOn(e, hub);
        }
        // turning it on mid-move coasts into a slide from wherever the beads are (no jump)
        var g = new NthBeadsEngine(8);
        g.SetState(NthBeadsState.Thinking, 0);
        g.Enter(NthBeadsEntrance.Ortus, 0);
        var at = RunUntilMotif(g, 3.3);
        var b = g.Frame(at);
        g.SetCalm(true, at);
        var a = g.Frame(at);
        for (var k = 0; k < 3; k++) Assert.True(Dist(b[k], a[k]) < 1e-9);
        Assert.Equal(NthBeadsMoveId.Glide, g.MoveId);
    }

    private static void AssertStillOn(NthBeadsEngine e, NthBeadsHub hub)
    {
        Assert.Equal(NthBeadsMoveId.Still, e.MoveId);
        Assert.True(e.IsStill);
        var f = e.Frame();
        for (var k = 0; k < 3; k++)
        {
            Assert.True(Dist(f[k], HubPoint(hub, k)) < 1e-12, $"slot {k} off {hub}");
            Assert.Equal(1, f[k].S);
        }
    }

    // ---------------- cost ----------------

    [Fact]
    [Trait("Decision", "ED-NEW-calculi-wu1-1")]
    public void Update_Frame_and_every_event_allocate_nothing_after_warm_up()
    {
        var e = new NthBeadsEngine(7);
        e.SetState(NthBeadsState.Tool, 0);
        e.Enter(NthBeadsEntrance.Ortus, 0);
        double sum = 0;
        void Run(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                var t = i * Frame60;
                if (i % 211 == 0) e.Step(t);
                if (i % 487 == 0) e.SetState(NthBeadsStress.States[i / 487 % 8], t);
                if (i % 1301 == 0) e.Reflow(.1, .4, t);
                if (i % 2003 == 0) e.SetCalm(!e.Calm, t);
                if (i % 3001 == 0) e.Finish(t);
                if (i % 3001 == 40) e.Enter(NthBeadsEntrance.Scriptio, t);
                e.Update(t);
                var f = e.Frame(t);
                sum += f.B0.X + f.B2.S;
            }
        }
        Run(1, 4000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(4000, 14000);
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, delta);
        Assert.False(double.IsNaN(sum));
    }
}
