using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Offline session: never resolves an executable, model or network.
public sealed class HandoffSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning { get; private set; }
    public string SessionId { get; init; } = "new-session";
    public bool FailStart { get; init; }
    public bool Stopped { get; private set; }
    public bool StartedBeforeSend { get; private set; }
    public List<TurnInput> Inputs { get; } = [];
    public Task<string> Start()
    {
        if (FailStart) throw new IOException("offline resume refusal");
        IsRunning = true;
        return Task.FromResult(SessionId);
    }
    public Task Send(TurnInput input) { StartedBeforeSend = IsRunning; Inputs.Add(input); return Task.CompletedTask; }
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { Stopped = true; IsRunning = false; }
}
