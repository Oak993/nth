namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-9, V-T60: one segment of a state's script: a number of 1/60 s frames at a constant raw level, with or without the processing flag.</summary>
internal sealed record VoiceGlowSegment(int Frames, double Level, bool Processing);

/// <summary>
/// VT-9, V-T60: one frame of the fidelity matrix: a state (a script of segments the driver is stepped through), the composer's size and the theme. The names are ref-glow.js's.
/// </summary>
internal sealed record VoiceGlowJob(string State, int Width, int Height, string Theme, IReadOnlyList<VoiceGlowSegment> Script, string Layers = "")
{
    /// <summary>ref-glow.js's name: <c>ref_glow_...</c>, or <c>ref_band_...</c> for the band canvas alone.</summary>
    public string Name => $"{(Layers == "band" ? "ref_band" : "ref_glow")}_{State}_{Width}x{Height}_{Theme}";

    public bool Dark => Theme == "dark";

    /// <summary>The composer card the glow is drawn over in the reference page.</summary>
    public string Card => Dark ? "#1c1c1c" : "#f4f4f4";
}
