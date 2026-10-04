// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): where the probe seam is bound (V-2.4, V-2.5).
using Marrow.Core.Port;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// The ambient hardware probe. The default fails closed; the app's start-up composition binds <see cref="HardwareProbe"/>. It lives here and not in Core's <c>VoiceSeams</c>
/// because <see cref="IHardwareProbe"/> is a Services type (VT-6's files are Services only; ED-310).
/// </summary>
public static class HardwareProbeSeams
{
    public static Ambient<IHardwareProbe> Probe { get; } = new(new FailClosedHardwareProbe());
}
