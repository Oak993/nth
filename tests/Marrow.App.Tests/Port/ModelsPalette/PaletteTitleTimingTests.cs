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
        Assert.Equal(EffortTitleFramesRig.Crop(root,380,f,f.Title),EffortTitleFramesRig.Crop(root,400,f,f.Title));
        Assert.NotEqual(EffortTitleFramesRig.Crop(root,420,f,f.Title),EffortTitleFramesRig.Crop(root,440,f,f.Title));
        foreach(var ms in f.Times.Where(ms=>ms>=460))Assert.Equal(EffortTitleFramesRig.Crop(root,460,f,f.Title),EffortTitleFramesRig.Crop(root,ms,f,f.Title));
    });
}
