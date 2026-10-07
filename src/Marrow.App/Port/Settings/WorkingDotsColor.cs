// PORT-ONLY CZ-5 (customization-SPEC CZ-10.4; OWNER OD-0085, OD-0089 amendment, OD-0099 Look picks): the colour the working dots wear, from the workingDots setting. Every composer's
// ComposerWorkingDotsSlot and the Customization preview ask the one rule, so the preview wears what a real composer of that maker wears.
using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal static class WorkingDotsColor
{
    // one object for one answer: a field handed the colour it already wears starts no blend (DotFieldControl.UpdateColor compares by reference)
    private static readonly ConcurrentDictionary<string, SwiftColor> Models = new();
    private static readonly ConcurrentDictionary<string, SwiftColor> Customs = new();

    /// <summary>CZ-10.4: the maker a brand's composer wears, as MakerLiquids names it (the brand's JewelPalettes maker; Purple has none in MakerLiquids).</summary>
    internal static string MakerOf(EffortBrand brand) => JewelPalettes.For(brand).Maker;

    /// <summary>
    /// CZ-10.4: the colour for the setting, as a colour that follows the appearance.
    /// <c>accent</c> is <see cref="Chrome.Accent"/> (the colour the source draws the dots in, ComposerWorkingDots.swift:13-19), which follows a pick and a theme itself; <c>model</c> is the
    /// maker's mid stop toned for the window; <c>custom</c> is the picked swatch's hex for the appearance.
    /// </summary>
    /// <param name="maker">The thread's maker (or, in the preview, the preview maker); null or a name MakerLiquids does not hold is the Purple rule.</param>
    internal static SwiftColor Of(WorkingDotsSettings settings, string? maker) => settings.Mode switch
    {
        WorkingDotsMode.Model => Models.GetOrAdd(maker is not null && Holds(maker) ? maker : "", Model),
        WorkingDotsMode.Custom => Customs.GetOrAdd(AccentPalette.Normalize(Known(settings.Custom)), Custom),
        _ => Chrome.Accent,
    };

    /// <summary>CZ-10.4: the colour the composer of a thread wears: the app's workingDots setting and the thread's provider brand, read inside a tracked read so a change of either reaches the composer.</summary>
    internal static SwiftColor For(AppModel model, Guid threadID) =>
        Of(model.Settings.WorkingDots, MakerOf(EffortBrand.Create(model.Thread(threadID)?.Provider)));

    /// <summary>CZ-10.4 <c>Resolve(settings, maker, theme)</c>: <see cref="Of"/> drawn in the appearance.</summary>
    internal static Color Resolve(WorkingDotsSettings settings, string? maker, ThemeVariant theme) => Of(settings, maker).Resolve(theme);

    private static bool Holds(string maker) => MakerLiquids.All.Any(m => m["id"] == maker);

    // the stored setting reads an unknown id as the default pick (WorkingDotsSettings); a record built in code is held to the same rule
    private static string Known(string id) => WorkingDotsSettings.KnownCustomIds.Contains(id) ? id : WorkingDotsSettings.Default.Custom;

    // czTone(mid, dark, title) per appearance (cz.js wdColour); the Purple rule has no title colour, so Tone's own fallback (white on dark, #1C1C1E on light) is the target
    private static SwiftColor Model(string maker)
    {
        string Tone(bool dark)
        {
            var theme = dark ? "dark" : "light";
            if (maker.Length > 0)
            {
                var m = MakerLiquids.For(maker);
                return CzText.Tone(m[theme + ".mid"], dark, m[theme + ".title"]);
            }
            return CzText.Tone(JewelPalettes.For(EffortBrand.Purple).High[1], dark, dark ? "#FFFFFF" : "#1C1C1E");
        }
        return SwiftColor.Dynamic(light: SwiftColor.Hex(Tone(false)), dark: SwiftColor.Hex(Tone(true)));
    }

    private static SwiftColor Custom(string id) =>
        id == AccentPalette.WindowsId ? SwiftColor.ControlAccentColor : AccentPalette.Pick(id)!;
}
