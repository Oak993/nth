// PORT-ONLY, owner-ruled addition (OD-0091, OD-0097; first-release voice typing, VT-3): the pill's Listening / Transcribing / Finishing
// label (voice-SPEC V-6.3).
namespace Marrow.Core.Port.Voice.Session;

/// <summary>The label rule of V-6.3. The words drawn for each state come from the picked set (V-12.9).</summary>
public sealed class SpeechLabel
{
    public static readonly TimeSpan WordWindow = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan SpeechWindow = TimeSpan.FromSeconds(0.6);

    public VoiceLabelState State(ContinuousInstant now) => throw new NotImplementedException();

    public void SetSpeech(bool inSpeech, ContinuousInstant now) => throw new NotImplementedException();

    public void WordArrived(ContinuousInstant now) => throw new NotImplementedException();

    public void Finish() => throw new NotImplementedException();

    public void Reset() => throw new NotImplementedException();

    public string Words(VoicePillWords set, ContinuousInstant now) => throw new NotImplementedException();
}
