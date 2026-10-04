// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-3): the model download transport seam (voice-SPEC V-2.4, V-9.5).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>A ranged GET's answer: the status (206, 200, 416, 5xx), the body length when known, and the body.</summary>
public sealed record ModelTransportResponse(int StatusCode, long? ContentLength, Stream? Body);

/// <summary>
/// <c>GetRange(uri, offset)</c> (V-2.4): GET <paramref name="uri"/> from byte <paramref name="offset"/> (a <c>Range: bytes=n-</c>
/// request). Production: <c>HttpClient</c> in <c>ModelDownloader</c> (VT-7); tests: a loopback server (R-TEST-10).
/// </summary>
public interface IModelTransport
{
    Task<ModelTransportResponse> GetRange(Uri uri, long offset, CancellationToken cancellationToken);
}
