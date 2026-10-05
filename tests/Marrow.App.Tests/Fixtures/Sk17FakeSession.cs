using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

// SK-17: an in-memory session, never a CLI or account.
public sealed class Sk17FakeSession(SessionConfiguration configuration) : ProviderSession
{
    public SessionConfiguration Configuration { get; } = configuration;
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public Exception? StartFailure { get; init; }
    public Task<string> Start()
    {
        Starts++;
        if (StartFailure is { } failure) return Task.FromException<string>(failure);
        IsRunning = true;
        return Task.FromResult("sk17-fake-session");
    }
    public Task Send(TurnInput input) => Task.CompletedTask;
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { Stops++; IsRunning = false; }
}
