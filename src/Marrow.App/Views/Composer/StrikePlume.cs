// DEVIATION OD-0093: locked plume geometry, strike-light-locked.html:426-490 (SPEC K-069..K-086).
namespace Marrow.App.Views.Composer;

public static class StrikePlume
{
    // P:429 tests JS truthiness: a primary with a twin object AND the twin's true flag both take this salt.
    private static double H(StrikePlan p, int seed, int id, int salt) => Strike.Hash(unchecked(p.Timing.K * 977 + id * 131 + (p.Timing.IsTwin || p.Timing.Event == 2 ? 7919 : 0)), unchecked(seed * 31 + salt));
    private static double ClampY(StrikeGeometry g, double y) => g.Middle + g.Height * 0.4 * Math.Tanh((y - g.Middle) / (g.Height * 0.4));

    public static StrikePlumeBuffer Geometry(StrikePlan p, StrikeGeometry g, int seed)
    {
        var result = p.Plume; if (result.Ready) return result;
        result.Count = result.Vertices = 0;
        var main = p.Channels[0]; var last = p.LastMain; var glass = p.Timing.Event == 4;
        var step = 3 * g.K + 1.5; var sub = Math.Max(1, Strike.Round((5 * g.K + 2) / step));
        for (var i = 0; i <= last; i++) result.Put(main.Points[i * 2], main.Points[i * 2 + 1], i / (double)last);
        result.Line(0, 0, 1, 0);
        var n = glass ? 3 : g.Height >= 34 ? 7 : 5; var spread = glass ? 0.08 : 0.42;
        for (var j = 0; j < n - 1; j++)
        {
            var id = j + 1; var lane = -1 + 2 * (j + 0.5 + (H(p, seed, id, 1) - 0.5) * 0.7) / (n - 1);
            var end = 0.6 + 0.4 * H(p, seed, id, 2); var arc = (H(p, seed, id, 3) - 0.5) * 2 * (glass ? 0.04 : 0.25) * g.Height;
            var weight = 0.35 + 0.65 * H(p, seed, id, 4); var sd = unchecked(seed * 53 + p.Timing.K * 7 + id);
            var nv = Math.Max(3, Strike.Round(last * end * sub)); var start = result.Vertices;
            for (var i = 0; i <= nv; i++)
            {
                var u = i / (double)nv * end; var f = Math.Min(last - 1e-6, u * last); var index = (int)Math.Floor(f); var r = f - index;
                var px = main.Points[index * 2] + (main.Points[index * 2 + 2] - main.Points[index * 2]) * r;
                var py = main.Points[index * 2 + 1] + (main.Points[index * 2 + 3] - main.Points[index * 2 + 1]) * r;
                var ni = Math.Min(last - 1, (int)Math.Floor(u * last)); var dx = main.Points[ni * 2 + 2] - main.Points[ni * 2]; var dy = main.Points[ni * 2 + 3] - main.Points[ni * 2 + 1];
                var length = double.Hypot(dx, dy); if (length == 0) length = 1;
                var fan = Math.Pow(u, 0.6);
                var off = fan * lane * spread * g.Height + arc * Math.Sin(Math.PI * u / end) * fan + Strike.Fbm(px * 2 / g.K, sd) * 0.1 * g.Height * fan + (Strike.Hash(i, sd) - 0.5) * 2.4 * g.K * Math.Min(1, u * 8);
                if (!result.Put(px - dy / length * off, ClampY(g, py + dx / length * off), u)) break;
            }
            result.Line(id, 0, weight, start);
        }
        if (p.ChannelCount > 1)
        {
            var fork = p.Channels[1]; var fl = fork.Count / 2 - 1; var u0 = fork.Split / (double)last; var start = result.Vertices;
            for (var i = 0; i <= fl; i++) result.Put(fork.Points[i * 2], fork.Points[i * 2 + 1], u0 + i / (double)fl * (1 - u0));
            result.Line(60, 0, 0.85, start);
        }
        var depth = glass ? 0 : g.Height >= 34 ? 2 : 1;
        var dir = Math.Sign(main.Points[last * 2] - main.Points[0]); if (dir == 0) dir = -1;
        var tubeLength = Math.Abs(main.Points[last * 2] - main.Points[0]); if (tubeLength == 0) tubeLength = g.Width * 0.5;
        var initial = result.Count;
        for (var i = 0; i < initial; i++) Grow(result, result.Lines[i], 1, depth, p, g, seed, dir, tubeLength, main.Points[last * 2], step);
        var grown = result.Count;
        for (var i = 0; i < grown; i++)
        {
            var line = result.Lines[i]; if (line.Id == 0 || line.N < 2) continue;
            var e = line.Start + line.N; var ex = result.Points[e * 2]; var ey = result.Points[e * 2 + 1];
            var angle = Math.Atan2(ey - result.Points[e * 2 - 1], ex - result.Points[e * 2 - 2]); var ue = result.U[e];
            for (var s = -1; s <= 1; s += 2)
            {
                var id = line.Id * 3 + (s + 1) / 2 + 5000; var a = angle + s * (0.35 + 0.4 * H(p, seed, id, 1)); var length = (3 + 4 * H(p, seed, id, 2)) * g.K;
                var start = result.Vertices;
                result.Put(ex, ey, ue);
                result.Put(ex + Math.Cos(a) * length * 0.5, ClampY(g, ey + Math.Sin(a) * length * 0.5 + (H(p, seed, id, 3) - 0.5) * g.K), ue + 0.004);
                result.Put(ex + Math.Cos(a) * length, ClampY(g, ey + Math.Sin(a) * length), ue + 0.008);
                result.Line(id, 9, line.Weight * 0.5, start);
            }
        }
        result.Ready = true; return result;
    }

