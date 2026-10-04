using System.Runtime.CompilerServices;
using Marrow.Services.Port.Downloads;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Fixtures;

// SK-3 test fixture: from the moment this assembly loads, the downloader's ambient transport is SK-0's handler with no fake server, which refuses
// every request before any connection. A test that does not hand its Downloader a handler therefore cannot reach the network (R-TEST-10).
internal static class DownloaderFailClosed
{
    [ModuleInitializer]
    internal static void Install() => DownloaderSeams.Handler.SetProcessDefault(FakeDownloadTransport.CreateHandler());
}
