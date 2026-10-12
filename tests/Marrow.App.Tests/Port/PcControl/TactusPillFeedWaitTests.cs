// PORT-ONLY: Tactus, what the pill says while a step waits or a pause waits on a step (ED-NEW-pcctl-37; DESIGN.md 2.9, 4.5
// "Waiting for you to type", 10.1 "Pausing after this step: dragging in Paint"). A write waiting while the user types gives
// the field and the app's name, and the facts change when it begins and ends; a pausing app's row names the step it waits
// for with its app's name, and nothing once it is paused.
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class TactusPillFeedWaitTests
{
    private const string Outlook = "exe:c:\\office\\outlook.exe";
    private const string Paint = "exe:c:\\windows\\mspaint.exe";

    [Fact]
    public void A_write_waiting_while_the_user_types_gives_the_field_and_the_app_s_name()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => 10);
        using var outlook = assembly.Pause.Join("chat-a", Outlook, new PcWaitState());
        var changes = 0;
        feed.Changed += () => changes++;

        var wait = assembly.Typing.Begin(new PcTypingWait(Outlook, "Subject"));
        Assert.Equal(1, changes);
        Assert.Equal(new PillTyping("Subject", "Outlook"), feed.Facts().Typing);
        Assert.Equal("Waiting while you type", PillStates.Compute(feed.Facts(), 10).Title);
        Assert.Equal("Subject (Outlook)", PillStates.Compute(feed.Facts(), 10).Line);

        wait.Dispose();
        Assert.Equal(2, changes);
        Assert.Null(feed.Facts().Typing);
    }

    [Fact]
    public void A_pausing_app_names_the_step_it_waits_for_and_nothing_once_paused()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => 10);
        using var paint = assembly.Pause.Join("chat-a", Paint, new PcWaitState());

        var unit = assembly.Pause.Acting(Paint, "dragging");
        Assert.Null(Assert.Single(feed.Facts().Apps).Busy);
        assembly.Pause.Pause(PcScope.App(Paint), PcPauseSource.Pill);
        var pausing = Assert.Single(feed.Facts().Apps);
        Assert.Equal(PillAppPhase.Pausing, pausing.Phase);
        Assert.Equal("dragging in Mspaint", pausing.Busy);
        Assert.Equal("Pausing after dragging in Mspaint", Assert.Single(PillStates.Compute(feed.Facts(), 10).Rows).Line);

        unit.Dispose();
        var paused = Assert.Single(feed.Facts().Apps);
        Assert.Equal(PillAppPhase.Paused, paused.Phase);
        Assert.Null(paused.Busy);
    }
}
