using Marrow.App.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-18: the brain of a voice conversation under test: answers what the test says and records what it was asked.</summary>
public sealed class FakeVoiceBrain : IVoiceBrain
{
    public Func<string, VoiceAnswer> Reply { get; set; } = _ => new VoiceAnswer("Okay, I am on it.");

    public List<string> Asked { get; } = [];

    public Task<VoiceAnswer> AskAsync(string heard, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Asked.Add(heard);
        return Task.FromResult(Reply(heard));
    }
}
