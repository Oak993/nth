// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the words of the probe's rows (V-10.1).
namespace Marrow.Services.Port.Voice.Probe;

/// <summary>The fixed words of the rows. V-10.1 gives the row shapes; the one text it leaves out is the Graphics row with no usable adapter (UNRESOLVED: Q-V-4 style wording, ED-NEW-f-vt-6).</summary>
public static class ProbeText
{
    /// <summary>V-10.1: shown for the GPU backend row when no backend works.</summary>
    public const string NoBackend = "None working, runs on the processor";

    /// <summary>The Graphics row when DXGI lists no adapter other than a software one. UNRESOLVED wording, proposed here.</summary>
    public const string NoGraphicsAdapter = "No graphics adapter";
}
