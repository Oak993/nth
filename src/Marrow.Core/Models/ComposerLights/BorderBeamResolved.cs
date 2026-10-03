// PORT-ONLY, owner-ruled addition (OD-0089; ED-251): logic.js beamEff() and beamVars(), the package's resolved props and the
// layer opacity products, for a composer of any size (the playground's is 820 x 52, radius 22).

namespace Marrow.Core.Models.ComposerLights;

/// <summary>The continuous values a Border beam draws with (beamEff), before the eased transitions.</summary>
public sealed record BorderBeamResolved(
    bool Dark, BeamPreset Preset, bool Pulse, bool Static,
    double StrokeOpacity, double InnerOpacity, double BloomOpacity,
    double Brightness, double Saturation, double HueBase, double HueRange, double Radius,
    double Blur, double CoreBlur, double BloomBlur, double GlowX, double GlowY, double Boost, double Spike,
    double Duration, double AutoDuration)
{
    /// <summary>
    /// beamEff(b, st): the theme (Auto follows the app), Mono halved except on Line, strength times the state's factor when
    /// "Follow the state" is on (x.55 at rest, x.8 typing, x1 running), the preset's brightness or 1.3, its saturation,
    /// Line's hue range capped at 13, the blur radii times the glow size, Pulse Outside's glow scale from the box.
    /// </summary>
    public static BorderBeamResolved For(BorderBeamOptions b, ComposerLightPhase phase, bool appDark, double w, double h, double defaultRadius)
    {
        var dark = b.Theme == BeamTheme.Auto ? appDark : b.Theme == BeamTheme.Dark;
        var size = ComposerLightSettings.TypeName(b.Type);
        var pr = BorderBeamTables.Shared.Presets[size][dark ? "dark" : "light"];
        var mono = b.Palette == BeamPalette.Mono;
        var mul = mono && b.Type != BeamType.Line ? 0.5 : 1;
        var resp = b.Respond ? phase switch { ComposerLightPhase.Typing => 0.8, ComposerLightPhase.Running => 1.0, _ => 0.55 } : 1;
        var str = Math.Clamp(b.Strength, 0, 1) * resp;
        var autoBr = pr.Brightness ?? 1.3;
        var pulse = b.Type is BeamType.PulseInner or BeamType.PulseOutside;
        var stat = mono || b.StaticColors;
        static double R2(double v) => BorderBeamMath.Fixed2(v);
        var gs = b.GlowSize;
        var auto = BorderBeamMath.AutoDuration(b, w, h, defaultRadius);
        return new BorderBeamResolved(
            dark, pr, pulse, stat,
            R2(pr.StrokeOpacity * mul) * b.StrokeX * str, R2(pr.InnerOpacity * mul) * b.InnerX * str, R2(pr.BloomOpacity * mul) * b.BloomX * str,
            b.Brightness ?? autoBr, b.Saturation ?? pr.Saturation, b.HueShift, b.Type == BeamType.Line ? Math.Min(13, b.HueRange) : b.HueRange,
            b.Radius ?? defaultRadius,
            BorderBeamMath.GlowRadius(8, gs),
            b.CoreBlur ?? BorderBeamMath.GlowRadius(dark ? 3 : 6, gs), b.BloomBlur ?? BorderBeamMath.GlowRadius(dark ? 22.5 : 15, gs),
            b.GlowX ?? BorderBeamMath.ToFixed(Math.Clamp(w / 350, 0.35, 4), 3), b.GlowY ?? BorderBeamMath.ToFixed(Math.Clamp(h / 140, 0.35, 4), 3),
            b.Boost, b.Spike,
            b.Duration ?? auto, auto);
    }

    /// <summary>
    /// The values the playground's sliders show on Auto for these options on a composer w x h (their Auto toggles): the
    /// duration, the brightness, the saturation, Pulse Outside's core and bloom blurs and glow scale.
    /// </summary>
    public static (double Duration, double Brightness, double Saturation, double CoreBlur, double BloomBlur, double GlowX, double GlowY) AutoValues(
        BorderBeamOptions b, bool appDark, double w, double h, double defaultRadius)
    {
        var auto = For(b with { Duration = null, Brightness = null, Saturation = null, CoreBlur = null, BloomBlur = null, GlowX = null, GlowY = null },
            ComposerLightPhase.Rest, appDark, w, h, defaultRadius);
        return (auto.Duration, auto.Brightness, auto.Saturation, auto.CoreBlur, auto.BloomBlur, auto.GlowX, auto.GlowY);
    }
}
