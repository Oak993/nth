using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

// PORT-ONLY test fixture, no Swift twin: one raw HTTP/1.1 GET against MCPProxy's loopback listener, answering the
// status and body. The proxy closes every plain reply (Connection: close), so the body is read to the end.
// Used to see whether a route is still registered: an unknown server id answers 502 {"error":"unknown server"}
// (MCPProxy.swift:291-294), a registered OAuth route with no session answers 401 (:333-339).
namespace Marrow.Services.Tests.Fixtures;

internal static class MCPProxyRawClient
{
    public static async Task<(int Status, string Body)> Get(int port, string path)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(30));
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"));
        using var all = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int n;
            try { n = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (IOException) { break; }
            if (n <= 0) break;
            all.Write(buffer, 0, n);
        }
        var text = Encoding.UTF8.GetString(all.ToArray());
        var divider = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = divider >= 0 ? text[..divider] : text;
        var body = divider >= 0 ? text[(divider + 4)..] : "";
        var status = int.Parse(head.Split(' ', 3)[1], CultureInfo.InvariantCulture);
        return (status, body);
    }
}
