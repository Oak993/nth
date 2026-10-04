// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): why a capture device could not open or stopped (voice-SPEC V-4.4).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>NotFound: the endpoint is not there (any more). InUse: another app holds it exclusively (AUDCLNT_E_DEVICE_IN_USE). Removed: it went away while capturing. Other: anything else.</summary>
public enum CaptureDeviceFailure { NotFound, InUse, Removed, Other }
