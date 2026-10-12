using Marrow.Core.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-10: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3cV1Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R4_types_all_200_characters_when_its_own_pieces_change_value_and_bounds(bool growing)
    {
        var rig = new C3cR4TextRig(growing);
        using var engine = rig.Harness.Engine();
        var text = new string('a', 200);
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"" + text + "\"}"));
        Assert.True(result.Ok, result.Render());
        Assert.Equal(text, rig.Field.Value);
        Assert.Equal(4, rig.Borrow.Host.Sent.Count);
    }
}
