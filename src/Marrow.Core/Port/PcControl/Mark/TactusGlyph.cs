// PORT-ONLY: Tactus's mark, Cuspis, "the tip" (owner-locked 2026-10-11, OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). THE ONE
// FILE THAT DEFINES THE GLYPH: every surface (pill slot, collapsed pill, fullscreen dot, window tags, approval badge, chat
// strip, tray, notifications, Settings row, composer chip) draws the mark through TactusMark and its images, which ask
// only this file for shapes, so a redraw of the mark changes this file alone.
//
// Cuspis is the Tactus pointer (a dart with an upright left edge, a 45 degree wing and a notched back) cut open at the
// heel, the cut stopping halfway so it never runs tip to tail. It is drawn from a few numbers, exactly as the review page
// (E:/Droppy/work/design/tactus-icon/index.html, option C) draws it, so its poses morph by moving those numbers on springs:
// Rest (the final master, pointer-1-final.svg), Wind (the wind-up before a press), Press (the cut gives way 2 units),
// Lift (Paused: the point lifts and the cut opens 2 units) and Zero (shrunk to its tip, the start of its arrival and the
// end of its leaving). The 16 px tray drawings (pointer-1-final-16*.svg) come from the same numbers at 16 units.
namespace Marrow.Core.Port.PcControl.Mark;

/// <summary>The poses of the master mark.</summary>
public enum TactusPose
{
    Rest,
    Wind,
    Press,
    Lift,
    Zero,
}

/// <summary>The hand-hinted 16 px drawings, for tray-size status.</summary>
public enum TactusSmallState
{
    Ready,
    Working,
    Paused,
}

/// <summary>A small drawing: the mark, the square cut out of it (Paused), and extra shapes drawn whole (the pause bars).</summary>
public sealed record TactusSmallDrawing(TactusPath Mark, (double X, double Y, double Width, double Height)? KnockOut, TactusPath? Extra);

/// <summary>One spring move of the arrival or leaving: parameters, their target (the base pose's value when null), spring, delay.</summary>
public sealed record TactusMove(IReadOnlyList<int> Parameters, double? Value, double Stiffness, double DampingRatio, int DelayMs);

public static class TactusGlyph
{
    public const string Name = "Cuspis";

    /// <summary>The master's square (pill slot, fullscreen dot, tray at 150 and 200 %).</summary>
    public const int MasterUnits = 24;

    /// <summary>The hand-hinted drawings' square (tray at 100 %).</summary>
    public const int SmallUnits = 16;

    /// <summary>
    /// Drawn smaller than this (in DIP or pixels) the mark uses the hand-hinted 16 unit drawing, scaled: the window tags
    /// (12), the approval badge (13), the chat strip (14), the Settings row, the composer chip and the tray at 100 % (16),
    /// as the canvas draws them; at this size and up the 24 unit master (the pill slot, the fullscreen dot, the tray at
    /// 150 and 200 %).
    /// </summary>
    public const double SmallestMasterSize = 20;

    /// <summary>True when a mark this big uses the hand-hinted drawing.</summary>
    public static bool UsesSmallDrawing(double size) => size < SmallestMasterSize;

    // The numbers, in this order.
    public const int X0 = 0;
    public const int Y0 = 1;
    public const int Length = 2;
    public const int Notch = 3;
    public const int Gap = 4;
    public const int Gap2 = 5;
    public const int CutBase = 6;
    public const int CutAngle = 7;
    public const int CutLength = 8;
    public const int CutRadius = 9;
    public const int Shift = 10;
    public const int Scale = 11;
    public const int TipRadius = 12;
    public const int HeelRadius = 13;
    public const int WingRadius = 14;
    public const int Count = 15;

    /// <summary>The wing's slope: the wing point sits this far right and down per unit of length.</summary>
    private const double Wing = 0.7;

    private static readonly double[] Rest = [7, 2.6, 18.6, 1.5, 3, 0, 0.38, -8, 8, 0.5, 0, 1, 0.85, 0.75, 0.75];
    private static readonly double[] Small = [5, 2, 12, 1, 2, 0, 0.38, -8, 5, 0.3, 0, 1, 0.55, 0.5, 0.5];

