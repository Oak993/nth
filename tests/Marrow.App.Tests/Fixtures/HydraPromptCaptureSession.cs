using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

internal sealed class HydraPromptCaptureSession : ProviderSession
{
    public List<TurnInput> Inputs { get; } = [];
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public Task<string> Start() => Task.FromResult("offline-hydra-prompt");
    public Task Send(TurnInput input) { Inputs.Add(input); return Task.CompletedTask; }
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
}