    private static void Grow(StrikePlumeBuffer result, StrikePlumeLine parent, int level, int depth, StrikePlan p, StrikeGeometry g, int seed, int dir, double tubeLength, double endX, double step)
    {
        if (level > depth) return;
        var nb = level == 1 ? 2 + (H(p, seed, parent.Id, 10) < 0.4 ? 1 : 0) : 1 + (H(p, seed, parent.Id, 11) < 0.35 ? 1 : 0);
        for (var b = 0; b < nb; b++)
        {
            var id = parent.Id * 7 + b + 1 + level * 1000;
            var at = Math.Max(1, Strike.Round((0.12 + 0.62 * ((b + H(p, seed, id, 1)) / nb)) * parent.N)) + parent.Start;
            var ox = result.Points[at * 2]; var oy = result.Points[at * 2 + 1]; var u0 = result.U[at];
            var length = tubeLength * (level == 1 ? 0.08 + 0.14 * H(p, seed, id, 2) : 0.04 + 0.08 * H(p, seed, id, 2));
            var side = (oy < g.Middle ? -1 : 1) * (H(p, seed, id, 3) < 0.72 ? 1 : -1);
            var dy = side * g.Height * (level == 1 ? 0.14 + 0.2 * H(p, seed, id, 4) : 0.08 + 0.14 * H(p, seed, id, 4));
            var nv = Math.Max(3, Strike.Round(length / step)); var sd = unchecked(id * 13 + seed); var start = result.Vertices;
            result.Put(ox, oy, u0);
            for (var i = 1; i <= nv; i++)
            {
                var v = i / (double)nv; var x = ox + dir * length * v; if (dir * (x - endX) > 0) break;
                if (!result.Put(x, ClampY(g, oy + dy * Math.Pow(v, 0.55) + Strike.Fbm(x * 2.6 / g.K, sd) * 0.05 * g.Height * v + (Strike.Hash(i, sd) - 0.5) * 2 * g.K * Math.Min(1, v * 6)), u0 + length * v / tubeLength)) break;
            }
            if (result.Vertices - start < 3) { result.Vertices = start; continue; }
            if (result.Line(id, level, parent.Weight * 0.62, start)) Grow(result, result.Lines[result.Count - 1], level + 1, depth, p, g, seed, dir, tubeLength, endX, step);
        }
    }
}
