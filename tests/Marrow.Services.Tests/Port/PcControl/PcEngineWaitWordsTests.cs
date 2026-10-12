// PORT-ONLY: Tactus, what the engine tells the pill while a step waits or runs (ED-NEW-pcctl-37; DESIGN.md 2.9, 10.1). A
// write into the control the user types in names that field and its app while it waits, and nothing once it ran; a step in
// flight names itself to a pause ("clicking Save"). Fakes only.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class PcEngineWaitWordsTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        return (harness, app, engine);
    }

    [Fact]
    public async Task A_write_into_the_users_field_names_the_field_and_its_app_while_it_waits()
    {
        var (harness, app, engine) = await Ready();
        var seen = new List<PcTypingWait?>();
        harness.Typing.Changed += () => seen.Add(harness.Typing.Current);
        app.Root.Children[0].States |= PcStates.Focused;
        harness.Desktop.Bump(0x1a);
        harness.UserTypes(0x1a);

        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"x\"}");

        Assert.True(typed.Ok, typed.Render());
        var key = harness.Desktop.Window(0x1a)!.App.Key;
        Assert.Equal([new PcTypingWait(key, "Text editor"), null], seen);
        Assert.Null(harness.Typing.Current);
    }

    [Fact]
    public async Task A_step_in_flight_names_itself_to_a_pause()
    {
        var (harness, _, engine) = await Ready();
        var key = harness.Desktop.Window(0x1a)!.App.Key;
        PcPauseStatus? status = null;
        harness.Desktop.BeforePerform = (_, _, _) =>
        {
            harness.Desktop.BeforePerform = null;
            harness.Pause.Pause(PcScope.App(key), PcPauseSource.Pill);
            status = harness.Pause.Status(key);
        };

        await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        harness.Pause.Resume(PcScope.App(key), PcPauseSource.Pill);
        engine.Dispose();

        Assert.NotNull(status);
        Assert.Equal(PcPausePhase.Pausing, status.Phase);
        Assert.Equal("clicking Save", status.Busy);
    }
}
