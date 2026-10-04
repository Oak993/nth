using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.UI.Port.Settings.ColorPicker;

/// <summary>
/// The picker's two edge colours. lp-page.css names them (--rim, --cap, --ring) without defining them; the values are the ones the
/// Customization design defines for the same names (design-src/customization/gen.py, .cz and .cz.light), dark and light.
/// </summary>
internal static class PickerTheme
{
    internal static bool Light(Control c) => c.ActualThemeVariant == ThemeVariant.Light;

    /// <summary>--rim: the .5 px inner edge of the square, strips, hex field, dot and swatches.</summary>
    internal static Color Rim(Control c) => Light(c) ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x33, 255, 255, 255);

    /// <summary>--cap: the hex field's fill.</summary>
    internal static Color Cap(Control c) => Light(c) ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x3D, 0, 0, 0);

    /// <summary>--ring (the dark/light values of the Customization design's D3 set): the chosen swatch's ring.</summary>
    internal static Color Ring(Control c) => Light(c) ? Color.FromArgb(0xC7, 0, 0, 0) : Color.FromArgb(0xEB, 255, 255, 255);

    /// <summary>--tx2: the secondary text's opacity (.66 dark, .64 light in the same set).</summary>
    internal static double Secondary(Control c) => Light(c) ? 0.64 : 0.66;
}
