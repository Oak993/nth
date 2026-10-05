using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Fixtures;

/// <summary>WV-1: the Welcome voice question over a scripted preparation (the pc and the slow profiles), recording every state it passes through. No network, microphone, download or CLI.</summary>
public sealed class VoiceQuestionRig
{
    public static readonly IReadOnlyDictionary<string, double> PcSeconds = new Dictionary<string, double> { ["large-v3-turbo-q8_0"] = 0.6 };

    public static readonly IReadOnlyDictionary<string, double> SlowSeconds = new Dictionary<string, double> { ["large-v3-turbo-q5_0"] = 9.0, ["medium-q8_0"] = 7.0, ["small-q8_0"] = 1.0 };

    public VoiceQuestionRig(ScriptedMachine machine, IReadOnlyDictionary<string, double> seconds)
    {
        Prep = new VoicePrepRig(machine, seconds);
        Prep.Preparation.ProbeAsync(null, CancellationToken.None).GetAwaiter().GetResult(); // Getting ready's hardware step has read the PC before Welcome asks (always allowed: nothing is fetched)
        Question = new VoiceSpeedQuestion(Prep.Preparation, Prep.Settings);
        States.Add(Question.State);
        Question.Changed += () => States.Add(Question.State);
    }

    public static VoiceQuestionRig Pc() => new(ScriptedMachines.OwnersDesktop, PcSeconds);

    public static VoiceQuestionRig Slow() => new(ScriptedMachines.CpuOnlyLaptop, SlowSeconds);

    public VoicePrepRig Prep { get; }

    public VoiceSpeedQuestion Question { get; }

    /// <summary>The state at construction, then after every change.</summary>
    public List<VoiceQuestionState> States { get; } = [];

    /// <summary>The body V2 says in <paramref name="state"/> now, with this PC's sizes filled.</summary>
    public string? Said(VoiceQuestionState state) => Marrow.App.Port.FirstRun.WelcomeVoiceWords.Body(state, Question.Mb, Question.SmallMb);

    public VoiceHardwareRecord? Record => Prep.Settings.Record;
}
