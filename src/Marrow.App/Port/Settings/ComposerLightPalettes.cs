// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.5; ED-251): the one rule that turns a maker's liquid stops into the composer light's palette. The composer slot
// (ComposerLightSlot.PaletteFor) and the Customization preview both use it, so the preview wears what a real composer of that maker wears.
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Port.Settings;

internal static class ComposerLightPalettes
{
    /// <summary>Dark: the mid and light stops (OD-0063: the heart and second); light: the deep and mid stops. Each argument is a theme's three stops, "#RRGGBB".</summary>
    internal static ComposerLightPalette FromStops((string Deep, string Mid, string Light) dark, (string Deep, string Mid, string Light) light, string accentDark, string accentLight) =>
        new(dark.Mid, dark.Light, light.Deep, light.Mid, accentDark, accentLight);
}
