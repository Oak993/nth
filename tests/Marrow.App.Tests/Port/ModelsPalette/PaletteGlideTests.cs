using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteGlideTests
{
    [Fact]
    public async Task Retarget_uses_the_actual_rgb_blend_and_title_glow_and_motion_share_the_clock()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var a=engine.Current.Resolve("acme/nova-1","low",false)!;
        var b=engine.Current.Resolve("acme/nova-1","max",false)!;
        var glide=new PaletteGlide(a);glide.Retarget(b,0);
        var row=new byte[1024];var motion=new double[glide.MotionCount];
        var frame=glide.Sample(.6,row,motion);
        Assert.Equal(.5,frame.Progress);Assert.Equal((byte)Math.Round((a.GradientRow[0]+b.GradientRow[0])/2d),row[0]);
        for(var i=0;i<motion.Length;i++)Assert.Equal((a.Motion[glide.MotionKeys[i]]+b.Motion[glide.MotionKeys[i]])/2,motion[i],10);
        var blended=row.ToArray();glide.Retarget(a,.6);
        var restarted=glide.Sample(.6,row,motion);Assert.Equal(blended,row);Assert.Equal(frame.Title,restarted.Title);Assert.Equal(frame.Glow,restarted.Glow);
        var completed=glide.Sample(1.8,row,motion);Assert.Equal(1,completed.Progress);Assert.Equal(a.GradientRow,row);
    }
    [Fact]
    public async Task A_running_arrival_defers_the_glide_and_warm_frame_sampling_allocates_nothing()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var a=engine.Current.Resolve("acme/nova-1","low",false)!;var b=engine.Current.Resolve("acme/nova-1","max",false)!;
        var glide=new PaletteGlide(a);var row=new byte[1024];var motion=new double[glide.MotionCount];
        glide.Retarget(b,0,arrivalRunning:true);glide.Sample(10,row,motion);Assert.Equal(a.GradientRow,row);
        glide.ArrivalFinished(10);Assert.Equal(0,glide.Sample(10,row,motion).Progress);
        glide.Sample(10.2,row,motion);var bytes=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++)glide.Sample(10.2,row,motion);
        Assert.Equal(bytes,GC.GetAllocatedBytesForCurrentThread());
    }
}
