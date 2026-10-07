// PORT-ONLY: IR-3 18..30 amplitude normalized to v22's full 5 / worker 6 / lead 10.
// SVG viewboxes remain 30 / blend 40, as in the locked source; ED-NEW-models-palette-21.
namespace Marrow.App.Port.ModelsPalette;
internal static class PaletteWaveProjection
{
    internal static double FastAmplitude(double amplitude,bool blend,bool lead)=>(blend?lead?10:6:5)*amplitude/30;
    internal static double ViewBox(bool blend)=>blend?40:30;
}
