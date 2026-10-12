// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Live versus snapshot in the thread", 2.4 airspace; ED-NEW-web-w19-4). An inline web
// widget's page is a window of its own that always draws above nth's glass, so in the thread it is live only when nothing of
// nth's can be over it: fully inside the thread's clear area (not under the header glass or the composer) while the thread is
// still. Otherwise it shows its snapshot, the same pixels. Scrolling makes every widget a snapshot in the same frame; 150 ms
// after the scroll stops, one widget goes live again: the one the user last touched, else the newest one that is fully
// visible. Never more than one at a time. A pure function so the rule is tested without a window. No Swift counterpart.
namespace Marrow.Core.Web;

/// <summary>A rectangle in DIPs (the thread's coordinates).</summary>
public readonly record struct InlineRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>True when this rectangle lies wholly inside <paramref name="outer"/> (half a DIP of rounding allowed).</summary>
    public bool IsInside(InlineRect outer) =>
        Width > 0 && Height > 0 && X >= outer.X - .5 && Y >= outer.Y - .5 && Right <= outer.Right + .5 && Bottom <= outer.Bottom + .5;
}

/// <summary>One inline web widget as the rule sees it.</summary>
/// <param name="Key">Whatever identifies the widget to the caller.</param>
/// <param name="Bounds">Its web rectangle in the thread's coordinates.</param>
/// <param name="Order">Larger is newer (its place in the thread).</param>
/// <param name="TouchedAt">When the user last pressed, typed or scrolled in it, or null.</param>
public sealed record InlineWebCandidate(object Key, InlineRect Bounds, long Order, DateTimeOffset? TouchedAt = null);

public static class InlineWebLive
{
    /// <summary>How long the thread must be still before a widget goes live again.</summary>
    public static readonly TimeSpan IdleAfterScroll = TimeSpan.FromMilliseconds(150);

    /// <summary>The widget that may be live, or null for none (scrolling, or nothing fully visible).</summary>
    /// <param name="candidates">The inline widgets on screen.</param>
    /// <param name="clear">The part of the thread nothing of nth's covers: below the header glass, above the composer.</param>
    /// <param name="scrolling">True while the thread scrolls, and until it has been still for <see cref="IdleAfterScroll"/>.</param>
    public static object? Choose(IReadOnlyList<InlineWebCandidate> candidates, InlineRect clear, bool scrolling)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (scrolling || clear.Width <= 0 || clear.Height <= 0) return null;
        var visible = candidates.Where(c => c.Bounds.IsInside(clear)).ToList();
        if (visible.Count == 0) return null;
        var touched = visible.Where(c => c.TouchedAt is not null).OrderByDescending(c => c.TouchedAt).FirstOrDefault();
        return (touched ?? visible.OrderByDescending(c => c.Order).First()).Key;
    }

    /// <summary>The inline height of a widget: what it asked for, at least <paramref name="minimum"/>, at most 60 % of the thread.</summary>
    public static double Height(double? requested, double threadHeight, double fallback, double minimum = 80)
    {
        var cap = Math.Max(minimum, threadHeight * MaxThreadShare);
        var wanted = requested is { } asked && double.IsFinite(asked) && asked > 0 ? asked : fallback;
        return Math.Clamp(wanted, minimum, cap);
    }

    /// <summary>DESIGN 7.6: an inline widget is at most 60 % of the thread's height; beyond it the widget scrolls inside.</summary>
    public const double MaxThreadShare = 0.6;
}
