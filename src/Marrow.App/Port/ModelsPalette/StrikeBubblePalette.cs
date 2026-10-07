// PORT-ONLY: IR-3 motion() :383-384, preserving Strike's retained lanes/history and random streams.
// Only new rises read changed duration/pause/size dials; existing rises finish continuously. ED-629.
namespace Marrow.App.Views.Composer;
public sealed partial class StrikeBubbleLanes
{
    private bool paletteBound;private double paletteRiseScale=1,palettePause=1.4,paletteLargeShare=.1;
    public int PaletteNormalCount {get;private set;}
    public int PaletteNucleationCount {get;private set;}
    public void SetPaletteDials(double pixelsPerLane,double riseScale,double pause,double largeShare)
    {
        paletteBound=true;paletteRiseScale=riseScale;palettePause=pause;paletteLargeShare=largeShare;
        PaletteNormalCount=Math.Clamp((int)Strike.Round(width/pixelsPerLane),4,NormalCount);
        PaletteNucleationCount=Math.Min(NucleationCount,(int)Strike.Round(PaletteNormalCount/3d));
    }
}
