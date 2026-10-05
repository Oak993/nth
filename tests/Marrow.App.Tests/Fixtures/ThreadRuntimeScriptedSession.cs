using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>An offline provider session a test scripts: it records every turn it is sent, counts its stops and
/// interrupts, and can hold or fail a send. It never starts a process or reaches a network.</summary>
public sealed class ThreadRuntimeScriptedSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    private bool running = true;

    /// <summary>Runs once, the first time the runtime asks whether the session is running (EnsureSession's reuse test),
    /// which is after the launch signature was built and before the prompt is.</summary>
    public Action? OnFirstIsRunningRead { get; set; }

    public bool IsRunning
    {
        get
        {
            var hook = OnFirstIsRunningRead;
            OnFirstIsRunningRead = null;
            hook?.Invoke();
            return running;
        }
        set => running = value;
    }
    public List<TurnInput> Inputs { get; } = [];
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int InterruptCalls { get; private set; }
    public Exception? SendFailure { get; set; }
    public Func<TurnInput, Task>? SendScript { get; set; }
    public Func<Task<string>>? StartScript { get; set; }
    public Func<Task>? InterruptScript { get; set; }
    public Func<Task>? CompactScript { get; set; }

    public Task<string> Start()
    {
        StartCalls++;
        return StartScript?.Invoke() ?? Task.FromResult("scripted-session");
    }

    public async Task Send(TurnInput input)
    {
        Inputs.Add(input);
        if (SendFailure is { } failure) throw failure;
        if (SendScript is { } script) await script(input);
    }

    public Task Interrupt()
    {
        InterruptCalls++;
        return InterruptScript?.Invoke() ?? Task.CompletedTask;
    }

    public List<(string Id, string Option, string? Reason)> ApprovalAnswers { get; } = [];
    public void ResolveApproval(string requestID, string optionID) => ApprovalAnswers.Add((requestID, optionID, null));
    public void ResolveApproval(string requestID, string optionID, string? reason) => ApprovalAnswers.Add((requestID, optionID, reason));
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => CompactScript?.Invoke() ?? Task.CompletedTask;

    public void Stop()
    {
        StopCalls++;
        IsRunning = false;
    }

    public void Emit(ProviderEvent @event) => OnEvent?.Invoke(@event);
}
