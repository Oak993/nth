using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>An offline CLI session that can wait for a switch holding injected history (ED-439): it records what was injected and every real turn.</summary>
public sealed class ThreadRuntimeInjectionSession : ProviderSession, IStandbyInjectionSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning { get; private set; }
    public bool Supported { get; set; } = true;
    public bool InjectFails { get; set; }
    public bool Ended { get; private set; }
    public int StartCalls { get; private set; }
    public int DiscardCalls { get; private set; }
    public int StopCalls { get; private set; }
    public List<string> Injected { get; } = [];
    public List<TurnInput> Inputs { get; } = [];
    public List<string> Log { get; } = [];
    public string? StandbyViolation { get; private set; }
    public Action<string>? OnStandbyViolation { get; set; }

    public Task<bool> SupportsStandbyInjection(CancellationToken ct) => Task.FromResult(Supported);

    public Task<string> StartInjectedStandby(CancellationToken ct)
    {
        Log.Add("start-standby");
        if (!Supported) throw new StandbyUnavailableException("unsupported");
        StartCalls++;
        IsRunning = true;
        return Task.FromResult("standby-thread");
    }

    public Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        if (InjectFails) throw new StandbyUnavailableException("refused");
        Log.Add("inject");
        Injected.Add(text);
        return Task.CompletedTask;
    }

    public Task DiscardStandby()
    {
        DiscardCalls++;
        Log.Add("discard");
        Stop();
        return Task.CompletedTask;
    }

    public bool EndFails { get; set; }

    public Task EndStandbyAsync(CancellationToken ct)
    {
        if (EndFails) throw new IOException("the handover failed");
        Ended = true;
        Log.Add("end-standby");
        return Task.CompletedTask;
    }

    public void Violate(string method)
    {
        StandbyViolation = method;
        OnStandbyViolation?.Invoke(method);
    }

    public Task<string> Start() => throw new InvalidOperationException("A standby never calls Start.");

    public Task Send(TurnInput input)
    {
        Log.Add("turn");
        Inputs.Add(input);
        return Task.CompletedTask;
    }

    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;

    public void Stop()
    {
        StopCalls++;
        IsRunning = false;
    }

    public void Emit(ProviderEvent @event) => OnEvent?.Invoke(@event);
}
