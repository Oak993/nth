// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-19 B; first-release voice typing, VT-5): one capture endpoint, as the Microphone row lists it.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>An active capture endpoint: its stable Windows id (what <c>mic</c> stores, V-16.2) and its friendly name.</summary>
public sealed record AudioEndpoint(string Id, string Name);
