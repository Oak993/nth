namespace Marrow.Testing.Downloads;

/// <summary>
/// PORT-ONLY test fixture (SK-0): how one route of a <see cref="FakeDownloadServer"/> answers. Every switch of the plan's DL-0
/// list is here: Accept-Ranges on or off, a drop after N bytes, a wrong byte, a wrong length, 404 and 500 (any status),
/// a slow trickle, a redirect to another loopback host and to plain HTTP. A test replaces a route's behaviour at any time
/// (<see cref="FakeDownloadServer.Behave"/>); the server reads it once per request.
/// </summary>
public sealed record FakeBehavior
{
    /// <summary>
    /// True (the default): the answer carries <c>Accept-Ranges: bytes</c> and a <c>Range: bytes=N-</c> request gets 206 with
    /// <c>Content-Range</c> (416 when N is at or past the end). False: no Accept-Ranges header, and a Range request is ignored,
    /// answered 200 with the whole body from byte 0 (a server that cannot resume).
    /// </summary>
    public bool AcceptRanges { get; init; } = true;

    /// <summary>A status to answer instead of the artifact (404, 500, 410 ...), with a short text body.</summary>
    public int? Status { get; init; }

    /// <summary>
    /// The response promises its full Content-Length, sends this many body bytes, then closes the connection: a cut stream. The
    /// count is of the bytes of that response's own body (after a Range request, from the range's first byte).
    /// </summary>
    public int? DropAfterBytes { get; init; }

    /// <summary>The artifact with the byte at this index flipped (XOR 0xFF): the right length, the wrong hash.</summary>
    public int? WrongByteAt { get; init; }

    /// <summary>
    /// A well-formed, complete answer whose body is this many bytes longer (zeros appended) or shorter (cut from the end) than the
    /// artifact: the Content-Length matches what is sent, so only the downloader's own length check can notice.
    /// </summary>
    public int WrongLengthDelta { get; init; }

    /// <summary>The body goes out in small chunks, one per interval of the server's clock (see <see cref="FakeTrickle"/>).</summary>
    public FakeTrickle? Trickle { get; init; }

    /// <summary>Answer a redirect to this address (any host or scheme, loopback or not) instead of the artifact.</summary>
    public Uri? RedirectTo { get; init; }

    /// <summary>The redirect's status: 302 unless set (301, 307 and 308 are the other usual ones).</summary>
    public int RedirectStatus { get; init; } = 302;

    /// <summary>
    /// How many of the route's first requests the faults above (Status, DropAfterBytes, WrongByteAt, WrongLengthDelta, Trickle,
    /// RedirectTo) apply to; later requests are answered as a plain artifact. Unlimited unless set, so a test that wants "the
    /// first attempt is cut, the retry is fine" writes <c>FaultyRequests = 1</c> and nothing else. AcceptRanges is not a fault and
    /// always applies.
    /// </summary>
    public int FaultyRequests { get; init; } = int.MaxValue;
}

/// <summary>A slow body: <see cref="ChunkBytes"/> bytes, then a wait of <see cref="Interval"/> on the server's clock, and so on.</summary>
public sealed record FakeTrickle(int ChunkBytes, TimeSpan Interval);
