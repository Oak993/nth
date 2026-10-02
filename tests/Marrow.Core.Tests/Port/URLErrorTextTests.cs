using System.Net.Http;
using System.Net.Sockets;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.Core.Tests.Fixtures.HttpReading;

namespace Marrow.Core.Tests.Port;

// OD-0056: where Swift shows a Foundation URLError's localizedDescription ("The request timed out." in MCPHub.swift:68
// and :77-79), the port shows the Windows networking error's own message. The decision lives in one place, the
// error's own description, which SwiftErrors.LocalizedDescription returns (R-ERR-2). Port-only code, so these carry
// Rule traits (the decision has no ED id). The expected text is asked of Windows itself, never written out: a
// SocketException built from the Winsock error number carries the operating system's message, in its language.
// Every request goes to a loopback socket or is answered in-process (R-TEST-9, R-TEST-10); timers run on the manual clock.
public sealed class URLErrorTextTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    // Foundation's English text for the same failure, which the port must NOT show any more.
    private const string FoundationTimedOut = "The request timed out.";

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public async Task A_request_that_times_out_shows_Windows_text_for_a_timed_out_connection()
    {
        // The idle limit is the port's own timer, so no exception came from the network: the text is Windows' for
        // WSAETIMEDOUT (10060), the error the same wait ends with on a real connection.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/silent")));
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(60));
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));

        var windows = new SocketException((int)SocketError.TimedOut).Message;
        Assert.False(string.IsNullOrWhiteSpace(windows));
        Assert.Equal(windows, SwiftErrors.LocalizedDescription(error));
        Assert.Equal(windows, error.Message);
        Assert.NotEqual(FoundationTimedOut, SwiftErrors.LocalizedDescription(error));
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public async Task A_refused_connection_shows_the_message_of_the_Windows_error_that_refused_it()
    {
        // The exception the transport raised is kept, and its message is the text: HttpRequestException carrying the
        // SocketException for WSAECONNREFUSED (10061).
        using var runtime = new TestRuntime();
        var pending = URLSession.Shared.Data(new URLRequest(LoopbackHttpServer.RefusedUrl("/closed")));
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);

        var underlying = Assert.IsType<HttpRequestException>(error.Underlying);
        var socket = Assert.IsType<SocketException>(underlying.InnerException);
        Assert.Equal(SocketError.ConnectionRefused, socket.SocketErrorCode);
        var description = SwiftErrors.LocalizedDescription(error);
        Assert.Equal(underlying.Message, description);
        Assert.Contains(socket.Message, description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public void A_generic_dotnet_wrapper_gives_way_to_the_Windows_socket_error_inside_it()
    {
        // OD-0056 asks for the Windows networking error's own message: when the outer exception is only .NET's
        // "An error occurred while sending the request." around a Winsock error, the Winsock text is shown.
        var reset = new SocketException((int)SocketError.ConnectionReset);
        var wrapped = new HttpRequestException("An error occurred while sending the request.", new IOException("Unable to read data from the transport connection.", reset));
        Assert.Equal(reset.Message, SwiftErrors.LocalizedDescription(new URLError(URLError.Code.NetworkConnectionLost, wrapped)));
        // An outer message that already carries the Windows text (and adds the address) is kept as it is.
        var refused = new SocketException((int)SocketError.ConnectionRefused);
        var named = new HttpRequestException(refused.Message + " (127.0.0.1:9)", refused);
        Assert.Equal(named.Message, SwiftErrors.LocalizedDescription(new URLError(URLError.Code.CannotConnectToHost, named)));
        // With no Windows error inside, the outer message stands.
        var plain = new HttpRequestException("The response ended prematurely.");
        Assert.Equal(plain.Message, SwiftErrors.LocalizedDescription(new URLError(URLError.Code.NetworkConnectionLost, plain)));
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public async Task A_connection_severed_inside_the_body_shows_the_message_of_the_error_that_cut_it()
    {
        // The read of the body fails with the transport's own exception; the URLError the caller gets keeps it.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/cut")));
        var exchange = await server.NextExchange();
        await exchange.WriteHead(200, "OK", ("Content-Length", "100"));
        await exchange.Write(Ascii("0123456789"));
        exchange.Abort();
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.NetworkConnectionLost, error.URLErrorCode);

        var underlying = Assert.IsAssignableFrom<Exception>(error.Underlying);
        var description = SwiftErrors.LocalizedDescription(error);
        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.Equal(underlying.Message, description);
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public async Task A_stream_that_times_out_while_being_read_shows_Windows_text_for_a_timed_out_connection()
    {
        // The same limit, ended while the body of a stream is being read (the byte sequence rebuilds the error).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 30));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("data: one\n"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("data: one\n"), await ReadExactly(reader, 10));
        runtime.Advance(TimeSpan.FromSeconds(30));
        var error = await Assert.ThrowsAsync<URLError>(() => reader.MoveNextAsync().AsTask().WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
        Assert.Equal(new SocketException((int)SocketError.TimedOut).Message, SwiftErrors.LocalizedDescription(error));
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public void An_error_made_from_a_Windows_exception_shows_that_exception_s_message_whatever_its_code()
    {
        var underlying = new IOException("The specified network name is no longer available.");
        var error = new URLError(URLError.Code.NetworkConnectionLost, underlying);
        Assert.Same(underlying, error.Underlying);
        Assert.Equal("The specified network name is no longer available.", SwiftErrors.LocalizedDescription(error));
        Assert.Equal("The specified network name is no longer available.", error.Message);
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public void A_cancelled_error_with_no_exception_shows_the_text_of_a_cancelled_operation()
    {
        Assert.Equal(new OperationCanceledException().Message, SwiftErrors.LocalizedDescription(new URLError(URLError.Code.Cancelled)));
    }

    [Theory]
    [Trait("Rule", "R-ERR-2")]
    [InlineData(URLError.Code.BadURL)]
    [InlineData(URLError.Code.UnsupportedURL)]
    [InlineData(URLError.Code.HTTPTooManyRedirects)]
    [InlineData(URLError.Code.BadServerResponse)]
    public void An_error_the_port_raised_itself_with_no_Windows_error_behind_it_still_has_no_invented_text(URLError.Code code)
    {
        // R-ERR-2's "no invented text" stands (OD-0056): only a platform message is shown, and these have none.
        Assert.Throws<NotSupportedException>(() => SwiftErrors.LocalizedDescription(new URLError(code)));
    }
}
