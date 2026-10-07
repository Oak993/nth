using Marrow.App.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-238")]
public sealed class PaletteTitleTimingTests(HeadlessSession headless)
{
    [Fact]
    public void The_original_plain_title_is_identical_after_the_last_letter_has_settled() => headless.OnUiThread(()=>
    {
        var root=Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!,"title-timing");
        var f=EffortTitleFramesRig.Render(new(root,"high;0:xhigh",20,600,false,null));
        File.WriteAllText(Path.Combine(root,"crop.json"),System.Text.Json.JsonSerializer.Serialize(f.Title));
        Assert.NotEqual(EffortTitleFramesRig.Crop(root,380,f,f.Title),EffortTitleFramesRig.Crop(root,400,f,f.Title));
        Assert.Equal(EffortTitleFramesRig.Crop(root,480,f,f.Title),EffortTitleFramesRig.Crop(root,500,f,f.Title));
        Assert.Equal(EffortTitleFramesRig.Crop(root,500,f,f.Title),EffortTitleFramesRig.Crop(root,600,f,f.Title));
    });
}
