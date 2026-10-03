// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the composer light in the app. One slot per composer
// group (ComposerArea), over the group's glass and under the tab and the pill's content; it reads the app's settings
// (AppSettings.ComposerLight, so a change reaches every composer at once), the composer's state (ENGINEERING DECISION:
// running while a turn runs, typing while the draft holds anything, else at rest), the thread's maker colours and the
// accent (the Uiverse light's Maker and Accent schemes), and where the composer and the window's bottom edge are.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
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
    private void UpdatePalette()
    {
        var brand = EffortBrand.Create(provider);
        var stops = brand.LiquidStops;
        string Hex(SwiftColor c, ThemeVariant theme) { var x = c.Resolve(theme); return $"#{x.R:X2}{x.G:X2}{x.B:X2}"; }
        light.Palette = new ComposerLightPalette(
            Hex(stops[1], ThemeVariant.Dark), Hex(stops[2], ThemeVariant.Dark), Hex(stops[0], ThemeVariant.Light), Hex(stops[1], ThemeVariant.Light),
            Hex(Chrome.Accent, ThemeVariant.Dark), Hex(Chrome.Accent, ThemeVariant.Light));
    }
}
