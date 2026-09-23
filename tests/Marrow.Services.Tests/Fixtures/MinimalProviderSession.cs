using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// A ProviderSession that implements only the protocol's eight required members, so its inherited
/// stopAgent stays the protocol's own default (PR-RUNTIME-008). Nothing here launches a real process
/// or agent (R-TEST-10).
/// </summary>
public sealed class MinimalProviderSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => false;
    public Task<string> Start() => Task.FromResult("");
    public Task Send(TurnInput input) => Task.CompletedTask;
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
}
