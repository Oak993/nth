using Marrow.UI.Port.Nth.Beads;

namespace Marrow.UI.Tests.Nth;

// Calculi (ED-NEW-calculi-wu1-1): reference/stress.js's STRESS.session, line for line, on the C# engine. Random turns with
// timestamped events (states, steps fired mid-move, one Reduce Motion toggle, exits asked during the entrance, a new turn while
// the last is leaving), sampled at FS Hz. The shipped lists are pair A (ED-NEW-calculi-wu1-2): Scriptio or Ortus in; Occasus,
// error or stop out. Quiet engines and random line reflows are the fixture's two additions (nth-beads-reference.js). WU2's
// frame gates judge these sessions.
internal static class NthBeadsStress
{
    public static readonly NthBeadsState[] States =
    [
        NthBeadsState.Connecting, NthBeadsState.Thinking, NthBeadsState.Tool, NthBeadsState.Streaming,
        NthBeadsState.Waiting, NthBeadsState.Tactus, NthBeadsState.Hydra, NthBeadsState.Background,
    ];

    public static readonly string[] Enters = ["scriptio", "ortus"], Exits = ["occasus", "fail", "stop"];

    public sealed class Session
    {
        public required int Seed { get; init; }
        public required string En { get; init; }
        public required string En2 { get; init; }
        public required string Ex { get; init; }
        public required bool Early { get; init; }
        public required bool Reenter { get; init; }
        public required bool Calm { get; init; }
        public required int Fs { get; init; }
        public required double LastExit { get; init; }
        public required List<NthBeadsSample> Rec { get; init; }
        public required List<double> Handoffs { get; init; }
        public required List<double> Gone { get; init; }
    }

    /// <summary>The reference's mulberry32 (stress.js's rng).</summary>
    public static Func<double> Rng(uint seed)
    {
        var a = seed;
        return () =>
        {
            unchecked
            {
                a += 0x6D2B79F5u;
                var t = (a ^ (a >> 15)) * (1u | a);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        };
    }

    private static NthBeadsEntrance EntranceOf(string s) => s == "scriptio" ? NthBeadsEntrance.Scriptio : NthBeadsEntrance.Ortus;

    public static Session Run(int seed, double secs, double calmP, int fs = 240, bool quiet = false, bool reflow = false)
    {
        var r = Rng((uint)(seed * 7919 + 1));
        var e = new NthBeadsEngine((uint)seed, quiet);
        e.SetState(States[1 + (int)Math.Floor(r() * 7)], 0);
        e.SetCalm(r() < calmP, 0);
        var sc = r();
        bool early = sc < .12, reenter = sc >= .12 && sc < .42;
        var en = early && r() < .7 ? "scriptio" : Enters[(int)Math.Floor(r() * Enters.Length)];
        string ex = Exits[(int)Math.Floor(r() * Exits.Length)], ex2 = Exits[(int)Math.Floor(r() * Exits.Length)],
            en2 = Enters[(int)Math.Floor(r() * Enters.Length)];
        void DoExit(string x, double at)
        {
            if (x == "fail") e.Fail(at);
            else if (x == "stop") e.Stop(at);
            else e.Finish(at);
        }
        var ev = new List<(double T, Action<double> A)>();
        var toggled = false;
        void RandomEvents(double t, double until)
        {
            while (t < until)
            {
                var p = r();
                if (p < .45) { var s = States[(int)Math.Floor(r() * 8)]; ev.Add((t, at => e.SetState(s, at))); }
                else if (p < .9) ev.Add((t, at => e.Step(at)));
                else if (!toggled && calmP > 0 && r() < .3) { toggled = true; ev.Add((t, at => e.SetCalm(!e.Calm, at))); }
                t += .15 + r() * r() * 4;
            }
        }
        var exitAt = early ? .05 + r() * .5 : secs - 2.5;
        RandomEvents(.3 + r() * 2, exitAt);
        ev.Add((exitAt, at => DoExit(ex, at)));
        double end = secs, lastExit = exitAt;
        var lastKind = ex;
        if (reenter)
        {
            var reAt = exitAt + .05 + r() * .9;
            ev.Add((reAt, at => e.Enter(EntranceOf(en2), at)));
            RandomEvents(reAt + .3 + r(), reAt + 3);
            lastExit = reAt + 3.2;
            lastKind = ex2;
            var le = lastExit;
            ev.Add((le, at => DoExit(ex2, at)));
            end = Math.Max(end, lastExit + 3);
        }
        if (reflow)
        {
            var q = Rng((uint)(seed * 31 + 7));
            for (var t = .4 + q(); t < end - .5; t += .3 + q() * 2.5)
            {
                double dx = (q() - .5) * 2, dy = (q() - .5) * 4;
                ev.Add((t, at => e.Reflow(dx, dy, at)));
            }
        }
        var sorted = ev.OrderBy(x => x.T).ToList(); // stable, as the reference's sort
        var hand = new List<double>();
        var gone = new List<double>();
        e.MoveBegan += t => hand.Add(t);
        e.Gone += t => gone.Add(t);
        e.Enter(EntranceOf(en), 0);
        var n = (int)Math.Floor(end * fs + .5);
        var rec = new List<NthBeadsSample>(n + 1);
        var j = 0;
        for (var i = 0; i <= n; i++)
        {
            var t = (double)i / fs;
            while (j < sorted.Count && sorted[j].T <= t) { sorted[j].A(sorted[j].T); j++; }
            e.Update(t);
            rec.Add(e.Frame());
        }
        return new Session
        {
            Seed = seed, En = en, En2 = en2, Ex = lastKind, Early = early, Reenter = reenter, Calm = e.Calm, Fs = fs,
            LastExit = lastExit, Rec = rec, Handoffs = hand, Gone = gone,
        };
    }
}
