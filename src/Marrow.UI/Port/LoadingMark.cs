using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY: The loading mark and boot motion specified by ED-097, ED-102, ED-103 and ED-154.

public static class CometEasing
{
    public static double Clamp(double value) => Math.Clamp(value, 0, 1);
    public static double Smooth(double start, double end, double time)
    {
        var u = Clamp((time - start) / (end - start));
        return u * u * (3 - 2 * u);
    }
    public static double OutCubic(double value) => 1 - Math.Pow(1 - Clamp(value), 3);
    public static double OutQuart(double value) => 1 - Math.Pow(1 - Clamp(value), 4);
    public static double InOutCubic(double value)
    {
        var u = Clamp(value);
        return u < .5 ? 4 * u * u * u : 1 - Math.Pow(2 - 2 * u, 3) / 2;
    }
    public static double OutBack(double value)
    {
        var u = Clamp(value) - 1;
        return 1 + 2.4 * u * u * u + 1.4 * u * u;
    }

    public static (double Value, double Speed) Bezier(double linear, double x1, double y1, double x2, double y2)
    {
        var x = Clamp(linear);
        var u = x;
        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 12; i++)
        {
            var actual = Cubic(u, x1, x2);
            var delta = actual - x;
            if (Math.Abs(delta) <= 1e-14) break;
            if (delta < 0) low = u; else high = u;
            var slope = Slope(u, x1, x2);
            var candidate = slope > 1e-14 ? u - delta / slope : double.NaN;
            u = double.IsFinite(candidate) && candidate > low && candidate < high ? candidate : (low + high) / 2;
        }
        if (Math.Abs(Cubic(u, x1, x2) - x) > 1e-12)
        {
            for (var i = 0; i < 48; i++)
            {
                u = (low + high) / 2;
                if (Cubic(u, x1, x2) < x) low = u; else high = u;
            }
        }
        return (Cubic(u, y1, y2), Slope(u, y1, y2) / Slope(u, x1, x2));
    }

    private static double Cubic(double u, double a, double b) =>
        3 * (1 - u) * (1 - u) * u * a + 3 * (1 - u) * u * u * b + u * u * u;
    private static double Slope(double u, double a, double b) =>
        3 * (1 - u) * (1 - u) * a + 6 * (1 - u) * u * (b - a) + 3 * u * u * (1 - b);
}

public readonly record struct CometState(double Angle, double Speed, double TrailFactor, double CoreScale,
    double GlowOpacity, double GlowScale, double? LastPulse, double? RingAge);

public static class CometMotion
{
    public const double TurnSeconds = 2.4;
    public const double WindSeconds = .28;
    public const double RingSeconds = 1.9;
    public const double RingStartRadius = 96;
    public const double RingEndRadius = 346;
    public const double RingStartOpacity = .6;
    public const double RingWidth = 3;
    public static Color RingColor { get; } = Color.Parse("#DCE6FF");
    public static (double OffsetDegrees, double Blur, double Opacity)[] Trails { get; } = [(14, 10, .42), (30, 20, .2)];
    public static double TrailFactor(double speed) => CometEasing.Clamp((speed - .35) / 1.4);
    public static double RingRadius(double age) => 96 + 250 * CometEasing.Bezier(age / RingSeconds, .2, .6, .3, 1).Value;
    public static double RingOpacity(double age) => .6 * (1 - CometEasing.Bezier(age / RingSeconds, .2, .6, .3, 1).Value);
    public static CometState Still { get; } = new(0, 0, 0, 1, .6, 1, null, null);

    public static CometState Running(double time) => At(time, null, 1, .55);

    internal static CometState At(double time, double? finalRest, double coreReveal, double baseGlow)
    {
        if (time < 0) return new(0, 0, 0, coreReveal, baseGlow, 1, null, null);
        var stopped = finalRest is { } end && time >= end;
        var turns = stopped ? Math.Max(0, Math.Round(finalRest!.Value / TurnSeconds) - 1) : Math.Floor(time / TurnSeconds);
        var inTurn = stopped ? 0 : time - turns * TurnSeconds;
        var eased = stopped ? 0 : CometEasing.Bezier(inTurn / TurnSeconds, .45, .05, .55, .95).Value;
        var speed = stopped ? 0 : CometEasing.Bezier(inTurn / TurnSeconds, .45, .05, .55, .95).Speed;
        var pulse = stopped ? Math.Max(0, finalRest!.Value - TurnSeconds) : turns * TurnSeconds;
        var since = time - pulse;
        var flash = Math.Exp(-7 * since);
        var rise = CometEasing.Smooth(0, .06, since);
        var next = stopped || finalRest is { } rest && pulse + TurnSeconds >= rest - 1e-9
            ? double.PositiveInfinity : pulse + TurnSeconds;
        var wind = double.IsPositiveInfinity(next) ? 0 : CometEasing.Smooth(next - WindSeconds, next, time);
        var scale = ((1 - rise) * .9 + rise * (1 + .14 * flash)) * (1 - .1 * wind);
        var glow = baseGlow * (1 - .45 * wind) + 1.1 * flash * rise;
        var glowScale = 1 + .5 * flash * rise;
        var age = since < RingSeconds ? since : (double?)null;
        return new(2 * Math.PI * (turns + eased), speed, TrailFactor(speed), scale, glow, glowScale, pulse, age);
    }
}

