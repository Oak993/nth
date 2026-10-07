using Marrow.App.Views.Composer;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-18")]
public sealed class PaletteBubbleDialsTests
{
    [Fact]
    public void Dials_affect_new_rises_without_restarting_existing_lanes_or_their_history()
    {
        var lanes=new StrikeBubbleLanes("17,12,4,1",384,34);
        var before=lanes.Now(0,.2,0);var original=lanes.Rise(0,10);
        lanes.SetPaletteDials(64,1.5,2.4,.04);
        Assert.Equal(before,lanes.Now(0,.2,0));Assert.Equal(0,lanes.Restarts);
        var low=lanes.Rise(0,10);Assert.Equal(original.Dur*1.5,low.Dur,12);Assert.Equal(original.Gap*2.4/1.4,low.Gap,12);
        Assert.Equal(6,lanes.PaletteNormalCount);Assert.Equal(2,lanes.PaletteNucleationCount);
        lanes.SetPaletteDials(32,1,1.4,.10);Assert.Equal(original,lanes.Rise(0,10));Assert.Equal(before,lanes.Now(0,.2,0));
    }
}
