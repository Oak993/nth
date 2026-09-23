using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the environment MCPOAuthTests run the OAuth flow in (R-TEST-9, R-TEST-10).
// - The secret store is an in-memory Credential Manager backend bound to SecretStore.Shared for the test's own async
//   flow, so nothing reaches the user's Credential Manager.
// - A TestRuntime binds the main actor (the browser is opened on main, MCPOAuth.swift:434) and the clock (the
//   300-second callback timeout, :452-454, and the 30-second request limits, :721, :750, :774).
// - The default-browser seam records the address instead of opening a browser.
// - CallbackClient plays the browser against the callback listener over a real loopback socket.
// Nothing here computes an expected value; the tests transcribe those from the Swift and the records.
public sealed class OAuthHarness : IDisposable
{
    private readonly IDisposable storeScope;
    private readonly IDisposable browserScope;
    private readonly TaskCompletionSource<string> firstOpen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> opened = [];

    public OAuthHarness()
    {
        Memory = new InMemoryCredentialBackend();
        Backend = new RefusingCredentialBackend(Memory);
        Store = new SecretStore(Backend);
        Runtime = new TestRuntime();
        storeScope = SecretStore.Shared.Override(Store);
        browserScope = Workspace.Open.Override(OpenInTest);
    }

    public static TimeSpan Guard => LoopbackHttpServer.Guard;

    public InMemoryCredentialBackend Memory { get; }

    public RefusingCredentialBackend Backend { get; }

    public SecretStore Store { get; }

    public TestRuntime Runtime { get; }

    /// <summary>kSecAttrService as String: AppInfo.name (MCPKeychain.swift:15).</summary>
    public static string Service => AppInfo.Current.Name;

    /// <summary>Every address handed to the default browser, in order.</summary>
    public IReadOnlyList<string> Opened
    {
        get { lock (opened) return [.. opened]; }
    }

    /// <summary>The current Unix time of the test clock, in whole seconds (the clock starts on a whole second).</summary>
    public long NowUnixSeconds => Runtime.Time.GetUtcNow().ToUnixTimeSeconds();

    public string? Stored(string account) =>
        Store.CopyMatching(Service, account, out var data) == SecretStoreStatus.Success && data is not null
            ? Encoding.UTF8.GetString(data)
            : null;