public sealed record GatherParticle(Point Target, Color Color, int Group, double Delay, double Duration,
    double StartRadius, double StartAngle, double Size, double Twinkle)
{
    public double TargetRadius => Distance(Target, new Point(512, 512));
    public double TargetAngle => Math.Atan2(Target.Y - 512, Target.X - 512);
    public double Landed(double time) => CometEasing.Clamp((time - Delay) / Duration);
    public Point Position(double time)
    {
        var q = Landed(time);
        var radius = StartRadius + (TargetRadius - StartRadius) * CometEasing.OutCubic(q);
        var angle = StartAngle + (TargetAngle - StartAngle) * CometEasing.OutQuart(q);
        return new(512 + Math.Cos(angle) * radius, 512 + Math.Sin(angle) * radius);
    }
    public double Radius(double time) => Size * (1 - .45 * Landed(time));
    public double Opacity(double time) => time < Delay + Duration ? 1 :
        1 - CometEasing.Smooth(1, 1.35, (time - Delay) / Duration);
    internal static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}

public sealed record BootWindowLayout(Rect Frame, double CornerRadius, IReadOnlyList<Rect> ContentAreas);

public sealed record WindowParticle(Point Source, Point Kick, Point Target, Color Color, double Size,
    bool IsEdge, double Delay, double Duration, double Bend)
{
    public double LandsAt => Delay + Duration;
    public double Fly(double time) => CometEasing.InOutCubic((time - Delay) / Duration);
    public Point Position(double time)
    {
        var kicked = Mix(Source, Kick, CometEasing.OutCubic(time / .28));
        var fly = Fly(time);
        if (fly <= 0) return kicked;
        var dx = Target.X - kicked.X;
        var dy = Target.Y - kicked.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var control = new Point((kicked.X + Target.X) / 2 - dy / length * Bend,
            (kicked.Y + Target.Y) / 2 + dx / length * Bend);
        var left = 1 - fly;
        return new(left * left * kicked.X + 2 * left * fly * control.X + fly * fly * Target.X,
            left * left * kicked.Y + 2 * left * fly * control.Y + fly * fly * Target.Y);
    }
    public double Radius(double time) => Size * BootSequence.MarkSize / 1024 * .7 +
        Size * BootSequence.MarkSize / 1024 * .3 * (1 - Fly(time));
    public double Opacity(double time) => CometEasing.Smooth(0, .2, time) *
        (1 - CometEasing.Smooth(LandsAt, LandsAt + .35, time));
    private static Point Mix(Point a, Point b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
}

public enum BootPhase { Gather, Turning, AtRest, Disintegrating, StillMark, Closed }

public sealed record BootFrame(BootPhase Phase, double MarkOpacity, double TileOpacity, double TileScale,
    IReadOnlyList<double> ArmReveal, double CoreReveal, CometState Comet, double EdgeShare, double AllShare,
    double OutlineOpacity, double ContentOpacity, double ContentScale, bool MainWindowShown, bool StageClosed);

public sealed class BootSequence
{
    private const double Ignite = 1.45;
    public const double MarkSize = 210;
    public const double OutlineWidth = 1.5;
    public const double OutlineOpacity = .85;
    public const double OutlineGlow = 14;
    public static Color OutlineColor { get; } = Color.Parse("#BED2FF");
    // boot_sequence.html:285 shadowColor 'rgba(156,187,255,.9)'.
    public static Color OutlineGlowColor { get; } = Color.Parse("#9CBBFF");
    public const double OutlineGlowOpacity = .9;

    private readonly BootWindowLayout window;
    private readonly bool reduceMotion;
    public Point Centre => window.Frame.Center;
    public BootWindowLayout WindowLayout => window;
    public IReadOnlyList<GatherParticle> GatherParticles { get; }
    public IReadOnlyList<WindowParticle> WindowParticles { get; }

    public BootSequence(BootWindowLayout window, bool reduceMotion)
    {
        this.window = window;
        this.reduceMotion = reduceMotion;
        GatherParticles = reduceMotion ? [] : BuildGather();
        WindowParticles = reduceMotion ? [] : BuildWindow();
    }

    public Point IconToStage(Point icon) => new(Centre.X + (icon.X - 512) * MarkSize / 1024,
        Centre.Y + (icon.Y - 512) * MarkSize / 1024);

    public static double FinalRest(double? readyAt)
    {
        if (readyAt is null) return double.PositiveInfinity;
        var turns = (readyAt.Value - Ignite) / CometMotion.TurnSeconds;
        var rounded = Math.Round(turns);
        if (Math.Abs(turns - rounded) <= 1e-12) turns = rounded;
        return Ignite + CometMotion.TurnSeconds * Math.Max(1, Math.Ceiling(turns));
    }

    public double MainWindowShownAt(double? readyAt) => reduceMotion ? 2.5 :
        FinalRest(readyAt) + .1 + WindowParticles.Where(p => p.IsEdge).Max(p => p.LandsAt);
    public double StageClosesAt(double? readyAt) => reduceMotion ? 2.9 :
        FinalRest(readyAt) + .1 + WindowParticles.Max(p => p.LandsAt) + .35;

    public BootFrame At(double time, double? readyAt)
    {
        if (reduceMotion)
        {
            var alpha = CometEasing.Smooth(0, .3, time) * (1 - CometEasing.Smooth(2.4, 2.7, time));
            var content = CometEasing.Smooth(2.5, 2.9, time);
            return new(time < 2.9 ? BootPhase.StillMark : BootPhase.Closed, alpha, alpha, 1,
                [alpha, alpha, alpha], alpha, CometMotion.Still, 0, 0, 0, content, 1,
                time >= 2.5, time >= 2.9);
        }

        var rest = FinalRest(readyAt);
        var dis = rest + .1;
        var closes = StageClosesAt(readyAt);
        var phase = time < Ignite ? BootPhase.Gather : time < rest ? BootPhase.Turning :
            time < dis ? BootPhase.AtRest : time < closes ? BootPhase.Disintegrating : BootPhase.Closed;
        var mark = double.IsPositiveInfinity(dis) ? 1 : 1 - CometEasing.Smooth(dis, dis + .22, time);
        var arms = new double[3];
        for (var k = 0; k < 3; k++)
        {
            var group = GatherParticles.Where(p => p.Group == k).ToArray();
            arms[k] = CometEasing.Smooth(0, 1, group.Sum(p => CometEasing.Smooth(.7, 1, p.Landed(time))) / group.Length) * mark;
        }
        var core = GatherParticles.Where(p => p.Group == 3).ToArray();
        var coreReveal = CometEasing.Smooth(0, 1, core.Sum(p => CometEasing.Smooth(.7, 1, p.Landed(time))) / core.Length);
        CometState comet;
        if (time < Ignite)
        {
            var wind = CometEasing.Smooth(Ignite - CometMotion.WindSeconds, Ignite, time);
            comet = new(0, 0, 0, coreReveal * (1 - .1 * wind),
                (.25 + .3 * coreReveal) * (1 - .45 * wind), 1, null, null);
        }
        else
        {
            var relative = CometMotion.At(time - Ignite, double.IsPositiveInfinity(rest) ? null : rest - Ignite, coreReveal, .55);
            comet = relative with { LastPulse = relative.LastPulse + Ignite };
        }
        var tileProgress = CometEasing.Clamp((time - .55) / .65);
        var tile = CometEasing.Smooth(.55, 1.2, time) * mark;
        var tileScale = .94 + .06 * CometEasing.OutBack(tileProgress);
        var td = time - dis;
        var edgeShare = 0.0;
        var allShare = 0.0;
        if (td >= 0)
        {
            edgeShare = WindowParticles.Where(p => p.IsEdge).Sum(p => CometEasing.Smooth(.85, 1, p.Fly(td))) / 174;
            allShare = WindowParticles.Sum(p => CometEasing.Smooth(.85, 1, p.Fly(td))) / 300;
        }
        var outline = CometEasing.Smooth(0, 1, edgeShare) * (1 - .85 * CometEasing.Smooth(.55, 1, allShare));
        var contentOpacity = CometEasing.Smooth(.45, 1, allShare);
        var contentScale = .985 + .015 * CometEasing.OutCubic(contentOpacity);
        return new(phase, mark, tile, tileScale, arms, coreReveal * mark, comet,
            edgeShare, allShare, outline, contentOpacity, contentScale,
            time >= MainWindowShownAt(readyAt), time >= closes);
    }

    private List<GatherParticle> BuildGather()
    {
        var random = new CometRandom(7);
        var particles = new List<GatherParticle>(216);
        var arms = new[]
        {
            (new Point(392.88, 217.16), new Point(654.59, 415.82), Color.Parse("#9CBBFF"), Color.Parse("#4F7DFF")),
            (new Point(826.91, 556.26), new Point(524, 683.58), Color.Parse("#CBA6FF"), Color.Parse("#8A5CF6")),
            (new Point(316.22, 762.59), new Point(357.41, 436.6), Color.Parse("#74F2D0"), Color.Parse("#2FC6A0"))
        };
        for (var k = 0; k < 3; k++)
        {
            var (p1, p2, c0, c1) = arms[k];
            var (centre, angle, sweep) = Arc(p1, p2);
            for (var i = 0; i < 60; i++)
            {
                var u = (i + random.Next()) / 60;
                var at = angle + sweep * u;
                var across = (random.Next() - .5) * 92 * .8;
                var target = new Point(centre.X + Math.Cos(at) * (236.58 + across),
                    centre.Y + Math.Sin(at) * (236.58 + across));
                var gx = p2.X - p1.X;
                var gy = p2.Y - p1.Y;
                var colorAt = Math.Clamp(((target.X - p1.X) * gx + (target.Y - p1.Y) * gy) / (gx * gx + gy * gy), 0, 1);
                var color = Lerp(c0, c1, colorAt);
                var delay = .06 + k * .09 + random.Next() * .34;
                var duration = .7 + random.Next() * .28;
                particles.Add(Gather(target, color, k, delay, duration, random));
            }
        }
        for (var i = 0; i < 36; i++)
        {
            var radius = Math.Sqrt(random.Next()) * 82 * .9;
            var angle = random.Next() * 2 * Math.PI;
            var target = new Point(512 + Math.Cos(angle) * radius, 512 + Math.Sin(angle) * radius);
            particles.Add(Gather(target, Color.Parse("#ECF2FF"), 3,
                .5 + random.Next() * .22, .6 + random.Next() * .15, random));
        }
        return particles;
    }

    private static GatherParticle Gather(Point target, Color color, int group, double delay, double duration, CometRandom random)
    {
        var targetAngle = Math.Atan2(target.Y - 512, target.X - 512);
        var startRadius = 820 + random.Next() * 260;
        var startAngle = targetAngle - (1.3 + random.Next() * .9);
        var size = 9 + random.Next() * 11;
        var twinkle = random.Next() * 6.28;
        return new(target, color, group, delay, duration, startRadius, startAngle, size, twinkle);
    }

    private static (Point Centre, double Angle, double Sweep) Arc(Point p1, Point p2)
    {
        var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var half = Math.Sqrt(dx * dx + dy * dy) / 2;
        var height = Math.Sqrt(236.58 * 236.58 - half * half);
        foreach (var sign in new[] { 1, -1 })
        {
            var centre = new Point(mid.X + sign * height * -dy / (2 * half),
                mid.Y + sign * height * dx / (2 * half));
            var angle = Math.Atan2(p1.Y - centre.Y, p1.X - centre.X);
            var sweep = Math.Atan2(p2.Y - centre.Y, p2.X - centre.X) - angle;
            while (sweep < 0) sweep += 2 * Math.PI;
            if (sweep < Math.PI) return (centre, angle, sweep);
        }
        throw new InvalidOperationException("SVG arc could not be resolved");
    }

    private static Color Lerp(Color from, Color to, double u) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * u),
        (byte)Math.Round(from.G + (to.G - from.G) * u),
        (byte)Math.Round(from.B + (to.B - from.B) * u));

    private List<WindowParticle> BuildWindow()
    {
        var random = new CometRandom(99);
        var sources = new List<(Point Point, Color Color, double Size)>(300);
        sources.AddRange(GatherParticles.Select(p => (IconToStage(p.Target), p.Color, p.Size * .7)));
        for (var i = 0; i < 84; i++)
        {
            double x, y;
            if (i < 56)
            {
                var side = (int)(random.Next() * 4);
                var f = random.Next();
                switch (side)
                {
                    case 0: x = 72 + 880 * f; y = 72; break;
                    case 1: x = 952; y = 72 + 880 * f; break;
                    case 2: x = 72 + 880 * f; y = 952; break;
                    default: x = 72; y = 72 + 880 * f; break;
                }
            }
            else
            {
                x = 150 + random.Next() * 724;
                y = 150 + random.Next() * 724;
            }
            sources.Add((IconToStage(new Point(x, y)), Color.FromRgb(110, 124, 210), 7 + random.Next() * 6));
        }
        var edgeCount = (int)Math.Round(sources.Count * .58);
        var perimeter = 2 * (window.Frame.Width + window.Frame.Height - 4 * window.CornerRadius) +
            2 * Math.PI * window.CornerRadius;
        var edgeTargets = Enumerable.Range(0, edgeCount)
            .Select(i => PointOnRoundRect((i + .5) / edgeCount * perimeter))
            .OrderBy(Angle).ToArray();
        var innerTargets = new List<Point>(sources.Count - edgeCount);
        for (var i = edgeCount; i < sources.Count; i++)
        {
            var area = window.ContentAreas[(int)(random.Next() * window.ContentAreas.Count)];
            innerTargets.Add(new Point(area.X + random.Next() * area.Width, area.Y + random.Next() * area.Height));
        }
        innerTargets.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
        var byRadius = sources.OrderByDescending(p => GatherParticle.Distance(p.Point, Centre)).ToArray();
        var edgeSources = byRadius.Take(edgeCount).OrderBy(p => Angle(p.Point)).ToArray();
        var innerSources = byRadius.Skip(edgeCount).OrderBy(p => Angle(p.Point)).ToArray();
        var result = new List<WindowParticle>(300);
        Pair(edgeSources, edgeTargets, true);
        Pair(innerSources, innerTargets.ToArray(), false);
        return result;

        void Pair((Point Point, Color Color, double Size)[] picked, Point[] targets, bool edge)
        {
            for (var i = 0; i < picked.Length; i++)
            {
                var item = picked[i];
                var outAngle = Angle(item.Point);
                var kickLength = 18 + random.Next() * 36;
                var kick = new Point(item.Point.X + Math.Cos(outAngle) * kickLength,
                    item.Point.Y + Math.Sin(outAngle) * kickLength);
                result.Add(new WindowParticle(item.Point, kick, targets[i], item.Color, item.Size, edge,
                    .14 + (edge ? 0 : .24) + random.Next() * .2,
                    .72 + random.Next() * .26, (random.Next() - .5) * 80));
            }
        }
    }

    private double Angle(Point point) => Math.Atan2(point.Y - Centre.Y, point.X - Centre.X);

    private Point PointOnRoundRect(double distance)
    {
        var frame = window.Frame;
        var radius = window.CornerRadius;
        var straightWidth = frame.Width - 2 * radius;
        var straightHeight = frame.Height - 2 * radius;
        var quarter = Math.PI * radius / 2;
        if (distance <= straightWidth) return new(frame.X + radius + distance, frame.Y);
        distance -= straightWidth;
        if (distance <= quarter) return ArcPoint(frame.Right - radius, frame.Y + radius, -Math.PI / 2 + distance / radius);
        distance -= quarter;
        if (distance <= straightHeight) return new(frame.Right, frame.Y + radius + distance);
        distance -= straightHeight;
        if (distance <= quarter) return ArcPoint(frame.Right - radius, frame.Bottom - radius, distance / radius);
        distance -= quarter;
        if (distance <= straightWidth) return new(frame.Right - radius - distance, frame.Bottom);
        distance -= straightWidth;
        if (distance <= quarter) return ArcPoint(frame.X + radius, frame.Bottom - radius, Math.PI / 2 + distance / radius);
        distance -= quarter;
        if (distance <= straightHeight) return new(frame.X, frame.Bottom - radius - distance);
        distance -= straightHeight;
        return ArcPoint(frame.X + radius, frame.Y + radius, Math.PI + distance / radius);

        Point ArcPoint(double x, double y, double angle) =>
            new(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius);
    }

    private sealed class CometRandom(int seed)
    {
        private long value = seed;
        public double Next()
        {
            value = value * 16807 % 2147483647;
            return (double)value / 2147483647;
        }
    }
}

