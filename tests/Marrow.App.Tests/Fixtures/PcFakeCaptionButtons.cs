// PORT-ONLY: Tactus (ED-NEW-pcctl-23). Windows over an area in Z order with their caption buttons, and a top taskbar.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeCaptionButtons : ICaptionButtonSource
{
    /// <summary>Top first: each window's rect and its buttons (screen pixels).</summary>
    public List<(nint Window, PcRect Rect, PcRect[] Buttons)> ZOrder { get; } = [];

    public int? Taskbar { get; set; }

    /// <summary>How many times the dodge read under the pill (each read walks the Z order once).</summary>
    public int Reads { get; private set; }

    /// <summary>The windows the last read skipped as Tactus's own.</summary>
    public IReadOnlySet<nint> Ignored { get; private set; } = new HashSet<nint>();

    public nint TopWindowOver(PcRect area, IReadOnlySet<nint> ignore)
    {
        Reads++;
        Ignored = ignore.ToHashSet();
        return ZOrder.FirstOrDefault(w => !ignore.Contains(w.Window) && w.Rect.X < area.Right && area.X < w.Rect.Right && w.Rect.Y < area.Bottom && area.Y < w.Rect.Bottom).Window;
    }

    public IReadOnlyList<PcRect> Buttons(nint window) => ZOrder.FirstOrDefault(w => w.Window == window).Buttons ?? [];

    public int? TopTaskbarBottom(PcRect monitor) => Taskbar;
}
