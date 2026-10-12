// PORT-ONLY: Tactus (ED-NEW-pcctl-23). Two test monitors: the main one at 100 % with a 48 px taskbar at the bottom, and a
// 150 % one to its right with its taskbar at the top.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public static class PcTestMonitors
{
    public static OverlayMonitor Main { get; } = new("DISPLAY#MAIN", "Main", new PcRect(0, 0, 1920, 1080), new PcRect(0, 0, 1920, 1032), 96, true);

    public static OverlayMonitor Right { get; } = new("DISPLAY#RIGHT", "Right", new PcRect(1920, 0, 2880, 1620), new PcRect(1920, 72, 2880, 1548), 144, false);
}
