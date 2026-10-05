// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): where the endpoint seam is bound (voice-SPEC V-2.4, V-2.5).
using Marrow.Core.Port;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>The ambient endpoint seam. The default fails closed; the app's start-up composition binds <see cref="NAudioCaptureHost"/>. A Services type, so it is not in Core's <c>VoiceSeams</c> (as <c>HardwareProbeSeams</c>).</summary>
public static class CaptureSeams
{
    public static Ambient<ICaptureHost> Host { get; } = new(new FailClosedCaptureHost());
}
