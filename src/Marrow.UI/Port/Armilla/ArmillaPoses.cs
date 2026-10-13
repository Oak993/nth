// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "Armilla, Dextra's presence"; the approved canvas's DextraMark, copied once
// into work/design/assistant/index.html lines 1103-1550; ED-NEW-dextra-as8-1). No Swift twin.
// Armilla's numbers, as the canvas holds them: the eight poses and the under-28 px Working pose, the four size tiers, the stroke
// ladder, the inks, the six Astur node shapes with their tints in both themes and their wobble periods, and how a task's or a
// head's state maps onto a pose (DX_STATE and DX_GLYPH). Pure data; ArmillaEngine moves between these poses.
using Marrow.Core.Port.Assistant;

namespace Marrow.UI.Port.Armilla;

/// <summary>Armilla's eight poses (2.5's table). Offline is the canvas's "error".</summary>
public enum ArmillaState { Idle, Listening, Thinking, Speaking, Working, NeedsYou, Paused, Offline }

/// <summary>The glyph three task states put in place of the presence point (2.5): a check, a hollow square, a filled square.</summary>
public enum ArmillaGlyph { None, Done, Stopping, Stopped }

/// <summary>The six Astur node shapes, in the canvas's order (each has its own tint).</summary>
public enum ArmillaShape { Squircle, Hexagon, Diamond, Triangle, Clover, Star }

/// <summary>
/// What a mark is for (the canvas's kinds): Dextra's live presence (it breathes and carries its Asturs as nodes), a row mark
/// (a task, card or decision: no nodes, it rests still and stops drawing once settled), or a still (a chat message: drawn once).
/// </summary>
public enum ArmillaKind { Live, Row, Still }

/// <summary>One Astur on the orbit: its shape, whether it works or thinks now, and an optional tint (null takes the shape's own).</summary>
public readonly record struct ArmillaNode(ArmillaShape Shape, bool Busy = false, ArmillaRgb? Tint = null);

