// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5; DESIGN cz.js:107-116 czTone, :155-190 modelSwitch): the preview's makers, how the switch steps through them, the colours
// they give the preview's light and dots, and the chip's accessible name. The data is MakerLiquids (CZ-1), pinned by its sha256.
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;

namespace Marrow.App.Port.Settings;

internal static class PreviewMakers
{
    /// <summary>All 30 makers, in the switch's order.</summary>
    internal static IReadOnlyList<PreviewMaker> All { get; } = MakerLiquids.All.Select(m => new PreviewMaker(m["id"], m["name"], m["model"])).ToArray();

    internal static PreviewMaker For(string id) => All.First(m => m.Id == id);

    /// <summary>CZ-4.5.3: the maker of the default provider's brand; a brand with no maker (Purple) gives Claude.</summary>
    internal static string Initial(ProviderKind provider)
    {
        var brand = EffortBrand.Create(provider);
        var name = JewelPalettes.For(brand).Maker;
        return All.Any(m => m.Id == name) ? name : "Claude";
    }

    /// <summary>CZ-4.5.1: the next maker (+1) or the previous (-1); the list wraps around.</summary>
    internal static string Step(string id, int delta)
    {
        var index = IndexOf(id);
        return All[((index + delta) % All.Count + All.Count) % All.Count].Id;
    }

    internal static int IndexOf(string id)
    {
        for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i;
        throw new ArgumentOutOfRangeException(nameof(id), id, "Not a maker of the preview's list.");
    }

    // OD-0089 and the GAP-2 owner ruling: Customization previews the real slot, not the mock-up's High swatches.
    // Purple has no maker (CZ-4.5.3); it must not shadow the Claude fallback.
    private static readonly IReadOnlyDictionary<string, EffortBrand> ComposerBrands = Enum.GetValues<EffortBrand>()
        .Where(brand => brand != EffortBrand.Purple).ToDictionary(brand => JewelPalettes.For(brand).Maker);

    /// <summary>The real composer slot's palette for a locked maker, retaining the preview's current accents.</summary>
    internal static ComposerLightPalette Palette(string id, string accentDark, string accentLight)
    {
        if (ComposerBrands.TryGetValue(id, out var brand))
            return ComposerLightSlot.PaletteFor(brand) with { AccentDark = accentDark, AccentLight = accentLight };
        // The remaining gallery makers have no fixed provider slot: keep the same pair rule on their own stops.
        var m = MakerLiquids.For(id);
        return ComposerLightPalettes.FromStops((m["dark.deep"], m["dark.mid"], m["dark.light"]), (m["light.deep"], m["light.mid"], m["light.light"]), accentDark, accentLight);
    }

    /// <summary>CZ-4.5.5: the chip's and menu's dot, <c>czTone(deep, dark, title)</c> with the title of that theme.</summary>
    internal static string Dot(string id, bool dark)
    {
        var m = MakerLiquids.For(id);
        var theme = dark ? "dark" : "light";
        return CzText.Tone(m[theme + ".deep"], dark, m[theme + ".title"]);
    }

    /// <summary>CZ-4.5.1: "Preview model: {name}" and " ({model})" when the maker has a model.</summary>
    internal static string ChipName(string id)
    {
        var maker = For(id);
        return "Preview model: " + maker.Name + (maker.Model.Length > 0 ? " (" + maker.Model + ")" : "");
    }
}
