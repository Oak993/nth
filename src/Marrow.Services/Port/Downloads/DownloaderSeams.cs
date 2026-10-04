// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): where the downloader's transport is bound.
using System.Net;
using Marrow.Core.Port;

namespace Marrow.Services.Port.Downloads;

public static class DownloaderSeams
{
    /// <summary>
    /// The transport every <see cref="Downloader"/> uses unless it is given one: in the app the system proxy, no automatic redirect (the
    /// downloader follows them itself, https only) and no decompression (the bytes counted are the bytes hashed). A test assembly installs
    /// a handler that fails closed as its process default (SK-0's <c>FakeDownloadTransport.CreateHandler()</c>), so a test that forgets to
    /// bind a fake server cannot reach the network (ED-304 (1)).
    /// </summary>
    public static Ambient<HttpMessageHandler> Handler { get; } = new(CreateProductionHandler);

    /// <summary>The app's transport (internal so a Windows test can run it against the loopback fake: ED-304 (15)).</summary>
    internal static HttpMessageHandler CreateProductionHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = true,
    };
}
