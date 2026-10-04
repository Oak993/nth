// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the microphone permission seam (voice-SPEC V-2.4, V-4.5).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>What Windows says about the microphone before the device is opened (V-4.5).</summary>
public enum MicrophoneAccessStatus { Allowed, Denied, NoDevice }

/// <summary>The privacy gate (V-4.5). Production: <c>MicrophoneAccess</c> (WinRT, VT-5). Tests: a fake answering Allowed, Denied or NoDevice.</summary>
public interface IMicrophoneAccess
{
    MicrophoneAccessStatus Status();
}
