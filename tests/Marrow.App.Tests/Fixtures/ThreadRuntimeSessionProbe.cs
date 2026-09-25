using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Event source used to deliver controlled session events to ThreadRuntime.Observe.</summary>
public sealed class ThreadRuntimeSessionProbe : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }

    public bool IsRunning => false;

    public Task<string> Start() => throw new InvalidOperationException("The event probe must not be started.");

    public Task Send(TurnInput input) => throw new InvalidOperationException("The event probe must not send turns.");

    public Task Interrupt() => throw new InvalidOperationException("The event probe must not interrupt.");

    public void ResolveApproval(string requestID, string optionID) => throw new InvalidOperationException("The event probe has no approvals.");

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) => throw new InvalidOperationException("The event probe has no questions.");

    public Task Compact() => throw new InvalidOperationException("The event probe must not compact.");

    public void Stop() => throw new InvalidOperationException("The event probe must not stop.");

    public void Emit(ProviderEvent @event) => OnEvent?.Invoke(@event);
}
