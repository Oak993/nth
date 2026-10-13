// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the composer light in the app. One slot per composer
// group (ComposerArea), over the group's glass and under the tab and the pill's content; it reads the app's settings
// (AppSettings.ComposerLight, so a change reaches every composer at once), the composer's state (ENGINEERING DECISION:
// running while a turn runs, typing while the draft holds anything, else at rest), the thread's maker colours and the
// accent (the Uiverse light's Maker and Accent schemes), and where the composer and the window's bottom edge are. OD-0150: the
// shader lights' keystrokes and caret, and their warm-up gate (ShaderLightGate).
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Light;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;

namespace Marrow.App.Views.Composer;

internal sealed class ComposerLightSlot : Decorator
{
    private readonly ComposerLightView light = new();
    private readonly Control composer;
    private readonly ShaderLightGate gate;
    private ProviderKind? provider;
    private ComposerNSTextView? box;
    private Avalonia.Controls.Presenters.TextPresenter? presenter;
    private int keystrokes;
    private bool lightOn;
    private int wallpaperRevision;
    private (Size Window, Size Composer) measuredFor;
    private ContinuousInstant sizeChangedAt;
    private TaskHandle? remeasure;

    /// <summary>ED-NEW-perf-motion-6: how long the window's and the composer's size must hold still before what lies under the composer is measured again.</summary>
    internal static readonly TimeSpan StillSize = TimeSpan.FromMilliseconds(200);

    public ComposerLightView Light => light;

