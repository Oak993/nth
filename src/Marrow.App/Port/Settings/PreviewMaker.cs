// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.4): one of the preview model switch's makers.
namespace Marrow.App.Port.Settings;

/// <summary>A maker of liquid-palettes.json: its key, its display name and its model id (empty when it has none).</summary>
internal sealed record PreviewMaker(string Id, string Name, string Model);
