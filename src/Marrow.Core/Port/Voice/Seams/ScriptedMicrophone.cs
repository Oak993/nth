// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the microphone permission double (voice-SPEC V-2.4
// "fake Allowed / Denied / NoDevice").
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>An <see cref="IMicrophoneAccess"/> that answers what the test sets.</summary>
public sealed class ScriptedMicrophone(MicrophoneAccessStatus status = MicrophoneAccessStatus.Allowed) : IMicrophoneAccess
{
    public MicrophoneAccessStatus Current { get; set; } = status;

    public int Asked { get; private set; }

    public MicrophoneAccessStatus Status()
    {
        Asked += 1;
        return Current;
    }
}
