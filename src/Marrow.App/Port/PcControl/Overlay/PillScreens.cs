// PORT-ONLY: Tactus pill v2, which monitor shows the pill (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Which monitor",
// "Displays", "Auto-dodge" rows 4 and 5). Settings "Show on": the monitor with the latest Tactus step (the default), the
// main monitor, or one monitor by its identity. Dragging the pill to a monitor chooses that monitor (and it is saved). In
// latest-step mode the pill moves only once the latest step's monitor has been different for 2 s, and never while it is
// hovered, dragged or showing a card; it never flies across, it leaves and arrives (its own poses, ED-NEW-pcctl-24). A
// monitor that was chosen but is unplugged sends the pill to the main monitor; the stored choice is kept, so it goes back
// when that monitor returns. Exclusive full screen or presentation mode on the pill's monitor sends it to another monitor
// if one is clear, else nothing is drawn (the tray and the chat show the state); a borderless full-screen window does the
// same, except that with no other monitor the pill stays as its 32 DIP dot. Pure apart from the inputs it is handed.
namespace Marrow.App.Port.PcControl.Overlay;

public enum PillShowOn
{
    LatestStep,
    Main,
    Chosen,
}

/// <summary>Where the pill is: its monitor (null: nowhere, nothing may be drawn), and whether it is the full-screen dot.</summary>
public sealed record PillScreen(OverlayMonitor? Monitor, bool Dot)
{
    public static PillScreen Nowhere { get; } = new(null, false);
}

public sealed class PillScreens
{
    /// <summary>The latest step's monitor must differ this long before the pill follows it.</summary>
    public const double FollowSeconds = 2;

    private string? stepMonitor;
    private double stepSince = double.NaN;
    private string? following;

    public PillShowOn ShowOn { get; set; } = PillShowOn.LatestStep;

    /// <summary>The chosen monitor's identity (Settings or the last drag); kept while it is unplugged.</summary>
    public string? ChosenId { get; set; }

    /// <summary>A drag released on <paramref name="monitorId"/>: that monitor is chosen from now (the caller saves it).</summary>
    public void Dropped(string monitorId)
    {
        ShowOn = PillShowOn.Chosen;
        ChosenId = monitorId;
        following = monitorId;
    }

    /// <summary>The latest Tactus step ran on <paramref name="monitorId"/> at <paramref name="now"/> seconds.</summary>
    public void Step(string monitorId, double now)
    {
        if (string.Equals(stepMonitor, monitorId, StringComparison.OrdinalIgnoreCase)) return;
        stepMonitor = monitorId;
        stepSince = now;
    }

    /// <summary>
    /// Where the pill shows at <paramref name="now"/>, given the monitors, the shade of each, and whether the pill is held
    /// in place (hovered, dragged or showing a card).
    /// </summary>
    public PillScreen Choose(MonitorMap map, Func<OverlayMonitor, MonitorShade> shade, double now, bool heldInPlace)
    {
        var main = map.Main;
        if (main is null) return PillScreen.Nowhere;
        var current = map.ById(following);
        OverlayMonitor wanted = ShowOn switch
        {
            PillShowOn.Main => main,
            PillShowOn.Chosen => map.ById(ChosenId) ?? main,
            _ => current ?? map.ById(stepMonitor) ?? main,
        };
        if (ShowOn == PillShowOn.LatestStep && map.ById(stepMonitor) is { } step && !Same(step, current))
        {
            var due = current is null || (!heldInPlace && now - stepSince >= FollowSeconds);
            wanted = due ? step : current!;
        }
        if (heldInPlace && current is not null) wanted = current;
        var screen = Avoid(map, wanted, shade);
        following = screen.Monitor?.Id ?? following;
        return screen;
    }

    private static PillScreen Avoid(MonitorMap map, OverlayMonitor wanted, Func<OverlayMonitor, MonitorShade> shade)
    {
        var here = shade(wanted);
        if (here == MonitorShade.Clear) return new PillScreen(wanted, false);
        var other = map.Monitors.Where(m => !Same(m, wanted) && shade(m) == MonitorShade.Clear).OrderByDescending(m => m.Primary).FirstOrDefault();
        if (other is not null) return new PillScreen(other, false);
        return here == MonitorShade.BorderlessFullscreen ? new PillScreen(wanted, true) : PillScreen.Nowhere;
    }

    private static bool Same(OverlayMonitor? a, OverlayMonitor? b) => a is not null && b is not null && string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
}
