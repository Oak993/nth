using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-21")]
public sealed class PaletteFirstFrameRetargetTests
{
    [Fact]
    public async Task A_delayed_first_frame_captures_the_previous_glides_RGB_and_dials_at_that_frames_time()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var a=engine.Current.Resolve("acme/nova-1","low",false)!;var b=engine.Current.Resolve("acme/nova-1","max",false)!;var c=engine.Current.Resolve("openai/gpt-6-astra","max",false)!;
        var glide=new PaletteGlide(a);glide.Retarget(b,0);glide.Retarget(c,.6);glide.StartAt(1.2);
        var row=new byte[1024];var motion=new double[glide.MotionCount];var frame=glide.Sample(1.2,row,motion);
        Assert.Equal(0,frame.Progress);Assert.Equal(b.GradientRow,row);
        for(var i=0;i<motion.Length;i++)Assert.Equal(b.Motion.GetValueOrDefault(glide.MotionKeys[i]),motion[i],12);
        Assert.Equal(Convert.ToUInt32("FF"+b.Title[1..],16),frame.Title);Assert.Equal(Convert.ToUInt32("FF"+b.Glow[1..],16),frame.Glow);
    }
}
