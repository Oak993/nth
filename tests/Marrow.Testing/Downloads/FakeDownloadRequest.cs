namespace Marrow.Testing.Downloads;

/// <summary>
/// PORT-ONLY test fixture (SK-0): one request a <see cref="FakeDownloadServer"/> answered, as its log records it. The log is
/// how a test proves what crossed the wire ("bytes not refetched", "no connection made") without trusting the client's own
/// account of itself. An entry is added when the response is finished, cut or abandoned by the client.
/// </summary>
/// <param name="Sequence">Arrival order, from 1.</param>
/// <param name="ConnectionId">The accepted connection, from 1; two requests on one connection share it.</param>
/// <param name="Target">The request target as sent, path and query.</param>
/// <param name="Range">The Range header exactly as sent, or null.</param>
/// <param name="RangeStart">The first byte of a "bytes=N-" or "bytes=N-M" Range, or null when there was none or it was not one of those.</param>
/// <param name="Status">The status the server answered.</param>
/// <param name="BodyBytesPromised">The Content-Length the server announced (the body a GET would carry, also for HEAD).</param>
/// <param name="BodyBytesSent">The body bytes actually written, which is less than promised after a drop or when the client left.</param>
/// <param name="Dropped">True when the server cut the connection on purpose (DropAfterBytes).</param>
/// <param name="ClientClosedEarly">True when a write failed because the client had gone.</param>
public sealed record FakeDownloadRequest(
    int Sequence,
    int ConnectionId,
    string Method,
    string Target,
    string Path,
    string? Range,
    long? RangeStart,
    int Status,
    long BodyBytesPromised,
    long BodyBytesSent,
    bool Dropped,
    bool ClientClosedEarly,
    IReadOnlyList<(string Name, string Value)> Headers)
{
    public string? Header(string name) =>
        Headers.Where(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();
}
