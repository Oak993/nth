using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>An offline CLI session that can hold injected history.</summary>
public sealed class StandbyInjectionSession : ProviderSession, IStandbyInjectionSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning { get; private set; }
    public bool Stopped { get; private set; }
    public bool Ended { get; private set; }
    public bool StartUnavailable { get; init; }
    public bool InjectFails { get; init; }
    public int DiscardCalls { get; private set; }
    public List<string> Injected { get; } = [];
    public List<TurnInput> Inputs { get; } = [];
    public string? StandbyViolation { get; private set; }
    public Action<string>? OnStandbyViolation { get; set; }

    public Task<bool> SupportsStandbyInjection(CancellationToken ct) => Task.FromResult(!StartUnavailable);
    public Task<string> Start() => throw new InvalidOperationException("A standby never calls Start.");
    public Task<string> StartInjectedStandby(CancellationToken ct)
    {
        if (StartUnavailable) throw new StandbyUnavailableException("too old");
        IsRunning = true;
        return Task.FromResult("standby-thread");
    }

    public Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        if (InjectFails) throw new StandbyUnavailableException("refused");
        Injected.Add(text);
        return Task.CompletedTask;
    }

    public Task DiscardStandby()
    {
        DiscardCalls++;
        Stop();
        return Task.CompletedTask;
    }

    public bool EndFails { get; init; }
    public Task EndStandbyAsync(CancellationToken ct)
    {
        if (EndFails) throw new IOException("the handover failed");
        Ended = true;
        return Task.CompletedTask;
    }
    public void Violate(string method)
    {
        StandbyViolation = method;
        OnStandbyViolation?.Invoke(method);
    }

    public Task Send(TurnInput input) { Inputs.Add(input); return Task.CompletedTask; }
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { Stopped = true; IsRunning = false; }
}
