// PORT-ONLY: Tactus (ED-NEW-pcctl-36). The auto-dodge loop on fakes: the pill host rig, the dodge over fake window events
// and fake caption buttons, posting to the UI thread and timing on the rig's manual clock, its reads run in place, and a
// count of the full-screen watch's invalidations.
using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed record PcDodgeRig(PcPillHostRig Host, TactusDodge Dodge, PcFakeDodgeEvents Events, PcFakeCaptionButtons Windows, Func<int> Invalidated)
{
    public static PcDodgeRig Create()
    {
        var host = PcPillHostRig.Create();
        var events = new PcFakeDodgeEvents();
        var windows = new PcFakeCaptionButtons();
        var invalidated = 0;
        var dodge = new TactusDodge(events, new CaptionButtons(windows), host.Host, host.Overlays, () => host.Now,
            action => Dispatcher.UIThread.Post(action),
            (seconds, run) => host.Clock.CreateTimer(_ => Dispatcher.UIThread.Post(run), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan),
            offUi: read => Task.FromResult(read()), invalidate: () => invalidated++);
        return new PcDodgeRig(host, dodge, events, windows, () => invalidated);
    }
}
