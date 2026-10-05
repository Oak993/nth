// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): the draining line's curves.
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Port.Voice.Countdown;

/// <summary>
/// OD-0106 look C: a 2 px line in the pill's foot, from the cross to the orb, that shortens over the last 30 s before the 5-minute cap (Q-V-6 B). There are no digits.
/// It is the pill's neutral label tone from 0:30 to 0:15, glides to the warning colour starting at 0:15 and to the error colour starting at 0:05 (the owner's second
/// amendment: not 0:10), never a cut. ENGINEERING DECISION (ED-NEW-cloud-vt-17, the owner left the manner of the change to the lead and asked for "premium"): each glide runs
/// through OKLCH over 0.8 s on cubic-bezier(.42, 0, .58, 1) (CSS ease-in-out), and as the colour arrives the line gives one soft brightening of 0.4 s (a sine-squared rise and
/// fall that lifts OKLCH lightness by up to 0.05). Under Reduce Motion the glide stays, over 0.2 s, with no brightening, and the line steps once a second.
/// </summary>
public static class CapCountdown
{
    public static TimeSpan Length { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The seconds left at which the line starts to glide to the warning colour.</summary>
    public static TimeSpan WarningAt { get; } = TimeSpan.FromSeconds(15);

    /// <summary>The seconds left at which the line starts to glide to the error colour.</summary>
    public static TimeSpan DangerAt { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan Glide { get; } = TimeSpan.FromSeconds(0.8);

    public static TimeSpan ReducedGlide { get; } = TimeSpan.FromSeconds(0.2);

    public static TimeSpan Brighten { get; } = TimeSpan.FromSeconds(0.4);

    /// <summary>How far the arrival brightening lifts OKLCH lightness at its peak.</summary>
    public const double BrightenLift = 0.05;

    /// <summary>The line at <paramref name="remaining"/> before the cap (not shown outside 0 &lt; remaining &lt;= 30 s).</summary>
    public static CapCountdownFrame Frame(TimeSpan remaining, bool reduceMotion, CountdownPalette palette)
    {
        var left = remaining.TotalSeconds;
        var visible = left > 0 && left <= Length.TotalSeconds;
        var fraction = Math.Clamp((reduceMotion ? Math.Ceiling(left) : left) / Length.TotalSeconds, 0, 1);
        var glide = (reduceMotion ? ReducedGlide : Glide).TotalSeconds;
        if (left > WarningAt.TotalSeconds) return new CapCountdownFrame(visible, fraction, palette.Neutral, 0);
        var (from, to, start) = left > DangerAt.TotalSeconds ? (palette.Neutral, palette.Warning, WarningAt.TotalSeconds) : (palette.Warning, palette.Danger, DangerAt.TotalSeconds);
        var elapsed = start - left;
        var eased = CssTiming.CubicBezier(0.42, 0, 0.58, 1, Math.Clamp(elapsed / glide, 0, 1));
        var arrived = elapsed - glide;
        var bright = !reduceMotion && arrived >= 0 && arrived < Brighten.TotalSeconds ? Math.Pow(Math.Sin(Math.PI * arrived / Brighten.TotalSeconds), 2) : 0;
        var alpha = from.A + (to.A - from.A) * eased;
        if (bright == 0 && eased <= 0) return new CapCountdownFrame(visible, fraction, from, 0); // the colours the glide starts and ends at are the palette's own
        if (bright == 0 && eased >= 1) return new CapCountdownFrame(visible, fraction, to, 0);
        var lab = Oklch.Glide(Oklch.FromSrgb(from.R, from.G, from.B), Oklch.FromSrgb(to.R, to.G, to.B), eased);
        var (r, g, b) = Oklch.ToSrgb(lab with { L = lab.L + BrightenLift * bright });
        return new CapCountdownFrame(visible, fraction, new CountdownColour(r, g, b, alpha), bright);
    }
}
