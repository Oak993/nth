using System.Text.RegularExpressions;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Match accent, the sixth pack (OPTIONS-FORMAT 5): built in OKLCH from the accent on the wallpaper's own roles, as the page's matchOf does.</summary>
public static partial class WallpaperMatchPack
{
    public const string Id = "match";
    public const string Name = "Match accent";
    /// <summary>The page's accent when none is given or the value is not #rrggbb (lib-page.js loadAccent).</summary>
    public const string FallbackAccent = "#005fb8";
    private const double Deg = Math.PI / 180;

    [GeneratedRegex(@"\A#[0-9a-fA-F]{6}\z")] private static partial Regex HexColor();

    public static string Normalize(string? hex) => hex is not null && HexColor().IsMatch(hex) ? hex.ToLowerInvariant() : FallbackAccent;

    private static double Wrap(double d) => (d + 540) % 360 - 180;

    // The page turns a ground by w * Wrap(target - hue), the short way. For a hue on the band's falling edge (above band[2], the 100 to 120 degree
    // ramp where w is between 1 and 0) the short way runs back down through orange and brown, so a half-turned colour is brown. The turn there goes
    // the other way round instead, through green, teal and blue: the same plum at full weight and the same hue at none, so the page is
    // matched everywhere but on that ramp, where nothing brown is passed through (ED-NEW-cloud-wl-3).
    private static double Turn(double target, double hue, double fallingEdgeStart)
        => hue > fallingEdgeStart ? ((target - hue) % 360 + 360) % 360 : Wrap(target - hue);

    private static double Smooth(double a, double b, double x) { var t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    public static WallpaperPack Build(WallpaperDefinition wall, string? accentHex)
    {
        var premium = wall.Premium ?? throw new InvalidOperationException($"{wall.Kind}/{wall.Id} has no Match accent data");
        var accent = WallpaperPalette.Lab(Normalize(accentHex)); var m = premium.Match;
        var chroma = double.Hypot(accent[1], accent[2]); var hue = Math.Atan2(accent[2], accent[1]) / Deg;
        var ck = m.Ck * Math.Clamp(chroma / 0.15, 0.55, 1.3);
        string[] Slots(bool dark)
        {
            int li = dark ? 0 : 2, ci = dark ? 1 : 3;
            var mid = premium.Roles.Sum(r => r[li]) / 6;
            return premium.Roles.Select((r, i) =>
            {
                var l = mid + (r[li] - mid) * m.Lk + (dark ? m.Dl[i] : 0);
                var c = r[ci] * ck * m.Cm[i] * (dark ? m.Dc[i] : 1); var h = hue + m.Off[i];
                if (dark)
                {
                    // warm hues turn brown or olive when dark: grounds go toward plum, mid tones toward rose, the lights keep the hue
                    var dw = m.DarkWarm; var hh = Wrap(h);
                    var w = Smooth(dw.Band[0], dw.Band[1], hh) * (1 - Smooth(dw.Band[2], dw.Band[3], hh));
                    if (w > 0)
                    {
                        if (r[0] < dw.GroundBelow) { h = hh + w * Turn(dw.GroundHue, hh, dw.Band[2]); c *= 1 + (dw.Kc - 1) * w; l += dw.Dl * w; }
                        else if (r[0] < dw.MidBelow) { h = hh + dw.MidAmount * w * Turn(dw.MidHue, hh, dw.Band[2]); c *= 1 + (dw.Kc - 1) * w; l += dw.Dl * w; }
                        else { l += dw.BrightDl * w; c *= 1 + (dw.BrightKc - 1) * w; }
                    }
                }
                return WallpaperOklch.Hex(WallpaperOklch.Fit(l, c, h * Deg));
            }).ToArray();
        }
        return new(Id, Name, Slots(true), Slots(false));
    }
}
