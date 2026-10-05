// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): the device notifications of voice-SPEC V-4.4 (IMMNotificationClient).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>Added: a capture endpoint became active. Removed: one was removed, unplugged or disabled. DefaultChanged: the default capture endpoint changed. FormatChanged: an endpoint's mix format changed.</summary>
public enum EndpointNoticeKind { Added, Removed, DefaultChanged, FormatChanged }
