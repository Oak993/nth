// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): a change to the capture endpoints.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>A change to the capture endpoints. <see cref="EndpointId"/> is the endpoint concerned (the new default for DefaultChanged; null when there is none).</summary>
public sealed record EndpointNotice(EndpointNoticeKind Kind, string? EndpointId);
