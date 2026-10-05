using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Fixtures;

// VT-5 test fixture: the Windows microphone privacy answer, scripted (voice-SPEC V-2.4 "fake Allowed / Denied / NoDevice"). It reads no setting of the machine.
public sealed class ScriptedPrivacySource(MicrophonePrivacy answer = MicrophonePrivacy.Allowed) : IMicrophonePrivacySource
{
    public MicrophonePrivacy Answer { get; set; } = answer;

    /// <summary>When set, <see cref="Current"/> throws it (a Windows that cannot say).</summary>
    public Exception? Failure { get; set; }

    public int Asked { get; private set; }

    public MicrophonePrivacy Current()
    {
        Asked += 1;
        if (Failure is { } failure) throw failure;
        return Answer;
    }
}
