// PORT-ONLY, owner-ruled addition (OD-0096, OD-0098 Q-V-1 C and the welcome-voice pick; first-release WV-1, voice-SPEC V-7.7, V-10.7): the Welcome voice question and the slow-PC question as one state machine.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>The question and its states; the Welcome section and the orb's card draw it.</summary>
public sealed class VoiceSpeedQuestion(VoicePreparation preparation, IVoicePreparationSettings settings)
{
    public VoiceQuestionState State => throw new NotImplementedException();

    public bool WelcomeGone => throw new NotImplementedException();

    public bool AsksAtOrb => throw new NotImplementedException();

    public string? Error => throw new NotImplementedException();

    public event Action? Changed;

    public Task YesAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken) => throw new NotImplementedException();

    public void No() => throw new NotImplementedException();

    public Task AnswerAsync(LiveWordsChoice choice, CancellationToken cancellationToken) => throw new NotImplementedException();

    public void CloseWelcome() => throw new NotImplementedException();
}
