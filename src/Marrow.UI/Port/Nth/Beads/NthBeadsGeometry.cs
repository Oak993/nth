// PORT-ONLY: Calculi, nth's working beads (ED-NEW-calculi-wu1-1; design E:/Droppy/work/design/working-motion/DESIGN.md
// sections 1, 2 and 9). The n letterform as an abacus wire, in n units: the arch's centre is (0, 0), its radius 1, y down.
// Ported 1:1 from reference/engine.js (PT, HUBS, HUB_C, wire, backRun, ENV). The numbers are NthGlyph.Regular normalised
// (stems x 70 and 158, arch radius 44, feet y 196, bead 186,74), rounded to two decimals as the design gives them.
namespace Marrow.UI.Port.Nth.Beads;

/// <summary>A point in n units.</summary>
public readonly record struct NthBeadsPoint(double X, double Y);

/// <summary>The universal transition points (section 2). <see cref="Locus"/> is one merged bead at the formation's own
/// centroid; <see cref="None"/> is a final exit's (it ends nowhere).</summary>
public enum NthBeadsHub : byte { None, Punctum, Arcus, Pedes, Signum, Clavis, Tripus, Locus }

/// <summary>The wire, the hubs, Rota's back run and the motion envelope.</summary>
public static class NthBeadsGeometry
{
    /// <summary>The feet's height (RT): the stems run from y 0 (the shoulders) down to it.</summary>
    public const double Foot = 1.64;
    /// <summary>The wire's length: up the left stem, over the arch, down the right stem.</summary>
    public const double WireLength = 2 * Foot + Math.PI;
    /// <summary>Wire positions of the left shoulder, the crown and the right shoulder.</summary>
    public const double SLeft = Foot, SCrown = Foot + Math.PI / 2, SRight = Foot + Math.PI;
    /// <summary>A bead's radius at scale 1, and its width (the unit of every travel gate).</summary>
    public const double BeadRadius = 0.34, BeadWidth = 2 * BeadRadius;
    /// <summary>The n box (what "18 DIP" names): 3.5 units, centred on (0.32, 0.25).</summary>
    public const double Box = 3.5, BoxCentreX = 0.32, BoxCentreY = 0.25;
    /// <summary>The motion envelope (section 9): the control's own bounds. Every bead edge of every move stays inside it.</summary>
    public const double EnvelopeX0 = -1.52, EnvelopeX1 = 2.16, EnvelopeY0 = -1.86, EnvelopeY1 = 2.44;

    /// <summary>The named points: feet (left, middle, right), shoulders, crown, the logo's dot D and the cursor K.</summary>
    public static readonly NthBeadsPoint FootLeft = new(-1, Foot), FootMiddle = new(0, Foot), FootRight = new(1, Foot),
        ShoulderLeft = new(-1, 0), Crown = new(0, -1), ShoulderRight = new(1, 0), Dot = new(1.64, -1.14), Cursor = new(0, .42);

    private static readonly NthBeadsPoint[][] Hubs =
    [
        [], // None
        [Dot, Dot, Dot],                           // Punctum
        [ShoulderLeft, Crown, ShoulderRight],      // Arcus
        [FootLeft, FootMiddle, FootRight],         // Pedes
        [FootLeft, Dot, FootRight],                // Signum
        [FootLeft, Cursor, FootRight],             // Clavis
        [FootLeft, Crown, FootRight],              // Tripus
    ];

    private static readonly NthBeadsPoint[] Centres = BuildCentres();

    private static NthBeadsPoint[] BuildCentres()
    {
        var c = new NthBeadsPoint[7];
        for (var h = 1; h < 7; h++)
        {
            var p = Hubs[h];
            c[h] = new((p[0].X + p[1].X + p[2].X) / 3, (p[0].Y + p[1].Y + p[2].Y) / 3);
        }
        return c;
    }

    /// <summary>True for the six hubs that have a pose (not Locus, not None).</summary>
    public static bool HasPose(NthBeadsHub hub) => hub is >= NthBeadsHub.Punctum and <= NthBeadsHub.Tripus;

    /// <summary>Slot <paramref name="slot"/> (0..2, left to right) of a hub's pose; every hub bead has scale 1.</summary>
    public static NthBeadsPoint HubPoint(NthBeadsHub hub, int slot) => Hubs[(int)hub][slot];

    /// <summary>A hub's centroid (HUB_C).</summary>
    public static NthBeadsPoint HubCentre(NthBeadsHub hub) => Centres[(int)hub];

    /// <summary>The point at arc length <paramref name="s"/> along the wire (reference <c>wire</c>).</summary>
    public static NthBeadsPoint Wire(double s)
    {
        if (s < Foot) return new(-1, Foot - s);
        if (s <= Foot + Math.PI)
        {
            var a = Math.PI - (s - Foot);
            return new(Math.Cos(a), -Math.Sin(a));
        }
        return new(1, s - (Foot + Math.PI));
    }

    /// <summary>Rota's back run: down the right stem, along the baseline on 0.4-radius corners, up the left stem.</summary>
    public const double BackCorner = .4;
    private static readonly double[] Back = [Foot - BackCorner, Math.PI * BackCorner / 2, 2 - 2 * BackCorner, Math.PI * BackCorner / 2, Foot - BackCorner];
    /// <summary>The back run's segment lengths (the first two are what Rota's scale ramp reads).</summary>
    public static double BackSegment(int i) => Back[i];
    /// <summary>The back run's length (4.94 units).</summary>
    public static readonly double BackLength = Back[0] + Back[1] + Back[2] + Back[3] + Back[4];

    /// <summary>The point at distance <paramref name="d"/> along the back run (reference <c>backRun</c>).</summary>
    public static NthBeadsPoint BackRun(double d)
    {
        var i = 0;
        for (; i < 4 && d > Back[i]; i++) d -= Back[i];
        const double rc = BackCorner;
        switch (i)
        {
            case 0: return new(1, d);
            case 1: { var a = d / rc; return new(1 - rc + rc * Math.Cos(a), Foot - rc + rc * Math.Sin(a)); }
            case 2: return new(1 - rc - d, Foot);
            case 3: { var a = Math.PI / 2 + d / rc; return new(-1 + rc + rc * Math.Cos(a), Foot - rc + rc * Math.Sin(a)); }
            default: return new(-1, Foot - rc - d);
        }
    }
}
