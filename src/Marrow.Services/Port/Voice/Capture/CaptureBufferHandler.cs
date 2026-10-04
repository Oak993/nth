// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the capture thread's callback (voice-SPEC V-2.2 item 1).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// One device buffer, on the capture thread: whole frames in the device's format. <paramref name="silent"/> is WASAPI's AUDCLNT_BUFFERFLAGS_SILENT:
/// the buffer's content is to be treated as zeros whatever it holds.
/// </summary>
public delegate void CaptureBufferHandler(ReadOnlySpan<byte> buffer, bool silent);