/// <summary>The in-app mark, driven by the frame source while attached.</summary>
public sealed class LoadingMark : Control
{
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private ContinuousInstant started;
    private bool waiting;
    private double elapsed;

    public LoadingMark()
    {
        frame = OnFrame;
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetName(this, "Loading");
        AttachedToVisualTree += (_, _) =>
        {
            started = SwiftClock.Now;
            elapsed = 0;
            source = FrameClock.Source.Current;
            Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source!.CancelFrame(frame);
            waiting = false;
            source = null;
        };
    }

    public bool ReduceMotion { get; set; }
    public double Elapsed => elapsed;
    public double CurrentOpacity => Opacity(elapsed);
    public CometState State { get; private set; } = CometMotion.Still;

    public new static double Opacity(double sinceWaitStart) =>
        CometEasing.Clamp((sinceWaitStart - .4) / .2);

    public static CometState Motion(double sinceWaitStart, bool reduceMotion) =>
        reduceMotion ? CometMotion.Still : CometMotion.Running(Math.Max(0, sinceWaitStart - .4));

    private void Request()
    {
        if (waiting || source is null) return;
        waiting = true;
        source.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (source is null) return;
        elapsed = (SwiftClock.Now - started).TotalSeconds;
        State = Motion(elapsed, ReduceMotion || EnvironmentValues.GetAccessibilityReduceMotion(this));
        InvalidateVisual();
        Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var alpha = Opacity(elapsed);
        if (alpha <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / 1024;
        var left = (Bounds.Width - size) / 2;
        var top = (Bounds.Height - size) / 2;
        var state = State;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
        using (context.PushOpacity(alpha))
        {
            CometPainter.DrawTile(context, 1, 1);
            if (state.RingAge is { } age)
            {
                using (context.PushOpacity(CometMotion.RingOpacity(age)))
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(CometMotion.RingColor), 3 * 1024 / size),
                        new Point(512, 512), CometMotion.RingRadius(age), CometMotion.RingRadius(age));
            }
            var arms = new[]
            {
                (new Point(392.88, 217.16), new Point(654.59, 415.82), Color.Parse("#9CBBFF"), Color.Parse("#4F7DFF")),
                (new Point(826.91, 556.26), new Point(524, 683.58), Color.Parse("#CBA6FF"), Color.Parse("#8A5CF6")),
                (new Point(316.22, 762.59), new Point(357.41, 436.6), Color.Parse("#74F2D0"), Color.Parse("#2FC6A0"))
            };
            CometPainter.DrawArms(context, [1, 1, 1], state, main: false);
            foreach (var (a, b, start, end) in arms)
            {
                var (centre, angle, sweep) = ArcForDrawing(a, b);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    for (var i = 0; i <= 32; i++)
                    {
                        var t = (double)i / 32;
                        var px = centre.X + 236.58 * Math.Cos(angle + sweep * t) - 512;
                        var py = centre.Y + 236.58 * Math.Sin(angle + sweep * t) - 512;
                        var p = new Point(512 + px * Math.Cos(state.Angle) - py * Math.Sin(state.Angle),
                            512 + px * Math.Sin(state.Angle) + py * Math.Cos(state.Angle));
                        if (i == 0) path.BeginFigure(p, false);
                        else path.LineTo(p);
                    }
                    path.EndFigure(false);
                }
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(CometPainter.Rotate(a, state.Angle), RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(CometPainter.Rotate(b, state.Angle), RelativeUnit.Absolute),
                    GradientStops = new GradientStops { new(start, 0), new(end, 1) }
                };
                context.DrawGeometry(null, new Pen(brush, 92, lineCap: PenLineCap.Round), geometry);
            }
            CometPainter.DrawCore(context, state, 1);
        }
    }

    private static (Point Centre, double Angle, double Sweep) ArcForDrawing(Point p1, Point p2)
    {
        var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var half = Math.Sqrt(dx * dx + dy * dy) / 2;
        var height = Math.Sqrt(236.58 * 236.58 - half * half);
        foreach (var sign in new[] { 1, -1 })
        {
            var centre = new Point(mid.X + sign * height * -dy / (2 * half),
                mid.Y + sign * height * dx / (2 * half));
            var angle = Math.Atan2(p1.Y - centre.Y, p1.X - centre.X);
            var sweep = Math.Atan2(p2.Y - centre.Y, p2.X - centre.X) - angle;
            while (sweep < 0) sweep += 2 * Math.PI;
            if (sweep < Math.PI) return (centre, angle, sweep);
        }
        throw new InvalidOperationException("SVG arc could not be resolved");
    }
}

