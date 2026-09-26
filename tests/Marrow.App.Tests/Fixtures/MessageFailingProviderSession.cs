using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A session whose Send always fails with a given message (via ProviderError.Case.Failed, an
/// ILocalizedError whose ErrorDescription is that message verbatim), for the App.Runtime.ThreadRuntime B3 CLI
/// update tests (PR-RUNTIME-077/265): unlike Win32FailingProviderSession, the message is not fixed to a Win32
/// error, so a test can drive a "CLI too old" phrase through StartTurn's catch.</summary>
public sealed class MessageFailingProviderSession(string message) : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public Task<string> Start() => Task.FromResult("session");
    public Task Send(TurnInput input) => Task.FromException(new ProviderError(new ProviderError.Case.Failed(message)));
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() { }
}
