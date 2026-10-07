namespace Marrow.App.Views.Composer;

// SPEC 4.4: maxima over the four specified geometries +25%, rounded up; eight primary/twin slots.
public sealed class StrikePlans
{
    private readonly int seed;
    private readonly StrikePlan[] slots = new StrikePlan[8];
    private StrikeGeometry? geometry;
    public StrikePlans(int seed)
    {
        this.seed = seed;
        for (var i = 0; i < slots.Length; i++) slots[i] = new StrikePlan(true);
    }

    public int SlotCount => slots.Length;
    public long Builds { get; private set; }

    public StrikePlan Get(StrikeGeometry g, int k, bool twin = false)
    {
        if (geometry != g)
        {
            geometry = g;
            for (var i = 0; i < slots.Length; i++) slots[i].Ready = false;
        }
        var slot = slots[k & 7];
        if (!slot.Ready || slot.Timing.K != k)
        {
            var timing = StrikeMix.Timing(k, seed);
            Build(slot, g, timing, timing);
            if (timing.Event == 2) Build(slot.TwinSlot!, g, StrikeMix.Timing(k, seed, true), timing);
            slot.Ready = true; Builds++;
        }
        return twin ? slot.Twin ?? throw new ArgumentException("This Mix strike has no twin.", nameof(twin)) : slot;
    }

    private static int Segments(StrikeLine line, StrikeGeometry g, double x0, double x1)
    {
        var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(x1 - x0) / (5 * g.K + 2)));
#if DEBUG
        if (2 * (n + 1) > line.Points.Length) throw new InvalidOperationException("Strike channel capacity exceeded.");