/// <summary>The drawing of the mark (ED-102 (7)); public so the headless tests can render its passes.</summary>
public static class CometPainter
{
    public static void DrawTile(DrawingContext context, double opacity, double scale)
    {
        if (opacity <= 0) return;
        var width = 880 * scale;
        var tile = new Rect(512 - width / 2, 512 - width / 2, width, width);
        var corner = 204 * scale;
        using (context.PushOpacity(opacity))
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(.1, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(.9, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse("#1D2147"), 0), new(Color.Parse("#121529"), .55),
                    new(Color.Parse("#0A0C18"), 1)
                }
            };
            context.DrawRectangle(background, null, tile, corner, corner);
            var glow = new RadialGradientBrush
            {
                Center = new RelativePoint(.3, .16, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(.3, .16, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(722.0 / 880, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(722.0 / 880, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(56, 147, 172, 255), 0),
                    new(Color.FromArgb(0, 147, 172, 255), 1)
                }
            };
            using (context.PushClip(new RoundedRect(tile, corner)))
                context.DrawRectangle(glow, null, tile);
            var keyline = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(56, 255, 255, 255), 0),
                    new(Color.FromArgb(8, 255, 255, 255), .6),
                    new(Color.FromArgb(0, 255, 255, 255), 1)
                }
            };
            var inset = 1.5 * scale;
            context.DrawRectangle(null, new Pen(keyline, 3 * scale),
                new Rect(tile.X + inset, tile.Y + inset, tile.Width - 2 * inset, tile.Height - 2 * inset),
                202.5 * scale, 202.5 * scale);
        }
    }

    /// <summary>
    /// The core and its glow (boot_sequence.html:215-225; ED-102 (3)): the glow is a radial gradient of radius 230 * glowS
    /// with rgba(255,255,255, clamp(.55 glowA)) at 0 and rgba(185,200,255, clamp(.28 glowA)) at .35 (:218-219), glowA and
    /// glowS being <see cref="CometState.GlowOpacity"/> and <see cref="CometState.GlowScale"/>.
    /// </summary>
    public static void DrawCore(DrawingContext context, CometState state, double opacity)
    {
        if (opacity <= 0) return;
        using (context.PushOpacity(opacity))
        {
            var glow = new RadialGradientBrush
            {
                Center = new RelativePoint(.5, .5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(.5, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(ToByte(.55 * state.GlowOpacity), 255, 255, 255), 0),
                    new(Color.FromArgb(ToByte(.28 * state.GlowOpacity), 185, 200, 255), .35),
                    new(Color.FromArgb(0, 147, 172, 255), 1)
                }
            };
            context.DrawEllipse(glow, null, new Point(512, 512),
                230 * state.GlowScale, 230 * state.GlowScale);
            if (state.CoreScale <= .001) return;
            var core = new RadialGradientBrush
            {
                Center = new RelativePoint(.38, .32, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(.38, .32, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(.8, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(.8, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse("#FFFFFF"), 0), new(Color.Parse("#F2F5FF"), .72),
                    new(Color.Parse("#D6DEFB"), 1)
                }
            };
            context.DrawEllipse(core, null, new Point(512, 512),
                82 * state.CoreScale, 82 * state.CoreScale);
        }
    }

    private static byte ToByte(double opacity) => (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));

    public static Point Rotate(Point point, double radians)
    {
        var x = point.X - 512;
        var y = point.Y - 512;
        return new(512 + x * Math.Cos(radians) - y * Math.Sin(radians),
            512 + x * Math.Sin(radians) + y * Math.Cos(radians));
    }

    public static void DrawDot(DrawingContext context, Point at, double radius, Color color, double alpha)
    {
        if (alpha <= .005) return;
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(.5, .5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.FromArgb(255, 255, 255, 255), 0),
                new(Color.FromArgb(255, color.R, color.G, color.B), .25),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        using (context.PushOpacity(alpha)) context.DrawEllipse(brush, null, at, radius, radius);
    }

    /// <summary>
    /// A Gaussian blur of standard deviation <paramref name="sigma"/> in the drawing units of the pushed transform.
    /// The reference blurs with CSS blur() (boot_sequence.html:188, trails, ED-102 (2)) and a canvas shadowBlur
    /// (:284, window glow, ED-103 (5)); a CSS blur() length is the standard deviation, a shadowBlur is twice it.
    /// ENGINEERING DECISION (review point 1): Avalonia.Skia 12.1.2 turns BlurEffect.Radius r into a Skia sigma of
    /// .288675 r + .5 (its SkBlurRadiusToSigma; the ldc.r4 0.288675 is in Avalonia.Skia.dll), in the units of the
    /// pushed transform, so the radius for a standard deviation s is (s - .5) / .288675. Measured by
    /// LoadingMarkHeadlessTests.The_blur_is_a_gaussian_whose_standard_deviation_is_the_blur_value_in_drawing_units_at_every_scale.
    /// </summary>
    public static BlurEffect GaussianBlur(double sigma) => new() { Radius = Math.Max(0, (sigma - .5) / .288675) };

    public static void DrawArms(DrawingContext context, IReadOnlyList<double> reveals, CometState state, bool main)
    {
        var arms = new[]
        {
            (new Point(392.88, 217.16), new Point(654.59, 415.82), Color.Parse("#9CBBFF"), Color.Parse("#4F7DFF")),
            (new Point(826.91, 556.26), new Point(524, 683.58), Color.Parse("#CBA6FF"), Color.Parse("#8A5CF6")),
            (new Point(316.22, 762.59), new Point(357.41, 436.6), Color.Parse("#74F2D0"), Color.Parse("#2FC6A0"))
        };
        for (var pass = 0; pass < (main ? 3 : 2); pass++)
        {
            var trail = pass < 2;
            if (trail && state.TrailFactor <= 0) continue;
            var offset = trail ? CometMotion.Trails[pass].OffsetDegrees * Math.PI / 180 : 0;
            var blur = trail ? CometMotion.Trails[pass].Blur : 0;
            var amount = trail ? CometMotion.Trails[pass].Opacity * state.TrailFactor : 1;
            if (trail)
            {
                using (context.PushEffect(GaussianBlur(blur), new Rect(0, 0, 1024, 1024))) DrawPass();
            }
            else DrawPass();

            void DrawPass()
            {
              for (var k = 0; k < 3; k++)
              {
                if (reveals[k] * amount <= .001) continue;
                var (a, b, start, end) = arms[k];
                var (centre, angle, sweep) = Arc(a, b);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    for (var i = 0; i <= 48; i++)
                    {
                        var t = (double)i / 48;
                        var px = centre.X + 236.58 * Math.Cos(angle + sweep * t) - 512;
                        var py = centre.Y + 236.58 * Math.Sin(angle + sweep * t) - 512;
                        var rotation = state.Angle - offset;
                        var p = new Point(512 + px * Math.Cos(rotation) - py * Math.Sin(rotation),
                            512 + px * Math.Sin(rotation) + py * Math.Cos(rotation));
                        if (i == 0) path.BeginFigure(p, false);
                        else path.LineTo(p);
                    }
                    path.EndFigure(false);
                }
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(Rotate(a, state.Angle - offset), RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(Rotate(b, state.Angle - offset), RelativeUnit.Absolute),
                    GradientStops = new GradientStops { new(start, 0), new(end, 1) }
                };
                using (context.PushOpacity(reveals[k] * amount))
                    context.DrawGeometry(null, new Pen(brush, 92, lineCap: PenLineCap.Round), geometry);
              }
            }
        }
    }

    private static (Point Centre, double Angle, double Sweep) Arc(Point p1, Point p2)
    {
        var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var half = Math.Sqrt(dx * dx + dy * dy) / 2;
        var height = Math.Sqrt(236.58 * 236.58 - half * half);
        foreach (var sign in new[] { 1, -1 })
        {
            var centre = new Point(mid.X + sign * height * -dy / (2 * half),
                mid.Y + sign * height * dx / (2 * half));
            var angle = Math.Atan2(p1.Y - centre.Y, p1.X - centre.X);
            var sweep = Math.Atan2(p2.Y - centre.Y, p2.X - centre.X) - angle;
            while (sweep < 0) sweep += 2 * Math.PI;
            if (sweep < Math.PI) return (centre, angle, sweep);
        }
        throw new InvalidOperationException("SVG arc could not be resolved");
    }
}

