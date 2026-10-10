using Avalonia;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-handoverfix-2: one launch of the boot sequence over the stripes, as GlassWindowNativeTests.Launch measures it, judged against
// Boot B's locked design (OD-0113; porting/first-release/design-src/loading-aurora/loading-aurora.html :98, :622, :663). The stage dims
// the desktop with an unblurred scrim (AuroraBootPainter: rgba(2,6,16,.5) dark, rgba(8,14,28,.46) light, times the frame's
// ScrimOpacity) drawn above the main window, from the stage's start until after the hand-over; the main window appears inside one
// growing, fading frame (the content clip and the whole window's alpha). What stays from ED-236 (probe P13) is that the main window
// is never seen as its tint over stripes that are still sharp.
//   Timeline: the screen samples. BareMean/BareContrast: the stripes alone, read before the stage opened. ShownAt: the hand-over.
//   Frames: at each stage frame (sampler clock), the scrim's strength and where the sampled block lies against the main window's
//   rounded content clip (+1 wholly inside, -1 wholly outside, 0 the clip's edge crosses it). Dark: the stage's theme.
internal sealed record BootHandOverRun(GlassSample[] Timeline, double BareMean, double BareContrast, double ShownAt,
    (double Milliseconds, double Scrim, int Place)[] Frames, bool Dark)
{
    private const double Blurred = 3;       // GlassWindowNativeTests.Blurred: below this the stripes are gone
    private const double ScreenLag = 50;    // a sample may show any frame drawn in the 50 ms before it (or the one before those)

    // ScreenGrab.Luma of the stage's scrim colour, and the scrim's alpha at a frame's strength.
    private double ScrimLuma => Dark ? Luma(2, 6, 16) : Luma(8, 14, 28);
    private double ScrimAlpha(double strength) => (Dark ? .5 : .46) * strength;
    private static double Luma(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

    /// <summary>The stage frames the screen can be showing at <paramref name="at"/> (before the stage's first frame: no scrim, no window).</summary>
    private (double Milliseconds, double Scrim, int Place)[] Showing(double at)
    {
        var recent = Frames.Where(f => f.Milliseconds <= at && f.Milliseconds >= at - ScreenLag).ToList();
        var before = Frames.Where(f => f.Milliseconds < at - ScreenLag).ToArray();
        if (before.Length > 0) recent.Add(before[^1]);
        if (Frames.Length == 0 || at < Frames[0].Milliseconds + ScreenLag) recent.Add((at, 0, -1));
        return recent.ToArray();
    }

    /// <summary>
    /// The samples that are not the stripes under the stage's scrim alone (mean within 1.5, contrast no more than 3 below, for
    /// some frame the screen can be showing): before the hand-over, nothing of the main window may show.
    /// </summary>
    public GlassSample[] NotTheStageScrim(IEnumerable<GlassSample> samples) =>
        samples.Where(s => !Showing(s.Milliseconds).Any(f =>
        {
            var alpha = ScrimAlpha(f.Scrim);
            return Math.Abs(s.Mean - (BareMean * (1 - alpha) + alpha * ScrimLuma)) < 1.5 && s.Contrast > BareContrast * (1 - alpha) - 3;
        })).ToArray();

    /// <summary>
    /// The frames that show the main window's tint over stripes that are still sharp (the P13 gap) with <paramref name="tint"/> the
    /// alpha of the window's black scrim. Under the stage's scrim (undone with the frame's known alpha), the sample's mean says how
    /// much of the tinted window is seen (w); stripes seen through the window's blur keep no contrast, so the contrast the uncovered
    /// stripes cannot explain, BareContrast * (1 - w), brought back to the screen, must stay under Blurred. For the most lenient
    /// frame the screen can be showing; a sample whose frames put the clip's edge across the block is the stage's own drawing
    /// (its growing frame's edge) and is not judged.
    /// </summary>
    public GlassSample[] TintedOverSharp(IEnumerable<GlassSample> samples, double tint) =>
        samples.Where(s =>
        {
            var frames = Showing(s.Milliseconds);
            if (frames.Length == 0 || frames.Any(f => f.Place == 0) || frames.Select(f => f.Place).Distinct().Count() > 1) return false;
            return frames.Min(f =>
            {
                var alpha = ScrimAlpha(f.Scrim);
                var mean = (s.Mean - alpha * ScrimLuma) / (1 - alpha);
                var seen = Math.Clamp((1 - mean / BareMean) / tint, 0, 1);
                return (s.Contrast / (1 - alpha) - BareContrast * (1 - seen)) * (1 - alpha);
            }) >= Blurred;
        }).ToArray();

    /// <summary>+1 when <paramref name="block"/> lies wholly inside the rounded <paramref name="clip"/>, -1 wholly outside, 0 across its edge.</summary>
    public static int Place(Rect block, Rect clip, double radius)
    {
        if (!clip.Intersects(block)) return -1;
        Point[] corners = [block.TopLeft, block.TopRight, block.BottomLeft, block.BottomRight];
        var r = Math.Min(radius, Math.Min(clip.Width, clip.Height) / 2);
        return corners.All(p =>
        {
            if (p.X < clip.Left || p.X > clip.Right || p.Y < clip.Top || p.Y > clip.Bottom) return false;
            var x = p.X - Math.Clamp(p.X, clip.Left + r, clip.Right - r);
            var y = p.Y - Math.Clamp(p.Y, clip.Top + r, clip.Bottom - r);
            return x * x + y * y <= r * r;
        }) ? 1 : 0;
    }
}