#endif
        return Math.Min(n, line.Points.Length / 2 - 1);
    }

    private static void Put(StrikeLine line, double x, double y)
    {
        if (line.Count + 2 > line.Points.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike path capacity exceeded.");
#else
            return;
#endif
        }
        line.Points[line.Count++] = x; line.Points[line.Count++] = y;
    }

    // strike-engine.js:376-407. The zigzag keys live on the stack; no closures are converted to delegates.
    private static void Shape(StrikeLine line, StrikeGeometry g, double x0, double y0, double x1, double y1, int family, int sd)
    {
        double H(int salt) => Strike.Hash(sd, salt);
        var h = g.Height; var n = Segments(line, g, x0, x1); var mid = (y0 + y1) / 2;
        var q = 0.7 + 0.9 * H(2); var amp = h * (0.08 + 0.06 * H(4)); var exponent = 0.4 + 0.4 * H(3);
        double peak = 0, power = 0, arc = 0, phase = 0, side = 0, wall = 0, a0 = 0, a1 = 0;
        Span<double> us = stackalloc double[6], ys = stackalloc double[6]; int nk = 0;
        if (family == 0) { amp = h * (0.06 + 0.06 * H(4)); q *= 1.3; }
        else if (family is 1 or 2)
        {
            peak = family == 1 ? h * (0.19 + 0.08 * H(5)) : h * (0.81 - 0.08 * H(5));
            power = 0.7 + 0.6 * H(6); amp = h * (0.08 + 0.09 * H(4));
        }
        else if (family == 3) { arc = h * (0.2 + 0.09 * H(5)) * (H(6) < 0.5 ? -1 : 1); phase = (H(7) - 0.5) * 0.5; }
        else if (family == 4)
        {
            nk = 2 + (int)Math.Floor(3 * H(5)); us[0] = 0; ys[0] = y0;
            var sign = H(6) < 0.5 ? -1 : 1;
            for (var j = 0; j < nk; j++) { us[j + 1] = (j + 0.3 + 0.4 * H(10 + j)) / nk * 0.84 + 0.08; ys[j + 1] = g.Middle + sign * h * (0.22 + 0.12 * H(20 + j)); sign = -sign; }
            us[nk + 1] = 1; ys[nk + 1] = y1; amp = h * (0.035 + 0.035 * H(4)); q *= 1.4;
        }
        else
        {
            side = H(5) < 0.5 ? -1 : 1; wall = side < 0 ? h * (0.17 + 0.04 * H(6)) : h * (0.83 - 0.04 * H(6));
            amp = h * (0.07 + 0.05 * H(4)); a0 = 0.12 + 0.1 * H(7); a1 = 0.86 - 0.1 * H(8);
        }
        line.Count = 0;
        for (var i = 0; i <= n; i++)
        {
            var u = i / (double)n; var x = x0 + (x1 - x0) * u; var baseline = y0 + (y1 - y0) * u;
            if (family is 1 or 2) baseline += (peak - mid) * Math.Pow(Math.Sin(Math.PI * u), power);
            else if (family == 3) baseline += arc * Math.Sin(2 * Math.PI * u + phase * Math.Sin(Math.PI * u));
            else if (family == 4)
            {
                var j = 1; while (j < nk + 1 && u > us[j]) j++;
                var v = (u - us[j - 1]) / (us[j] - us[j - 1]); baseline = ys[j - 1] + (ys[j] - ys[j - 1]) * v;
            }
            else if (family == 5) { var w = Strike.Ss(0, a0, u) * (1 - Strike.Ss(a1, 1, u)); baseline = baseline * (1 - w) + wall * w; }
            var envelope = Math.Pow(Math.Sin(Math.PI * u), exponent); var noise = Strike.Fbm(x * q / g.K, sd);
            var y = baseline + envelope * amp * noise;
            if (family == 5) { var w = Strike.Ss(0, 0.2, u) * (1 - Strike.Ss(0.8, 1, u)); y = baseline - side * Math.Abs(noise) * amp * 1.4 * w + noise * amp * (1 - w) * envelope; }
            Put(line, x, Math.Min(h * 0.87, Math.Max(h * 0.13, y)));
        }
    }

    // E:412-423, K-052: the glass wall includes the cap arc and its return along the far wall.
    private static void GlassPath(StrikeLine line, StrikeGeometry g, int sd)
    {
        var d = 2.2 * g.K + 0.8; var radius = g.Height / 2 - d; var center = g.Height / 2; var step = 5 * g.K + 2;
        var side = Strike.Hash(sd, 1) < 0.5 ? -1 : 1; var x0 = g.Knob - g.KnobRadius * 0.55;
        var back = (0.1 + 0.22 * Strike.Hash(sd, 2)) * g.Width; double distance = 0;
        line.Count = 0; Put(line, g.Knob, g.Middle + side * radius * 0.45);
        void Point(double x, double y, double nx, double ny)
        {
            var jitter = Math.Abs(Strike.Fbm(distance * 1.6 / g.K, sd)) * 2.2 * g.K;
            Put(line, x + nx * jitter, y + ny * jitter);
        }
        for (var x = x0; x > center; x -= step, distance += step) Point(x, g.Middle + side * radius, 0, -side);
        var na = Math.Max(4, (int)Math.Ceiling(Math.PI * radius / step));
        for (var i = 0; i <= na; i++, distance += step)
        {
            var p = Math.PI * i / na; Point(center - radius * Math.Sin(p), g.Middle + side * radius * Math.Cos(p), Math.Sin(p), -side * Math.Cos(p));
        }
        for (var x = center + step; x < center + back; x += step, distance += step) Point(x, g.Middle - side * radius, 0, side);
    }

    private static void Core(StrikeLine line, StrikeGeometry g, int sd)
    {
        var radius = g.KnobRadius - 0.5;
        for (var i = 0; i < line.Count / 2; i++)
        {
            var distance = Math.Abs(line.Points[2 * i] - g.Knob); if (distance >= radius) continue;
            var s = Strike.Ss(0, radius, distance); var kink = 4 * s * (1 - s) * (Strike.Hash(unchecked(sd * 3 + i), 77) - 0.5) * 0.3 * radius;
            line.Points[2 * i + 1] = g.Middle + (line.Points[2 * i + 1] - g.Middle) * s + kink;
        }
    }

    private static void Branches(StrikePlan plan, StrikeGeometry g, int count, int sb, int forward, double reach)
    {
        var main = plan.Channels[0]; var last = main.Count / 2 - 1; plan.BranchCount = count;
        for (var b = 0; b < count; b++)
        {
            double H(int salt) => Strike.Hash(unchecked(sb * 7 + b), salt);
            var branch = plan.Branches[b]; var u = 0.1 + (Math.Max(0.2, reach - 0.04) - 0.1) * ((b + H(1)) / count);
            var at = Strike.Round(u * last); branch.At = at; var bx = main.Points[at * 2]; var by = main.Points[at * 2 + 1];
            var up = by - g.Height * 0.1; var down = g.Height * 0.9 - by;
            var dir = H(2) < down / Math.Max(1, up + down) ? 1 : -1; var room = dir < 0 ? up : down;
            var fwd = H(3) < 0.68 ? forward : -forward; var length = (8 + 40 * Math.Pow(H(4), 1.3)) * g.K;
            var dy = dir * Math.Max(2.5, room * (0.25 + 0.65 * H(5))); var power = 0.55 + 0.7 * H(6); var ns = 4 + (int)Math.Floor(3 * H(8));
            branch.Count = 0; Put(branch, bx, by);
            for (var i = 1; i <= ns; i++)
            {
                var v = i / (double)ns;
                Put(branch, bx + fwd * length * v + (H(20 + i) - 0.5) * 4 * g.K,
                    Math.Min(g.Height * 0.92, Math.Max(g.Height * 0.08, by + dy * Math.Pow(v, power) + (H(40 + i) - 0.5) * 3 * g.K)));
            }
        }
    }

    private void Build(StrikePlan p, StrikeGeometry g, StrikeTiming timing, StrikeTiming parent)
    {
        p.Plume.Ready = false; p.Contacts.Ready = false;
        var k = timing.K; double H(int salt) => Strike.Hash(k, unchecked(seed * 13 + salt));
        var main = p.Channels[0]; p.ChannelCount = 1; p.Timing = timing; p.Geometry = g;
        var sd = unchecked(seed * 41 + k * 3 + 1); var sb = unchecked(k * 11 + seed * 7 + 5);
        var bias = parent.Event is 0 or 3 ? parent.Family == 1 ? 1 : parent.Family == 2 ? -1 : 0 : 0;
        var knobY = g.Middle + (H(101) - 0.5 + 0.3 * bias) * 0.5 * g.Height;
        var capY = bias != 0 ? g.Middle + bias * g.Height * (0.02 + 0.28 * H(102)) : g.Height * (0.2 + 0.6 * H(102));
        if (timing.IsTwin)
        {
            var y2 = g.Height * (0.2 + 0.6 * H(115));
            if (Math.Abs(y2 - capY) < 0.22 * g.Height) y2 = capY < g.Middle ? g.Height * (0.66 + 0.12 * H(118)) : g.Height * (0.22 + 0.12 * H(118));
            Shape(main, g, g.Knob, g.Middle + (H(117) - 0.5) * 0.4 * g.Height, g.CapEnd, y2, timing.Family, unchecked(sd + 13));
            Core(main, g, unchecked(sd + 13)); Branches(p, g, 2 + (int)Math.Floor(2 * H(116)), unchecked(sb + 3), -1, 0.9);
        }
        else if (timing.Style == 0)
        {
            main.Count = Strike.ChannelVertices(g, g.Knob, g.CapEnd) * 2;
            Strike.ChannelInto(main.Points.AsSpan(0, main.Count), g, g.Knob, g.Middle, g.CapEnd,
                g.Middle + (Strike.Hash(k, unchecked(seed + 1)) - 0.5) * 0.45 * g.Height, unchecked(seed * 37 + k), 0.34 * g.Height);
            p.BranchCount = 5;
            for (var b = 0; b < 5; b++)
            {
                var branch = p.Branches[b]; branch.At = Strike.Round((0.12 + 0.75 * Strike.Hash(unchecked(k * 5 + b), unchecked(seed + 31))) * p.LastMain); branch.Count = 12;
                Strike.BranchInto(branch.Points.AsSpan(0, 12), g, main.Points.AsSpan(0, main.Count), branch.At, unchecked(k * 5 + b + seed * 11), -1);
            }
        }
        else
        {
            if (timing.Event == 4) GlassPath(main, g, sd);
            else if (timing.Event == 1) Shape(main, g, g.Knob, knobY, g.CapEnd, g.Height * (H(104) < 0.5 ? 0.2 + 0.1 * H(105) : 0.7 + 0.1 * H(105)), timing.Family, sd);
            else if (timing.Event == 3) Shape(main, g, g.CapEnd, capY, g.Knob, knobY, timing.Family, sd);
            else Shape(main, g, g.Knob, knobY, g.CapEnd, capY, timing.Family, sd);
            Core(main, g, sd);
            if (timing.Event == 1)
            {
                var fork = p.Channels[1]; fork.Split = Strike.Round((0.35 + 0.25 * H(107)) * p.LastMain); fork.Gain = 0.85;
                var y = main.Points[main.Count - 1] < g.Middle ? g.Height * (0.7 + 0.1 * H(106)) : g.Height * (0.2 + 0.1 * H(106));
                Shape(fork, g, main.Points[fork.Split * 2], main.Points[fork.Split * 2 + 1], g.CapEnd, y, 0, unchecked(sd + 7)); p.ChannelCount = 2;
            }
            var branches = timing.Event == 4 ? 2 + (int)Math.Floor(2 * H(103)) : 2 + (int)Math.Floor(5 * H(103));
            Branches(p, g, branches, sb, timing.Event == 3 ? 1 : -1, timing.Partial ? timing.Reach : 0.9);
        }
        p.Extent = timing.Partial ? Strike.Round(timing.Reach * p.LastMain) : p.LastMain;
        p.FlashX = timing.Event == 3 ? g.CapEnd + 0.2 * g.Height : g.Knob; p.FlashY = g.Middle;
        p.OrbCount = 0;
        for (var n = 0; n <= p.LastMain; n++)
        {
            var i = timing.Event == 3 ? p.LastMain - n : n; var dx = main.Points[i * 2] - g.Knob; var dy = main.Points[i * 2 + 1] - g.Middle;
            p.OrbIndices[p.OrbCount] = i; p.OrbSegment[2 * p.OrbCount] = dx; p.OrbSegment[2 * p.OrbCount + 1] = dy; p.OrbCount++;
            if (dx * dx + dy * dy >= (g.KnobRadius - 0.5) * (g.KnobRadius - 0.5)) break;
        }
        p.ExitAngle = Math.Atan2(p.OrbSegment[2 * p.OrbCount - 1], p.OrbSegment[2 * p.OrbCount - 2]);
        var spark = StrikeMix.Spark(k, seed); p.SparkAt = spark?.At; p.SparkBlocked = spark?.Blocked ?? false;
        var x0 = g.Knob - 0.85 * g.KnobRadius; var y0 = g.Middle + (H(122) - 0.5) * 1.1 * g.KnobRadius; var length = (6 + 9 * H(123)) * g.K;
        p.SparkPoints[0] = x0; p.SparkPoints[1] = y0;
        p.SparkPoints[2] = x0 - length * 0.4; p.SparkPoints[3] = y0 + (H(124) - 0.5) * 5 * g.K;
        p.SparkPoints[4] = x0 - length * 0.75; p.SparkPoints[5] = y0 + (H(125) - 0.5) * 6 * g.K;
        p.SparkPoints[6] = x0 - length; p.SparkPoints[7] = y0 + (H(126) - 0.5) * 7 * g.K;
    }
}
