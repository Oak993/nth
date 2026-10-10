// PORT-ONLY: OD-0136 amendment 2 (ED-NEW-nth-motion). The motion pack's colour tokens, exactly as the review page defines
// them for its light (-l) and dark (-d) specimen panels (nth-motion-review.html :15-29). The drawn marks of every nth
// primitive (spinner, bars, rings, ticks, skeletons, tube, link) use these; labels keep the app's own text colours.
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>One theme's tokens (the page's <c>--d-*</c> custom properties).</summary>
public sealed record NthPalette(
    SKColor Background, SKColor Raise, SKColor Strong, SKColor On, SKColor Mark, SKColor Fill, SKColor FillTop, SKColor FillBottom,
    SKColor Text, SKColor Text2, SKColor Text3, SKColor Track, SKColor Skeleton, SKColor SkeletonHigh, SKColor SkeletonLight,
    SKColor FillLight, SKColor Tube, SKColor Rim, SKColor Edge, SKColor Glass, SKColor Stop, SKColor StopBottom, SKColor Shadow)
{
    /// <summary>The light panel (<c>--*-l</c>).</summary>
    public static readonly NthPalette Light = new(
        Background: new(0xf5, 0xf6, 0xf8), Raise: new(0xff, 0xff, 0xff), Strong: new(0x15, 0x17, 0x1d), On: new(0xff, 0xff, 0xff),
        Mark: new(0x34, 0x3a, 0x47), Fill: new(0x34, 0x3a, 0x47), FillTop: new(0x5a, 0x62, 0x73), FillBottom: new(0x20, 0x24, 0x2d),
        Text: new(0x1d, 0x20, 0x27), Text2: new(0x52, 0x59, 0x68), Text3: new(0x5f, 0x66, 0x76), Track: new(0xd9, 0xdc, 0xe2),
        Skeleton: new(0xc6, 0xcb, 0xd4), SkeletonHigh: new(0xd6, 0xda, 0xe1), SkeletonLight: new(255, 255, 255, 255),
        FillLight: Alpha(255, 255, 255, .5), Tube: new(0xc9, 0xce, 0xd7), Rim: Alpha(255, 255, 255, .95), Edge: Alpha(20, 28, 48, .12),
        Glass: Alpha(255, 255, 255, .80), Stop: new(0x3a, 0x40, 0x4d), StopBottom: new(0x1a, 0x1d, 0x24), Shadow: Alpha(20, 28, 48, .16));

    /// <summary>The dark panel (<c>--*-d</c>).</summary>
    public static readonly NthPalette Dark = new(
        Background: new(0x15, 0x18, 0x1e), Raise: new(0x1f, 0x23, 0x2b), Strong: new(0xf2, 0xf4, 0xf8), On: new(0x15, 0x17, 0x1d),
        Mark: new(0xd3, 0xd8, 0xe0), Fill: new(0xbc, 0xc3, 0xcf), FillTop: new(0xee, 0xf1, 0xf5), FillBottom: new(0x9e, 0xa6, 0xb4),
        Text: new(0xe9, 0xec, 0xf1), Text2: new(0xa9, 0xb0, 0xbd), Text3: new(0x95, 0x9c, 0xab), Track: new(0x2f, 0x34, 0x3e),
        Skeleton: new(0x30, 0x35, 0x3f), SkeletonHigh: new(0x3b, 0x40, 0x4b), SkeletonLight: Alpha(255, 255, 255, .22),
        FillLight: Alpha(255, 255, 255, .85), Tube: new(0x2c, 0x31, 0x3b), Rim: Alpha(255, 255, 255, .10), Edge: Alpha(255, 255, 255, .07),
        Glass: Alpha(44, 49, 60, .80), Stop: new(0xf2, 0xf4, 0xf8), StopBottom: new(0xc9, 0xcf, 0xd9), Shadow: Alpha(0, 0, 0, .55));

    /// <summary>The puck over an image (both themes): <c>--pk-bg</c>, <c>--pk-rim</c>, <c>--pk-mark</c>, <c>--pk-track</c>.</summary>
    public static readonly SKColor PuckBackground = Alpha(18, 20, 26, .62), PuckRim = Alpha(255, 255, 255, .18),
        PuckMark = SKColors.White, PuckTrack = Alpha(255, 255, 255, .30);

    /// <summary>The specimen's model gradient (<c>--m1..--m3</c>); a site passes its model's own colours instead.</summary>
    public static readonly SKColor Model1 = new(0x1f, 0xa5, 0x95), Model2 = new(0x3f, 0x6d, 0xf0), Model3 = new(0x7b, 0x4f, 0xf0);

    public static NthPalette For(bool dark) => dark ? Dark : Light;

    /// <summary>A CSS rgba() colour (alpha rounded as the browser stores it, to the nearest of 255).</summary>
    public static SKColor Alpha(byte r, byte g, byte b, double a) => new(r, g, b, (byte)Math.Round(a * 255, MidpointRounding.AwayFromZero));

    /// <summary>An Avalonia colour of a token, for sites that draw it with Avalonia brushes.</summary>
    public static Avalonia.Media.Color ToColor(SKColor c) => Avalonia.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
}
