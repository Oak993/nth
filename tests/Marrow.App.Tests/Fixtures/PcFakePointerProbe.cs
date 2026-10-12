// PORT-ONLY: Tactus (ED-NEW-pcctl-34). Windows' cursor and buttons as a test sets them (GetCursorPos, GetAsyncKeyState).
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePointerProbe : IPointerProbe
{
    public PcPoint? At { get; set; }

    public bool Down { get; set; }

    public PcPoint? Cursor() => At;

    public bool ButtonDown() => Down;
}
