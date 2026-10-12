// PORT-ONLY: where the engine tells the Tactus pointer it acts (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5 "The Tactus
// pointer"): once per action, before it runs, at the target's centre, as a press for a click and as typing for text;
// visible only when the window's own root is the one under the point (a covering window or a minimized window parks it);
// nothing at all with "Show Tactus pointer" off.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-21")]
public sealed class PcPointerFeedTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";
    private const string ClickSave = "{\"window\":\"w:1a\",\"element\":3}";
    private const string TypeText = "{\"window\":\"w:1a\",\"element\":2,\"text\":\"Q3\"}";

    [Fact]
    public async Task A_click_aims_the_pointer_at_the_button_s_centre_as_a_press_before_it_runs()
    {
        var (harness, app, engine, aims) = await Ready();
        var performsAtAim = -1;
        harness.Pointer.Aimed += _ => performsAtAim = harness.Desktop.Performs;
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickSave);

        Assert.True(result.Ok, result.Render());
        var aim = Assert.Single(aims);
        Assert.Equal(new PcPoint(150, 125), aim.Point);
        Assert.Equal(PcPointerAct.Press, aim.Act);
        Assert.True(aim.Visible);
        Assert.Equal("notepad", aim.AppName);
        Assert.Equal((nint)0x1a, aim.Window);
        Assert.Equal(0, performsAtAim);
    }

    [Fact]
    public async Task Typing_aims_it_at_the_field_as_typing()
    {
        var (_, _, engine, aims) = await Ready();

        var result = await PcEngineHarness.Call(engine, "pc_type", TypeText);

        Assert.True(result.Ok, result.Render());
        var aim = Assert.Single(aims);
        Assert.Equal(new PcPoint(110 + 550, 160 + 300), aim.Point);
        Assert.Equal(PcPointerAct.Type, aim.Act);
    }

    [Fact]
    public async Task A_window_covering_the_point_or_a_minimized_window_makes_it_not_visible()
    {
        var (harness, app, engine, aims) = await Ready();
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        harness.Desktop.AddApp(0x2b, "Inbox - Outlook", "outlook.exe", 202);

        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickSave)).Ok);
        app.Title = "Untitled - notepad";
        app.Minimized = true;
        await PcEngineHarness.Call(engine, "pc_click", ClickSave);

        Assert.NotEmpty(aims);
        Assert.All(aims, aim => Assert.False(aim.Visible));
    }

    [Fact]
    public async Task With_Show_Tactus_pointer_off_nothing_is_raised()
    {
        var (harness, app, engine, aims) = await Ready();
        harness.Settings = harness.Settings with { ShowPointer = false };
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";

        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickSave)).Ok);

        Assert.Empty(aims);
    }

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine, List<PcPointerAim> Aims)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var aims = new List<PcPointerAim>();
        harness.Pointer.Aimed += aims.Add;
        return (harness, app, engine, aims);
    }
}
