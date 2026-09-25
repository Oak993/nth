using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

internal sealed class TestProviderSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public Task<string> Start() => Task.FromResult("session");
    public Task Send(TurnInput input) => Task.CompletedTask;
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
    public void Emit(ProviderEvent @event) => OnEvent?.Invoke(@event);
}