    /// <summary>A pose's numbers (a fresh copy).</summary>
    public static double[] Pose(TactusPose pose)
    {
        var values = (double[])Rest.Clone();
        switch (pose)
        {
            case TactusPose.Wind:
                values[Gap] = 3.6;
                values[Shift] = 0.3;
                break;
            case TactusPose.Press:
                values[Gap] = 1.2;
                values[Shift] = -0.6;
                break;
            case TactusPose.Lift:
                values[Gap] = 5;
                values[Shift] = 0.4;
                break;
            case TactusPose.Zero:
                values[Gap] = 0;
                values[Scale] = 0;
                break;
        }
        return values;
    }

    /// <summary>The arrival from Zero: the mark grows from its tip, then its cut opens to the base pose.</summary>
    public static IReadOnlyList<TactusMove> Enter { get; } =
    [
        new([Scale], 1, 520, 0.72, 0),
        new([Gap, Shift], null, 420, 0.85, 170),
    ];

    /// <summary>The leaving: the cut closes, then the mark shrinks to its tip.</summary>
    public static IReadOnlyList<TactusMove> Leave { get; } =
    [
        new([Gap, Shift], 0, 700, 1, 0),
        new([Scale], 0, 520, 1, 190),
    ];

    /// <summary>The master mark at the given numbers, in 24 units.</summary>
    public static TactusPath Draw(ReadOnlySpan<double> p)
    {
        if (p.Length != Count) throw new ArgumentException("The mark takes " + Count + " numbers.", nameof(p));
        var (tip, heel, wing, notch) = Dart(p);
        TactusPath path;
        if (p[Gap] < 0.15)
        {
            // Closed: the plain pointer, with no hairline seam.
            path = Rounded([tip, heel, notch, wing], [p[TipRadius], p[HeelRadius], 0.3, p[WingRadius]]);
        }
        else
        {
            var angle = p[CutAngle] * Math.PI / 180;
            var down = new V(Math.Sin(angle), -Math.Cos(angle));
            var across = new V(Math.Cos(angle), Math.Sin(angle));
            var at = p[CutBase];
            var cutBase = at <= 0.5 ? heel + ((notch - heel) * (at * 2)) : notch + ((wing - notch) * ((at * 2) - 1));
            var end = cutBase + (down * p[CutLength]);
            var gap = p[Gap];
            var gap2 = Math.Min(p[Gap2], gap);
            V Side(double k)
            {
                var origin = cutBase + (across * (k * gap / 2)) - (down * 40);
                return Hit(origin, down, heel, notch) ?? Hit(origin, down, notch, wing) ?? cutBase;
            }
            var sideA = Side(-1);
            var sideB = Side(1);
            var endA = end + (across * (-gap2 / 2));
            var endB = end + (across * (gap2 / 2));
            var cornerRadius = Math.Min(0.5, p[Gap] * 0.2);
            var endRadius = gap2 > 0.05 ? Math.Min(p[CutRadius], gap2 / 2 * 0.98) : 0.12 * Math.Pow(Math.Min(1, p[Gap] / 2.7), 2);
            List<V> points = [tip, heel, sideA, endA];
            List<double> radii = [p[TipRadius], p[HeelRadius], cornerRadius, endRadius];
            if (gap2 > 0.05)
            {
                points.Add(endB);
                radii.Add(endRadius);
            }
            points.Add(sideB);
            radii.Add(cornerRadius);
            if (Math.Abs(Cross(notch - heel, sideB - heel)) < 1e-6)
            {
                points.Add(notch);
                radii.Add(0.3);
            }
            points.Add(wing);
            radii.Add(p[WingRadius]);
            path = Rounded(points, radii);
        }
        return Math.Abs(p[Scale] - 1) > 1e-4 ? path.Scaled(tip.X, tip.Y, Math.Max(0, p[Scale])) : path;
    }

    /// <summary>The master mark in a pose.</summary>
    public static TactusPath Draw(TactusPose pose) => Draw(Pose(pose));

