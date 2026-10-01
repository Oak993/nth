using Avalonia;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A cursor platform with a scripted position, so a hover read after a release never depends on the real
/// pointer of the machine running the tests (the drag handle re-reads the cursor on attach and on release).</summary>
public sealed class FixedCursorPlatform(PixelPoint? position) : ICursorPlatform
{
    public PixelPoint? ScreenPosition { get; set; } = position;
}
