// PORT-ONLY CZ-5 (customization-SPEC CZ-10.1; DESIGN cz.js:619-641): every label and fixed text of the Customization page's Working dots section, and the detail line the Dot colour row shows for a mode.
// The tests hold these against the locked cz.js's own dotsSec() (tests/fixtures/customization/dots.json).
namespace Marrow.Core.Port.Settings;

public static class CzDotsText
{
    public const string Heading = "Working dots";
    public const string DotColour = "Dot colour";
    public const string CustomDotColour = "Custom dot colour";
    public const string Classic = "Classic";
    public const string Signature = "Signature";
    public const string ClassicGroup = "Classic dot colours";
    public const string SignatureGroup = "Signature dot colours";
    public const string Note = "The dots run along the bottom of the composer while a turn runs; touching this switches the preview to Running a turn.";

    private const string AccentDetail = "Your accent colour, as the rest of nth";
    private const string ModelDetail = "The colour of the model in use: ";

    /// <summary>The Dot colour row's detail: accent and model say what they follow; custom names the picked swatch.</summary>
    /// <param name="makerName">The name of the model's maker (the preview's, on the page).</param>
    /// <param name="swatchName">The name of the picked accent swatch.</param>
    public static string Detail(WorkingDotsMode mode, string makerName, string swatchName) => mode switch
    {
        WorkingDotsMode.Model => ModelDetail + makerName,
        WorkingDotsMode.Custom => swatchName,
        _ => AccentDetail,
    };
}
