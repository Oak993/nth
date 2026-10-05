// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-19 B; first-release voice typing, VT-5): which endpoint a stored Microphone pick opens.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>The endpoint to open (null: the Windows default) and whether the stored pick was missing so the default is used instead (Q-V-19 B).</summary>
public readonly record struct MicrophoneResolution(string? EndpointId, bool FellBack);
