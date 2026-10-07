using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Offline injection boundary with explicit gates; never launches a process or makes a request.
public sealed class ReviewInjectionSession : ProviderSession, IStandbyInjectionSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public Action<string>? OnStandbyViolation { get; set; }
    public string? StandbyViolation => null;
    public bool IsRunning { get; private set; }
    public Func<Task>? Starting { get; init; }
    public Action<string>? Injecting { get; init; }
    public List<string> Injected { get; } = [];
    public TaskCompletionSource DiscardedAfterStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> SupportsStandbyInjection(CancellationToken ct) => Task.FromResult(true);
    public async Task<string> StartInjectedStandby(CancellationToken ct)
    {
        if (Starting is not null) await Starting();
        IsRunning = true;
        return "review-thread";
    }
    public Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        Injected.Add(text);
        Injecting?.Invoke(text);
        return Task.CompletedTask;
    }
    public Task DiscardStandby()
    {
        if (IsRunning) DiscardedAfterStart.TrySetResult();
        Stop();
        return Task.CompletedTask;
    }
    public Task EndStandbyAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<string> Start() => throw new InvalidOperationException("Only injected startup is allowed.");
    public Task Send(TurnInput input) => throw new InvalidOperationException("No model turn is allowed.");
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => throw new InvalidOperationException("No model turn is allowed.");
    public void Stop() => IsRunning = false;
}
