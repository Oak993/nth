// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): V-18.4.
namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>
/// A cloud transcription that did not happen (V-18.4: the error maps to a cloud note; no automatic fallback to the local engine).
/// The message never holds the key or anything OpenAI wrote back (an error body can quote part of a key).
/// </summary>
public sealed class OpenAiTranscriptionException(CloudNote note, int? statusCode = null, bool timedOut = false, Exception? inner = null)
    : Exception("The cloud transcription failed: " + note + (statusCode is { } status ? " (HTTP " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : "") + (timedOut ? " (timed out)" : ""), inner)
{
    public CloudNote Note { get; } = note;

    /// <summary>The HTTP status OpenAI answered with, when it answered.</summary>
    public int? StatusCode { get; } = statusCode;

    /// <summary>True when the request ran out of time.</summary>
    public bool TimedOut { get; } = timedOut;
}
