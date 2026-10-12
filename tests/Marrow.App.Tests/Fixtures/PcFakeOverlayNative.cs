// PORT-ONLY: Tactus (ED-NEW-pcctl-20). A fake of the overlay's Win32 calls that records them; nothing touches a real window.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeOverlayNative : IOverlayNative
{
    public uint Style { get; set; }
    public bool Excludes { get; init; } = true;
    public List<string> Calls { get; } = [];

    public uint ExtendedStyle(nint window) => Style;

    public void SetExtendedStyle(nint window, uint style)
    {
        Style = style;
        Calls.Add($"style {window:x}");
    }

    public bool ExcludeFromCapture(nint window)
    {
        Calls.Add($"capture {window:x}");
        return Excludes;
    }

    public void BringToTop(nint window) => Calls.Add($"top {window:x}");

    public PixelPoint? CursorInClient(nint window) => null;
}
