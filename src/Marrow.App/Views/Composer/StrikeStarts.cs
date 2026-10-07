using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

// P:1331-1387; SPEC K-159..K-166. Timing only: a clean start never builds geometry.
public sealed class StrikeStarts(int seed)
{
    private readonly StrikeWindowBuffer windows = new();
    private readonly StrikeWindowBuffer cleanWindows = new();

    public double VisibleEnd(bool partial) => Math.Min(1.9, (partial ? 0.35 : 0.5) * Math.Log(0.86 * 255));

    public StrikeWindowBuffer Windows(double from, double to, bool clean = false)
    {
        BuildWindows(clean ? from - 4 : from, clean ? to + 4 : to);
        if (!clean) return windows;
        cleanWindows.Count = 0;
        for (var i = 0; i + 1 < windows.Count; i++)
        {
            var a = windows.Items[i].To; var b = windows.Items[i + 1].From;
            if (b > a && b >= from && a <= to) cleanWindows.Add(Math.Max(a, from), Math.Min(b, to));
        }
        return cleanWindows;
    }

    private void BuildWindows(double from, double to)
    {
        windows.Count = 0;
        for (var k = (int)Math.Floor(from / 2.4) - 3; k <= (int)Math.Floor(to / 2.4) + 2; k++)
        {
            var p = StrikeMix.Timing(k, seed); Add(p);
            if (p.Event == 2) Add(StrikeMix.Timing(k, seed, true));
            if (StrikeMix.Spark(k, seed) is { Blocked: false } spark) windows.Add(spark.At, spark.At + 0.12);
        }
        // Stable insertion sort, then merge inclusive overlaps and filter touching windows.
        for (var i = 1; i < windows.Count; i++)
        {
            var item = windows.Items[i]; var j = i;
            while (j > 0 && windows.Items[j - 1].From > item.From) { windows.Items[j] = windows.Items[j - 1]; j--; }
            windows.Items[j] = item;
        }
        var merged = 0;
        for (var i = 0; i < windows.Count; i++)
        {
            var item = windows.Items[i];
            if (merged > 0 && item.From <= windows.Items[merged - 1].To)
                windows.Items[merged - 1] = new(windows.Items[merged - 1].From, Math.Max(windows.Items[merged - 1].To, item.To));
            else windows.Items[merged++] = item;
        }
        var kept = 0;
        for (var i = 0; i < merged; i++) if (windows.Items[i].To >= from && windows.Items[i].From <= to) windows.Items[kept++] = windows.Items[i];
        windows.Count = kept;

        void Add(StrikeTiming p) => windows.Add(p.At - p.Lead - p.Charge - 0.35, p.At + 1.9);
    }

    public bool IsClean(double time) => Clear(time, false);
    public bool IsQuiet(double time) => Clear(time, true);

    private bool Clear(double time, bool quiet)
    {
        var k0 = (int)Math.Floor(time / 2.4);
        for (var k = k0 - 3; k <= k0 + 2; k++)
        {
            var p = StrikeMix.Timing(k, seed); if (Contains(p)) return false;
            if (p.Event == 2 && Contains(StrikeMix.Timing(k, seed, true))) return false;
            if (StrikeMix.Spark(k, seed) is { Blocked: false } spark && time >= spark.At && time <= spark.At + 0.12) return false;
        }
        return true;
        bool Contains(StrikeTiming p) => time >= p.At - p.Lead - p.Charge - (quiet ? 0 : 0.35) && time <= p.At + (quiet ? VisibleEnd(p.Partial) : 1.9);
    }

    public StrikeEntry RandomClean(IStrikeDraws draws)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var k = 20000 + (int)Math.Floor(draws.Next() * 400000); var t0 = k * 2.4;
            var clean = Windows(t0, t0 + 60, true); var kept = 0;
            for (var i = 0; i < clean.Count; i++) if (clean.Items[i].To - clean.Items[i].From > 1e-3) clean.Items[kept++] = clean.Items[i];
            clean.Count = kept; if (kept == 0) continue;
            var index = (int)Math.Floor(draws.Next() * kept); var w = clean.Items[index];
            return new(k, t0, kept, index, w.From, w.To, w.From + (w.To - w.From) * draws.Next());
        }
        throw new InvalidOperationException("no clean window found");
    }
}
