// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the composer light in the app. One slot per composer
// group (ComposerArea), over the group's glass and under the tab and the pill's content; it reads the app's settings
// (AppSettings.ComposerLight, so a change reaches every composer at once), the composer's state (ENGINEERING DECISION:
// running while a turn runs, typing while the draft holds anything, else at rest), the thread's maker colours and the
// accent (the Uiverse light's Maker and Accent schemes), and where the composer and the window's bottom edge are.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Voice.Light;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;

namespace Marrow.App.Views.Composer;

internal sealed class ComposerLightSlot : Decorator
{
    private readonly ComposerLightView light = new();
    private readonly Control composer;
    private ProviderKind? provider;

    public ComposerLightView Light => light;

    public ComposerLightSlot(ThreadRuntime runtime, Control composer)
    {
        this.composer = composer;
        IsHitTestVisible = false;
        ClipToBounds = false;
        Child = light;
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.ComposerLight, settings => light.Settings = settings);
        this.Track(() => runtime.IsRunning ? ComposerLightPhase.Running : runtime.DraftIsEmpty ? ComposerLightPhase.Rest : ComposerLightPhase.Typing,
            phase => light.Phase = phase);
        this.Track(() => AppEnvironment.GetAppModel(this).Thread(runtime.ThreadID)?.Provider, p => { provider = p; UpdatePalette(); });
        // PORT-ONLY (voice-SPEC V-13.4, OD-0097 B1 to B3; VT-12): the light's level while the voice is on (the setting's: steps aside, dims to 35 %, stays on) and 1 otherwise; the light eases to it itself
        if ((composer as ComposerView)?.Voice is { } voice)
            this.Track(() => VoiceLightTarget.For(voice.OrbSettings.LightVoice, voice.State), level => light.VoiceLight = level);
        ActualThemeVariantChanged += (_, _) => UpdatePalette();
        LayoutUpdated += (_, _) => Place();
        composer.LayoutUpdated += (_, _) => Place();
    }

    // the composer's rectangle in the light's coordinates, and the window's bottom edge (the playground's 13 px fade)
    private void Place()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || TopLevel.GetTopLevel(composer) != top) return;
        if (composer.TranslatePoint(default, light) is not { } origin) return;
        light.ComposerRect = new Rect(origin, composer.Bounds.Size);
        light.FadeBottom = top.TranslatePoint(new Point(0, top.Bounds.Height), light)?.Y;
    }

    // the maker's liquid stops (OD-0063: heart and second, dark; the deeper pair, light) and the accent, per appearance
    private void UpdatePalette() => light.Palette = PaletteFor(EffortBrand.Create(provider));

    /// <summary>The palette a composer of this brand wears. The preview in Settings > Customization takes the same rule (CZ-4.5.5, ComposerLightPalettes).</summary>
    internal static ComposerLightPalette PaletteFor(EffortBrand brand)
    {
        var stops = brand.LiquidStops;
        string Hex(SwiftColor c, ThemeVariant theme) { var x = c.Resolve(theme); return $"#{x.R:X2}{x.G:X2}{x.B:X2}"; }
        (string, string, string) Of(ThemeVariant theme) => (Hex(stops[0], theme), Hex(stops[1], theme), Hex(stops[2], theme));
        return ComposerLightPalettes.FromStops(Of(ThemeVariant.Dark), Of(ThemeVariant.Light), Hex(Chrome.Accent, ThemeVariant.Dark), Hex(Chrome.Accent, ThemeVariant.Light));
    }
}
