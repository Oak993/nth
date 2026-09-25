using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A session whose Send always fails with a given Win32Exception, for
/// ThreadRuntimePart9Tests.StartTurn_uses_the_Win32_exception_message_in_its_error_notice (PR-RUNTIME-077).</summary>
public sealed class Win32FailingProviderSession(System.ComponentModel.Win32Exception failure) : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public Task<string> Start() => Task.FromResult("session");
    public Task Send(TurnInput input) => Task.FromException(failure);
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
}
