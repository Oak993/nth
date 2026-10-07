// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). What the ONE shared capsule shows, as plain numbers and roles with no Avalonia in them (multi-account.html Capsule(), set(), build()).
// The renderer in Marrow.App draws these; the maths of "what is left", the roles of the steps and the geometry of the styles live here so they can be proved without a screen.
namespace Marrow.Core.Port.MultiAccount.Capsule;

/// <summary>One step of a pill: the account's mark, its hue (0xRRGGBB, the mark's tint and never the usage light), and whether it is a local model (it never runs low).</summary>
public sealed record CapsuleStep(string Mono, uint Hue, bool Local);

/// <summary>What a pill capsule shows (the page's <c>set(o)</c> of a pill): the steps, the current one, the steps already run (null: those before the current one), the step on stand-by, a running Handshake, and the used share of the current and of the next account.</summary>
public sealed record CapsuleView(IReadOnlyList<CapsuleStep> Steps, int Cur, IReadOnlyList<int>? Used, int? Stand, bool Handshake, double? Use, double? NextUse, string? Name);

public enum StepRole { Used, Cur, Next }

public readonly record struct StepMark(StepRole Role, bool Stand);

/// <summary>A dash on a path measured as 0...100 (the page's <c>pathLength="100"</c>).</summary>
public readonly record struct PathDash(double Start, double Length);

public static class CapsuleLayout
{
    public const double Height = 22;
    public const double OrbitSeconds = 28;
    public const double HotUse = 0.9;
    private const double UnknownUse = 0.3;
    private const double UnknownNextUse = 0.1;

    public static int Current(CapsuleView view) => Math.Max(0, Math.Min(view.Steps.Count - 1, view.Cur));

    public static IReadOnlyList<StepMark> Roles(CapsuleView view)
    {
        var current = Current(view);
        bool IsUsed(int i) => view.Used is { } used ? used.Contains(i) : i < current;
        return [.. Enumerable.Range(0, view.Steps.Count).Select(i =>
            new StepMark(i == current ? StepRole.Cur : IsUsed(i) ? StepRole.Used : StepRole.Next, view.Stand == i))];
    }

    /// <summary>The step the capsule looks ahead to: the one on stand-by, else the one after the current step.</summary>
    public static int? NextIndex(CapsuleView view)
    {
        var current = Current(view);
        if (view.Stand is { } stand && stand >= 0 && stand < view.Steps.Count) return stand;
        return current + 1 < view.Steps.Count ? current + 1 : null;
    }

    /// <summary>What is left (0...1) of the current account: a local model never runs low; the thread's own <paramref name="use"/> else the account's; 30 percent used when neither is known.</summary>
    public static double Left(CapsuleView view, double? accountUse)
    {
        if (view.Steps[Current(view)].Local) return 1;
        return 1 - Math.Min(1, view.Use ?? accountUse ?? UnknownUse);
    }

    /// <summary>What is left of the next account while a Handshake runs; else what is left of the current one.</summary>
    public static double NextLeft(CapsuleView view)
    {
        var left = Left(view, null);
        if (NextIndex(view) is not { } next || !view.Handshake) return left;
        return view.Steps[next].Local ? 1 : 1 - Math.Min(1, view.NextUse ?? UnknownNextUse);
    }

    private static double Smooth(double a, double b, double x)
    {
        var t = Math.Max(0, Math.Min(1, (x - a) / (b - a)));
        return t * t * (3 - 2 * t);
    }

    /// <summary>The glow's strength: 1, swelling to 1.45 as what is left falls from half to 4 percent.</summary>
    public static double GlowBoost(double left) => 1 + 0.45 * Smooth(0.5, 0.04, left);

    /// <summary>The liquid fill's level: the used share, never below 4 percent.</summary>
    public static double Fill(CapsuleView view) => Math.Max(0.04, Math.Min(1, view.Use ?? UnknownUse));

    /// <summary>The name the capsule gives on hover or focus. Name below shows the name alone; the others add the step.</summary>
    public static string? NameText(string style, CapsuleView view) =>
        view.Name is null ? null : style == "2" ? view.Name : view.Name + " · step " + (Current(view) + 1) + " of " + view.Steps.Count;

    public static IReadOnlyList<PathDash> RimSegments(int count) => Dashes(count, 4, 2);

    public static IReadOnlyList<PathDash> DialSegments(int count) => Dashes(count, 5, 2.5);

