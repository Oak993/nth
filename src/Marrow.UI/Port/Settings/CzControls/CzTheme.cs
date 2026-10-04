// PORT-ONLY CZ-1: the exact dark/light tokens of CZ-12, DESIGN gen.py:91-92.
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Chrome;
namespace Marrow.UI.Port.Settings;

internal static class CzTheme
{
    public static bool Dark(Control owner) => owner.ActualThemeVariant != ThemeVariant.Light;
    public static Color Token(Control owner, string name)
    {
        var dark = Dark(owner);
        return name switch
        {
            "cardfill" => Alpha(dark, .03), "div" => Alpha(dark, dark ? .12 : .1),
            "tile" => Alpha(dark, dark ? .07 : .05), "tilesel" => Alpha(dark, dark ? .1 : .07),
            "sel" => Alpha(dark, dark ? .24 : .72), "cap" => Alpha(!dark, dark ? .2 : .55),
            "caph" => Alpha(!dark, dark ? .3 : .82), "pill" => dark ? Alpha(true, .16) : Colors.White,
            "rim" => Alpha(dark, .22), "trk" => Alpha(dark, .1), "trkfill" => Alpha(dark, dark ? .24 : .72),
            "ring" => Alpha(dark, dark ? .92 : .78), "hov" => Alpha(dark, dark ? .08 : .05),
            "prs" => Alpha(dark, dark ? .14 : .1), "popbg" => Color.Parse(dark ? "#1E1E1E" : "#FFFFFF"),
            "popedge" => Alpha(dark, dark ? .2 : .08), _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }
    private static Color Alpha(bool white, double alpha) => Color.FromArgb((byte)Math.Floor(alpha * 255 + .5), white ? (byte)255 : (byte)0, white ? (byte)255 : (byte)0, white ? (byte)255 : (byte)0);
    public static IBrush Brush(Color color) => new SolidColorBrush(color);
    public static IBrush Text(Control owner, bool secondary = false) => Brush(Dark(owner) ? Color.FromArgb(secondary ? (byte)179 : (byte)255, 236, 236, 236) : Color.FromArgb(secondary ? (byte)173 : (byte)255, 28, 28, 30));
    public static Color Accent(Control owner) => Marrow.UI.Chrome.Chrome.Accent.Resolve(owner.ActualThemeVariant);
}