/// <summary>A colour in 0..255 per channel, mixed in doubles as the canvas mixes its arrays.</summary>
public readonly record struct ArmillaRgb(double R, double G, double B)
{
    public static ArmillaRgb Hex(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new ArmillaRgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public static ArmillaRgb Mix(ArmillaRgb a, ArmillaRgb b, double k) => new(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
}

/// <summary>The sprung values of a pose (the canvas's SPRUNG keys), in one fixed order so a pose is one array.</summary>
internal enum PoseKey
{
    RingK, W, W2, L1, L2, Delta, Off2, InR, InLen, InA, InBase, TickA, LobeA, LobeW, ScanAmp, CoreA, Ink, LongA, Warm, Breathe,
    SatA, BusyK, SatOut, Wob, VIn, VOut, Frac, Count,
}

/// <summary>One pose: the sprung values, where attention points (rho) and the unsprung motion of the pose.</summary>
internal sealed class ArmillaPose
{
    public readonly double[] V = new double[(int)PoseKey.Count];

    /// <summary>Where attention points, in degrees from twelve o'clock, clockwise.</summary>
    public double Rho;

    /// <summary>The figure's turn (deg/s), the inner ring's and the nodes'.</summary>
    public double Spin, InSpin, SatSpin;

    /// <summary>The spring rate of this pose (the canvas's w, rad/s); damping is always 0.8.</summary>
    public double Rate;

    /// <summary>The needs-you step-out period (s); 0 when the pose has none.</summary>
    public double Reach;

    /// <summary>Paused and Offline park: nodes settle on their slots, the ticks stop drifting.</summary>
    public bool Park;

    public double this[PoseKey key] => V[(int)key];

    public ArmillaPose Copy()
    {
        var p = new ArmillaPose { Rho = Rho, Spin = Spin, InSpin = InSpin, SatSpin = SatSpin, Rate = Rate, Reach = Reach, Park = Park };
        Array.Copy(V, p.V, V.Length);
        return p;
    }
}

/// <summary>A size tier: the ring's radius alone (R0) and with nodes (R1), the node orbit, the cap (2.5's sizes).</summary>
internal readonly record struct ArmillaTier(double R0, double R1, double Track, bool RoundCap);

/// <summary>The canvas's tables.</summary>
public static class ArmillaPoses
{
    internal const double Deg = Math.PI / 180, Tau = Math.PI * 2;

    /// <summary>The spring damping of every move (2.5: "one spring (damping 0.8)").</summary>
    public const double Damping = 0.8;

    private static readonly ArmillaPose[] poses = Build();
    private static readonly ArmillaPose xsWorking = BuildXsWorking();
    private static readonly ArmillaPose[] rest = BuildRest();

    internal static readonly ArmillaTier[] Tiers =
    [
        new(.72, .55, .83, true),  // XS  < 28 px
        new(.74, .58, .83, true),  // S   28-47
        new(.66, .58, .80, false), // M   48-99
        new(.66, .57, .81, false), // L   100+
    ];

    private static readonly (double Size, double Stroke)[] strokes =
        [(16, 1.6), (20, 1.8), (24, 2.0), (32, 2.2), (48, 2.5), (64, 2.8), (100, 3.4), (160, 4.2), (240, 5.4)];

    internal static readonly ArmillaRgb InkDark = new(236, 242, 247), InkLight = new(22, 24, 29);
    internal static readonly ArmillaRgb WarmDark = new(242, 180, 94), WarmLight = new(181, 112, 12);
    internal static readonly ArmillaRgb MonoDark = new(255, 255, 255), MonoLight = new(0, 0, 0);

    /// <summary>The six node tints, dark theme then light theme, in shape order (2.5's table).</summary>
    public static readonly IReadOnlyList<string> TintsDark = ["#8FD8B8", "#F2A99E", "#A9C1FA", "#EBCB82", "#C9B0F5", "#86D3E3"];

    public static readonly IReadOnlyList<string> TintsLight = ["#2E8B66", "#C0574A", "#4A6FD0", "#9C7414", "#7E5BC4", "#2A8FA6"];

    private static readonly ArmillaRgb[] tintsDark = [.. TintsDark.Select(ArmillaRgb.Hex)];
    private static readonly ArmillaRgb[] tintsLight = [.. TintsLight.Select(ArmillaRgb.Hex)];

    /// <summary>The node wobble periods (s): each node has its own slow wobble, 17 to 37 s (2.5 "Moves").</summary>
    internal static readonly double[] WobblePeriods = [17, 19.5, 23, 29, 31, 37];

    /// <summary>The words of each pose (the canvas's LABELS).</summary>
    public static string Label(ArmillaState state) => state switch
    {
        ArmillaState.Idle => "Idle",
        ArmillaState.Listening => "Listening",
        ArmillaState.Thinking => "Thinking",
        ArmillaState.Speaking => "Speaking",
        ArmillaState.Working => "Working",
        ArmillaState.NeedsYou => "Needs you",
        ArmillaState.Paused => "Paused",
        ArmillaState.Offline => "Offline",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    internal static int TierOf(double size) => size < 28 ? 0 : size < 48 ? 1 : size < 100 ? 2 : 3;

    internal static double StrokeFor(double s)
    {
        if (s <= strokes[0].Size) return strokes[0].Stroke;
        for (var i = 1; i < strokes.Length; i++)
        {
            if (s <= strokes[i].Size)
            {
                var (a, b) = strokes[i - 1];
                var (c, d) = strokes[i];
                return b + (d - b) * (s - a) / (c - a);
            }
        }
        return strokes[^1].Stroke * s / 240;
    }

    internal static ArmillaRgb Tint(int index, bool dark) => (dark ? tintsDark : tintsLight)[((index % 6) + 6) % 6];

    /// <summary>The pose a mark of this size shows for a state; rows drop the breathe so a settled row stops drawing.</summary>
    internal static ArmillaPose Pose(ArmillaState state, double size, bool rest)
    {
        if (TierOf(size) == 0 && state == ArmillaState.Working) return xsWorking;
        return rest ? BuildRestFor(state) : poses[(int)state];
    }

    private static ArmillaPose BuildRestFor(ArmillaState state) => rest[(int)state];

    /// <summary>A task's state as a pose (DX_STATE): Planning and Reviewing think, Pausing works, Stopping and Stopped are paused.</summary>
    public static ArmillaState For(MissionState state) => state switch
    {
        MissionState.Planning or MissionState.Reviewing => ArmillaState.Thinking,
        MissionState.Working or MissionState.Pausing => ArmillaState.Working,
        MissionState.NeedsYou => ArmillaState.NeedsYou,
        MissionState.Paused or MissionState.Stopping or MissionState.Stopped => ArmillaState.Paused,
        MissionState.Blocked or MissionState.Failed => ArmillaState.Offline,
        MissionState.Done => ArmillaState.Idle,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>A head's state as a pose: Queued and Done rest, Pausing works, Stopping and Stopped are paused.</summary>
    public static ArmillaState For(HeadState state) => state switch
    {
        HeadState.Queued or HeadState.Done => ArmillaState.Idle,
        HeadState.Working or HeadState.Pausing => ArmillaState.Working,
        HeadState.Paused or HeadState.Stopping or HeadState.Stopped => ArmillaState.Paused,
        HeadState.Blocked => ArmillaState.Offline,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The centre glyph of a task's state (DX_GLYPH): Done a check, Stopping a hollow square, Stopped a filled one.</summary>
    public static ArmillaGlyph GlyphFor(MissionState state) => state switch
    {
        MissionState.Done => ArmillaGlyph.Done,
        MissionState.Stopping => ArmillaGlyph.Stopping,
        MissionState.Stopped => ArmillaGlyph.Stopped,
        _ => ArmillaGlyph.None,
    };

    public static ArmillaGlyph GlyphFor(HeadState state) => state switch
    {
        HeadState.Done => ArmillaGlyph.Done,
        HeadState.Stopping => ArmillaGlyph.Stopping,
        HeadState.Stopped => ArmillaGlyph.Stopped,
        _ => ArmillaGlyph.None,
    };

    private static ArmillaPose Base()
    {
        var p = new ArmillaPose { Rho = 45, Spin = 0, InSpin = 0, SatSpin = .4, Rate = 9, Reach = 0, Park = false };
        void S(PoseKey k, double v) => p.V[(int)k] = v;
        S(PoseKey.RingK, 1); S(PoseKey.W, 1); S(PoseKey.W2, 1.3); S(PoseKey.L1, 236); S(PoseKey.L2, 34); S(PoseKey.Delta, 180);
        S(PoseKey.Off2, 0); S(PoseKey.InR, .40); S(PoseKey.InLen, 0); S(PoseKey.InA, 0); S(PoseKey.InBase, .14); S(PoseKey.TickA, .10);
        S(PoseKey.LobeA, .30); S(PoseKey.LobeW, 30); S(PoseKey.ScanAmp, 0); S(PoseKey.CoreA, .14); S(PoseKey.Ink, 1); S(PoseKey.LongA, .78);
        S(PoseKey.Warm, 0); S(PoseKey.Breathe, 0); S(PoseKey.SatA, 1); S(PoseKey.BusyK, 1); S(PoseKey.SatOut, 0); S(PoseKey.Wob, 1);
        S(PoseKey.VIn, 0); S(PoseKey.VOut, 0); S(PoseKey.Frac, 0);
        return p;
    }

    private static ArmillaPose[] Build()
    {
        var all = new ArmillaPose[8];
        for (var i = 0; i < all.Length; i++) all[i] = Base();
        static void S(ArmillaPose p, PoseKey k, double v) => p.V[(int)k] = v;

        var idle = all[(int)ArmillaState.Idle];
        S(idle, PoseKey.Breathe, 1); idle.Rate = 7;

        var listening = all[(int)ArmillaState.Listening];
        listening.Rho = 180; S(listening, PoseKey.L1, 276); S(listening, PoseKey.L2, 44); S(listening, PoseKey.W2, 1.4); S(listening, PoseKey.InLen, 360);
        S(listening, PoseKey.InA, .26); S(listening, PoseKey.LobeA, .42); S(listening, PoseKey.LobeW, 34); S(listening, PoseKey.CoreA, .2);
        S(listening, PoseKey.LongA, .86); listening.SatSpin = .15; S(listening, PoseKey.VIn, 1); listening.Rate = 12;

        var thinking = all[(int)ArmillaState.Thinking];
        S(thinking, PoseKey.RingK, .9); thinking.Rho = 90; S(thinking, PoseKey.L1, 204); S(thinking, PoseKey.L2, 26); S(thinking, PoseKey.W2, 1.35);
        S(thinking, PoseKey.InR, .45); S(thinking, PoseKey.InLen, 120); S(thinking, PoseKey.InA, .7); S(thinking, PoseKey.InBase, .1);
        thinking.InSpin = -30; S(thinking, PoseKey.LobeA, .85); S(thinking, PoseKey.LobeW, 20); S(thinking, PoseKey.ScanAmp, 50);
        S(thinking, PoseKey.CoreA, .24); S(thinking, PoseKey.LongA, .66); thinking.SatSpin = .25; S(thinking, PoseKey.SatA, .85); thinking.Rate = 10;

        var speaking = all[(int)ArmillaState.Speaking];
        speaking.Rho = 0; S(speaking, PoseKey.L1, 248); S(speaking, PoseKey.L2, 38); S(speaking, PoseKey.W2, 1.4); S(speaking, PoseKey.InLen, 360);
        S(speaking, PoseKey.InA, .2); S(speaking, PoseKey.LobeA, .38); S(speaking, PoseKey.LobeW, 36); S(speaking, PoseKey.CoreA, .18);
        S(speaking, PoseKey.LongA, .86); speaking.SatSpin = .25; S(speaking, PoseKey.VOut, 1); speaking.Rate = 12;

        var working = all[(int)ArmillaState.Working];
        working.Rho = 270; S(working, PoseKey.L1, 222); S(working, PoseKey.L2, 64); S(working, PoseKey.W2, 1.3); S(working, PoseKey.InLen, 240);
        S(working, PoseKey.InA, .42); S(working, PoseKey.InBase, .1); working.InSpin = -8; working.Spin = 12; S(working, PoseKey.LobeA, .5);
        S(working, PoseKey.LobeW, 42); S(working, PoseKey.CoreA, .2); working.SatSpin = 1; working.Rate = 9;

        var needs = all[(int)ArmillaState.NeedsYou];
        S(needs, PoseKey.RingK, 1.03); needs.Rho = 150; S(needs, PoseKey.L1, 240); S(needs, PoseKey.L2, 40); S(needs, PoseKey.W2, 1.65);
        S(needs, PoseKey.Off2, .09); S(needs, PoseKey.InBase, .12); S(needs, PoseKey.TickA, .08); S(needs, PoseKey.LobeA, .62);
        S(needs, PoseKey.LobeW, 30); S(needs, PoseKey.CoreA, .16); S(needs, PoseKey.LongA, .68); S(needs, PoseKey.Warm, 1); S(needs, PoseKey.SatA, .8);
        S(needs, PoseKey.BusyK, .6); S(needs, PoseKey.SatOut, .03); S(needs, PoseKey.Wob, .3); needs.SatSpin = 0; needs.Rate = 13; needs.Reach = 30;

        var paused = all[(int)ArmillaState.Paused];
        S(paused, PoseKey.RingK, .96); S(paused, PoseKey.W, .9); S(paused, PoseKey.W2, .9); S(paused, PoseKey.L1, 136); S(paused, PoseKey.L2, 136);
        paused.Rho = 90; S(paused, PoseKey.InBase, .07); S(paused, PoseKey.TickA, .06); S(paused, PoseKey.LobeA, 0); S(paused, PoseKey.CoreA, 0);
        S(paused, PoseKey.Ink, .52); S(paused, PoseKey.LongA, 1); S(paused, PoseKey.SatA, .6); S(paused, PoseKey.BusyK, 0); S(paused, PoseKey.Wob, 0);
        paused.SatSpin = 0; paused.Rate = 6; paused.Park = true;

        var error = all[(int)ArmillaState.Offline];
        S(error, PoseKey.RingK, .98); S(error, PoseKey.W, .9); S(error, PoseKey.W2, 1); S(error, PoseKey.L1, 252); S(error, PoseKey.L2, 0);
        error.Rho = 45; S(error, PoseKey.InBase, .06); S(error, PoseKey.TickA, .05); S(error, PoseKey.LobeA, 0); S(error, PoseKey.CoreA, 0);
        S(error, PoseKey.Ink, .58); S(error, PoseKey.LongA, 1); S(error, PoseKey.SatA, .5); S(error, PoseKey.BusyK, 0); S(error, PoseKey.Wob, 0);
        error.SatSpin = 0; S(error, PoseKey.Frac, 1); error.Rate = 8; error.Park = true;
        return all;
    }

    // Under 28 px, Working is one still ring with a small opening at the left: no turning, no second arc, so it never reads as a
    // spinner. Busy shows only through the outer band and brightness.
    private static ArmillaPose BuildXsWorking()
    {
        var p = Build()[(int)ArmillaState.Working].Copy();
        p.Spin = 0; p.InSpin = 0; p.V[(int)PoseKey.L2] = 0; p.V[(int)PoseKey.L1] = 324; p.V[(int)PoseKey.LongA] = 1; p.Rate = 9;
        return p;
    }

    // Rows and cards rest still: their idle pose drops the breathe (the canvas's SlotMark REST).
    private static ArmillaPose[] BuildRest()
    {
        var all = Build();
        foreach (var p in all) p.V[(int)PoseKey.Breathe] = 0;
        return all;
    }
}
