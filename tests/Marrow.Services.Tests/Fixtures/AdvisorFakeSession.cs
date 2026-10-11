using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// ED-NEW-ignisadvisor-2: a provider session that never launches anything. Send plays the scripted events (or, with
// Hang, never finishes); it records the turn it was sent and whether it was stopped.
public sealed class AdvisorFakeSession(IReadOnlyList<ProviderEvent> script, bool hang = false) : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning { get; private set; }
    public TurnInput? Sent { get; private set; }
    public bool Stopped { get; private set; }
    public List<(string Request, string Option)> Resolved { get; } = [];

    public Task<string> Start()
    {
        IsRunning = true;
        return Task.FromResult("advisor-session");
    }

    public Task Send(TurnInput input)
    {
        Sent = input;
        if (hang) return Task.CompletedTask;
        foreach (var providerEvent in script) OnEvent?.Invoke(providerEvent);
        return Task.CompletedTask;
    }

    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) => Resolved.Add((requestID, optionID));
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;

    public void Stop()
    {
        Stopped = true;
        IsRunning = false;
    }
}
