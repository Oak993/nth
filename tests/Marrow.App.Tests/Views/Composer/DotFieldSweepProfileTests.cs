using Avalonia;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Views.Composer;

// PR-HYDRA-169's capture comparison, done on numbers: the reference video (E:\Droppy\work\reference-video, 2-chat-use.mov, C 109.133-110.950 Ada
// and C 123.730-125.560 Otto, catalogue item hydra.card-finish-grain-wipe, frames/gap-R3-04) measures the finish sweep of a 400 x 280 card;
// the port's sample of the same field (DotFieldCompositionFrame, the source's per-dot keyframes ComposerWorkingDots.swift:77-80, :158-175) is
// compared with those measurements. SOURCE FACT for the measurements is the catalogue; UNVERIFIED against a Mac (OD-0001).
public sealed class DotFieldSweepProfileTests
{
    // The catalogue's per-dot (= per-column) opacity, normalised to the peak, t measured from the 10 % point of the rise.
    private static readonly (double T, double Opacity)[] Measured =
    [
        (-0.025, 0.00), (0.000, 0.10), (0.025, 0.22), (0.050, 0.33), (0.075, 0.45), (0.100, 0.55), (0.125, 0.65), (0.150, 0.74),
        (0.175, 0.82), (0.200, 0.88), (0.225, 0.94), (0.250, 0.98), (0.275, 0.99), (0.300, 1.00), (0.350, 1.00), (0.375, 0.99),
        (0.400, 0.97), (0.425, 0.95), (0.450, 0.90), (0.475, 0.85), (0.500, 0.81), (0.525, 0.75), (0.550, 0.69), (0.575, 0.64),
        (0.600, 0.57), (0.625, 0.50), (0.650, 0.43), (0.675, 0.36), (0.700, 0.28), (0.725, 0.20), (0.750, 0.11), (0.775, 0.02), (0.800, 0.00),
    ];

    private const double Peak = 0.7; // the keyframe peak (ComposerWorkingDots.swift:174 values 0, 0.7, 0)

    private static DotFieldCompositionFrame Frame(double seconds, double width = 400, double height = 280)
    {
        var start = new ContinuousInstant(0);
        return DotFieldCompositionFrame.Create(new Size(width, height), Colors.Red, null, start, animated: true, sweeps: true,
            TimeProvider.System, start, start + TimeSpan.FromSeconds(seconds));
    }

    private static double Normalised(double seconds, int column) => Frame(seconds).OpacityAt(0, column) / Peak;

    // The first time the column reaches 10 % of the peak, found on a fine grid as the video's "10 % point".
    private static double TenPercentPoint(int column)
    {
        for (var tick = 0; tick <= 20_000; tick++)
        {
            var seconds = tick / 10_000.0 + column * 0.022;
            if (Normalised(seconds, column) >= 0.10) return seconds;
        }
        throw new InvalidOperationException("the column never reaches 10 %");
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-169")] // HydraPanel.swift:72-91; ComposerWorkingDots.swift:77-80,158-175; catalogue hydra.card-finish-grain-wipe (gap-R3-04)
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169: the wave's measured timing and shape; the 22-point continuous clip is ED-181's deferred curve
    public void Sweep_profile_matches_the_measured_finish_wipe()
    {
        // Lattice (catalogue DOTS): pitch 8, one dot per cell, first dot 4 px in from the card's left and top edges, 50 columns x 35 rows on 400 x 280
        // (the port's one extra column sits at x = 404, beyond the card's edge, where the clip hides it).
        var frame = Frame(0);
        Assert.Equal(51, frame.Columns);
        Assert.Equal(35, frame.Rows);
        Assert.Equal(new Point(4, 4), frame.PositionAt(34, 0));
        Assert.Equal(new Point(4, 276), frame.PositionAt(0, 0));
        Assert.Equal(new Point(8 * 49 + 4, 276), frame.PositionAt(0, 49));

        // Per-column opacity table, within the spread the catalogue itself reports between its two events (0.04) plus the 8-bit resolution of a 2 px dot.
        var t10 = TenPercentPoint(0);
        foreach (var (offset, measured) in Measured)
            Assert.InRange(Normalised(t10 + offset, 0) - measured, -0.06, 0.06);

        // Every column runs the same profile, one column delay (8 px / 365 px/s = 21.9 ms) after its neighbour: "no other stagger".
        foreach (var column in new[] { 1, 12, 37, 49 })
        foreach (var (offset, measured) in Measured)
            Assert.InRange(Normalised(t10 + offset + column * 0.022, column) - measured, -0.06, 0.06);

        // Front speed: the 10 % point travels 8 px per column delay, 365 px/s (+-10) in the video.
        var speed = 8 * 49 / (TenPercentPoint(49) - TenPercentPoint(0));
        Assert.InRange(speed, 355, 375);

        // Band width between 10 % levels ~0.71-0.75 s; every row is in phase (the wavefront is exactly vertical).
        var width = Enumerable.Range(0, 8001).Select(tick => tick / 10_000.0).Where(seconds => Normalised(seconds, 0) >= 0.10).ToArray();
        Assert.InRange(width[^1] - width[0], 0.71, 0.76);
        for (var row = 1; row < frame.Rows; row++)
            Assert.Equal(Frame(0.32).OpacityAt(0, 5), Frame(0.32).OpacityAt(row, 5), 12);

        // Swap to the right edge clear: the last column's trailing 10 % point, 1.83 s after the swap in the video (card clean from 1.87 s).
        var lastColumnClear = Enumerable.Range(0, 30_000).Select(tick => tick / 10_000.0).Last(seconds => Normalised(seconds, 49) >= 0.10);
        Assert.InRange(lastColumnClear, 1.78, 1.90);
    }
}
