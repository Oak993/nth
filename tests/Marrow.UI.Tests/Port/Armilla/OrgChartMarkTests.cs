using System.Globalization;
using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-17 (DESIGN.md rev 5, 2.5 "Asturs on Armilla", 9.4 "its mark (Armilla at 64 px with a node per Astur for Dextra)"; ED-NEW-dextra-as17-2): the org chart's top node is
// Armilla at 64 px, the size where nodes begin to be drawn. It carries one node per Astur (0 to 6) spread evenly round the hairline orbit, a busy Astur's node brightens, grows
// and points a short stem back to the centre, and nothing runs outside the slot.
public sealed class OrgChartMarkTests
{
    private const double Size = 64, Half = 32;

    private static ArmillaNode[] Asturs(params bool[] busy) => [.. busy.Select((b, i) => new ArmillaNode((ArmillaShape)(i % 6), b))];

    private static string Settled(ArmillaState state, ArmillaNode[] nodes, double seconds = 4, bool dark = true, double size = Size)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = size, State = state, Nodes = nodes, Dark = dark });
        var clock = 0.0;
        ArmillaDrive.Hold(mark, ref clock, seconds);
        var canvas = new RecordingArmillaCanvas();
        mark.Draw(canvas);
        return canvas.Log;
    }

    // "shape <name> x y r colour alpha" for each node, in the order the Asturs are drawn
    private static List<(double X, double Y, double R, string Colour, double Alpha)> Nodes(string log) => [.. log.Split('\n')
        .Where(l => l.StartsWith("shape ", StringComparison.Ordinal)).Select(l => l.Split(' '))
        .Select(p => (double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture), double.Parse(p[4], CultureInfo.InvariantCulture), p[5], double.Parse(p[6], CultureInfo.InvariantCulture)))];

    // "line x0 y0 x1 y1 width colour alpha cap" for each 1 px line well inside the tick bezel: the stems
    private static List<double[]> Stems(string log) => [.. log.Split('\n')
        .Where(l => l.StartsWith("line ", StringComparison.Ordinal))
        .Select(l => l.Split(' ').Skip(1).Take(5).Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray())
        .Where(l => l[4] == 1 && Math.Sqrt(l[0] * l[0] + l[1] * l[1]) < .85 * Half && Math.Sqrt(l[2] * l[2] + l[3] * l[3]) < .85 * Half)];

    private static double Angle(double x, double y) => (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;

    private static double Distance(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Nodes_begin_at_48_px_so_the_64_px_node_of_the_org_chart_carries_them_and_the_40_px_header_does_not()
    {
        Assert.Equal(2, ArmillaPoses.TierOf(64));
        Assert.Equal(2, ArmillaPoses.TierOf(48));
        Assert.Equal(1, ArmillaPoses.TierOf(40));
        Assert.Equal(2, Nodes(Settled(ArmillaState.Idle, Asturs(false, false))).Count);
        Assert.Empty(Nodes(Settled(ArmillaState.Idle, Asturs(false, false, false), 3, size: 40)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void At_64_px_there_is_one_node_for_each_Astur_spread_evenly_on_one_orbit(int asturs)
    {
        var centres = Nodes(Settled(ArmillaState.Paused, Asturs(new bool[asturs])));

        Assert.Equal(asturs, centres.Count);
        if (asturs == 0) return;
        Assert.All(centres, c => Assert.InRange(Math.Sqrt(c.X * c.X + c.Y * c.Y), .79 * Half, .81 * Half));      // all on the orbit at 80 % of the half-box
        if (asturs == 1) return;
        var angles = centres.Select(c => Angle(c.X, c.Y)).Order().ToList();
        for (var i = 0; i < angles.Count; i++)
        {
            var next = angles[(i + 1) % angles.Count] + (i + 1 == angles.Count ? 360 : 0);
            Assert.InRange(next - angles[i], 360.0 / asturs - .6, 360.0 / asturs + .6);
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void While_Dextra_idles_the_nodes_wobble_a_little_but_stay_evenly_spread()
    {
        var angles = Nodes(Settled(ArmillaState.Idle, Asturs(false, false, false), 7)).Select(c => Angle(c.X, c.Y)).Order().ToList();

        Assert.Equal(3, angles.Count);
        Assert.InRange(angles[1] - angles[0], 120 - 6, 120 + 6);
        Assert.InRange(angles[2] - angles[1], 120 - 6, 120 + 6);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_busy_Astur_brightens_grows_by_a_quarter_and_points_a_short_stem_back_to_the_centre()
    {
        var log = Settled(ArmillaState.Working, Asturs(true, false, true));
        var nodes = Nodes(log);                                                                // in the order of the Asturs: busy, idle, busy

        Assert.InRange(nodes[0].R / nodes[1].R, 1.2, 1.3);
        Assert.InRange(nodes[2].R / nodes[1].R, 1.2, 1.3);
        Assert.True(nodes[0].Alpha > nodes[1].Alpha + .2 && nodes[2].Alpha > nodes[1].Alpha + .2, "a busy node is brighter");

        var stems = Stems(log);
        Assert.Equal(2, stems.Count);
        foreach (var node in new[] { nodes[0], nodes[2] })
        {
            var nodeAngle = Angle(node.X, node.Y);
            var stem = stems.Single(s => Distance(Angle(s[0], s[1]), nodeAngle) < 1.5 && Distance(Angle(s[2], s[3]), nodeAngle) < 1.5);
            var from = Math.Sqrt(stem[0] * stem[0] + stem[1] * stem[1]);
            var to = Math.Sqrt(stem[2] * stem[2] + stem[3] * stem[3]);
            var nodeRadius = Math.Sqrt(node.X * node.X + node.Y * node.Y);
            Assert.True(Math.Max(from, to) < nodeRadius - node.R, "the stem stops short of its node");
            Assert.True(Math.Min(from, to) > .3 * Half, "and is a short one");
        }
    }

    [Theory]
    [InlineData(ArmillaState.Paused)]
    [InlineData(ArmillaState.Offline)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void When_Dextra_is_paused_or_offline_no_stem_is_drawn_even_for_a_busy_Astur(ArmillaState state) =>
        Assert.Empty(Stems(Settled(state, Asturs(true, true))));

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_node_tints_are_the_six_of_the_table_so_two_Asturs_never_look_alike()
    {
        var dark = Nodes(Settled(ArmillaState.Paused, Asturs(false, false, false, false, false, false), dark: true)).Select(n => n.Colour).ToList();
        var light = Nodes(Settled(ArmillaState.Paused, Asturs(false, false, false, false, false, false), dark: false)).Select(n => n.Colour).ToList();

        Assert.Equal(["143,216,184", "242,169,158", "169,193,250", "235,203,130", "201,176,245", "134,211,227"], dark);
        Assert.Equal(["46,139,102", "192,87,74", "74,111,208", "156,116,20", "126,91,196", "42,143,166"], light);
        Assert.Equal(6, dark.Distinct().Count());
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void At_64_px_with_none_three_or_six_Asturs_every_pose_and_move_stays_inside_the_slot()
    {
        var worst = 0.0;
        foreach (var n in new[] { 0, 3, 6 })
        foreach (var from in Enum.GetValues<ArmillaState>())
        foreach (var to in Enum.GetValues<ArmillaState>())
        {
            var look = new ArmillaLook { Size = Size, State = from, Nodes = ArmillaDrive.Asturs(n) };
            var mark = new ArmillaEngine(look);
            var clock = 0.0;
            ArmillaDrive.Hold(mark, ref clock, .4);
            mark.Set(look with { State = to });
            var level = to is ArmillaState.Listening or ArmillaState.Speaking ? 1.0 : (double?)null;
            worst = Math.Max(worst, ArmillaDrive.Hold(mark, ref clock, 1.2, level));
        }
        Assert.InRange(worst, .5, .999);
    }
}
