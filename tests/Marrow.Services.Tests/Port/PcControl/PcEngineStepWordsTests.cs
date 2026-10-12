// PORT-ONLY: Tactus, the words the engine gives the pill's hands-off request (ED-NEW-pcctl-38; DESIGN.md 2.7, 2.9, 4.5).
// A still moment while the user types in the app names its step and holds nothing ("click Save", no kind); a keyboard
// borrow that goes at once names what it typed for the chip ("typing", the keyboard). Fakes only.
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class PcEngineStepWordsTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    [Fact]
    public async Task A_still_moment_names_its_step_and_holds_nothing()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        var waits = new List<PcBorrowWait>();
        harness.Lease.Waiting += waits.Add;
        harness.UserTypes(0x1a);

        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");

        Assert.True(result.Ok, result.Render());
        var wait = Assert.Single(waits.Distinct());
        Assert.Null(wait.Kind);
        Assert.Equal(new PcStepWords("click Save", "clicked Save", "one click"), wait.Words);
        Assert.StartsWith("a step in ", wait.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_keyboard_borrow_that_goes_at_once_names_what_it_typed_for_the_chip()
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        var used = new List<PcBorrowWait>();
        rig.Borrow.Lease.Used += used.Add;

        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"hello\"}"));

        Assert.True(result.Ok, result.Render());
        var chip = Assert.Single(used);
        Assert.Equal(PcBorrowKind.Focus, chip.Kind);
        Assert.Equal(new PcStepWords("type in Text editor", "typed in Text editor", "typing"), chip.Words);
    }
}
