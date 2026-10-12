// PORT-ONLY: which monitor shows the pill (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Which monitor", "Displays", "Auto-
// dodge" rows 4 and 5): latest step after 2 s and never while held in place, main, a chosen monitor that a drag sets, an
// unplugged choice falling back to main and returning, and full-screen shades moving the pill, making it the dot, or
// leaving nothing drawn.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PillScreensTests
{
    private static readonly Func<OverlayMonitor, MonitorShade> Clear = _ => MonitorShade.Clear;

    private static MonitorMap Map(params OverlayMonitor[] monitors)
    {
        var map = new MonitorMap(new PcFakeOverlayMonitors { Now = [.. monitors] });
        map.Refresh();
        return map;
    }

    [Fact]
    public void In_latest_step_mode_the_pill_follows_a_new_monitor_only_after_2_s_and_never_while_held_in_place()
    {
        var map = Map(PcTestMonitors.Main, PcTestMonitors.Right);
        var screens = new PillScreens();
        screens.Step(PcTestMonitors.Main.Id, 0);
        Assert.Same(PcTestMonitors.Main, screens.Choose(map, Clear, 0, false).Monitor);

        screens.Step(PcTestMonitors.Right.Id, 10);
        Assert.Same(PcTestMonitors.Main, screens.Choose(map, Clear, 11.9, false).Monitor);
        Assert.Same(PcTestMonitors.Main, screens.Choose(map, Clear, 12.5, heldInPlace: true).Monitor);
        Assert.Same(PcTestMonitors.Right, screens.Choose(map, Clear, 12.5, false).Monitor);
    }

    [Fact]
    public void Main_mode_and_a_drag_s_chosen_monitor()
    {
        var map = Map(PcTestMonitors.Main, PcTestMonitors.Right);
        var screens = new PillScreens { ShowOn = PillShowOn.Main };
        screens.Step(PcTestMonitors.Right.Id, 0);
        Assert.Same(PcTestMonitors.Main, screens.Choose(map, Clear, 5, false).Monitor);

        screens.Dropped(PcTestMonitors.Right.Id);

        Assert.Equal(PillShowOn.Chosen, screens.ShowOn);
        Assert.Equal(PcTestMonitors.Right.Id, screens.ChosenId);
        Assert.Same(PcTestMonitors.Right, screens.Choose(map, Clear, 6, false).Monitor);
    }

    [Fact]
    public void An_unplugged_choice_falls_back_to_main_and_returns_with_its_monitor()
    {
        var screens = new PillScreens { ShowOn = PillShowOn.Chosen, ChosenId = PcTestMonitors.Right.Id };

        Assert.Same(PcTestMonitors.Main, screens.Choose(Map(PcTestMonitors.Main), Clear, 0, false).Monitor);
        Assert.Equal(PcTestMonitors.Right.Id, screens.ChosenId);
        Assert.Same(PcTestMonitors.Right, screens.Choose(Map(PcTestMonitors.Main, PcTestMonitors.Right), Clear, 1, false).Monitor);
    }

    [Fact]
    public void A_full_screen_monitor_sends_the_pill_to_a_clear_one_else_the_dot_or_nowhere()
    {
        var both = Map(PcTestMonitors.Main, PcTestMonitors.Right);
        var screens = new PillScreens { ShowOn = PillShowOn.Main };
        MonitorShade MainShaded(OverlayMonitor m, MonitorShade shade) => m == PcTestMonitors.Main ? shade : MonitorShade.Clear;

        Assert.Equal(new PillScreen(PcTestMonitors.Right, false), screens.Choose(both, m => MainShaded(m, MonitorShade.BorderlessFullscreen), 0, false));
        Assert.Equal(new PillScreen(PcTestMonitors.Right, false), screens.Choose(both, m => MainShaded(m, MonitorShade.NothingDrawn), 0, false));

        var one = Map(PcTestMonitors.Main);
        Assert.Equal(new PillScreen(PcTestMonitors.Main, true), screens.Choose(one, _ => MonitorShade.BorderlessFullscreen, 0, false));
        Assert.Equal(PillScreen.Nowhere, screens.Choose(one, _ => MonitorShade.NothingDrawn, 0, false));
        Assert.Equal(PillScreen.Nowhere, screens.Choose(Map(), Clear, 0, false));
    }
}
