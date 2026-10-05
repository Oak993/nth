// PORT-ONLY, owner-ruled addition (OD-0098 welcome-voice pick "Place A, Words V2"; first-release WV-1): the states of the Welcome voice question.
namespace Marrow.App.Port.Voice;

/// <summary>
/// Ask: the question is up. Yes: the model downloads and is timed. Ready: voice typing is ready. No: nothing was downloaded. Slow: the PC is too slow for live words with the accurate model and the Q-V-1 C
/// question is up. SlowLive / SlowAcc: its two answers. Gone: Welcome has gone and nothing is left to ask there.
/// </summary>
public enum VoiceQuestionState { Ask, Yes, Ready, No, Slow, SlowLive, SlowAcc, Gone }
