using System.Globalization;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The design host's sRGB → OKLab matrices, also used before theme and pack interpolation.</summary>
public static class WallpaperPalette
{
    public static float[] For(WallpaperPack pack, double dark)
    {
        var result = new float[18];
        for (var i = 0; i < 6; i++)
        {
            var light = Lab(pack.Light[i]); var night = Lab(pack.Dark[i]);
            for (var j = 0; j < 3; j++) result[i * 3 + j] = (float)(light[j] + (night[j] - light[j]) * dark);
        }
        return result;
    }

    public static double[] Lab(string hex)
    {
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static double Linear(int c) { var v = c / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        var r = Linear((rgb >> 16) & 255); var g = Linear((rgb >> 8) & 255); var b = Linear(rgb & 255);
        var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return [ .2104542553 * l + .7936177850 * m - .0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .8086757660 * s ];
    }
}
