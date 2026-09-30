// PORT-ONLY: SwiftTerm v1.20.0 Colors.swift:96-114,200-327, default base16Lab palette.
/*
Copyright (c) 2019-2026 Miguel de Icaza (https://github.com/migueldeicaza)
Copyright (c) 2017-2019, The xterm.js authors (https://github.com/xtermjs/xterm.js)
Copyright (c) 2014-2016, SourceLair Private Company (https://www.sourcelair.com)
Copyright (c) 2012-2013, Christopher Jeffrey (https://github.com/chjj/)

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using Avalonia.Media;

namespace Marrow.App.Views.Terminal.Port;

internal static class TerminalPalette
{
    public static readonly Color[] Ansi =
    [
        Color.FromRgb(0, 0, 0), Color.FromRgb(194, 54, 33), Color.FromRgb(37, 188, 36),
        Color.FromRgb(173, 173, 39), Color.FromRgb(73, 46, 225), Color.FromRgb(211, 56, 211),
        Color.FromRgb(51, 187, 200), Color.FromRgb(203, 204, 205), Color.FromRgb(129, 131, 131),
        Color.FromRgb(252, 57, 31), Color.FromRgb(49, 231, 34), Color.FromRgb(234, 236, 35),
        Color.FromRgb(88, 51, 255), Color.FromRgb(249, 53, 248), Color.FromRgb(20, 240, 240),
        Color.FromRgb(233, 235, 235),
    ];

    public static Color[] Generate(Color? background = null, Color? foreground = null)
    {
        var anchors = Ansi.Take(8).Select(Lab.FromColor).ToArray();
        anchors[0] = Lab.FromColor(background ?? Ansi[0]);
        anchors[7] = Lab.FromColor(foreground ?? Ansi[7]);
        if (anchors[7].L < anchors[0].L) (anchors[0], anchors[7]) = (anchors[7], anchors[0]);
        var colors = new List<Color>(Ansi);
        for (var red = 0; red < 6; red++)
        {
            var c0 = Lab.Lerp(red / 5.0, anchors[0], anchors[1]);
            var c1 = Lab.Lerp(red / 5.0, anchors[2], anchors[3]);
            var c2 = Lab.Lerp(red / 5.0, anchors[4], anchors[5]);
            var c3 = Lab.Lerp(red / 5.0, anchors[6], anchors[7]);
            for (var green = 0; green < 6; green++)
            {
                var c4 = Lab.Lerp(green / 5.0, c0, c1);
                var c5 = Lab.Lerp(green / 5.0, c2, c3);
                for (var blue = 0; blue < 6; blue++) colors.Add(Lab.Lerp(blue / 5.0, c4, c5).ToColor());
            }
        }
        for (var index = 0; index < 24; index++) colors.Add(Lab.Lerp((index + 1) / 25.0, anchors[0], anchors[7]).ToColor());
        return colors.ToArray();
    }

    private readonly record struct Lab(double L, double A, double B)
    {
        public static Lab FromColor(Color color)
        {
            static double Linear(byte component)
            {
                var value = component / 255.0;
                return value > 0.04045 ? Math.Pow((value + 0.055) / 1.055, 2.4) : value / 12.92;
            }
            static double Transform(double value) => value > 0.008856 ? Math.Pow(value, 1.0 / 3.0) : 7.787 * value + 16.0 / 116.0;
            var red = Linear(color.R); var green = Linear(color.G); var blue = Linear(color.B);
            var x = Transform((red * 0.4124564 + green * 0.3575761 + blue * 0.1804375) / 0.95047);
            var y = Transform(red * 0.2126729 + green * 0.7151522 + blue * 0.0721750);
            var z = Transform((red * 0.0193339 + green * 0.1191920 + blue * 0.9503041) / 1.08883);
            return new Lab(116 * y - 16, 500 * (x - y), 200 * (y - z));
        }

        public Color ToColor()
        {
            static double Inverse(double value)
            {
                var cube = value * value * value;
                return cube > 0.008856 ? cube : (value - 16.0 / 116.0) / 7.787;
            }
            static byte Component(double value)
            {
                value = value > 0.0031308 ? 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055 : 12.92 * value;
                return (byte)(Math.Clamp(value, 0, 1) * 255 + 0.5);
            }
            var y = (L + 16) / 116;
            var x = Inverse(A / 500 + y) * 0.95047;
            var z = Inverse(y - B / 200) * 1.08883;
            y = Inverse(y);
            return Color.FromRgb(Component(x * 3.2404542 - y * 1.5371385 - z * 0.4985314),
                Component(-x * 0.9692660 + y * 1.8760108 + z * 0.0415560),
                Component(x * 0.0556434 - y * 0.2040259 + z * 1.0572252));
        }

        public static Lab Lerp(double t, Lab from, Lab to) => new(from.L + t * (to.L - from.L),
            from.A + t * (to.A - from.A), from.B + t * (to.B - from.B));
    }
}