    /// <summary>The hand-hinted 16 px drawing of a state: Ready open, Working pressed shut, Paused lifted 1 px with the pause bars.</summary>
    public static TactusSmallDrawing SmallDrawing(TactusSmallState state)
    {
        var values = (double[])Small.Clone();
        if (state == TactusSmallState.Working) values[Gap] = 0;
        if (state != TactusSmallState.Paused) return new TactusSmallDrawing(Draw(values), null, null);
        values[Y0] = 1;
        // Two 2 px bars take the lower-right corner; the mark is cut 1 px clear of them.
        var bars = new TactusPath()
            .MoveTo(11, 11).LineTo(13, 11).LineTo(13, 16).LineTo(11, 16).Close()
            .MoveTo(14, 11).LineTo(16, 11).LineTo(16, 16).LineTo(14, 16).Close();
        return new TactusSmallDrawing(Draw(values), (10, 10, 6, 6), bars);
    }

    private static (V Tip, V Heel, V Wing, V Notch) Dart(ReadOnlySpan<double> p)
    {
        var tip = new V(p[X0], p[Y0]);
        var heel = new V(p[X0], p[Y0] + p[Length]);
        var wing = new V(p[X0] + (Wing * p[Length]), p[Y0] + (Wing * p[Length]));
        var middle = (heel + wing) * 0.5;
        var along = (middle - tip).Unit();
        var shift = along * p[Shift];
        return (tip + shift, heel + shift, wing + shift, middle + shift - (along * p[Notch]));
    }

    /// <summary>Where the ray from <paramref name="origin"/> along <paramref name="direction"/> crosses the segment a-b; null when it misses.</summary>
    private static V? Hit(V origin, V direction, V a, V b)
    {
        var edge = b - a;
        var denominator = Cross(direction, edge);
        if (Math.Abs(denominator) < 1e-9) return null;
        var t = Cross(a - origin, edge) / denominator;
        var v = Cross(a - origin, direction) / denominator;
        return v >= -1e-6 && v <= 1 + 1e-6 ? origin + (direction * t) : null;
    }

    /// <summary>A closed polygon with a corner radius per vertex (as the review page's rounded-polygon helper).</summary>
    private static TactusPath Rounded(IReadOnlyList<V> points, IReadOnlyList<double> radii)
    {
        var n = points.Count;
        var corners = new (V A, V B, double Radius, bool Clockwise)?[n];
        for (var i = 0; i < n; i++)
        {
            var vertex = points[i];
            var before = points[(i - 1 + n) % n];
            var after = points[(i + 1) % n];
            var toBefore = (before - vertex).Length();
            var toAfter = (after - vertex).Length();
            var radius = radii[i];
            if (radius <= 0 || toBefore < 1e-6 || toAfter < 1e-6) continue;
            var a = (before - vertex).Unit();
            var b = (after - vertex).Unit();
            var angle = Math.Acos(Math.Clamp((a.X * b.X) + (a.Y * b.Y), -1, 1));
            if (angle < 1e-3 || angle > Math.PI - 1e-3) continue;
            var t = radius / Math.Tan(angle / 2);
            var limit = Math.Min(toBefore * 0.5, toAfter * 0.5);
            if (t > limit)
            {
                t = limit;
                radius = t * Math.Tan(angle / 2);
            }
            corners[i] = (vertex + (a * t), vertex + (b * t), radius, Cross(vertex - before, after - vertex) > 0);
        }
        var path = new TactusPath();
        var start = corners[0] is { } first ? first.B : points[0];
        path.MoveTo(start.X, start.Y);
        for (var i = 1; i <= n; i++)
        {
            if (corners[i % n] is { } corner)
            {
                path.LineTo(corner.A.X, corner.A.Y);
                path.ArcTo(corner.B.X, corner.B.Y, corner.Radius, corner.Clockwise);
            }
            else
            {
                var point = points[i % n];
                path.LineTo(point.X, point.Y);
            }
        }
        return path.Close();
    }

    private static double Cross(V a, V b) => (a.X * b.Y) - (a.Y * b.X);

    private readonly record struct V(double X, double Y)
    {
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y);

        public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y);

        public static V operator *(V a, double k) => new(a.X * k, a.Y * k);

        public double Length() => Math.Sqrt((X * X) + (Y * Y));

        public V Unit()
        {
            var length = Length();
            return length < 1e-9 ? new V(0, 0) : new V(X / length, Y / length);
        }
    }
}
