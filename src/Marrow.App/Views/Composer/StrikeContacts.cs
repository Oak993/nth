namespace Marrow.App.Views.Composer;

// E:875-890 + P:601-652; SPEC K-139..K-146. One render-thread contact state, stable chronological events.
public sealed class StrikeContacts(int seed, StrikeBubbleLanes lanes, StrikeFastClock clock)
{
    private readonly Dictionary<(int Lane, int Rise), double> dead = new(128);
    private readonly (int Lane, int Rise)[] expired = new (int, int)[128];
    private readonly StrikePendingContact[] pending = new StrikePendingContact[384];
    private readonly StrikeLit[] segments = new StrikeLit[175];
    private double? processedAt;
    public StrikeBurst[] NewBursts { get; } = new StrikeBurst[128];
    public int NewCount { get; private set; }
    public StrikeBurst[] Recent { get; } = new StrikeBurst[128];
    public int RecentCount { get; private set; }
    public bool IsDead(int lane, int rise) => dead.ContainsKey((lane, rise));

    public void Process(StrikeGeometry g, StrikePlans plans, StrikeResolvedFrame frame, double time, double offset, double ink)
    {
        NewCount = 0; var old = processedAt; var reset = old is null || time < old.Value - 1e-9 || time - old.Value > 0.75;
        var from = reset ? time - 6 : old!.Value;
        if (reset) { dead.Clear(); RecentCount = 0; }
        var t0 = from + offset; var t1 = time + offset; var count = 0;
        if (t1 > t0)
        {
            for (var k = (int)Math.Floor(t0 / 2.4) - 2; k <= (int)Math.Floor(t1 / 2.4) + 1; k++)
            {
                var timing=plans.TimingFor(k);
                if(!EventWindow(timing)&&!(timing.Event==2&&EventWindow(plans.TimingFor(k,true))))continue;
                var p = plans.Get(g, k); Events(p); if (p.Twin is { } twin) Events(twin);
            }
            // JS sort is stable: equal timestamps preserve primary/twin and event insertion order.
            for (var i = 1; i < count; i++) { var item = pending[i]; var j = i; while (j > 0 && pending[j - 1].T > item.T) { pending[j] = pending[j - 1]; j--; } pending[j] = item; }
            for (var i = 0; i < count; i++)
            {
                var e = pending[i]; var p = e.Plan; var n = e.Event.Count;
                for (var j = 0; j < n; j++)
                {
                    var s = e.Event.Segments[j]; var line = s.Reference < 2 ? p.Channels[s.Reference] : p.Branches[s.Reference - 2];
                    var kind = s.Reference < 2 ? s.Reference : 2; var index = s.Reference < 2 ? 0 : s.Reference - 2;
                    segments[j] = new(new(new(line.Points, 0, line.Count / 2, p.Timing.K, p.Timing.IsTwin, kind, index), s.First, s.Last, 0, 0), (0.6 + 0.6 * ink) * g.K);
                }
                Contact(e.T - offset, p.Timing.K, 0, n);
            }
        }
        processedAt = time;
        var kept = 0; for (var i = 0; i < RecentCount; i++) if (time - Recent[i].T <= 0.4) Recent[kept++] = Recent[i]; RecentCount = kept;
        if (dead.Count > 64)
        {
            var n = 0; foreach (var pair in dead) if (time - pair.Value > 10) expired[n++] = pair.Key;
            for (var i = 0; i < n; i++) dead.Remove(expired[i]);
        }
        // P:604 returns after a base reset: no spark/frame contacts on that first frame.
        if (reset) return;
        for (var k = (int)Math.Floor(t0 / 2.4) - 2; k <= (int)Math.Floor(t1 / 2.4); k++)
        {
            if (StrikeMix.Spark(k, seed) is not { Blocked: false } spark || !(spark.At > t0 && spark.At <= t1)) continue;
            var p = plans.Get(g, k); segments[0] = new(new(new(p.SparkPoints, 0, 4, k, false, 4, 0), 0, 3, 0, 0), (0.6 + 0.6 * ink) * g.K); Contact(spark.At - offset, k, 1, 1);
        }
        if (frame.LitCount > 0) { Array.Copy(frame.Lit, segments, frame.LitCount); Contact(time, -1, 2, frame.LitCount); }

        void Events(StrikePlan p)
        {
            var events = p.ContactEvents();
            for (var i = 0; i < events.Count; i++)
            {
                var e = events.Items[i]; var t = p.Timing.At + e.Age; if (!(t > t0 && t <= t1)) continue;
                if (count == pending.Length) throw new InvalidOperationException("Strike pending contact capacity exceeded");
                pending[count++] = new(t, p, e);
            }
        }
        bool EventWindow(StrikeTiming timing)
        {
            var last=0.3;
            for(var i=0;i<timing.FlickerCount;i++)last=Math.Max(last,Strike.Round(timing.Flicker(i).At*1000)/1000d);
            return timing.At-timing.Lead<=t1&&timing.At+last>t0;
        }
        void Contact(double at, int k, int source, int n)
        {
            var f = clock.FastAt(at); if (f < 0.3) return; var bubbleTime = lanes.Simmer ? clock.ClockB(at) : at;
            for (var i = 0; i < lanes.Count; i++)
            {
                var b = lanes.Now(i, bubbleTime, f); if (b.Alpha < 0.03 || dead.ContainsKey((i, b.K))) continue;
                var hit = false;
                for (var j = 0; j < n; j++) if (Near(b.X, b.Y, segments[j].Trace, b.Radius + segments[j].ContactHalfWidth)) { hit = true; break; }
                if (!hit) continue;
                if (NewCount == NewBursts.Length || RecentCount == Recent.Length) throw new InvalidOperationException("Strike burst capacity exceeded");
                dead[(i, b.K)] = at; var hash = (int)Math.Floor(Strike.Hash(unchecked(i * 977 + b.K), seed) * 1000000);
                var burst = new StrikeBurst(at, i, b.K, b.X, b.Y, b.Radius, b.Alpha, hash % 628 / 100d, hash, k, source);
                NewBursts[NewCount++] = burst; Recent[RecentCount++] = burst;
            }
        }
    }

