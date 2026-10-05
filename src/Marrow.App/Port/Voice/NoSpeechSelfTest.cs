// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): the self-test seam's default, until VT-15 binds the real one.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>Times nothing (no model is loaded, no clip is run): "Use recommended" then selects the candidate as it is. VT-15 replaces it in the start-up composition.</summary>
internal sealed class NoSpeechSelfTest : ISpeechSelfTest
{
    public Task<SelfTestOutcome?> RunAsync(string candidateId, HardwareProfile profile, CancellationToken cancellationToken) => Task.FromResult<SelfTestOutcome?>(null);
}
