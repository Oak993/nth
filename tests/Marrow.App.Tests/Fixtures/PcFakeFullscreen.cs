// PORT-ONLY: Tactus (ED-NEW-pcctl-23). The foreground window and the notification state, as a test sets them; reads count.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeFullscreen : IFullscreenSource
{
    public int State { get; set; } = 5;

    public nint Front { get; set; } = 0x500;

    public PcRect? Rect { get; set; }

    public bool Caption { get; set; } = true;

    public int Reads { get; private set; }

    public int NotificationState()
    {
        Reads++;
        return State;
    }

    public nint Foreground() => Front;

    public PcRect? WindowRect(nint window) => Rect;

    public bool HasCaption(nint window) => Caption;
}