    private static IReadOnlyList<PathDash> Dashes(int count, double gap, double offset)
    {
        var each = 100.0 / count;
        return [.. Enumerable.Range(0, count).Select(i => new PathDash(i * each + offset, each - gap))];
    }

    /// <summary>The x of each step's tick on the liquid fill's rim (twelve in from either end; a lone step in the middle).</summary>
    public static IReadOnlyList<double> Ticks(int count, double width) =>
        [.. Enumerable.Range(0, count).Select(i => count > 1 ? 12 + i * (width - 24) / (count - 1) : width / 2)];

    /// <summary>Where each orbit dot starts on the lap (0...1), evenly spaced; the lap takes <see cref="OrbitSeconds"/>.</summary>
    public static IReadOnlyList<double> Orbit(int count) => [.. Enumerable.Range(0, count).Select(i => (double)i / count)];

    public static bool IsHot(double use) => use >= HotUse;

    public static double Arc(double use) => Math.Max(0, Math.Min(1, use));

    /// <summary>The corner points of the capsule outline inset by <paramref name="inset"/>: from the top middle (the plain arc, the orbit) or from the left middle (the segmented rim reads left to right).
    /// Top-middle: top middle, top right end, bottom right end, bottom left end, top left end, top middle; the arcs join the 2nd to 3rd and the 4th to 5th points.
    /// Left-middle: left middle, top left end, top right end, bottom right end, bottom left end, left middle; the arcs join 1-2, 3-4 and 5-6.</summary>
    public static IReadOnlyList<(double X, double Y)> CapsuleOutline(double width, double height, double inset, bool fromLeft)
    {
        var r = height / 2;
        return fromLeft
            ? [(inset, r), (r, inset), (width - r, inset), (width - r, height - inset), (r, height - inset), (inset, r)]
            : [(width / 2, inset), (width - r, inset), (width - r, height - inset), (r, height - inset), (r, inset), (width / 2, inset)];
    }

    /// <summary>The length of the capsule outline inset by <paramref name="inset"/>: two straights and a circle of radius (height / 2 - inset).</summary>
    public static double Perimeter(double width, double height, double inset) => 2 * (width - height) + 2 * Math.PI * (height / 2 - inset);

    /// <summary>The point <paramref name="fraction"/> of the way round the capsule outline (any fraction: the lap wraps). <paramref name="fromLeft"/>: from the left middle (the rim); else from the top middle (the arc, the orbit). Clockwise.</summary>
    public static (double X, double Y) Along(double width, double height, double inset, bool fromLeft, double fraction)
    {
        var r = height / 2;
        var q = r - inset;
        var straight = width - height;
        var half = Math.PI * q;
        var lap = Perimeter(width, height, inset);
        var s = fraction - Math.Floor(fraction);
        var d = s * lap;
        if (fromLeft)
        {
            // left middle up to the top of the left end, along the top, the right end, back along the bottom, the left end's lower quarter
            if (d <= half / 2) return Round(r, r, q, Math.PI + d / q);
            d -= half / 2;
            if (d <= straight) return (r + d, inset);
            d -= straight;
            if (d <= half) return Round(width - r, r, q, -Math.PI / 2 + d / q);
            d -= half;
            if (d <= straight) return (width - r - d, height - inset);
            d -= straight;
            return Round(r, r, q, Math.PI / 2 + d / q);
        }
        // top middle to the right, the right end, back along the bottom, the left end, along the top to the middle
        var first = straight / 2;
        if (d <= first) return (width / 2 + d, inset);
        d -= first;
        if (d <= half) return Round(width - r, r, q, -Math.PI / 2 + d / q);
        d -= half;
        if (d <= straight) return (width - r - d, height - inset);
        d -= straight;
        if (d <= half) return Round(r, r, q, Math.PI / 2 + d / q);
        d -= half;
        return (r + d, inset);
    }

    /// <summary>The point <paramref name="fraction"/> round a circle from its top, clockwise (the dial).</summary>
    public static (double X, double Y) CircleAlong(double cx, double cy, double radius, double fraction)
    {
        var angle = -Math.PI / 2 + 2 * Math.PI * (fraction - Math.Floor(fraction));
        return Round(cx, cy, radius, angle);
    }

    private static (double X, double Y) Round(double cx, double cy, double radius, double angle) => (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
}