    public ComposerLightSlot(ThreadRuntime runtime, Control composer)
    {
        this.composer = composer;
        IsHitTestVisible = false;
        ClipToBounds = false;
        // OD-0150 (ED-NEW-lightv2-3): a shader light is shown once its program is warm; the warm-up's 1 px host sits beside the light
        gate = new ShaderLightGate(light);
        Child = new Panel { IsHitTestVisible = false, Children = { light, gate.Host } };
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.ComposerLight, settings =>
        {
            lightOn = settings.Style != ComposerLightStyle.Off;
            if (box is not null) box.LightOn = lightOn; // OD-0150 (ED-NEW-lightv2-7): the lifted placeholder
            gate.Apply(settings);
        });
        this.Track(() => runtime.IsRunning ? ComposerLightPhase.Running : runtime.DraftIsEmpty ? ComposerLightPhase.Rest : ComposerLightPhase.Typing,
            phase => light.Phase = phase);
        this.Track(() => AppEnvironment.GetAppModel(this).Thread(runtime.ThreadID)?.Provider, p => { provider = p; UpdatePalette(); });
        AttachedToVisualTree+=(_,_)=>BindSnapshot(runtime.ThreadID);
        // PORT-ONLY (voice-SPEC V-13.4, OD-0097 B1 to B3; VT-12): the light's level while the voice is on (the setting's: steps aside, dims to 35 %, stays on) and 1 otherwise; the light eases to it itself
        if ((composer as ComposerView)?.Voice is { } voice)
            this.Track(() => VoiceLightTarget.For(voice.OrbSettings.LightVoice, voice.State), level => light.VoiceLight = level);
        // OD-0150 (ED-NEW-lightv2-6): the wallpaper the composer sits on, for the veil near it (measured again when it changes)
        this.Track(() => Marrow.UI.Theme.Wallpapers.WallpaperLibraryStore.Shared.Current.Selection, _ => UpdateWallpaper());
        this.Track(() => Marrow.UI.Theme.WallpaperStore.Shared.Current.Image, _ => UpdateWallpaper());
        ActualThemeVariantChanged += (_, _) => UpdatePalette();
        LayoutUpdated += (_, _) => Place();
        composer.LayoutUpdated += (_, _) => Place();
        composer.LayoutUpdated += (_, _) => FindBox();
    }

    // OD-0150: the shader lights' keystroke lift at the caret (the page's uKey and uCaret): each change the user types into the
    // composer's text is a keystroke, at the caret's x in the composer
    private void FindBox()
    {
        if (box is not null && TopLevel.GetTopLevel(box) is not null) return;
        if (box is not null) box.TextChanged -= Typed;
        box = composer.GetVisualDescendants().OfType<ComposerNSTextView>().FirstOrDefault();
        presenter = null;
        if (box is not null) { box.TextChanged += Typed; box.LightOn = lightOn; }
    }

    private void Typed(object? sender, TextChangedEventArgs e)
    {
        if (box is not { IsFocused: true } || !box.IsKeyboardFocusWithin) return;
        light.CaretX = CaretX();
        light.Keystrokes = ++keystrokes;
    }

    private double CaretX()
    {
        if (box is null) return double.NaN;
        presenter ??= box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault();
        if (presenter is null) return double.NaN;
        try
        {
            var at = presenter.TextLayout.HitTestTextPosition(Math.Clamp(box.CaretIndex, 0, box.Text?.Length ?? 0));
            return presenter.TranslatePoint(new Point(at.X, 0), composer) is { } p ? p.X : double.NaN;
        }
        catch (Exception) { return double.NaN; }
    }

    private void UpdateWallpaper()
    {
        var library = Marrow.UI.Theme.Wallpapers.WallpaperLibraryStore.Shared.Current.Selection;
        var picture = Marrow.UI.Theme.WallpaperStore.Shared.Current.Image;
        var kind = library?.Kind ?? Marrow.UI.Theme.Wallpapers.WallpaperKind.None;
        light.Wallpaper = new WallpaperState(kind != Marrow.UI.Theme.Wallpapers.WallpaperKind.None || picture is not null,
            kind == Marrow.UI.Theme.Wallpapers.WallpaperKind.Animated, ++wallpaperRevision);
    }

    // the composer's rectangle in the light's coordinates, and the window's bottom edge (the playground's 13 px fade)
    private void Place()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || TopLevel.GetTopLevel(composer) != top) return;
        // the window's or the composer's size changed: what lies under the composer is measured again (the veil's amount)
        var size = (top.Bounds.Size, composer.Bounds.Size);
        if (size != measuredFor && light.Wallpaper.Showing)
        {
            // ED-NEW-perf-motion-6: a measure is a GPU readback on the render thread; a size that changes every frame (a zoom glide, a sidebar
            // slide, a live resize) is measured once it has held still for StillSize, not on each frame (the veil eases to what it measures).
            var first = measuredFor == default;
            measuredFor = size;
            if (first) Remeasure();
            else
            {
                sizeChangedAt = SwiftClock.Now;
                remeasure ??= MainActor.Spawn(RemeasureWhenStill);
            }
        }
        if (composer.TranslatePoint(default, light) is not { } origin) return;
        light.ComposerRect = new Rect(origin, composer.Bounds.Size);
        light.FadeBottom = top.TranslatePoint(new Point(0, top.Bounds.Height), light)?.Y;
    }

    private void Remeasure() => light.Wallpaper = light.Wallpaper with { Revision = ++wallpaperRevision };

    private async Task RemeasureWhenStill()
    {
        try
        {
            for (var wait = StillSize; wait > TimeSpan.Zero; wait = StillSize - (SwiftClock.Now - sizeChangedAt))
                if (!await SwiftTask.TrySleep(wait)) return;
            if (light.Wallpaper.Showing) Remeasure();
        }
        finally { remeasure = null; }
    }

    // the maker's liquid stops (OD-0063: heart and second, dark; the deeper pair, light) and the accent, per appearance
    private void UpdatePalette(){if(!snapshotBound)light.Palette=PaletteFor(EffortBrand.Create(provider));}
    private bool snapshotBound;
    private void BindSnapshot(Guid id)
    {
        if(snapshotBound)return;snapshotBound=true;
        Marrow.App.Port.ModelsPalette.PaletteSurfaceBinding.Attach(this,AppEnvironment.GetAppModel(this),id,target=>light.Palette=target.Composer);
    }

    /// <summary>The palette a composer of this brand wears. The preview in Settings > Customization takes the same rule (CZ-4.5.5, ComposerLightPalettes).</summary>
    internal static ComposerLightPalette PaletteFor(EffortBrand brand)
    {
        var stops = brand.LiquidStops;
        string Hex(SwiftColor c, ThemeVariant theme) { var x = c.Resolve(theme); return $"#{x.R:X2}{x.G:X2}{x.B:X2}"; }
        (string, string, string) Of(ThemeVariant theme) => (Hex(stops[0], theme), Hex(stops[1], theme), Hex(stops[2], theme));
        return ComposerLightPalettes.FromStops(Of(ThemeVariant.Dark), Of(ThemeVariant.Light), Hex(Chrome.Accent, ThemeVariant.Dark), Hex(Chrome.Accent, ThemeVariant.Light));
    }
}
