using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

// A folder that cannot be written (AS-9): reading finds nothing, writing is refused as a locked or read-only folder would.
internal sealed class RefusingToastIconFiles : IToastIconFiles
{
    public byte[]? Read(string path) => null;

    public void Write(string path, byte[] bytes) => throw new UnauthorizedAccessException("fake read-only folder");
}
