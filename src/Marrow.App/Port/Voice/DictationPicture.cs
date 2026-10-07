// PORT-ONLY: OD-0091, voice-SPEC V-7.5/V-8.3/V-8.9. A word's shaped lines, including every wrapped piece and the context that shaped it.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

internal sealed class DictationPicture : IDisposable
{
    internal sealed record Fragment(int Character, int Length, Rect Bounds, Point Origin, GlyphRun Glyphs, DictationFlip Place);
    private readonly SolidColorBrush brush = new(Colors.Black);
    internal IReadOnlyList<Fragment> Fragments { get; private set; }
    internal Point Anchor { get; }

    internal DictationPicture(TextLayout source, int start, int length,
        ContinuousInstant now, bool still, IReadOnlyList<Fragment>? previous = null, DictationFlip? first = null)
    {
        var rects = length > 0 ? source.HitTestTextRange(start, length).ToArray() : [];
        if (rects.Length > 0)
        {
            var top = rects.Min(rect => rect.Top);
            Anchor = rects.Where(rect => Math.Abs(rect.Top - top) < .01).Aggregate((a, b) => a.Union(b)).TopLeft;
            first?.Place(Anchor, now, still);
        }
        var fragments = new List<Fragment>();
        var lineTop = 0.0;
        foreach (var line in source.TextLines)
        {
            if (length > 0 && line.FirstTextSourceIndex < start + length && line.FirstTextSourceIndex + line.Length > start)
            {
                // Native bounds supply each FULL run's global character origin and physical x. Glyph buffers are already in visual order, including RTL.
                var seen = new HashSet<ShapedTextRun>();
                foreach (var bound in line.GetTextBounds(line.FirstTextSourceIndex, line.Length).SelectMany(bounds => bounds.TextRunBounds))
                {
                    if (bound.TextRun is not ShapedTextRun run || !seen.Add(run)) continue;
                    var native = run.GlyphRun;
                    var infos = native.GlyphInfos;
                    var logicalClusters = infos.Select(info => info.GlyphCluster).Distinct().Order().ToArray();
                    var advance = 0.0;
                    for (var at = 0; at < infos.Count;)
                    {
                        var cluster = infos[at].GlyphCluster;
                        var end = at + 1;
                        while (end < infos.Count && infos[end].GlyphCluster == cluster) end++;
                        var index = Array.BinarySearch(logicalClusters, cluster);
                        var count = (index + 1 < logicalClusters.Length ? logicalClusters[index + 1] : native.Metrics.FirstCluster + run.Length) - cluster;
                        var character = bound.TextSourceCharacterIndex + cluster - native.Metrics.FirstCluster;
                        var width = Enumerable.Range(at, end - at).Sum(i => infos[i].GlyphAdvance);
                        if (character < start + length && character + count > start)
                        {
                            // Own the complete shaped cluster, including zero-advance marks and ligatures. Neither target disposal nor rewrapping can release these arrays.
                            var copy = Enumerable.Range(at, end - at).Select(i => new GlyphInfo(infos[i].GlyphIndex, 0, infos[i].GlyphAdvance, infos[i].GlyphOffset)).ToArray();
                            var characters = native.Characters.Slice(cluster - native.Metrics.FirstCluster, count).ToArray();
                            var glyphs = new GlyphRun(native.GlyphTypeface, native.FontRenderingEmSize, characters, copy, native.BaselineOrigin, native.BiDiLevel);
                            var bounds = new Rect(bound.Rectangle.Left + advance, lineTop, width, line.Height);
                            var origin = new Point(bounds.X, lineTop + line.Baseline - run.Baseline);
                            var prior = previous?.FirstOrDefault(fragment => fragment.Character == character - start && fragment.Length == count);
                            var flip = prior?.Place ?? new DictationFlip();
                            if (prior is null && first is not null)
                                flip.Place(origin + (first.Displayed(now) - Anchor), now, true);
                            flip.Place(origin, now, still);
                            fragments.Add(new(character - start, count, bounds, origin, glyphs, flip));
                        }
                        advance += width; at = end;
                    }
                }
            }
            lineTop += line.Height;
        }
        Fragments = fragments;
    }

    // A revision owns the picture that was showing. Its geometry and shaping survive later revisions, wraps and disposal of the new target layout.
    internal void Capture(ContinuousInstant now) => Fragments = Fragments.Select(fragment =>
    {
        var flip = new DictationFlip(); flip.Place(fragment.Place.Displayed(now), now, true);
        return fragment with { Place = flip };
    }).ToArray();

    internal void FollowAnchor(Point anchor, ContinuousInstant now, bool still)
    {
        if (Fragments.Count == 0) return;
        foreach (var fragment in Fragments) fragment.Place.Place(anchor + (fragment.Origin - Anchor), now, still);
    }

    internal bool IsMoving(ContinuousInstant now) => Fragments.Any(fragment => fragment.Place.IsMoving(now));

    internal void Draw(DrawingContext context, Point origin, ContinuousInstant now, Color color, double opacity, double rise)
    {
        brush.Color = Color.FromArgb((byte)Math.Round(Math.Clamp(color.A * opacity, 0, 255)), color.R, color.G, color.B);
        if (brush.Color.A == 0) return;
        foreach (var fragment in Fragments)
        {
            var at = origin + (Vector)fragment.Place.Displayed(now) + new Vector(0, rise);
            using (context.PushTransform(Matrix.CreateTranslation(at.X, at.Y))) context.DrawGlyphRun(brush, fragment.Glyphs);
        }
    }

    public void Dispose() { foreach (var fragment in Fragments) fragment.Glyphs.Dispose(); }
}
