using Marrow.Core.Accounts;

namespace Marrow.App.Runtime;

internal sealed class ThreadAccountRouting(ThreadRuntime runtime, HandshakeMachine machine)
{
    public ThreadRuntime Runtime { get; } = runtime;
    public HandshakeMachine Machine { get; } = machine;
    public CancellationTokenSource Cancellation { get; } = new();
    public HandshakeEvent? State { get; set; }
    public long LimitedEpoch { get; set; }
    public Guid? LimitedTurn { get; set; }
    public Guid? LimitedAccount { get; set; }
    public string? LimitedSessionId { get; set; }
    public bool ResumeWaiting { get; set; }
    public ITimer? ResetTimer { get; set; }
}