    private static bool Near(double x, double y, StrikeTrace trace, double limit)
    {
        var squared = limit * limit;
        if (trace.Last <= trace.First) { var dx = trace.Line.X(trace.First) - x; var dy = trace.Y(trace.First) - y; return dx * dx + dy * dy <= squared; }
        for (var i = trace.First; i < trace.Last; i++)
        {
            var ax = trace.Line.X(i); var ay = trace.Y(i); var bx = trace.Line.X(i + 1); var by = trace.Y(i + 1);
            if (x < Math.Min(ax, bx) - limit || x > Math.Max(ax, bx) + limit) continue;
            var dx = bx - ax; var dy = by - ay; var length = dx * dx + dy * dy; var u = length > 0 ? ((x - ax) * dx + (y - ay) * dy) / length : 0; u = Math.Clamp(u, 0, 1);
            var qx = ax + dx * u - x; var qy = ay + dy * u - y; if (qx * qx + qy * qy <= squared) return true;
        }
        return false;
    }

    // E:1063/1068, the burst washes are on zapb and never take the bolt's light opacity.
    public void ResolveLight(StrikeGeometry g, StrikeResolvedFrame frame, double time)
    {
        var sk = Math.Sqrt(g.K);
        for (var i = 0; i < RecentCount; i++)
        {
            var b = Recent[i]; var u = (time - b.T) / 0.19; if (u < 0 || u >= 1) continue; var r0 = Math.Max(b.Radius, 1.6);
            if (u < 0.27) Add(r0 + 3.5 * sk, 0.55 * Math.Pow(1 - u / 0.27, 2));
            if (u < 0.6) Add(r0 + 5 * g.K, 0.34 * Math.Pow(1 - u / 0.6, 2));
            void Add(double radius, double alpha) { if (alpha >= 0.006) frame.AddLight(new(0, Math.Min(1, alpha), b.X, b.Y, radius, 1, default, 0, 1)); }
        }
    }
}
