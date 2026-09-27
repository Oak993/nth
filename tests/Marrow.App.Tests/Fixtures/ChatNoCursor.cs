using Avalonia;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: headless windows do not have a real operating-system pointer position.
internal sealed class ChatNoCursor : ICursorPlatform
{
    public PixelPoint? ScreenPosition => null;
}
