using System.Threading.Channels;

namespace Marrow.Setup.Tests.Fakes;

// Fake-only duplex transport; no handle, process, socket, pipe name or filesystem.
internal static class DuplexMemoryPipe
{
    internal static (Stream Client, Stream Server) Create()
    {
        var clientToServer = Channel.CreateUnbounded<byte>();
        var serverToClient = Channel.CreateUnbounded<byte>();
        return (new DuplexMemoryStream(serverToClient.Reader, clientToServer.Writer),
            new DuplexMemoryStream(clientToServer.Reader, serverToClient.Writer));
    }
}
