// PORT-ONLY: what the auto-dodge reads (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-13; DESIGN.md 10.3 "Auto-dodge", "Dodge
// rules"): only the topmost window over the pill's area (plus 8 DIP) counts, Tactus's own overlays never do, its caption
// buttons and a showing top taskbar arrive in the monitor's DIP, and the planner turns them into the dodge.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class CaptionButtonsTests
{
    private static readonly PcRect[] MainButtons = [new(1782, 0, 46, 32), new(1828, 0, 46, 32), new(1874, 0, 46, 32)];

    [Fact]
    public void The_topmost_window_s_buttons_under_a_Right_pill_move_it_left_to_clear_them_by_12_DIP()
    {
        var source = new PcFakeCaptionButtons();
        source.ZOrder.Add((0x10, new PcRect(0, 0, 1920, 1032), MainButtons));
        var reader = new CaptionButtons(source);
        var pill = new PillRect(1584, 8, 320, 48);

        var inputs = reader.Read(PcTestMonitors.Main, pill);
        var dodge = CaptionButtons.Plan(pill, PillAnchor.Right, PcTestMonitors.Main, inputs);

        Assert.Equal((nint)0x10, inputs.TopWindow);
        Assert.Equal(3, inputs.CaptionButtons.Count);
        Assert.Equal(-(1904 - (1782 - 12)), dodge.Dx);
        Assert.Equal(0, dodge.Dy);
    }

    [Fact]
    public void Buttons_on_a_150_percent_monitor_arrive_in_its_DIP()
    {
        var source = new PcFakeCaptionButtons();
        source.ZOrder.Add((0x20, new PcRect(1920, 72, 2880, 1548), [new PcRect(4593, 72, 69, 48)]));
        var reader = new CaptionButtons(source);

        var inputs = reader.Read(PcTestMonitors.Right, new PillRect(1584, 56, 320, 48));

        Assert.Equal(new PillRect(1782, 48, 46, 32), Assert.Single(inputs.CaptionButtons));
    }

    [Fact]
    public void Only_the_top_window_counts_and_the_overlays_are_skipped()
    {
        var source = new PcFakeCaptionButtons();
        source.ZOrder.Add((0x99, new PcRect(0, 0, 1920, 120), [new PcRect(900, 0, 46, 32)]));
        source.ZOrder.Add((0x30, new PcRect(200, 0, 1600, 900), []));
        source.ZOrder.Add((0x10, new PcRect(0, 0, 1920, 1032), MainButtons));
        var reader = new CaptionButtons(source);
        reader.Ignore.Add(0x99);

        var inputs = reader.Read(PcTestMonitors.Main, new PillRect(740, 8, 440, 48));

        // The window over the pill has no caption buttons there, so the maximized one beneath it does not count.
        Assert.Equal((nint)0x30, inputs.TopWindow);
        Assert.Empty(inputs.CaptionButtons);
        Assert.False(CaptionButtons.Plan(new PillRect(740, 8, 440, 48), PillAnchor.Centre, PcTestMonitors.Main, inputs).Active);
    }

    [Fact]
    public void A_showing_top_taskbar_pushes_the_pill_below_it()
    {
        var source = new PcFakeCaptionButtons { Taskbar = 72 };
        var reader = new CaptionButtons(source);
        var pill = new PillRect(740, 56, 440, 48);

        var inputs = reader.Read(PcTestMonitors.Right, pill);
        var dodge = CaptionButtons.Plan(pill, PillAnchor.Centre, PcTestMonitors.Right, inputs);

        Assert.Equal(48, inputs.TopTaskbarBottom);
        Assert.Equal(0, dodge.Dy);

        source.Taskbar = 72 + 72;
        var shown = reader.Read(PcTestMonitors.Right, pill);
        Assert.Equal(96 + 8 - 56, CaptionButtons.Plan(pill, PillAnchor.Centre, PcTestMonitors.Right, shown).Dy);
    }
}