/// <summary>The frame-driven particle stage; its host window is supplied by the App layer.</summary>
public sealed class BootStage : Control
{
    private readonly BootSequence sequence;
    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private ContinuousInstant started;
    private bool waiting;
    private bool shown;
    private bool closed;
    private bool attached;
    private double? readyAt;

    public BootStage(BootSequence sequence)
    {
        this.sequence = sequence;
        tick = OnFrame;
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetName(this, "Loading");
        Frame = sequence.At(0, null);
        AttachedToVisualTree += (_, _) =>
        {
            started = SwiftClock.Now;
            attached = true;
            source = FrameClock.Source.Current;
            Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source!.CancelFrame(tick);
            waiting = false;
            source = null;
        };
    }

    public double Elapsed { get; private set; }
    public BootFrame Frame { get; private set; }
    public event EventHandler? MainWindowShown;
    public event EventHandler? Closed;
    // ED-103 (3): the moment the app is ready, read from the clock at the call (not the last frame's time, which would
    // pick a turn end already past); readiness before the stage starts counts as ready at 0.
    public void Ready() => readyAt ??= attached ? (SwiftClock.Now - started).TotalSeconds : 0;

    private void Request()
    {
        if (waiting || source is null || closed) return;
        waiting = true;
        source.RequestFrame(tick);
    }

    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (source is null || closed) return;
        Elapsed = (SwiftClock.Now - started).TotalSeconds;
        Frame = sequence.At(Elapsed, readyAt);
        InvalidateVisual();
        if (Frame.MainWindowShown && !shown)
        {
            shown = true;
            MainWindowShown?.Invoke(this, EventArgs.Empty);
        }
        if (Frame.StageClosed)
        {
            closed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
        else Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var centre = sequence.Centre;
        if (Frame.MarkOpacity > 0)
        {
            var scale = BootSequence.MarkSize / 1024;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) *
                   Matrix.CreateTranslation(centre.X - BootSequence.MarkSize / 2,
                       centre.Y - BootSequence.MarkSize / 2)))
            {
                CometPainter.DrawTile(context, Frame.TileOpacity, Frame.TileScale);
                CometPainter.DrawArms(context, Frame.ArmReveal, Frame.Comet, main: true);
                CometPainter.DrawCore(context, Frame.Comet, Frame.MarkOpacity);
            }
            if (Frame.Comet.RingAge is { } age)
            {
                using (context.PushOpacity(Frame.MarkOpacity * CometMotion.RingOpacity(age)))
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(CometMotion.RingColor), CometMotion.RingWidth),
                        centre, CometMotion.RingRadius(age) * BootSequence.MarkSize / 1024,
                        CometMotion.RingRadius(age) * BootSequence.MarkSize / 1024);
            }
        }
        foreach (var p in sequence.GatherParticles) DrawGatherDot(context, sequence, p, Elapsed);
        if (Frame.Phase is BootPhase.Disintegrating or BootPhase.Closed)
        {
            var dis = readyAt is null ? double.PositiveInfinity : BootSequence.FinalRest(readyAt) + .1;
            var td = Elapsed - dis;
            foreach (var p in sequence.WindowParticles) DrawWindowDot(context, p, td);
        }
        if (Frame.OutlineOpacity > 0) DrawOutline(context, sequence.WindowLayout, Frame.OutlineOpacity);
    }

    /// <summary>
    /// One gather particle at <paramref name="time"/> seconds into the boot, in stage DIP (boot_sequence.html:338-345).
    /// Before its start it twinkles on its start ring as a dot of 1.8 times its size at alpha
    /// smooth(0, .18, t) * .35 * (.6 + .4 sin(tw + 9 t)) (:340; ED-102 (4) says nothing of a waiting particle, so the
    /// reference governs). In flight and after landing it is a dot at alpha fade * (.55 + .45 smooth(0, .25, q)) (:342-344).
    /// ENGINEERING DECISION (final review finding 2): the flying dot's drawn radius is the particle's size (9 to 20 icon
    /// units) times (1 - .45 q), as ED-102 (4) states it ("a particle is drawn as a soft dot of radius 9 to 20 shrinking
    /// to 55 % as it lands"); the reference draws 2.2 times that (:344), and ED-102 says that where its text and the
    /// preview differ, the text rules. Flagged in the unit report for the orchestrator's confirmation.
    /// </summary>
    public static void DrawGatherDot(DrawingContext context, BootSequence sequence, GatherParticle particle, double time)
    {
        var at = sequence.IconToStage(particle.Position(time));
        if (time <= particle.Delay)
        {
            var waitingAlpha = CometEasing.Smooth(0, .18, time) * .35 *
                (.6 + .4 * Math.Sin(particle.Twinkle + time * 9));
            CometPainter.DrawDot(context, at, particle.Size * BootSequence.MarkSize / 1024 * 1.8,
                particle.Color, waitingAlpha);
            return;
        }
        var alpha = particle.Opacity(time) * (.55 + .45 * CometEasing.Smooth(0, .25, particle.Landed(time)));
        if (alpha <= 0) return;
        CometPainter.DrawDot(context, at, particle.Radius(time) * BootSequence.MarkSize / 1024, particle.Color, alpha);
    }

    /// <summary>
    /// One window particle at <paramref name="td"/> seconds after the disintegration starts, in stage DIP
    /// (boot_sequence.html:355-362): a dot of 2.2 times its size times (1 - .3 fly) (<see cref="WindowParticle.Radius"/>
    /// carries the size, the 210/1024 scale and the (1 - .3 fly)) at alpha appear * melt * (.6 + .4 (1 - fly)).
    /// ED-103 (4) gives no dot size, so the reference governs.
    /// </summary>
    public static void DrawWindowDot(DrawingContext context, WindowParticle particle, double td)
    {
        var alpha = particle.Opacity(td) * (.6 + .4 * (1 - particle.Fly(td)));
        if (alpha <= 0) return;
        CometPainter.DrawDot(context, particle.Position(td), particle.Radius(td) * 2.2, particle.Color, alpha);
    }

    /// <summary>
    /// ED-103 (5): the window outline, 1.5 DIP of #BED2FF at .85 with a 14 DIP glow, all under <paramref name="share"/>
    /// (boot_sequence.html:283-288, globalAlpha = edge). The glow colour is the reference's shadowColor
    /// rgba(156,187,255,.9) (:285), which ED-103 does not restate.
    /// </summary>
    public static void DrawOutline(DrawingContext context, BootWindowLayout layout, double share)
    {
        var outline = layout.Frame;
        var radius = layout.CornerRadius;
        // ENGINEERING DECISION: a canvas shadow takes the drawn shape's alpha times the shadow colour's alpha, so the
        // glow is #9CBBFF at .9 * .85, blurred under the stroke.
        var glow = Color.FromArgb((byte)Math.Round(255 * BootSequence.OutlineGlowOpacity * BootSequence.OutlineOpacity),
            BootSequence.OutlineGlowColor.R, BootSequence.OutlineGlowColor.G, BootSequence.OutlineGlowColor.B);
        var stroke = Color.FromArgb((byte)Math.Round(255 * BootSequence.OutlineOpacity),
            BootSequence.OutlineColor.R, BootSequence.OutlineColor.G, BootSequence.OutlineColor.B);
        using (context.PushOpacity(share))
        {
            // The glow is ctx.shadowBlur = px(14) (boot_sequence.html:284): a Gaussian of standard deviation 7 DIP. The
            // effect bounds reach 4 sigma past the outline so the glow is not cut off.
            var sigma = BootSequence.OutlineGlow / 2;
            using (context.PushEffect(CometPainter.GaussianBlur(sigma), outline.Inflate(4 * sigma)))
                context.DrawRectangle(null, new Pen(new SolidColorBrush(glow), BootSequence.OutlineWidth),
                    outline, radius, radius);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(stroke), BootSequence.OutlineWidth),
                outline, radius, radius);
        }
    }

}