    public void Seed(string account, string value) =>
        Assert.Equal(SecretStoreStatus.Success, Store.Add(Service, account, Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// How many times the backend read the item stored under the account. The backend appends to its log under the
    /// secret store's lock, which the test cannot take, so a scan that races an append is simply retried.
    /// </summary>
    public int ReadsOf(string account)
    {
        var target = SecretStore.TargetName(Service, account);
        while (true)
        {
            try
            {
                return Memory.Operations.ToArray().Count(operation => operation == "read " + target);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                // The log grew during the copy; read it again.
            }
        }
    }

    /// <summary>
    /// Pumps the main loop until the flow hands an address to the default browser, and returns it. When the flow ends
    /// first, its own failure is rethrown so the test reports the real cause.
    /// </summary>
    public string AwaitBrowser(Task flow)
    {
        Runtime.Loop.RunUntil(() => firstOpen.Task.IsCompleted || flow.IsCompleted, Guard);
        if (!firstOpen.Task.IsCompleted)
        {
            flow.GetAwaiter().GetResult();
            Assert.Fail("The sign-in finished without opening the browser.");
        }
        return firstOpen.Task.Result;
    }

    /// <summary>The next request the server received, or the flow's own failure when it ends first.</summary>
    public static async Task<LoopbackExchange> Next(LoopbackHttpServer server, Task flow)
    {
        var next = server.NextExchange();
        var first = await Task.WhenAny(next, flow).WaitAsync(Guard);
        if (first == flow && !next.IsCompleted)
        {
            await flow;
            Assert.Fail("The flow finished before the next request reached the server.");
        }
        return await next;
    }

    public static Task RespondJson(LoopbackExchange exchange, int status, string json) =>
        exchange.Respond(status, Reason(status), Encoding.UTF8.GetBytes(json), ("Content-Type", "application/json"));

    public static string Reason(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "Status",
    };

    /// <summary>The name and value pairs of an x-www-form-urlencoded body or a query, in order, percent-decoded.</summary>
    public static List<(string Name, string Value)> Pairs(string encoded)
    {
        var pairs = new List<(string, string)>();
        if (encoded.Length == 0) return pairs;
        foreach (var item in encoded.Split('&'))
        {
            var equals = item.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? item : item[..equals];
            var value = equals < 0 ? "" : item[(equals + 1)..];
            pairs.Add((Uri.UnescapeDataString(name), Uri.UnescapeDataString(value)));
        }
        return pairs;
    }

    /// <summary>The query of an absolute address as ordered pairs.</summary>
    public static List<(string Name, string Value)> QueryPairs(string address)
    {
        var question = address.IndexOf('?', StringComparison.Ordinal);
        return question < 0 ? [] : Pairs(address[(question + 1)..]);
    }

    public static string Value(List<(string Name, string Value)> pairs, string name) =>
        pairs.Single(pair => pair.Name == name).Value;

    public void Dispose()
    {
        browserScope.Dispose();
        storeScope.Dispose();
        Runtime.Dispose();
    }

    private bool OpenInTest(SwiftURL url)
    {
        lock (opened) opened.Add(url.AbsoluteString);
        firstOpen.TrySetResult(url.AbsoluteString);
        return true;
    }
}

/// <summary>Plays the browser against the sign-in callback listener over a real loopback socket.</summary>
public static class CallbackClient
{
    /// <summary>The port of a redirect URI "http://127.0.0.1:&lt;port&gt;/callback".</summary>
    public static int PortOf(string redirectURI)
    {
        const string prefix = "http://127.0.0.1:";
        Assert.StartsWith(prefix, redirectURI, StringComparison.Ordinal);
        var rest = redirectURI[prefix.Length..];
        return int.Parse(rest[..rest.IndexOf('/', StringComparison.Ordinal)], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    public static async Task<TcpClient> Connect(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(OAuthHarness.Guard);
        return client;
    }

    /// <summary>
    /// Sends the bytes and reads the answer: a head, then Content-Length bytes of body. Returns what arrived, which is
    /// empty when the listener closed the connection without replying.
    /// </summary>
    public static async Task<byte[]> Exchange(int port, byte[] request)
    {
        using var client = await Connect(port);
        var stream = client.GetStream();
        try
        {
            await stream.WriteAsync(request).AsTask().WaitAsync(OAuthHarness.Guard);
        }
        catch (Exception error) when (error is IOException or SocketException)
        {
            // The listener may close the connection while the request is still being written.
        }
        return await ReadAnswer(stream);
    }

    public static Task<byte[]> Get(int port, string target) =>
        Exchange(port, Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"));

    /// <summary>Reads a head and its Content-Length body, or up to the end of the stream when no head arrives.</summary>
    public static async Task<byte[]> ReadAnswer(NetworkStream stream)
    {
        var read = new List<byte>();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var headEnd = IndexOf(read, "\r\n\r\n"u8);
            if (headEnd >= 0)
            {
                var head = Encoding.ASCII.GetString(read.GetRange(0, headEnd).ToArray());
                var lengthLine = head.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length: ", StringComparison.Ordinal));
                if (lengthLine is not null)
                {
                    var length = int.Parse(lengthLine["Content-Length: ".Length..], NumberStyles.None, CultureInfo.InvariantCulture);
                    if (read.Count >= headEnd + 4 + length) return [.. read];
                }
            }
            int count;
            try
            {
                count = await stream.ReadAsync(chunk).AsTask().WaitAsync(OAuthHarness.Guard);
            }
            catch (Exception error) when (error is IOException or SocketException)
            {
                return [.. read];
            }
            if (count == 0) return [.. read];
            read.AddRange(chunk.AsSpan(0, count).ToArray());
        }
    }

    public static (string Head, byte[] Body) Split(byte[] answer)
    {
        var headEnd = IndexOf([.. answer], "\r\n\r\n"u8);
        Assert.True(headEnd >= 0, "no response head arrived");
        return (Encoding.ASCII.GetString(answer, 0, headEnd + 4), answer[(headEnd + 4)..]);
    }

    private static int IndexOf(List<byte> buffer, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= buffer.Count; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++) match = buffer[i + j] == needle[j];
            if (match) return i;
        }
        return -1;
    }
}
