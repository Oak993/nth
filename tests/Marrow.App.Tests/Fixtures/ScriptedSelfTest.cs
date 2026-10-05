using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-14 test double of the self-test seam: it answers the outcome the test gives (or null) and records the candidates it was asked about. No model is loaded and no clip is run.</summary>
internal sealed class ScriptedSelfTest(Func<string, SelfTestOutcome?> outcome) : ISpeechSelfTest
{
    public List<string> Candidates { get; } = [];

    public Task<SelfTestOutcome?> RunAsync(string candidateId, HardwareProfile profile, CancellationToken cancellationToken)
    {
        Candidates.Add(candidateId);
        return Task.FromResult(outcome(candidateId));
    }
}
