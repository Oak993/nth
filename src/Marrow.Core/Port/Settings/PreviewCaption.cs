// PORT-ONLY CZ-3 (customization-SPEC CZ-4.4; DESIGN cz.js:726-731 caption()): the words at the right of the preview's foot.
using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Port.Settings;

public static class PreviewCaption
{
    /// <summary>" · ": space, U+00B7, space.</summary>
    private const string Separator = " \u00b7 ";

    /// <summary>The caption for the settings being previewed; <paramref name="makerName"/> is the preview model switch's display name (CZ-4.5.4).</summary>
    public static string Text(ComposerLightSettings settings, string makerName) => settings.Style switch
    {
        ComposerLightStyle.Beam => "Border beam" + Separator + Shape(settings.Beam) + Separator + CzText.PaletteNames[PaletteId(settings.Beam.Palette)],
        ComposerLightStyle.Aura => "Uiverse light" + Separator + Colours(settings.Aura.Scheme, makerName) + Separator + CzText.Fmt(settings.Aura.Speed, 1) + " s a turn",
        ComposerLightStyle.Shader when settings.ActiveShader is { } light => Shader(light, settings.ShaderOptions(light.Id)),
        _ => "No light",
    };

    // OD-0150: a shader light's name, its pack and its spread
    private static string Shader(ShaderLightDef light, ShaderLightOptions options) =>
        light.Name + Separator + (ShaderLightCatalog.Pack(options.Pack)?.Name ?? options.Pack) + Separator + CzText.Fmt(options.Spread, 0) + " px";

    // b.family === 'pulse' ? 'Pulse ' + (inner ? 'inside' : 'outside') : { md: 'Large', line: 'Line', sm: 'Small' }[size]
    private static string Shape(BorderBeamOptions beam) => beam.Type switch
    {
        BeamType.PulseInner => "Pulse inside",
        BeamType.PulseOutside => "Pulse outside",
        BeamType.Line => "Line",
        BeamType.Small => "Small",
        _ => "Large",
    };

    private static string Colours(AuraScheme scheme, string makerName) => scheme switch
    {
        AuraScheme.Maker => makerName + " colours",
        AuraScheme.Accent => "Accent",
        AuraScheme.Custom => "Custom",
        _ => "Original",
    };

    private static string PaletteId(BeamPalette palette) => palette.ToString().ToLowerInvariant();
}
