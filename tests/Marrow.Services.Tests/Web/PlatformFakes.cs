namespace Marrow.Services.Tests.Web;

/// <summary>An HTTP handler that answers every request from a function, so no test reaches a network.</summary>
internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        var response = respond(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}

/// <summary>An HTTP handler whose every request fails the way a dead connection does.</summary>
internal sealed class ThrowingHandler(Exception error) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw error;
}

/// <summary>A stream that never ends, for a server that says less than it sends.</summary>
internal sealed class EndlessStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
