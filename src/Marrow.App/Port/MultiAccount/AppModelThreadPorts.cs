using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App;

public sealed partial class AppModel
{
    private LiveThreadPorts? threadPorts;
    internal LiveThreadPorts LiveAccountPorts => threadPorts ??= new(this);
    internal void DetachThreadPorts(Guid thread) => threadPorts?.Detach(thread);
    internal void DisposeThreadPorts() { threadPorts?.Dispose(); threadPorts = null; }
    internal void CheckCompletedAccountAnswer(ThreadRuntime runtime, Guid turn, Guid author, string message, string answer)
        => threadPorts?.Completed(runtime, turn, author, message, answer);
}

public sealed partial class AppSettings
{
    internal ConversationTransferConsent ConversationConsent(IConversationTransferPrompt prompt) => new(defaults, prompt);
    internal Marrow.Core.Port.MultiAccount.Opinions.OtherServiceConsentStore ViewConversationConsent() => new(defaults);
}
