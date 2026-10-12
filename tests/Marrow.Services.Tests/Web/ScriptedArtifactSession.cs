using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Web;

/// <summary>W13: a provider session that answers a turn with recorded events (no provider is called).</summary>
internal sealed class ScriptedArtifactSession(params ProviderEvent[] reply) : ProviderSession
{
    public List<TurnInput> Inputs { get; } = [];

    public bool Stopped { get; private set; }

    public Action<ProviderEvent>? OnEvent { get; set; }

    public bool IsRunning => !Stopped;

    public Task<string> Start() => Task.FromResult("artifact-model-fixture");

    public Task Send(TurnInput input)
    {
        Inputs.Add(input);
        foreach (var item in reply) OnEvent?.Invoke(item);
        return Task.CompletedTask;
    }

    public Task Interrupt() => Task.CompletedTask;

    public void ResolveApproval(string requestID, string optionID) { }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }

    public Task Compact() => Task.CompletedTask;

    public void Stop() => Stopped = true;
}
