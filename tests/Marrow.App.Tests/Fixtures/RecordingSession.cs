using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

internal sealed class RecordingSession : ProviderSession
{
    public Action<string, string>? OnResolve { get; set; }
    public Action<string, ValueMap<string, ValueList<string>>>? OnAnswer { get; set; }
    public (string RequestID, string OptionID)? Resolved { get; private set; }
    public ValueMap<string, ValueList<string>>? Answered { get; private set; }
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public Task<string> Start() => Task.FromResult("session");
    public Task Send(TurnInput input) => Task.CompletedTask;
    public Task Interrupt() => Task.CompletedTask;

    public void ResolveApproval(string requestID, string optionID)
    {
        Resolved = (requestID, optionID);
        OnResolve?.Invoke(requestID, optionID);
    }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers)
    {
        Answered = answers;
        OnAnswer?.Invoke(requestID, answers);
    }

    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
}
