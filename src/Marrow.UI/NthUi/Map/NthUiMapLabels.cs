// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "labels for countries and the 300 largest cities"; 10 text 4.5:1; ED-NEW-web-w16c-1).
// No Swift counterpart. Which labels a map view shows: the stops' own always (each tries right of its dot, left, above and below
// and keeps the first free place), then the countries the view is big enough to name and the cities whose rank the zoom has
// reached, each only where it overlaps nothing placed before it. The rules are pure (no drawing, no text engine: sizes come in), so
// a test can hold them: no two shown labels overlap, a stop is never dropped, a bigger city beats a smaller one, and what the
// zoom has not reached stays hidden.
using Avalonia;

namespace Marrow.UI.NthUi.Map;

public enum NthUiMapLabelKind
{
    Stop,
    Country,
    City,
}

/// <summary>A label that may be placed: where its anchor is on screen and how big its text is.</summary>
/// <param name="Priority">Higher places first.</param>
/// <param name="DotRadius">The radius of the dot the label belongs to (0 for a country, which is centred on its anchor).</param>
public sealed record NthUiMapLabelCandidate(string Key, NthUiMapLabelKind Kind, string Text, Point Anchor, Size Size, double Priority, double DotRadius);

/// <summary>A label with its place.</summary>
public sealed record NthUiMapPlacedLabel(string Key, NthUiMapLabelKind Kind, string Text, Rect Box, Point Anchor);

public static class NthUiMapLabelPlanner
{
    /// <summary>The space kept between a label and its dot, and between labels.</summary>
    public const double Gap = 4;

    /// <summary>The zoom from which the city of this rank (0 is the largest) is named.</summary>
    public static double CityZoom(int rank) => rank switch
    {
        < 12 => 1.5,
        < 30 => 2.5,
        < 60 => 3.5,
        < 110 => 4.5,
        < 180 => 5.5,
        _ => 6.5,
    };

    /// <summary>Whether a country is named at <paramref name="zoom"/>: inside the data's own label range, give or take a step (a map
    /// card is smaller than the screens that range was made for), and not so far in that its cities should speak instead.</summary>
    public static bool CountryZoom(double zoom, double minLabel, double maxLabel)
    {
        var low = double.IsNaN(minLabel) ? 2.5 : minLabel - 1;
        var high = double.IsNaN(maxLabel) ? 6.5 : maxLabel + .5;
        return zoom >= low && zoom <= Math.Min(high, 7.5);
    }

    /// <summary>
    /// Places the candidates, highest priority first, inside <paramref name="viewport"/>. Stops are always returned (when none of
    /// their four places is free they take the right-hand one, over whatever is there); every other label is dropped where it
    /// would overlap a label, a dot or the view's edge.
    /// </summary>
    public static IReadOnlyList<NthUiMapPlacedLabel> Place(IEnumerable<NthUiMapLabelCandidate> candidates, Rect viewport)
    {
        var ordered = candidates.OrderByDescending(c => c.Kind == NthUiMapLabelKind.Stop).ThenByDescending(c => c.Priority).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
        var taken = new List<Rect>();
        // Every dot is an obstacle from the start, so a label never covers another label's dot.
        foreach (var c in ordered)
        {
            if (c.DotRadius > 0) taken.Add(new Rect(c.Anchor.X - c.DotRadius - 1, c.Anchor.Y - c.DotRadius - 1, c.DotRadius * 2 + 2, c.DotRadius * 2 + 2));
        }

        var placed = new List<NthUiMapPlacedLabel>();
        foreach (var c in ordered)
        {
            var done = false;
            foreach (var box in Positions(c))
            {
                if (!viewport.Contains(box.TopLeft) || !viewport.Contains(box.BottomRight)) continue;
                if (taken.Any(t => Overlaps(t, box, c.Kind == NthUiMapLabelKind.Country ? Gap : 2))) continue;
                placed.Add(new NthUiMapPlacedLabel(c.Key, c.Kind, c.Text, box, c.Anchor));
                taken.Add(box);
                done = true;
                break;
            }

            // A stop is never dropped: no free place means the right-hand one, pulled into the view.
            if (done || c.Kind != NthUiMapLabelKind.Stop) continue;
            var first = Positions(c).First();
            var forced = new Rect(Math.Clamp(first.X, viewport.X, Math.Max(viewport.X, viewport.Right - first.Width)),
                Math.Clamp(first.Y, viewport.Y, Math.Max(viewport.Y, viewport.Bottom - first.Height)), first.Width, first.Height);
            placed.Add(new NthUiMapPlacedLabel(c.Key, c.Kind, c.Text, forced, c.Anchor));
            taken.Add(forced);
        }
        return placed;
    }

    private static bool Overlaps(Rect a, Rect b, double gap) =>
        a.X < b.Right + gap && b.X < a.Right + gap && a.Y < b.Bottom + gap && b.Y < a.Bottom + gap;

    private static IEnumerable<Rect> Positions(NthUiMapLabelCandidate c)
    {
        var (w, h) = (c.Size.Width, c.Size.Height);
        switch (c.Kind)
        {
            case NthUiMapLabelKind.Country:
                yield return new Rect(c.Anchor.X - w / 2, c.Anchor.Y - h / 2, w, h);
                break;
            case NthUiMapLabelKind.City:
                yield return new Rect(c.Anchor.X + c.DotRadius + Gap, c.Anchor.Y - h / 2, w, h);
                yield return new Rect(c.Anchor.X - c.DotRadius - Gap - w, c.Anchor.Y - h / 2, w, h);
                break;
            default:
                yield return new Rect(c.Anchor.X + c.DotRadius + Gap, c.Anchor.Y - h / 2, w, h);
                yield return new Rect(c.Anchor.X - c.DotRadius - Gap - w, c.Anchor.Y - h / 2, w, h);
                yield return new Rect(c.Anchor.X - w / 2, c.Anchor.Y - c.DotRadius - Gap - h, w, h);
                yield return new Rect(c.Anchor.X - w / 2, c.Anchor.Y + c.DotRadius + Gap, w, h);
                break;
        }
    }
}
