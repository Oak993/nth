using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: offline mission gate, ED-NEW-mission-pc-1. No desktop or provider.
internal sealed class MissionPcHost : IAssistantToolHost
{
    public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Queue<TaskCompletionSource<bool>> Answers { get; } = [];
    public List<string> Calls { get; } = [];
    public Task<bool> ApprovePc(PcRegistration registration, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        Calls.Add(tool);
        return (Answers.TryDequeue(out var answer) ? answer : Answer).Task.WaitAsync(cancellationToken);
    }
    public AssistantChatIdentity? Identify(Guid thread) => null;
    public bool AutoAnswering => false;
    public PolicyContext PolicyContext(AssistantChat chat) => throw new InvalidOperationException();
    public Task<AssistantToolResult> Run(AssistantToolCall call, CancellationToken cancellationToken) => throw new InvalidOperationException();
    public Task<string> Ask(AssistantToolCall call, CancellationToken cancellationToken) => throw new InvalidOperationException();
    public void Offer(AssistantToolCall call) { }
    public void Audit(AuditEntry entry) => throw new InvalidOperationException();
    public void Absorb(AssistantChat chat, Taint taint) => throw new InvalidOperationException();
}
