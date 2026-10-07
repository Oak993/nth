using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteDialRetargetTests
{
    [Fact]
    public async Task Every_new_shader_kind_dial_glides_and_preserves_the_previous_kinds_displayed_dials()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var pack=fake.Pack.WithBaseline(fake.Pack.Baseline.Concat([new Marrow.Core.Port.ModelsPalette.ModelPaletteFact("x-ai/grok-4","x-ai",0,0,0,true)]).ToArray());
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,pack);await engine.CheckNow();
        var a=engine.Current.Resolve("x-ai/grok-4","max",false)!;var b=engine.Current.Resolve("openai/gpt-6-astra","max",false)!;
        var glide=new PaletteGlide(a);glide.Retarget(b,0);
        Assert.Contains("specular_scale",glide.MotionKeys);Assert.Contains("oil_sheen_alpha",glide.MotionKeys);
        var row=new byte[1024];var motion=new double[glide.MotionCount];glide.Sample(.6,row,motion);
        for(var i=0;i<motion.Length;i++)Assert.Equal((a.Motion.GetValueOrDefault(glide.MotionKeys[i])+b.Motion.GetValueOrDefault(glide.MotionKeys[i]))/2,motion[i],12);
        var displayed=motion.ToArray();glide.Retarget(a,.6);glide.Sample(.6,row,motion);Assert.Equal(displayed,motion);
    }
}
